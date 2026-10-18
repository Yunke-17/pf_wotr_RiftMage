using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Designers;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 每场战斗的开场状态（说明书 §6.1）。
    ///
    /// 规则：
    ///   战斗开始 → 同时获得「特殊恍惚」与「特殊纠缠」，各 1 轮
    ///   先攻顺序确定后：
    ///     我方先攻 >= 敌方最高（含相同） → 移除特殊恍惚，保留特殊纠缠
    ///     我方先攻 <  任一敌方        → 移除特殊纠缠，保留特殊恍惚
    ///
    /// 「敌人」的定义（用户已定）：已进入战斗状态的敌对单位。
    ///
    /// ========== 为什么不能只用 CombatStateTrigger ==========
    ///
    /// 原版「知命」那类写法靠 CombatStateTrigger.HandleUnitJoinCombat，
    /// 它在单位**加入战斗**时立刻触发 —— 那时所有单位的先攻还没掷完
    ///（先攻由 UnitCombatPrepareController 在后续帧批量结算）。
    /// 所以在那里做「二选一」必然拿不到结果。
    ///
    /// 做法：加入战斗时先把**两个状态都挂上**（立即生效，玩家能看到），
    /// 再登记一个「待判定」，延迟一小段时间（默认 1.2 秒）后读取
    /// UnitCombatState.Initiative（最终先攻值）做决策并移除其中一个。
    ///
    /// ========== 为什么用延迟而不是精确时机 ==========
    ///
    /// 引擎没有「所有单位先攻结算完毕」的单一事件。可用的替代：
    ///   - 订阅 RuleInitiativeRoll 逐个收集 → 仍不知道「谁是最后一个」
    ///   - 读 CombatState.Initiative → 只要结算完就能拿到正确值
    /// 因此用「短延迟 + 读最终值」最稳妥；延迟期内两个状态都在，
    /// 与文档「战斗开始时同时获得两个状态」的描述一致。
    /// </summary>
    internal class SpecialOpeningState : UnitFactComponentDelegate, IUnitCombatHandler
    {
        public void HandleUnitJoinCombat(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;

            // ★ 设计（2026-10-18 用户确认）：
            //   两个状态互斥（最终只会留一个），所以**先判定先攻、再施加**，
            //   而不是「先给两个、再删一个」。
            //
            //   好处：
            //     1. 不会出现「两个惩罚同时生效」的短暂瞬间；
            //     2. 少一次 Buff 增删与 UI 图标闪烁；
            //     3. 结果与文档等价：最终都是「1 轮、只有一个状态」。
            //   代价：判定完成前（通常 1~2 帧，约 0.02~0.05 秒）身上没有任何开场状态。
            //   文档 §6.1「同时获得两个状态」描述的是**最终效果**，
            //   这里用等价且更干净的实现路径表达同一条规则。
            OpeningStateDecider.Register(Owner);
        }

        public void HandleUnitLeaveCombat(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;

            OpeningStateDecider.Unregister(Owner);

            // §9.2：能力 Y 的修正持续至战斗结束 ⇒ 离开战斗时归零
            if (AbilityY.GetS(Owner) != 0)
            {
                Main.Log?.Log("[能力Y] 战斗结束，净层数归零");
                AbilityY.SetS(Owner, 0);
            }

            // 防御性清理：正常情况下状态由 CombatStateTrigger 自行清除，
            // 这里再兜一次，避免极端时序下残留（例如判定恰好发生在离开战斗的同一帧）。
            RemoveBuff(SpecialStatuses.SpecialStaggered);
            RemoveBuff(SpecialEntangleBlueprints.SpecialEntangled);
        }

        /// <summary>按判定结果施加**其中一个**状态（1 轮）。</summary>
        internal static void ApplyChosen(UnitEntityData unit, bool keepEntangled)
        {
            if (unit == null) return;

            var buff = keepEntangled
                ? SpecialEntangleBlueprints.SpecialEntangled
                : SpecialStatuses.SpecialStaggered;

            if (buff == null) return;

            try
            {
                GameHelper.ApplyBuff(unit, buff, new Rounds(1));
                Main.Log?.Log("[开场状态] 施加" + (keepEntangled ? "特殊纠缠" : "特殊恍惚") + "（1 轮）");
                AbilityXScope.AcceptanceHook("X.OpeningApplied", true);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[开场状态] 施加状态失败：" + e.Message);
            }
        }

        private void RemoveBuff(BlueprintBuff buff)
        {
            if (buff == null || Owner == null) return;
            try { GameHelper.RemoveBuff(Owner, buff); }
            catch (Exception e) { Main.Log?.Warning("[开场状态] 移除状态失败：" + e.Message); }
        }
    }

    /// <summary>
    /// 开场状态的判定器。由 Main.OnUpdate 每帧驱动。
    ///
    /// 判定时机（2026-10-18 调整）：
    ///   最初用固定延迟 1.2 秒等先攻结算，实测体感太长（状态挂上后要等一秒才消失）。
    ///   改为「就绪即判 + 短兜底」：
    ///     - 每帧检查先攻是否都已掷出（我方 > 0 且每个战斗中的敌人 > 0）
    ///     - 一旦就绪立刻判定（通常只要 1~2 帧，约 0.02~0.05 秒）
    ///     - 超过 MaxWaitSeconds 仍未就绪（例如有敌人先攻恒为 0）则强制判定，避免永远不判定
    ///
    /// 为什么用「> 0」判就绪：先攻 = d20 + 调整值，最低也有 1；
    /// 0 只可能是「尚未掷出」的初始值。
    /// </summary>
    internal static class OpeningStateDecider
    {
        /// <summary>最早允许判定的时间，给引擎留一两帧把状态挂完。</summary>
        private const float EarliestSeconds = 0.05f;

        /// <summary>兜底上限：超过这个时间还没就绪就强制判定。</summary>
        private const float MaxWaitSeconds = 0.5f;

        private sealed class Pending
        {
            public UnitEntityData Unit;
            public float Earliest;
            public float Deadline;
        }

        private static readonly List<Pending> s_Pending = new List<Pending>();

        internal static void Register(UnitEntityData unit)
        {
            if (unit == null) return;
            s_Pending.RemoveAll(p => p.Unit == null || p.Unit == unit);

            float now = UnityEngine.Time.realtimeSinceStartup;
            s_Pending.Add(new Pending
            {
                Unit = unit,
                Earliest = now + EarliestSeconds,
                Deadline = now + MaxWaitSeconds,
            });
        }

        internal static void Unregister(UnitEntityData unit)
        {
            s_Pending.RemoveAll(p => p.Unit == null || p.Unit == unit);
        }

        internal static void Tick()
        {
            if (s_Pending.Count == 0) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                var p = s_Pending[i];
                if (p.Unit == null) { s_Pending.RemoveAt(i); continue; }
                if (now < p.Earliest) continue;

                bool ready = IsReady(p.Unit);
                bool giveUp = now >= p.Deadline;
                if (!ready && !giveUp) continue;

                s_Pending.RemoveAt(i);
                try
                {
                    Decide(p.Unit);
                    if (!ready)
                    {
                        Main.Log?.Log("[开场状态] 先攻未全部就绪，已按当前值兜底判定");
                    }
                }
                catch (Exception e) { Main.Log?.Warning("[开场状态] 判定失败：" + e.Message); }
            }
        }

        /// <summary>我方与所有战斗中的敌人的先攻是否都已掷出。</summary>
        private static bool IsReady(UnitEntityData unit)
        {
            try
            {
                if (unit.CombatState == null || unit.CombatState.Initiative <= 0) return false;

                int enemyCount;
                int enemyMax = GetEnemyMaxInitiative(unit, out enemyCount);
                if (enemyCount == 0) return false;

                // enemyMax > 0 说明至少有一个敌人已掷出；
                // 但还要确认「每个」敌人都掷出了，避免用未掷出的 0 当最低值。
                foreach (var other in Game.Instance.State.Units)
                {
                    if (other == null || other == unit) continue;
                    if (!other.IsInCombat) continue;
                    if (!unit.Group.IsEnemy(other.Group)) continue;

                    if (other.CombatState == null || other.CombatState.Initiative <= 0) return false;
                }

                return enemyMax > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Decide(UnitEntityData unit)
        {
            if (!unit.IsInCombat) return;

            int mine = unit.CombatState != null ? unit.CombatState.Initiative : 0;
            int enemyMax = GetEnemyMaxInitiative(unit, out int enemyCount);

            // 没有敌人在战斗状态：不做二选一（避免凭空给状态）
            if (enemyCount == 0)
            {
                Main.Log?.Log("[开场状态] 未发现战斗中的敌人，跳过开场状态");
                return;
            }

            bool keepEntangled = mine >= enemyMax;

            // 直接施加最终保留的那个，不做「先给两个再删一个」
            SpecialOpeningState.ApplyChosen(unit, keepEntangled);

            string keep = keepEntangled ? "特殊纠缠" : "特殊恍惚";
            Main.Log?.Log($"[开场状态] 我方先攻 {mine}，敌方最高 {enemyMax}（{enemyCount} 个敌人）→ 施加{keep}");

            AbilityXScope.AcceptanceHook("X.OpeningKeepEntangled", keepEntangled);
            AbilityXScope.AcceptanceHook("X.OpeningMyInit", mine);
            AbilityXScope.AcceptanceHook("X.OpeningEnemyMax", enemyMax);
        }

        private static int GetEnemyMaxInitiative(UnitEntityData unit, out int enemyCount)
        {
            enemyCount = 0;
            int max = int.MinValue;

            try
            {
                // 「敌人」= 已进入战斗状态的敌对单位（用户定义）
                foreach (var other in Game.Instance.State.Units)
                {
                    if (other == null || other == unit) continue;
                    if (!other.IsInCombat) continue;
                    if (!unit.Group.IsEnemy(other.Group)) continue;

                    enemyCount++;
                    int init = other.CombatState != null ? other.CombatState.Initiative : 0;
                    if (init > max) max = init;
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[开场状态] 扫描敌人失败：" + e.Message);
            }

            return max == int.MinValue ? 0 : max;
        }
    }
}
