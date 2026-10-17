using HarmonyLib;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 D —— 瞬发超魔按基础 CL 减费（方案 D）。
    ///
    /// 为什么补在这里：
    /// MetamagicHelper.DefaultCost(this Metamagic) 是所有超魔成本的唯一源头
    /// （MetamagicHelper.cs:17-54）。RuleApplyMetamagic 的累加、RuleCollectMetamagic
    /// 的越界判断、以及两处 UI 显示都直接调它。
    /// 在源头把值改掉，下游所有消费点自动一致——包括那些用
    /// "SpellLevel - SpellLevelCost" 反算基础环位的地方（MetamagicBuilder.cs:38、
    /// Spellbook.cs:1108/1733、SpellSlot.cs:110）。因为 SpellLevelCost
    /// 从此存的就是"真实最终值"，反算天然正确，存档也安全。
    ///
    /// 签名说明：DefaultCost 是静态扩展方法，IL 层面第一个参数就是 Metamagic，
    /// 因此 Harmony 用 `Metamagic __0` 接收。
    /// </summary>
    [HarmonyPatch(typeof(MetamagicHelper), nameof(MetamagicHelper.DefaultCost))]
    internal static class MetamagicDefaultCostPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Metamagic __0, ref int __result)
        {
            if (!Main.Enabled) return;

            // 只处理瞬发；其它超魔保持原样
            if (__0 != Metamagic.Quicken) return;

            // 原版瞬发成本固定 4（MetamagicHelper.cs:31-32）
            if (__result != 4) return;

            var caster = MetamagicCost.ResolveCurrentCaster(null);
            if (caster == null) return;

            // 优先用"当前法术书"的 CL；拿不到才回落到角色总等级
            var spellbook = MetamagicCost.ResolveCurrentSpellbook();
            int cl = MetamagicCost.GetBaseCasterLevel(spellbook, caster);

            int reduction = MetamagicCost.GetReductionFor(caster, cl);

            if (Main.DebugLog)
            {
                Main.Log?.Log($"[瞬发] caster={caster.CharacterName}, " +
                              $"isArchetype={Main.IsWizardArchetype(caster)}, " +
                              $"book={(spellbook != null ? "有" : "无")}, CL={cl}, reduction={reduction}");
            }

            if (reduction <= 0) return;

            // Q2：允许与"偏好超魔"叠加，但不低于 0。
            // 偏好超魔对瞬发的 -1 由下游 RuleApplyMetamagic / UI 各自处理，
            // 这里只负责把 4 降到 4-reduction。
            int reduced = 4 - reduction;
            __result = reduced < 0 ? 0 : reduced;
        }
    }
}
