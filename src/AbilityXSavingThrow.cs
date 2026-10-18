using System;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X：敌方对被强化法术的豁免检定「掷 n 颗 D20 取最低」。
    ///
    /// ========== 档位（说明书 §8.2 / §8.5） ==========
    ///
    ///     CL 1–4  → 共 2 颗取最低（追加 1 颗）
    ///     CL 5–9  → 共 3 颗取最低（追加 2 颗）
    ///     CL 10+  → 共 4 颗取最低（追加 3 颗）
    ///
    /// ========== 为什么用 IWasRoll 而不是 ModifyD20 ==========
    ///
    /// ModifyD20 的 Rule=All 会波及目标身上的**所有** D20（含他自己的攻击），
    /// 且无法收窄到「特定一次施法」。IWasRoll 直接给出产生这次掷骰的规则对象
    /// （RuleSavingThrow），再配合 Reason.Context 就能精确到「是不是我被强化的那一发」。
    /// 原版灵使道途「卓越魔法」AzataFavorableMagic 就是这种写法。
    ///
    /// ========== 时机：为什么能追加 ==========
    ///
    /// RuleSavingThrow.RollD20()（RuleSavingThrow.cs:310-317）：
    ///     Rulebook.Trigger&lt;RuleRollD20&gt;(base.D20);      ← 第一次掷骰已经完成
    ///     EventBus.RaiseEvent&lt;IWasRoll&gt;(... x.WasRoll(this, base.D20) ...);
    ///
    /// 所以 WasRoll 里拿到的是「已掷完」的 D20。
    /// RuleRollDice.Reroll(fact, takeBest)（RuleRollDice.cs:104-108）
    ///   = AddReroll(1, takeBest, fact) + 立刻 Reroll()，
    /// 即「再掷一颗并立即与当前结果合并」。
    ///
    /// AddReroll 对 takeBest=false 会把 m_RerollAmount 减 1（变负），
    /// Reroll() 里 `m_RerollAmount > 0 ? Max : Min` ⇒ 走 Min。
    /// 因此要「共 n 颗」，需要循环调用 n-1 次（原版卓越魔法只要 2 颗，所以只调 1 次）。
    ///
    /// ========== 为什么挂特性上 ==========
    ///
    /// 与 SR / 加骰面同理：豁免检定发生在施法结算期间（RuleCastSpell.Did 之后），
    /// 那时强化状态 Buff 已被移除，挂 Buff 上的订阅收不到通知。
    ///
    /// ========== 持续效果的边界（§8.5 最后一句） ==========
    ///
    /// 需求：「只适用于被强化法术直接引发的豁免；后续回合独立触发的持续效果豁免
    /// 不自动继承」。
    /// 实现：记录施法时的「战斗轮次」，只有当次轮次内的豁免才追加骰。
    /// 一轮 6 秒，用 GameTime / 6 估算；拿不到 Game 实例时退化为「不限制」。
    /// </summary>
    internal class AbilityXSavingThrow : UnitFactComponentDelegate, IWasRoll
    {
        /// <summary>最多 4 颗（CL 10+）。</summary>
        private const int MaxDice = 4;

        /// <summary>Pathfinder 一轮 = 6 秒。</summary>
        private const double SecondsPerRound = 6.0;

        public void WasRoll(RulebookEvent ruleEvent, RuleRollD20 ruleRoll)
        {
            if (ruleEvent == null || ruleRoll == null) return;

            // 只处理豁免检定；攻击骰、先攻、专注、SR 都不碰
            var save = ruleEvent as RuleSavingThrow;
            if (save == null) return;

            // 只处理「本次被强化法术引发的豁免」
            var info = AbilityXScope.GetEmpowerInfo(save.Reason != null ? save.Reason.Context : null);
            if (info == null) return;

            // 跨轮次的持续效果豁免不继承（§8.5）
            if (!IsSameRound(info)) return;

            int want = GetSaveDiceCount(info.CasterLevel);
            if (want <= 1) return;

            int have = ruleRoll.RollHistory != null ? ruleRoll.RollHistory.Count : 1;
            int need = want - have;
            if (need <= 0) return;   // 已经是目标颗数（幂等，避免重复追加）

            // 结果被硬性覆盖（自动成功/失败）时追加没有意义。
            // RuleRollDice 暴露的只有 protected 的 ResultOverride 字段，
            // 这里改用可读的 Result 与「是否掷过」间接判断不划算，
            // 因此不做这项判断 —— 追加骰对已覆盖的结果没有副作用。

            // 记录追加前的条目数：追加操作会往 Rerolls 里各加一条（只用于战斗日志显示）
            int rerollsBefore = ruleRoll.Rerolls != null ? ruleRoll.Rerolls.Count : 0;

            for (int i = 0; i < need; i++)
            {
                // takeBest: false ⇒ 取最低
                ruleRoll.Reroll(Fact, false);
            }

            // ★ 合并日志条目，避免战斗文本出现「强化施法强化施法…」
            //
            // 原因：LogHelper.GetRollDescription（Kingmaker/TextTools/LogHelper.cs:66-78）
            //   foreach (var rerollData in ruleRollDice.Rerolls)
            //       text3 += blueprintUnitFact.Name;      ← 无分隔符拼接
            //   ... Append(" [").Append(text3).Append("]")
            // 我们追加 n-1 颗就产生 n-1 条 RerollData，日志于是显示成
            //   [强化施法强化施法]（n=3 时）
            //
            // Rerolls 只被日志使用（全程序集搜索确认），合并不会影响结算：
            // m_RerollAmount 与 RollHistory 都已在 Reroll() 里更新完毕。
            // 保留第一条代表本次强化，删掉其余重复条目。
            var rerolls = ruleRoll.Rerolls;
            if (rerolls != null && rerolls.Count > rerollsBefore + 1)
            {
                rerolls.RemoveRange(rerollsBefore + 1, rerolls.Count - (rerollsBefore + 1));
            }

            // 验收埋点
            var history = ruleRoll.RollHistory ?? new System.Collections.Generic.List<int>();
            string rolls = "[" + string.Join(", ", history) + "]";
            int min = int.MaxValue;
            foreach (var v in history) if (v < min) min = v;

            Main.Log?.Log($"[能力X] 豁免多骰取低：{info.SpellName}，CL {info.CasterLevel}，共 {want} 颗 {rolls} → {ruleRoll.Result}");

            AbilityXScope.AcceptanceHook("X.SaveDice", want);
            AbilityXScope.AcceptanceHook("X.SaveRolls", rolls);
            AbilityXScope.AcceptanceHook("X.SaveIsMin", ruleRoll.Result == min);
        }

        /// <summary>CL 1–4 → 2 颗；5–9 → 3 颗；10+ → 4 颗。</summary>
        private static int GetSaveDiceCount(int casterLevel)
        {
            if (casterLevel >= 10) return 4;
            if (casterLevel >= 5) return 3;
            if (casterLevel >= 1) return 2;
            return 1;
        }

        /// <summary>
        /// 是否仍在施法的那一轮里。拿不到游戏时间时返回 true（宁可生效也不误杀）。
        /// </summary>
        private static bool IsSameRound(AbilityXScope.EmpowerInfo info)
        {
            if (info.RoundIndex < 0) return true;

            int now = CurrentRound();
            if (now < 0) return true;

            return now == info.RoundIndex;
        }

        private static int CurrentRound()
        {
            try
            {
                var game = Game.Instance;
                if (game == null || game.Player == null) return -1;
                return (int)(game.Player.GameTime.TotalSeconds / SecondsPerRound);
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
