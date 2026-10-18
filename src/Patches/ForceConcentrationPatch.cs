using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 H —— 让「特殊纠缠」真正触发专注检定。
    ///
    /// ========== 问题 ==========
    ///
    /// 引擎只有在下列条件之一成立时才会掷专注检定
    /// （UnitUseAbility.MakeConcentrationCheckIfCastingIsDifficult，:1165-1172）：
    ///     HasCondition(Entangled)
    ///     HasCondition(SpellCastingIsDifficult)
    ///     HasCondition(SpellCastingIsVeryDifficult)
    ///
    /// 而特殊纠缠**刻意不挂 Entangled**（挂上会减速，且会被行动自如免疫）。
    /// 结果就是：我们在 RuleCheckConcentration 上写的 CustomDC 永远没用 ——
    /// 因为引擎压根不发起那一次检定。
    ///
    /// ========== 解法 ==========
    ///
    /// 不改原方法、不跳过它，只在它执行的那一刻「临时让引擎看到施法困难」：
    ///   Prefix  : 置一个 [ThreadStatic] 标志，并记下本次施放的法术
    ///   Postfix : 清掉标志
    ///   期间的 UnitState.HasCondition(SpellCastingIsDifficult) 由补丁 G 返回 true
    ///
    /// 为什么用「临时伪装条件」而不是自己跑一次检定：
    ///   - 原方法里还有法术愚弄、阻碍魔法、Spellblight 等多个检查，
    ///     自己跑一次就得复制这些逻辑，容易漏；
    ///   - 让引擎照常走，它自己会调 RuleCheckConcentration，
    ///     我们的 SpecialEntangleConcentration 再把 CustomDC 改成文档公式的 DC。
    ///
    /// 为什么不会影响别的法术/别的单位：
    ///   - 标志只在这一次调用期间为 true（ThreadStatic，战斗在主线程）；
    ///   - 还要求「该单位身上有我们的特殊纠缠」且「这一发是合格的法师法术」。
    /// </summary>
    [HarmonyPatch(typeof(UnitUseAbility), "MakeConcentrationCheckIfCastingIsDifficult")]
    internal static class ForceConcentrationPatch
    {
        [ThreadStatic] private static bool s_InCheck;
        [ThreadStatic] private static AbilityDataLike s_Ability;

        /// <summary>简化持有 AbilityData，避免在 [ThreadStatic] 上声明泛型/游戏类型引发的初始化问题。</summary>
        private sealed class AbilityDataLike
        {
            public Kingmaker.UnitLogic.Abilities.AbilityData Ability;
        }

        [HarmonyPrefix]
        private static void Prefix(UnitUseAbility __instance)
        {
            s_InCheck = false;
            s_Ability = null;
            try
            {
                if (!Main.Enabled || __instance == null) return;

                var ability = __instance.Ability;
                if (ability == null || !AbilityXHelper.IsEligible(ability)) return;

                var unit = __instance.Executor;
                if (unit == null || !HasSpecialEntangle(unit)) return;

                s_Ability = new AbilityDataLike { Ability = ability };
                s_InCheck = true;

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[特殊纠缠] 触发专注检定：{ability.Blueprint?.name}");
                }
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[特殊纠缠] 强制专注前缀异常：" + e.Message);
                s_InCheck = false;
                s_Ability = null;
            }
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            s_InCheck = false;
            s_Ability = null;
        }

        /// <summary>补丁 G 调用：此刻是否应该把 SpellCastingIsDifficult 视作成立。</summary>
        internal static bool ShouldForceDifficult(UnitState state, UnitCondition condition)
        {
            if (!s_InCheck) return false;
            if (condition != UnitCondition.SpellCastingIsDifficult) return false;
            if (state == null || state.Owner == null) return false;

            var unit = state.Owner.Unit;
            if (unit == null || !HasSpecialEntangle(unit)) return false;

            return s_Ability != null && s_Ability.Ability != null && AbilityXHelper.IsEligible(s_Ability.Ability);
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
