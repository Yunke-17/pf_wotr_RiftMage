using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.Utility;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 H —— 特殊纠缠：直接做一次「专属专注检定」。
    ///
    /// ========== 为什么需要它 ==========
    ///
    /// UnitUseAbility.MakeConcentrationCheckIfCastingIsDifficult（:1165-1172）只在
    /// HasCondition(Entangled / SpellCastingIsDifficult / SpellCastingIsVeryDifficult)
    /// 成立时才发起 RuleCheckConcentration。
    /// 特殊纠缠刻意不挂 Entangled（会减速、会被行动自如免疫），也不该伪装成
    /// 「施法困难」这种通用条件 —— 所以引擎不会替我们掷。
    ///
    /// ========== 做法（不再伪装条件，直接触发）==========
    ///
    /// 在这个方法前面插入一段：
    ///   若「该单位有特殊纠缠」且「这一发是合格的法师法术」
    ///     → 自己构造并 Trigger 一次 RuleCheckConcentration
    ///        DC 用专属公式：min(32, 25 + round(3(L-1)/7))，L = 法师等级
    ///     → 失败则把 UnitUseAbility.ConcentrationCheckFailed 置为 true
    ///   然后仍返回 true，让原方法继续跑：
    ///     它第一行就是 if (this.ConcentrationCheckFailed) return;
    ///     失败时立即退出；成功时它自己去跑法术愚弄/阻碍魔法/Spellblight 等检查，
    ///     并因为没有任何「施法困难」条件而**不会**再掷一次专注 —— 不会双骰。
    ///
    /// 好处：
    ///   - 不需要 ThreadStatic 伪装、不需要改 HasCondition 的语义；
    ///   - 原方法的其它检查一条都不丢；
    ///   - DC 公式由本 MOD 完全掌控（这就是用户说的「独属于该 debuff 的专注」）。
    ///
    /// 检定成功率与「专注通过后移除纠缠」「失败掉 1d2 体质」等后果，
    /// 仍由 Buff 上的 SpecialEntangleConcentration 组件处理
    /// （它订阅同一个 RuleCheckConcentration）。
    /// </summary>
    [HarmonyPatch(typeof(UnitUseAbility), "MakeConcentrationCheckIfCastingIsDifficult")]
    internal static class ForceConcentrationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitUseAbility __instance)
        {
            if (!Main.Enabled || __instance == null) return true;

            try
            {
                var ability = __instance.Ability;
                var unit = __instance.Executor;
                if (ability == null || unit == null) return true;

                // 只对「有特殊纠缠」「合格法师法术」两个条件都成立时生效
                if (!HasSpecialEntangle(unit)) return true;
                if (!AbilityXHelper.IsEligible(ability)) return true;

                int level = GetWizardLevel(unit);
                int dc = SpecialEntangleConcentration.GetConcentrationDC(level);

                var rule = new RuleCheckConcentration(unit, ability)
                {
                    CustomDC = dc,
                };

                var game = Game.Instance;
                if (game == null || game.Rulebook == null) return true;

                bool success = game.Rulebook.TriggerEvent<RuleCheckConcentration>(rule).Success;

                Main.Log?.Log($"[特殊纠缠] 专属专注检定：{ability.Blueprint?.name}，" +
                              $"法师等级 {level}，DC {dc}，结果 {(success ? "通过" : "失败")}");

                AbilityXScope.AcceptanceHook("X.EntangleCheckRan", true);
                AbilityXScope.AcceptanceHook("X.EntangleCheckDC", dc);
                AbilityXScope.AcceptanceHook("X.EntangleCheckPassed", success);

                if (!success)
                {
                    // ConcentrationCheckFailed 是 public get / private set，用 Traverse 写
                    Traverse.Create(__instance).Property("ConcentrationCheckFailed").SetValue(true);
                }

                // 让原方法继续：失败时它会因 ConcentrationCheckFailed 立即返回；
                // 成功时它继续跑其它检查，且不会重复掷专注（没有施法困难类条件）。
                return true;
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特殊纠缠] 专属专注检定异常，已放行原逻辑：" + e.Message);
                return true;
            }
        }

        private static int GetWizardLevel(UnitEntityData unit)
        {
            try
            {
                var wiz = WizardArchetypeBlueprints.WizardClass;
                if (wiz == null) return 1;
                return Math.Max(1, unit.Progression.GetClassLevel(wiz));
            }
            catch (Exception)
            {
                return 1;
            }
        }

        // ------------------------------------------------------------------
        // 「该单位身上有特殊纠缠」登记表
        //
        // 不用 HasCondition(Entangled) 判断：特殊纠缠刻意不挂那个条件。
        // 由 SpecialEntangleMarker 在 Buff 生效/失效时登记。
        // ------------------------------------------------------------------

        private static readonly ConditionalWeakTable<UnitEntityData, object> s_Entangled =
            new ConditionalWeakTable<UnitEntityData, object>();

        private static readonly object Marker = new object();

        internal static void SetEntangled(UnitEntityData unit, bool on)
        {
            if (unit == null) return;
            if (on) s_Entangled.GetValue(unit, _ => Marker);
            else s_Entangled.Remove(unit);
        }

        internal static bool HasSpecialEntangle(UnitEntityData unit)
        {
            if (unit == null) return false;
            object dummy;
            return s_Entangled.TryGetValue(unit, out dummy);
        }
    }
}
