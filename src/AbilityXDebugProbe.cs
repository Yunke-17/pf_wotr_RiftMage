using System;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 临时诊断组件：只打日志，不产生任何效果。
    /// 用于确认「施法后移除」这条链路到底断在哪一环。
    /// 第 0 步验证通过后删除。
    /// </summary>
    internal class AbilityXDebugProbe : UnitBuffComponentDelegate,
        IInitiatorRulebookHandler<RuleCastSpell>,
        IApplyAbilityEffectHandler
    {
        public void OnEventAboutToTrigger(RuleCastSpell evt)
        {
            var sp = evt.Spell;
            Main.Log?.Log($"[探针][RuleCastSpell.About] spell={sp?.Blueprint?.name}, " +
                          $"type={sp?.Blueprint?.Type}, spellLevel={sp?.SpellLevel}, " +
                          $"book={(sp?.Spellbook != null ? sp.Spellbook.Blueprint?.name : "null")}, " +
                          $"success={evt.Success}");
        }

        public void OnEventDidTrigger(RuleCastSpell evt)
        {
            var sp = evt.Spell;
            Main.Log?.Log($"[探针][RuleCastSpell.Did] spell={sp?.Blueprint?.name}, " +
                          $"success={evt.Success}, umdFailed={evt.IsUMDFailed}");
        }

        public void OnAbilityEffectApplied(AbilityExecutionContext context)
        {
            var ab = context?.Ability;
            var caster = context?.MaybeCaster;
            Main.Log?.Log($"[探针][AbilityEffectApplied] ability={ab?.Blueprint?.name}, " +
                          $"type={ab?.Blueprint?.Type}, spellLevel={ab?.SpellLevel}, " +
                          $"caster={caster?.CharacterName}, owner={Owner?.CharacterName}");
        }

        public void OnAbilityEffectAppliedToTarget(AbilityExecutionContext context, TargetWrapper target)
        {
        }

        public void OnTryToApplyAbilityEffect(AbilityExecutionContext context, TargetWrapper target)
        {
        }
    }
}
