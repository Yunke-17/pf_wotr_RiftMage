using System;
using System.Runtime.CompilerServices;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 特殊异常状态（目前只有「特殊恍惚」）的蓝图与免疫旁路登记。
    ///
    /// ========== 为什么不用原版 Staggered ==========
    ///
    /// 说明书 §6.2：特殊恍惚「不会被行动自如效果免疫或规避」。
    /// 原版 Staggered 会被行动自如免疫，直接用就不满足需求。
    ///
    /// ========== 为什么不能「按来源豁免」（重要，避免走回头路） ==========
    ///
    /// UnitState 里的条件存储是**纯计数数组**（UnitState.cs:565）：
    ///     public bool HasCondition(UnitCondition c)
    ///         => m_Conditions[(int)c] > 0 && m_ConditionsImmunity[(int)c] <= 0;
    /// 它**不记录来源**：AddCondition 的 source 参数只用于刷图标（:498），不参与判定。
    /// 所以「行动自如只免疫别人的恍惚、不免疫我的」无法在数据层表达。
    ///
    /// ========== 采用的方案：自建状态 + 精确放行 ==========
    ///
    /// 1. 自建 Buff，挂 BuffStatusCondition{Condition: Staggered}（条件语义与原版一致）；
    /// 2. Buff 上再挂 SpecialConditionMarker，在 OnTurnOn/OnTurnOff 时登记/注销
    ///    「本单位身上有本 MOD 的某个特殊条件」；
    /// 3. HarmonyPostfix UnitState.HasCondition：当引擎因免疫返回 false、
    ///    而本单位确实挂着我们的特殊状态时，改回 true。
    ///
    /// 这样只影响**我们自己施加的**条件；先知「知命」等原版互动、
    /// 其它 MOD、原版怪物行为完全不变（符合「不全局改、不暗改」）。
    ///
    /// ========== 为什么用弱引用表而不是 UnitPart ==========
    ///
    /// UnitPart 需要注册进 UnitDescriptor 的部件表并处理存档序列化，
    /// 而这份登记纯粹是运行期缓存（Buff 本身会被存档，重开后 OnTurnOn 会重新登记）。
    /// ConditionalWeakTable 随单位回收，不会漏内存，也不会污染存档。
    /// </summary>
    internal static class SpecialStatuses
    {
        internal const string GuidSpecialStaggered = "b1000000000000000000000000000020";

        private const string KeyName = "WizardArchetype.SpecialStaggered.Name";
        private const string KeyDesc = "WizardArchetype.SpecialStaggered.Desc";

        internal static BlueprintBuff SpecialStaggered { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            if (SpecialStaggered != null) return;

            // ★ 显示名 2026-10-20 由「时序反冲」定为「时序恍惚」（用户要求）。
            //   理由：状态本身挂的就是 UnitCondition.Staggered（恍惚），
            //   名字直接体现效果，也与术语表条目、开场特性描述用词统一。
            //   内部类名 / GUID / 本地化 key 均不变（改 key 会让旧存档的文本丢失）。
            WizardArchetypeBlueprints.RegisterText(KeyName, "时序恍惚");
            WizardArchetypeBlueprints.RegisterText(KeyDesc,
                "现实正在加倍回推时空隙法师身上的每一道裂纹。" +
                "其效果如同恍惚：每轮只能进行一个移动动作或一个标准动作、" +
                "移动速度减半，不能进行" + Glossary.Link(Glossary.KFullRoundAction, "整轮动作") +
                "，但可以正常使用" + Glossary.Link(Glossary.KFreeAction, "自由动作") +
                "与" + Glossary.Link(Glossary.KSwiftAction, "迅捷动作") + "。" +
                "该状态无法被《行动自如》或类似效果预防与解除。");

            var condition = new BuffStatusCondition
            {
                name = "$BuffStatusCondition$SpecialStaggered_0",
                Condition = UnitCondition.Staggered,
                SaveEachRound = false,
                SaveType = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown,
            };

            var marker = new SpecialConditionMarker
            {
                name = "$SpecialConditionMarker$SpecialStaggered_1",
                Condition = UnitCondition.Staggered,
            };

            // 战斗结束时立即清除（§6.2）
            var combatTrigger = new CombatStateTrigger
            {
                name = "$CombatStateTrigger$SpecialStaggered_2",
                CombatStartActions = new ActionList { Actions = new GameAction[0] },
                CombatEndActions = new ActionList
                {
                    Actions = new GameAction[]
                    {
                        new ContextActionRemoveSelf { name = "$ContextActionRemoveSelf$SpecialStaggered_3" },
                    },
                },
            };

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_SpecialStaggered",
                AssetGuid = BlueprintGuid.Parse(GuidSpecialStaggered),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = false,
                ComponentsArray = new BlueprintComponent[] { condition, marker, combatTrigger },

                // 玩家棋子必定带 ParticlesSnapMap，Buf 到期移除时会读 FxOnRemove，
                // 为 null 会 NRE（见 05 踩坑记录）。给个空实例，不设 AssetId 即无特效。
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            ReflectionFields.Set(buff, "m_DisplayName", new LocalizedString { Key = KeyName });
            ReflectionFields.Set(buff, "m_Description", new LocalizedString { Key = KeyDesc });

            foreach (var c in buff.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = buff;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidSpecialStaggered), buff);
            SpecialStaggered = buff;

            // 图标与光环统一由 Presentation.Apply 装配（2026-10-21）

            Main.Log?.Log("WizardArchetype: 已创建「特殊恍惚」蓝图");
        }
    }

    /// <summary>
    /// 登记「本单位身上有本 MOD 施加的哪个特殊条件」。
    /// 由 SpecialStatuses 创建的 Buff 携带；HasCondition 补丁据此精确放行。
    /// </summary>
    internal class SpecialConditionMarker : UnitBuffComponentDelegate
    {
        public UnitCondition Condition;

        protected override void OnTurnOn()
        {
            SpecialConditionRegistry.Add(Owner, Condition);
        }

        protected override void OnTurnOff()
        {
            SpecialConditionRegistry.Remove(Owner, Condition);
        }
    }

    /// <summary>
    /// 运行期登记表：单位 → 该单位身上由本 MOD 施加的特殊条件集合。
    /// </summary>
    internal static class SpecialConditionRegistry
    {
        private sealed class Entry
        {
            public readonly System.Collections.Generic.HashSet<UnitCondition> Conditions =
                new System.Collections.Generic.HashSet<UnitCondition>();

            /// <summary>已打过「旁路生效」日志的条件（日志节流用）。</summary>
            public readonly System.Collections.Generic.HashSet<UnitCondition> Logged =
                new System.Collections.Generic.HashSet<UnitCondition>();
        }

        private static readonly ConditionalWeakTable<UnitEntityData, Entry> s_Entries =
            new ConditionalWeakTable<UnitEntityData, Entry>();

        internal static void Add(UnitEntityData unit, UnitCondition condition)
        {
            if (unit == null) return;
            s_Entries.GetValue(unit, _ => new Entry()).Conditions.Add(condition);
        }

        internal static void Remove(UnitEntityData unit, UnitCondition condition)
        {
            if (unit == null) return;
            Entry e;
            if (s_Entries.TryGetValue(unit, out e)) e.Conditions.Remove(condition);
        }

        internal static bool Has(UnitEntityData unit, UnitCondition condition)
        {
            if (unit == null) return false;
            Entry e;
            return s_Entries.TryGetValue(unit, out e) && e.Conditions.Contains(condition);
        }

        /// <summary>
        /// 每个 (单位, 条件) 只返回一次 true，用于「只记第一次」的日志节流。
        /// HasCondition 是热路径，实测一轮战斗触发数万次，不能每次都打日志。
        /// </summary>
        internal static bool MarkLogged(UnitEntityData unit, UnitCondition condition)
        {
            if (unit == null) return false;
            Entry e;
            if (!s_Entries.TryGetValue(unit, out e)) return false;
            return e.Logged.Add(condition);
        }
    }
}
