using HarmonyLib;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 I —— 特殊纠缠下「体质低于 3 无法施法」（§6.4 / C15）。
    ///
    /// ========== 为什么不用原版 ForbidSpellCasting ==========
    ///
    /// Kingmaker.UnitLogic.FactLogic.ForbidSpellCasting（TypeId 355f1ed2…）是特性/状态级的：
    /// OnTurnOn 时一次性 AddCondition(SpellcastingForbidden)，没有「按属性阈值启停」的能力。
    /// 直接挂它 ⇒ 只要还带着特殊纠缠就永久禁施法，与「CON 掉到 3 以下才禁」不符。
    ///
    /// 而引擎里**没有**「属性变化」的订阅接口（已查 PubSubSystem，只有 IUIUnitStatsRefresh
    /// 这类 UI 刷新），所以「监听属性变化再切换条件」这条路走不通。
    ///
    /// ========== 做法：在施法可用性判定处直接看当前 CON ==========
    ///
    /// 门控点是 AbilityData.CanBeCastByCaster（private **属性**，AbilityData.cs:1110）：
    ///     if (Caster.State.HasCondition(SpellcastingForbidden) && ...) return false;
    ///
    /// 我们在 getter 的 Postfix 里补一条：若「该单位有特殊纠缠」且「当前 CON < 3」
    /// 且「这一发是法师法术书的法术」→ 判定为不可施放（__result = false）。
    ///
    /// 好处：
    ///   - 不需要长期持有 SpellcastingForbidden 条件，不会误伤其它来源；
    ///   - CON 回升到 3 以上立刻恢复（每次判定都重新算，无需事件）；
    ///   - 只影响我们自己的特殊纠缠 + 法师法术，原版狂怒/变形等完全不受影响。
    /// </summary>
    [HarmonyPatch(typeof(AbilityData), "get_CanBeCastByCaster")]
    internal static class LowConForbidPatch
    {
        private const int Threshold = 3;

        [HarmonyPostfix]
        private static void Postfix(AbilityData __instance, ref bool __result)
        {
            // 原判定已经说「不能放」时不用管；只处理「原本能放但 CON 太低」
            if (!__result || !Main.Enabled || __instance == null) return;

            try
            {
                var caster = __instance.Caster;
                if (caster == null) return;

                // 只对「带着我们特殊纠缠」的单位生效
                if (!ForceConcentrationPatch.HasSpecialEntangle(caster)) return;

                // 只限制法师法术（与 Q1 判据一致）
                if (!AbilityXHelper.IsEligible(__instance)) return;

                if (caster.Stats.Constitution >= Threshold) return;

                __result = false;

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[特殊纠缠] 体质 {caster.Stats.Constitution} < {Threshold}，" +
                                  $"禁止施放：{__instance.Blueprint?.name}");
                }

                AbilityXScope.AcceptanceHook("X.LowConForbid", true);
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[特殊纠缠] CON 阈值判定异常：" + e.Message);
            }
        }
    }
}
