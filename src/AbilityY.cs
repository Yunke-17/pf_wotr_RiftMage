using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y「施法参数交换」（说明书 §9）。
    ///
    /// ========== 规则 ==========
    ///
    ///   净层数 S ∈ [-2, +2]，ΔDC = 2S，ΔCL = -S
    ///
    ///   S=+2 → CL -2, DC +4        S=-2 → CL +2, DC -4
    ///   S=+1 → CL -1, DC +2        S=-1 → CL +1, DC -2
    ///
    /// 模式 A：CL -1 / DC +2（S 加 1）
    /// 模式 B：CL +1 / DC -2（S 减 1）
    /// 取消  ：S 归 0（消耗 1 次使用次数，§9.4）
    ///
    /// ========== S 存在哪 ==========
    ///
    /// 不用 Buff.Rank：Rank 被 BlueprintBuff.Ranks 夹住（Buff.SetRank，:673），
    /// 且不能为负。改为**运行期状态**：
    ///   - ConditionalWeakTable<UnitEntityData, State> 保存 S（不持久化）
    ///   - 施法时按 S 施加 AddBonusCasterLevel(-S) / AddBonusDC(2S)
    ///
    /// 为什么不持久化：§9.2「修正持续至当前战斗结束」，跨存档没有意义；
    /// 且读档后战斗已结束，保留反而会带来幽灵修正。
    ///
    /// ========== 为什么修正要每次施法重算 ==========
    ///
    /// RuleCalculateAbilityParams.m_BonusCasterLevel 是**规则实例字段**
    /// （RuleCalculateAbilityParams.cs:301），每条规则都新建 ⇒ 修正天然只作用于当前这一发。
    /// 因此必须在每次 RuleCalculateAbilityParams 里按当前 S 重新施加，
    /// 这正好满足 §9.2「持续至战斗结束」。
    /// </summary>
    internal static class AbilityY
    {
        internal const string GuidFeature = "b1000000000000000000000000000030";
        internal const string GuidResource = "b1000000000000000000000000000031";
        internal const string GuidParent = "b1000000000000000000000000000032";   // 变体父能力
        internal const string GuidModeA = "b1000000000000000000000000000033";
        internal const string GuidModeB = "b1000000000000000000000000000034";
        internal const string GuidCancel = "b1000000000000000000000000000035";

        /// <summary>净层数上限（§9.3）。</summary>
        internal const int MaxS = 2;

        private const string KeyBase = "AbilityY";

        // ------------------------------------------------------------------
        // 运行期状态
        // ------------------------------------------------------------------

        internal sealed class State
        {
            /// <summary>净层数 S ∈ [-2, +2]。</summary>
            public int S;
        }

        private static readonly ConditionalWeakTable<UnitEntityData, State> s_States =
            new ConditionalWeakTable<UnitEntityData, State>();

        internal static int GetS(UnitEntityData unit)
        {
            if (unit == null) return 0;
            State st;
            return s_States.TryGetValue(unit, out st) ? st.S : 0;
        }

        internal static void SetS(UnitEntityData unit, int s)
        {
            if (unit == null) return;
            if (s < -MaxS) s = -MaxS;
            if (s > MaxS) s = MaxS;

            s_States.Remove(unit);
            s_States.Add(unit, new State { S = s });

            Main.Log?.Log($"[能力Y] 净层数 S = {s}（ΔCL = {-s}，ΔDC = {2 * s}）");
            AbilityXScope.AcceptanceHook("X.YS", s);
        }

        internal static void ResetAll()
        {
            // CWT 不支持遍历清除，用显式登记的单位列表
            foreach (var u in s_Tracked)
            {
                if (u == null) continue;
                s_States.Remove(u);
            }
            s_Tracked.Clear();
        }

        private static readonly List<UnitEntityData> s_Tracked = new List<UnitEntityData>();

        internal static void Track(UnitEntityData unit)
        {
            if (unit != null && !s_Tracked.Contains(unit)) s_Tracked.Add(unit);
        }

        // ------------------------------------------------------------------
        // 修正施加：订阅 RuleCalculateAbilityParams
        // ------------------------------------------------------------------

        internal class Modifier : UnitFactComponentDelegate,
            IInitiatorRulebookHandler<RuleCalculateAbilityParams>
        {
            public void OnEventAboutToTrigger(RuleCalculateAbilityParams evt)
            {
                if (evt == null) return;

                var unit = Owner;
                int s = GetS(unit);
                if (s == 0) return;

                // 只作用于法师法术书的法术（用户 2026-10-18 确认，与 Q1 一致）
                var data = evt.AbilityData;
                if (data == null || !AbilityXHelper.IsEligible(data)) return;

                evt.AddBonusCasterLevel(-s);
                evt.AddBonusDC(2 * s);

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力Y] 施加修正 S={s}：{data.Blueprint?.name}（CL {-s:+0;-0}，DC {2 * s:+0;-0}）");
                }

                AbilityXScope.AcceptanceHook("X.YApplied", true);
            }

            public void OnEventDidTrigger(RuleCalculateAbilityParams evt)
            {
            }
        }
    }
}
