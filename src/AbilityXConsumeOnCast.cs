using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X：合格法术施放后移除强化状态。
    ///
    /// 为什么不照抄法术大师的 AddAbilityUseTrigger：
    ///   它的 CheckAbilityType 只判断「是不是法术」，无法表达
    ///   「法术书归属职业 == 法师」（Q1）。白名单 m_Spellbooks 也不行 ——
    ///   会漏掉萨西隆等变体法术书，正是 06 D5c 明确禁止的做法。
    ///   所以触发时机借用规则事件，判定用 AbilityXHelper.IsEligible。
    ///
    /// 挂在「强化施法」Buff 上：
    ///   Buff 存在 = 强化窗口开启；放出一发合格法术 = 窗口关闭（移除自己）。
    ///
    /// 时机选择 RuleCastSpell.OnEventDidTrigger 而非 AboutToTrigger：
    ///   原版 MetamagicOnNextSpell 在 About 阶段就 Remove，但那时施法成败未定
    ///   （UMD/失败率/专注检定都在 RuleCastSpell.OnTrigger 里）。
    ///   需求是「施法成功后消耗」，故必须等 Did + Success。
    /// </summary>
    internal class AbilityXConsumeOnCast : UnitBuffComponentDelegate,
        IInitiatorRulebookHandler<RuleCastSpell>
    {
        public void OnEventAboutToTrigger(RuleCastSpell evt)
        {
        }

        public void OnEventDidTrigger(RuleCastSpell evt)
        {
            if (evt == null || evt.Spell == null) return;
            if (!evt.Success) return;              // 施法失败不消耗
            if (evt.IsUMDFailed) return;           // UMD 失败不消耗

            if (!AbilityXHelper.IsEligible(evt.Spell)) return;

            Main.Log?.Log($"[能力X] 合格法术已释放，移除强化状态：{evt.Spell.Blueprint?.name}");
            Buff?.Remove();
        }
    }
}
