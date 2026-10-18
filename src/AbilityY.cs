using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y「施法参数交换」（说明书 §9 + 用户 2026-10-19 确认的修正）。
    ///
    /// ========== 规则（最终版） ==========
    ///
    ///   三个按钮：
    ///     +1 层：CL +1，DC -2      （迅捷动作，仅战斗中）
    ///     -1 层：CL -1，DC +2      （迅捷动作，仅战斗中）
    ///     取消  ：层数归 0         （**自由动作**，消耗 1 次）
    ///
    ///   层数（净层数 N）：
    ///     N ∈ [-2, +2]，ΔCL = N，ΔDC = -2N
    ///     两种方向**互斥**：已有正层数时不能加负层，反之亦然
    ///     （实现为「只能往同方向继续叠加，或先取消」）
    ///     到 ±2 后对应按钮灰显
    ///
    ///   持续：至战斗结束（离开战斗归零）
    ///
    ///   次数：ceil(IntMod / 2) + ceil(法师等级 / 2)，分别取整后相加
    ///     每次休息恢复满
    ///
    /// ========== 为什么不用 Buff 存层数 ==========
    ///
    /// 引擎的 Buff.Rank 被 BlueprintBuff.Ranks 夹住（Buff.SetRank，:673）且不能为负，
    /// 而 N 需要表示 -2。改用运行期状态（ConditionalWeakTable），
    /// 并配合 Buff 仅作为「视觉标记」（可选）。
    ///
    /// ========== 为什么修正要每次施法重算 ==========
    ///
    /// RuleCalculateAbilityParams.m_BonusCasterLevel 是规则实例字段（:301），
    /// 每条规则都新建 ⇒ 修正天然只作用于当前这一发。
    /// 所以订阅 RuleCalculateAbilityParams 按当前 N 施加即可满足「持续至战斗结束」。
    /// </summary>
    internal static class AbilityY
    {
        internal const string GuidFeature = "b1000000000000000000000000000030";
        internal const string GuidResource = "b1000000000000000000000000000031";
        internal const string GuidParent = "b1000000000000000000000000000032";
        internal const string GuidUp = "b1000000000000000000000000000033";     // +1 层
        internal const string GuidDown = "b1000000000000000000000000000034";   // -1 层
        internal const string GuidCancel = "b1000000000000000000000000000035";

        internal const int MaxStack = 2;

        // ------------------------------------------------------------------
        // 运行期状态：净层数 N
        // ------------------------------------------------------------------

        internal sealed class State
        {
            public int N;
        }

        private static readonly ConditionalWeakTable<UnitEntityData, State> s_States =
            new ConditionalWeakTable<UnitEntityData, State>();

        internal static int GetN(UnitEntityData unit)
        {
            if (unit == null) return 0;
            State st;
            return s_States.TryGetValue(unit, out st) ? st.N : 0;
        }

        internal static void SetN(UnitEntityData unit, int n)
        {
            if (unit == null) return;
            if (n < -MaxStack) n = -MaxStack;
            if (n > MaxStack) n = MaxStack;

            s_States.Remove(unit);
            s_States.Add(unit, new State { N = n });

            Main.Log?.Log($"[能力Y] 层数 N = {n}（ΔCL = {n:+0;-0}，ΔDC = {-2 * n:+0;-0}）");
            AbilityXScope.AcceptanceHook("X.YN", n);
        }

        internal static void Reset(UnitEntityData unit)
        {
            if (unit == null) return;
            if (GetN(unit) == 0) return;
            Main.Log?.Log("[能力Y] 战斗结束，层数归零");
            SetN(unit, 0);
        }

        // ------------------------------------------------------------------
        // 次数：ceil(IntMod/2) + ceil(法师等级/2)
        // ------------------------------------------------------------------

        /// <summary>
        /// 引擎的 GetMaxAmount 没有除法能力（IncreasedByStat 加的是原始调整值），
        /// 所以资源上限用 m_Min=0 打底，再由本方法在「休息后 / 升级后」补足。
        ///
        /// 实现方式：在 IncreaseResourceAmount 之外，用一个常驻组件
        /// 在 IUnitRestHandler 里把资源直接设为公式值。
        /// </summary>
        internal static int GetMaxUses(UnitEntityData unit)
        {
            if (unit == null) return 0;

            try
            {
                int intMod = unit.Stats.Intelligence.BonusWithoutTemp;
                int halfInt = CeilDiv2(intMod);
                if (halfInt < 0) halfInt = 0;

                int wizardLevel = GetWizardLevel(unit);
                int halfLevel = CeilDiv2(wizardLevel);

                return halfInt + halfLevel;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>ceil(v / 2)，对负数按 0 处理（由调用方决定）。</summary>
        private static int CeilDiv2(int v)
        {
            if (v >= 0) return (v + 1) / 2;
            return -((-v) / 2);
        }

        internal static int GetWizardLevel(UnitEntityData unit)
        {
            try
            {
                var wiz = WizardArchetypeBlueprints.WizardClass;
                if (wiz == null || unit == null) return 0;
                return unit.Progression.GetClassLevel(wiz);
            }
            catch (Exception)
            {
                return 0;
            }
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

                int n = GetN(Owner);
                if (n == 0) return;

                // 只作用于法师法术书的法术（用户 2026-10-18 确认，与 Q1 一致）
                var data = evt.AbilityData;
                if (data == null || !AbilityXHelper.IsEligible(data)) return;

                evt.AddBonusCasterLevel(n);
                evt.AddBonusDC(-2 * n);

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力Y] 施加修正 N={n}：{data.Blueprint?.name}" +
                                  $"（CL {n:+0;-0}，DC {-2 * n:+0;-0}）");
                }

                AbilityXScope.AcceptanceHook("X.YApplied", true);
            }

            public void OnEventDidTrigger(RuleCalculateAbilityParams evt)
            {
            }
        }
    }
}
