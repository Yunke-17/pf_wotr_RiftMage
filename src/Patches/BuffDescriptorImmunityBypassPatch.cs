using HarmonyLib;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 J2 —— 突破「Buff 描述符免疫」这条独立路径（§8.6 / D15）。
    ///
    /// ========== 为什么需要第二个补丁（实测发现的） ==========
    ///
    /// 游戏里有**两条互不相干**的免疫路径：
    ///
    ///   ① UnitPartSpellResistance.IsImmune
    ///        —— 由单位部件持有的「免疫某某法术」条目（SpellImmunityType.SpellDescriptor 等）
    ///        —— 补丁 J 已覆盖
    ///
    ///   ② BuffDescriptorImmunity
    ///        —— 挂在单位特性上的「免疫带某描述符的 Buff」
    ///        —— 通过 RuleCanApplyBuff 的 evt.Immunity 生效（本文件覆盖）
    ///
    /// 实测表现：能力X 强化后
    ///   - 「诡影杀手」类法术能生效（走 ① ）
    ///   - 「灭顶悲恸」类法术不生效（死亡描述符，走 ② ）
    /// 正是这条差异暴露了 ② 没被覆盖。
    ///
    /// ========== 做法 ==========
    ///
    /// Prefix 打在 BuffDescriptorImmunity.IsImmune(MechanicsContext)（private）：
    ///   若这次施法是被本 MOD 强化的一发 ⇒ 返回 false（本特性认为「不免疫」）。
    ///
    /// 边界不变：只对本 MOD 强化的那一发生效，
    /// 其它法术/其它来源完全不受影响；也不区分具体描述符（与原版行为一致）。
    /// </summary>
    [HarmonyPatch(typeof(BuffDescriptorImmunity), "IsImmune",
        new[] { typeof(MechanicsContext) })]
    internal static class BuffDescriptorImmunityBypassPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(MechanicsContext context, ref bool __result)
        {
            if (!Main.Enabled || context == null) return true;

            try
            {
                if (AbilityXScope.GetEmpowerInfo(context) == null) return true;

                __result = false;   // 本次强化法术不受该描述符免疫限制

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力X] 突破 Buff 描述符免疫：{context.SourceAbilityContext?.Ability?.Blueprint?.name}");
                }

                AbilityXScope.AcceptanceHook("X.ImmunityBypassed", true);
                return false;
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[能力X] Buff 描述符免疫突破异常，已放行原逻辑：" + e.Message);
                return true;
            }
        }
    }
}
