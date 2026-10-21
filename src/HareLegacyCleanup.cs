using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 旧存档清理：把「幸运野兔」合并前遗留的旧特性从角色身上摘掉
    /// （用户 2026-10-20：合并为单一特性后要求自动清理）。
    ///
    /// ========== 要清理什么 ==========
    ///
    /// 合并前幸运野兔是三条独立特性：
    ///   ① 原版 97dff21a（HareFamiliarBondFeature，1 级）—— 原版共享蓝图，**绝不能删**
    ///   ② b100…0006（3 级 法术穿透）—— 我们的旧自建特性
    ///   ③ b100…0005（7 级 先攻双骰）—— 我们的旧自建特性
    ///
    /// ② ③ 的 GUID 现在被复用为「子特性」，由 AddFeatureOnClassLevel 按等级授予。
    /// 所以清理时必须区分来源：
    ///   · 由 AddFeatureOnClassLevel 授予的（SourceFact != null）→ 保留
    ///   · 旧存档里直接挂在特性表上的（SourceFact == null）→ 摘掉
    ///
    /// ========== API 依据（已逐个核实） ==========
    ///
    ///   EntityFact.SourceFact        EntityFact.cs:60
    ///   FeatureCollection.GetRank    FeatureCollection.cs:48
    ///   EntityFactsManager.Get<T>    EntityFactsManager.cs:142   （按蓝图取实例）
    ///   EntityFactsManager.Remove    EntityFactsManager.cs:212
    ///
    /// FeatureCollection 本身**没有** RemoveFeature / GetFact，
    /// 移除必须走 Features.Manager.Remove(fact)。
    /// </summary>
    internal class HareLegacyCleanup : UnitFactComponentDelegate,
        Kingmaker.PubSubSystem.IUnitLevelUpHandler
    {
        /// <summary>合并后的「幸运野兔」特性 GUID（与 WizardArchetypeFeatures.GuidHareUnified 一致）。</summary>
        private const string HareUnifiedGuid = "b1000000000000000000000000000009";

        /// <summary>旧的 3 级「法术穿透」特性（现在是子特性）。</summary>
        private const string LegacySpellPen = "b1000000000000000000000000000006";

        /// <summary>旧的 7 级「先攻双骰」特性（现在是子特性）。</summary>
        private const string LegacyDoubleRoll = "b1000000000000000000000000000005";

        protected override void OnTurnOn()
        {
            Cleanup(Owner);
        }

        public void HandleUnitBeforeLevelUp(UnitEntityData unit)
        {
        }

        public void HandleUnitAfterLevelUp(UnitEntityData unit,
            Kingmaker.UnitLogic.Class.LevelUp.LevelUpController controller)
        {
            if (unit == null || unit != Owner) return;
            Cleanup(unit);
        }

        private static void Cleanup(UnitEntityData unit)
        {
            if (unit == null) return;

            try
            {
                RemoveStaleFact(unit, LegacySpellPen, "法术穿透");
                RemoveStaleFact(unit, LegacyDoubleRoll, "先攻双骰");
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[幸运野兔] 旧特性清理失败：" + e.Message);
            }
        }

        private static void RemoveStaleFact(UnitEntityData unit, string guid, string label)
        {
            // BlueprintGuid 没有 TryParse，这里用 Parse + 外层 try/catch 兜底
            BlueprintGuid g;
            try
            {
                g = BlueprintGuid.Parse(guid);
            }
            catch (Exception)
            {
                return;
            }

            var bp = ResourcesLibrary.BlueprintsCache?.Load(g) as BlueprintFeature;
            if (bp == null) return;

            var features = unit.Descriptor?.Progression?.Features;
            if (features == null) return;

            var fact = features.Manager.Get<Feature>(bp);
            if (fact == null) return;

            // ★ 关键（2026-10-21 修复「法术穿透/双骰取高不生效」）：
            //
            // 不能用 SourceFact == null 判断「是不是旧存档残留」——
            // AddFeatureOnClassLevel 授予时走的是 Owner.AddFact(feature, null, null)
            // （AddFeatureOnClassLevel.cs:98），**SourceFact 同样是 null**。
            // 上一版据此判断，结果把等级开关刚授予的特性当成残留当场删掉，
            // 表现为「法术穿透 / 双骰取高永远不生效」，日志里反复刷「已清理旧存档残留特性」。
            //
            // 正确判据：直接问等级开关组件「你现在有没有授予这个特性」。
            // 只要开关还在授予，这个 Fact 就是合法的，一律不动。
            if (IsGrantedByLevelGate(unit, bp)) return;

            features.Manager.Remove(fact);
            Main.Log?.Log($"[幸运野兔] 已清理旧存档残留特性：{label}");
        }

        /// <summary>
        /// 这个特性是不是由本变体「幸运野兔」特性上的 AddFeatureOnClassLevel 授予的。
        ///
        /// 做法：遍历单位身上「幸运野兔」特性的所有组件，找 AddFeatureOnClassLevel，
        /// 看它缓存的 AppliedFact 是否就是目标 Fact。命中即说明是等级开关授予的。
        /// </summary>
        private static bool IsGrantedByLevelGate(UnitEntityData unit, BlueprintFeature target)
        {
            try
            {
                BlueprintGuid hareGuid;
                try { hareGuid = BlueprintGuid.Parse(HareUnifiedGuid); }
                catch (Exception) { return false; }

                var hareBp = ResourcesLibrary.BlueprintsCache?.Load(hareGuid) as BlueprintFeature;
                if (hareBp == null) return false;

                var hareFact = unit.Descriptor.Progression.Features.Manager.Get<Feature>(hareBp);
                if (hareFact == null) return false;

                foreach (var comp in hareFact.Components)
                {
                    var gate = comp.SourceBlueprintComponent
                        as Kingmaker.Designers.Mechanics.Facts.AddFeatureOnClassLevel;
                    if (gate == null) continue;

                    // 这个开关的目标就是我们正在检查的特性 ⇒ 属于正常授予
                    if (gate.Feature == target) return true;
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[幸运野兔] 等级开关判定异常，按保留处理：" + e.Message);
                return true;   // 判定不了就保留，宁可留残留也不要误删生效中的特性
            }

            return false;
        }
    }
}
