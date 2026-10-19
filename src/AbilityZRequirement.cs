using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Components.Base;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的可用性条件（用户 2026-10-19 修正版）。
    ///
    ///   唯一条件：**能力 X 的次数不为 1**（即必须为 0 才能用）
    ///
    /// 说明：
    ///   - 原需求「仅战斗中可用」已被用户取消，现在非战斗也可用，
    ///     因此不再挂 AbilityCasterInCombat。
    ///   - 原需求「无未到期强化状态」也已按新规格去掉，只保留次数判断。
    ///   - 「基础 CL ≥ 7」由 AbilityZVisibility 控制**是否显示**，不在这里判断。
    ///
    /// 为什么不用 AbilityCasterHasNoFacts：
    ///   它只能检查「有没有某个 Fact」，查不了**资源数量**。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f653")]
    internal class AbilityZRequirement : BlueprintComponent, IAbilityCasterRestriction
    {
        public bool IsCasterRestrictionPassed(UnitEntityData caster)
        {
            if (caster == null) return false;

            try
            {
                var res = AbilityX.Resource;
                if (res == null) return false;

                int amount = caster.Descriptor.Resources.GetResourceAmount(res);

                // 只有「次数为 1」时不能用；为 0 时可用（这正是透支的意义）
                return amount != 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string GetAbilityCasterRestrictionUIText()
        {
            return "「强化施法」尚有 1 次充能，无需透支";
        }
    }
}
