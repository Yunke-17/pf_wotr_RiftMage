using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components.Base;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的可见性：基础 CL &lt; 7 时**不在能力栏出现**（用户 2026-10-19 要求）。
    ///
    /// ========== 为什么用 IAbilityVisibilityProvider 而不是灰显 ==========
    ///
    ///   IAbilityCasterRestriction → 能力仍在栏位里，只是变灰不可点
    ///   IAbilityVisibilityProvider → 能力**完全不显示**
    ///
    /// 需求明确是「在此之前应该不会出现在能力栏中」，所以必须用后者。
    ///
    /// 引擎里已有的 AbilityShowIfCasterHasFact 只能按「有没有某个 Fact」判断，
    /// 表达不了「基础 CL ≥ 7」这种数值条件，因此自建。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f654")]
    internal class AbilityZVisibility : BlueprintComponent, IAbilityVisibilityProvider
    {
        public bool IsAbilityVisible(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var caster = ability.Caster;
                if (caster == null) return false;

                var unit = caster.Unit;
                if (unit == null) return false;

                return AbilityZ.GetBaseCasterLevel(unit) >= AbilityZ.UnlockCasterLevel;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
