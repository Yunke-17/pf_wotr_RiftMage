using HarmonyLib;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.FactLogic;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 E —— 超魔越界判断修复（全局生效，默认开启）。
    ///
    /// 原版缺陷（RuleCollectMetamagic.cs:47）：
    ///     if (m_SpellLevel + component.Metamagic.DefaultCost() > 10) return;
    ///
    /// 它用"单个超魔的孤立成本"预判整套超魔的结果，且阈值硬编码 10，
    /// 不区分角色实际能放到几环。典型表现：9 环法术挂顽强（原始 +2），
    /// 即使有偏好超魔（实际 +1），9+2>10 仍被拒绝。
    ///
    /// 为什么要用 Postfix 补回而不是 Prefix 拦截：
    /// 原判断在 AddMetamagic 内部中途 return，Prefix 看不到；而 Prefix 返回 false
    /// 等于整个方法不执行，会把 KnownMetamagics.Add 等副作用一并跳过。
    /// 因此：先让原方法照常跑，再在 Postfix 里把"被误拒"的项补进可选列表。
    /// 这样在无减免时与原版完全等价，只有被误拒的才会被补回。
    /// </summary>
    [HarmonyPatch(typeof(RuleCollectMetamagic), nameof(RuleCollectMetamagic.AddMetamagic))]
    internal static class MetamagicLimitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(RuleCollectMetamagic __instance, Feature metamagicFeature, int ___m_SpellLevel)
        {
            if (!Main.Enabled || !Main.FixMetamagicLimit) return;
            if (__instance == null || metamagicFeature == null) return;

            var component = metamagicFeature.GetComponent<AddMetamagicFeat>();
            if (component == null) return;

            // 原方法已经放进去了 → 什么都不用做
            if (__instance.SpellMetamagics.Contains(metamagicFeature)) return;

            // 复刻原版前置守卫：基础环非法时不应补回
            if (___m_SpellLevel < 0 || ___m_SpellLevel >= 10) return;

            var spellbook = __instance.Spellbook;
            var spell = __instance.Spell;
            if (spellbook == null || spell == null) return;

            var metamagic = component.Metamagic;

            // 法术本身不支持该超魔 → 不该补回
            if ((spell.AvailableMetamagic & metamagic) != metamagic) return;

            var caster = spellbook.Owner?.Unit;
            int cost = metamagic.DefaultCost();
            if (caster != null && MetamagicCost.IsFavoriteMetamagic(metamagic, caster))
            {
                cost--;
            }
            if (metamagic != Metamagic.CompletelyNormal)
            {
                cost = System.Math.Max(0, cost);
            }

            // 动态上限，与 UI 层 AbilityDataVM.cs:92 同构：
            // 普通角色上限 10；巫妖等 MaxSpellLevel==10 的法术书上限 10
            int max = spellbook.MaxSpellLevel;
            int limit = (max == 9) ? max + 1 : max;

            if (___m_SpellLevel + cost <= limit)
            {
                __instance.SpellMetamagics.Add(metamagicFeature);
            }
        }
    }
}
