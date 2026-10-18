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

            ApplyBoth();
            OpeningStateDecider.Register(Owner);
        }

        public void HandleUnitLeaveCombat(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;

            // 文档：两个状态都在战斗结束时清除（Buff 自带 CombatStateTrigger 做兜底）
            OpeningStateDecider.Unregister(Owner);
            RemoveBuff(SpecialStatuses.SpecialStaggered);
            RemoveBuff(SpecialEntangleBlueprints.SpecialEntangled);
        }

        private void ApplyBoth()
        {
            var staggered = SpecialStatuses.SpecialStaggered;
            var entangled = SpecialEntangleBlueprints.SpecialEntangled;

            if (staggered != null)
            {
                GameHelper.ApplyBuff(Owner, staggered, new Rounds(1));
                Main.Log?.Log("[开场状态] 施加特殊恍惚（1 轮）");
            }
            if (entangled != null)
            {
                GameHelper.ApplyBuff(Owner, entangled, new Rounds(1));
                Main.Log?.Log("[开场状态] 施加特殊纠缠（1 轮）");
            }

            AbilityXScope.AcceptanceHook("X.OpeningApplied", true);
        }

        private void RemoveBuff(BlueprintBuff buff)
        {
            if (buff == null || Owner == null) return;
            try { GameHelper.RemoveBuff(Owner, buff); }
            catch (Exception e) { Main.Log?.Warning("[开场状态] 移除状态失败：" + e.Message); }
        }
    }

    /// <summary>
    /// 开场状态的延迟判定器。由 Main.OnUpdate 每帧驱动。
    /// </summary>
    internal static class OpeningStateDecider
    {
        /// <summary>等待先攻结算的时间（秒）。1.2 秒足够覆盖普通战斗的先攻批处理。</summary>
        private const float DelaySeconds = 1.2f;

        private sealed class Pending
        {
            public UnitEntityData Unit;
            public float DueTime;
        }

        private static readonly List<Pending> s_Pending = new List<Pending>();

        internal static void Register(UnitEntityData unit)
        {
            if (unit == null) return;
            s_Pending.RemoveAll(p => p.Unit == null || p.Unit == unit);
            s_Pending.Add(new Pending
            {
                Unit = unit,
                DueTime = UnityEngine.Time.realtimeSinceStartup + DelaySeconds,
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
                if (now < p.DueTime) continue;

                s_Pending.RemoveAt(i);
                try { Decide(p.Unit); }
                catch (Exception e) { Main.Log?.Warning("[开场状态] 判定失败：" + e.Message); }
            }
        }

        private static void Decide(UnitEntityData unit)
        {
            if (!unit.IsInCombat) return;

            int mine = unit.CombatState != null ? unit.CombatState.Initiative : 0;
            int enemyMax = GetEnemyMaxInitiative(unit, out int enemyCount);

            // 没有敌人在战斗状态：不做二选一（避免误删）
            if (enemyCount == 0)
            {
                Main.Log?.Log("[开场状态] 未发现战斗中的敌人，跳过二选一");
                return;
            }

            bool keepEntangled = mine >= enemyMax;

            var toRemove = keepEntangled
                ? SpecialStatuses.SpecialStaggered
                : SpecialEntangleBlueprints.SpecialEntangled;

            if (toRemove != null)
            {
                try { GameHelper.RemoveBuff(unit, toRemove); }
                catch (Exception e) { Main.Log?.Warning("[开场状态] 移除状态失败：" + e.Message); }
            }

            string keep = keepEntangled ? "特殊纠缠" : "特殊恍惚";
            Main.Log?.Log($"[开场状态] 我方先攻 {mine}，敌方最高 {enemyMax}（{enemyCount} 个敌人）→ 保留{keep}");

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
