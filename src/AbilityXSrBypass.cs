using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X 的「无视法术抗力」—— 用引擎自带的静默通过钩子。
    ///
    /// ========== 原版是怎么做的（已逐个核实） ==========
    ///
    /// 全游戏只有**宗师之杖**会自动通过 SR：
    ///   MetamagicRodMasterpieceBuff.jbp → MetamagicRodMechanics.ForceIgnoreSpellResist = True
    ///   （已用 blueprints 全库扫描确认：只有这 1 个蓝图带这个字段）
    /// 它在 RuleCastSpell.Did 里置 evt.Context.ForceIgnoreSpellResist = true。
    ///
    /// 但这条路的副作用是：RuleSpellResistanceCheck.OnTrigger **照样掷骰**
    /// （因为它只检查 IgnoreSpellResistance，不检查 ForceIgnoreSpellResist），
    /// 于是战斗日志会打出一条自相矛盾的记录：
    ///     检定: 5（投骰 3 + 调整值 2） / 法术抗力(SR): 20 / 结果: 成功
    /// 5 < 20 却成功 —— 因为成功来自标志位而非骰值。
    ///
    /// ⇒ **原版自己也没处理这个显示问题**，宗师之杖一样会出现这种日志。
    ///   所以「照抄原版」在这里不是最优解。
    ///
    /// ========== 引擎给的更好钩子 ==========
    ///
    /// RuleSpellResistanceCheck 有一个可写属性 IgnoreSpellResistance
    /// （RuleSpellResistanceCheck.cs:46），它在 OnTrigger **开头**就被检查（:125）：
    ///
    ///     if (!this.TargetIsImmune && !this.IgnoreSpellResistance)
    ///     {
    ///         ... 计算 SpellResistance / SpellPenetration / 掷 D20 ...
    ///     }
    ///
    /// 置 true 后：
    ///   - 连骰都不掷，SpellResistance / SpellPenetration / Roll 全部保持默认 0
    ///   - ⇒ HasSpellResistance = false ⇒ HasResistanceRoll = false
    ///   - ⇒ 日志线程 RulebookSpellResistanceCheckLogThread 第一行
    ///        if (!rule.HasResistanceRoll && !rule.TargetIsImmune) return;
    ///     直接提前返回 ⇒ **根本不产生日志**，不需要任何补丁
    ///   - IsSpellResisted 里也有 !IgnoreSpellResistance 项（:66）⇒ 判定为未抵抗
    ///
    /// 属性在 Assembly-CSharp 里**从未被赋值过**，是留给 mod 的钩子。
    /// 用它比补丁干净得多：不改任何原版方法、不掷无用的骰、不产生日志。
    ///
    /// ========== 为什么挂在「特性」上而不是「强化状态 Buff」上 ==========
    ///
    /// SR 检定发生在 RuleCastSpell.Did **之后**的帧里
    /// （AbilityExecutionProcess.cs:402，协程 Tick）。
    /// 而强化状态在 Did 里就被移除了 —— 如果订阅挂在 Buff 上，
    /// 移除后订阅即失效，等 SR 检定来临时根本收不到通知。
    ///
    /// 所以挂在本变体 1 级的「强化施法」特性上（常驻）。
    /// 它监听该单位参与的所有 SR 检定，再用 AbilityXScope 标记的
    /// 「这次施法是不是我们强化的」精确过滤，不影响其他任何法术。
    /// </summary>
    internal class AbilityXSrBypass : UnitFactComponentDelegate,
        IInitiatorRulebookHandler<RuleSpellResistanceCheck>
    {
        public void OnEventAboutToTrigger(RuleSpellResistanceCheck evt)
        {
            if (evt == null) return;

            // 只处理「本 MOD 强化过的那一发」
            if (!AbilityXScope.IsSrBypassedByUs(evt)) return;

            // 关键：走引擎的静默通过钩子（不掷骰、不进日志）
            evt.IgnoreSpellResistance = true;

            // 验收埋点：证明「静默通过」这条路真的走到了（不需要日志补丁兜底）
            AbilityXScope.AcceptanceHook("X.SRQuiet", true);

            if (Main.DebugLog)
            {
                Main.Log?.Log($"[能力X] SR 静默通过：{evt.Ability?.name}（IgnoreSpellResistance=true，不掷骰）");
            }
        }

        public void OnEventDidTrigger(RuleSpellResistanceCheck evt)
        {
        }
    }
}
