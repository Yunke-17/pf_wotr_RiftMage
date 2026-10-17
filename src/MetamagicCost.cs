using System;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using UniRx;

namespace WizardArchetype
{
    /// <summary>
    /// 瞬发减费的核心计算与"当前施法者"的上下文获取。
    ///
    /// 设计要点（方案 D）：
    /// 游戏里超魔成本最终都来自 MetamagicHelper.DefaultCost(this Metamagic)，
    /// 它是一个静态扩展方法，签名里没有施法者。所以在这个位置改成本，
    /// 可以让下面所有消费点同时正确（累加、反算、越界判断、UI 显示）。
    ///
    /// 代价是这个函数拿不到"谁在施法"。本类负责从调用栈/事件栈里把施法者找回来。
    /// </summary>
    internal static class MetamagicCost
    {
        /// <summary>瞬发被减到的目标基础值 +4 减去多少环。返回 0 表示不减免。</summary>
        internal static int GetQuickenReduction(int baseCasterLevel)
        {
            if (baseCasterLevel >= 17) return 3;
            if (baseCasterLevel >= 15) return 2;
            if (baseCasterLevel >= 13) return 1;
            return 0;
        }

        /// <summary>
        /// 尝试确定"当前正在计算超魔成本的施法者"。
        ///
        /// 按可靠性从高到低依次尝试：
        /// 1. RuleApplyMetamagic / RuleCollectMetamagic —— 规则结算路径，带 Initiator
        /// 2. MetamagicBuilder —— 构建路径，带 Spellbook.Owner
        /// 3. UI 选择器 —— 带 m_Unit
        ///
        /// 拿不到就返回 null，此时不减免（保持原版行为）。
        /// </summary>
        internal static UnitEntityData ResolveCurrentCaster(Spellbook fallbackSpellbook)
        {
            // 1. 规则事件栈：取最靠近栈顶的超魔相关事件
            var ctx = Rulebook.CurrentContext;
            if (ctx != null)
            {
                var apply = ctx.LastEvent<RuleApplyMetamagic>();
                if (apply?.Initiator != null) return apply.Initiator;

                var collect = ctx.LastEvent<RuleCollectMetamagic>();
                if (collect?.Spellbook?.Owner?.Unit != null) return collect.Spellbook.Owner.Unit;
            }

            // 2. 调用方直接给的 spellbook（UI 侧有 m_Spellbook，可绕道 owner）
            if (fallbackSpellbook?.Owner?.Unit != null) return fallbackSpellbook.Owner.Unit;

            // 3. UI 法术书选择器：直接读它的 m_Unit（由补丁传入）
            if (s_CurrentSelectorUnit != null) return s_CurrentSelectorUnit;

            return null;
        }

        /// <summary>
        /// UI 侧临时上下文：SpellbookMetamagicSelectorVM.GetCost 内部调用 DefaultCost 时，
        /// 由另一个补丁在这里临时写入正在查看的角色，调用结束后清空。
        /// </summary>
        [ThreadStatic]
        internal static UnitEntityData s_CurrentSelectorUnit;

        /// <summary>
        /// 判断施法者是否享用本 MOD 的瞬发减费，并给出应减的环数。
        /// </summary>
        internal static int GetReductionFor(UnitEntityData caster, int baseCasterLevel)
        {
            if (caster == null) return 0;
            if (!Main.IsWizardArchetype(caster)) return 0;
            return GetQuickenReduction(baseCasterLevel);
        }

        /// <summary>
        /// 判断某个超魔是否是施法者的"偏好超魔"（复刻 RuleApplyMetamagic.cs:38）。
        /// </summary>
        internal static bool IsFavoriteMetamagic(Metamagic metamagic, UnitEntityData caster)
        {
            if (caster == null) return false;
            var f = caster.State?.Features;
            if (f == null) return false;

            switch (metamagic)
            {
                case Metamagic.Empower: return f.FavoriteMetamagicEmpower;
                case Metamagic.Maximize: return f.FavoriteMetamagicMaximize;
                case Metamagic.Quicken: return f.FavoriteMetamagicQuicken;
                case Metamagic.Reach: return f.FavoriteMetamagicReach;
                case Metamagic.Extend: return f.FavoriteMetamagicExtend;
                case Metamagic.Selective: return f.FavoriteMetamagicSelective;
                case Metamagic.Bolstered: return f.FavoriteMetamagicBolstered;
                case Metamagic.Persistent: return f.FavoriteMetamagicPersistent;
                case Metamagic.Piercing: return f.FavoriteMetamagicPiercing;
                case Metamagic.Intensified: return f.FavoriteMetamagicIntensified;
                default: return false;
            }
        }

        /// <summary>
        /// 单个超魔的最终成本（含偏好减免与 0 下限），与 RuleApplyMetamagic.cs:37-50 同构。
        /// </summary>
        internal static int GetSingleCost(Metamagic metamagic, UnitEntityData caster)
        {
            int cost = metamagic.DefaultCost();
            if (IsFavoriteMetamagic(metamagic, caster)) cost--;
            if (metamagic != Metamagic.CompletelyNormal) cost = Math.Max(0, cost);
            return cost;
        }

        /// <summary>
        /// 基础 CL：优先取"当前正在被计算的这个法术书"的 CL（Spellbook.CasterLevel，
        /// Spellbook.cs:66-77）。神话合书只增加被合入的那个法术书，天然满足定义。
        /// 拿不到 spellbook 时才回落到角色总等级。
        /// </summary>
        internal static int GetBaseCasterLevel(Spellbook spellbook, UnitEntityData caster)
        {
            if (spellbook != null) return spellbook.CasterLevel;
            return caster?.Descriptor?.Progression?.CharacterLevel ?? 1;
        }

        /// <summary>
        /// 从事件栈/UI 上下文里同时取回"施法者"和"它正在使用的法术书"。
        /// DefaultCost 是静态方法拿不到 spellbook，只能这样间接还原。
        /// </summary>
        internal static Spellbook ResolveCurrentSpellbook()
        {
            var ctx = Rulebook.CurrentContext;
            if (ctx != null)
            {
                var apply = ctx.LastEvent<RuleApplyMetamagic>();
                if (apply?.Spellbook != null) return apply.Spellbook;

                var collect = ctx.LastEvent<RuleCollectMetamagic>();
                if (collect?.Spellbook != null) return collect.Spellbook;
            }

            return s_CurrentSelectorSpellbook;
        }

        [ThreadStatic]
        internal static Spellbook s_CurrentSelectorSpellbook;
    }
}
