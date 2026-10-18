using HarmonyLib;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 J —— 强化法术突破「以法术描述符判定的魔法免疫」（§8.6 / D15）。
    ///
    /// ========== 边界（用户 2026-10-18 明确） ==========
    ///
    /// 只突破 **SpellDescriptor 类型** 的免疫（免疫恐惧、免疫火焰、免疫心智等），
    /// 不碰 Simple / Specific / SingleTarget / AllSpells 类型的免疫，
    /// 也不影响物理、能量、条件、暴击免疫 —— 等价于宗师之杖的边界。
    ///
    /// ========== 为什么不用原版 IgnoreSpellImmunity 组件 ==========
    ///
    /// Kingmaker.UnitLogic.FactLogic.IgnoreSpellImmunity（TypeId f31ec1e5…）的
    /// OnTurnOn 是：
    ///     base.Owner.Ensure&lt;UnitPartSpellResistance&gt;().IgnoreImmunity(SpellDescriptor)
    /// 它改的是**持有者自己**的免疫部件；
    /// 而「目标是否免疫」判定读的是**目标**的部件
    /// （RuleSpellResistanceCheck.cs:124 → target.Get&lt;UnitPartSpellResistance&gt;().IsImmune(...)）。
    /// 也就是说，把该组件挂在自己身上并不能影响你打出去的法术 —— 语义不对。
    ///
    /// ========== 做法 ==========
    ///
    /// 在 UnitPartSpellResistance.CanApply（3 参数重载，带 MechanicsContext）
    /// 上加 Prefix：若
    ///   ① 该免疫是 SpellDescriptor 类型；且
    ///   ② 这次施法是被本 MOD 强化的那一发（复用 AbilityXScope 的强化标记）
    /// 则直接返回 false（视为「这条免疫不适用」），从而绕过它。
    ///
    /// 为什么用带 context 的重载：
    ///   它能拿到 MechanicsContext → SourceAbilityContext，正好对应我们的强化标记；
    ///   而无 context 的 5 参数重载拿不到施法上下文，无法精确判断。
    ///
    /// 影响范围：只有被强化标记过的那一发；其它法术、其它来源、其它免疫类型完全不变。
    /// </summary>
    [HarmonyPatch(typeof(UnitPartSpellResistance), "CanApply",
        new[] { typeof(UnitPartSpellResistance.SpellImmunity), typeof(MechanicsContext) })]
    internal static class ImmunityBypassPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitPartSpellResistance.SpellImmunity immunity,
            MechanicsContext context, ref bool __result)
        {
            if (!Main.Enabled || immunity == null || context == null) return true;

            try
            {
                // 只突破「法术描述符」类免疫（§8.6 的边界）
                if (immunity.Type != SpellImmunityType.SpellDescriptor) return true;

                // 只对「被能力X 强化的那一发」生效
                if (AbilityXScope.GetEmpowerInfo(context) == null) return true;

                __result = false;   // 这条免疫对本发法术不适用

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力X] 突破描述符免疫：{context.SourceAbilityContext?.Ability?.Blueprint?.name}" +
                                  $"（descriptor={immunity.SpellDescriptor}）");
                }

                AbilityXScope.AcceptanceHook("X.ImmunityBypassed", true);
                return false;   // 跳过原方法
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[能力X] 免疫突破判定异常，已放行原逻辑：" + e.Message);
                return true;
            }
        }
    }
}
