using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Components.Base;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的可用性条件（§10.2 ③④）。
    ///
    ///   ③ 能力 X 当前没有可用充能
    ///   ④ 当前没有未到期/未消耗的「强化施法」状态
    ///
    /// 为什么不用 AbilityCasterHasNoFacts：
    ///   它只能检查「有没有某个 Fact」，查不了**资源数量**（条件③）。
    ///   因此用 IAbilityCasterRestriction 自己判定，两者一起表达。
    ///
    /// ① 战斗中由 AbilityCasterInCombat 负责；② 有可用次数由 AbilityResourceLogic 负责。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f653")]
    internal class AbilityZRequirement : BlueprintComponent, IAbilityCasterRestriction
    {
        public bool IsCasterRestrictionPassed(UnitEntityData caster)
        {
            if (caster == null) return false;

            // ③ 能力X 必须无充能
            try
            {
                var res = AbilityX.Resource;
                if (res != null)
                {
                    int amount = caster.Descriptor.Resources.GetResourceAmount(res);
                    if (amount > 0) return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            // ④ 不能还有未到期的强化状态
            if (HasEmpowerBuff(caster)) return false;

            return true;
        }

        private static bool HasEmpowerBuff(UnitEntityData unit)
        {
            try
            {
                var bp = AbilityX.Buff;
                if (bp == null || unit.Buffs == null) return false;

                foreach (var b in unit.Buffs)
                {
                    if (b != null && b.Blueprint == bp) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        public string GetAbilityCasterRestrictionUIText()
        {
            return "需要：战斗中、能力X 已无充能、且没有未到期的强化施法状态";
        }
    }
}
