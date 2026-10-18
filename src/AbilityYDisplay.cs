using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的层数显示（用户 2026-10-19 要求：效果要出现在角色 Buff 栏）。
    ///
    /// ========== 为什么用「两个 Buff」而不是一个 ==========
    ///
    /// 层数 N 有正负两种方向，而 Buff.Rank 被 BlueprintBuff.Ranks 夹在 [1, Ranks]
    /// （Buff.SetRank，Buff.cs:665-680），**不能为 0 或负**。
    /// 因此用两个 Buff 表示方向：
    ///     UpBuff   : N > 0 时存在，Rank = N      （显示「CL +N / DC -2N」）
    ///     DownBuff : N &lt; 0 时存在，Rank = -N     （显示「CL -N / DC +2N」）
    /// N == 0 时两个都不存在。
    ///
    /// 这也天然满足「两个方向互斥」的视觉表达：Buff 栏里只会出现一个。
    ///
    /// ========== 层数怎么显示 ==========
    ///
    /// BlueprintBuff.Ranks = 2，Buff.SetRank(1|2) 会让图标右下角显示 1/2。
    /// 描述文本用固定文案说明公式，具体数值由玩家按 Rank 推算（也可后续加动态文本）。
    /// </summary>
    internal static class AbilityYDisplay
    {
        internal const string GuidUpBuff = "b1000000000000000000000000000036";
        internal const string GuidDownBuff = "b1000000000000000000000000000037";

        internal static BlueprintBuff UpBuff { get; private set; }
        internal static BlueprintBuff DownBuff { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            UpBuff = MakeBuff(cache, GuidUpBuff, "YUp",
                "施法参数交换（CL 强化）",
                "施法者等级提高，法术 DC 降低。\n" +
                "每层：CL +1 / DC -2。层数见图标右下角。\n" +
                "战斗结束时自动清除。");

            DownBuff = MakeBuff(cache, GuidDownBuff, "YDown",
                "施法参数交换（DC 强化）",
                "法术 DC 提高，施法者等级降低。\n" +
                "每层：CL -1 / DC +2。层数见图标右下角。\n" +
                "战斗结束时自动清除。");
        }

        private static BlueprintBuff MakeBuff(BlueprintsCache cache, string guid, string keyBase,
            string name, string desc)
        {
            const string root = "WizardArchetype.AbilityY.";
            WizardArchetypeBlueprints.RegisterText(root + keyBase + ".Name", name);
            WizardArchetypeBlueprints.RegisterText(root + keyBase + ".Desc", desc);

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_AbilityY_" + keyBase,
                AssetGuid = BlueprintGuid.Parse(guid),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                // 允许 2 层（SetRank 的上限）；Buff 本身永久，由层数归零时手动移除
                Ranks = 2,
                IsClassFeature = true,
                ComponentsArray = new BlueprintComponent[0],
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            ReflectionFields.Set(buff, "m_DisplayName", new LocalizedString { Key = root + keyBase + ".Name" });
            ReflectionFields.Set(buff, "m_Description", new LocalizedString { Key = root + keyBase + ".Desc" });

            cache.AddCachedBlueprint(BlueprintGuid.Parse(guid), buff);
            return buff;
        }

        /// <summary>
        /// 把 Buff 栏同步到当前层数 N。
        /// 由 AbilityY.SetN 调用；N=0 时两个 Buff 都移除。
        /// </summary>
        internal static void Sync(UnitEntityData unit, int n)
        {
            if (unit == null) return;

            try
            {
                if (n > 0)
                {
                    RemoveBuff(unit, DownBuff);
                    var b = EnsureBuff(unit, UpBuff);
                    b?.SetRank(Math.Min(n, 2), false);
                }
                else if (n < 0)
                {
                    RemoveBuff(unit, UpBuff);
                    var b = EnsureBuff(unit, DownBuff);
                    b?.SetRank(Math.Min(-n, 2), false);
                }
                else
                {
                    RemoveBuff(unit, UpBuff);
                    RemoveBuff(unit, DownBuff);
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Y] Buff 显示同步失败：" + e.Message);
            }
        }

        private static Buff EnsureBuff(UnitEntityData unit, BlueprintBuff bp)
        {
            if (bp == null) return null;

            // 已存在就复用
            foreach (var b in unit.Buffs)
            {
                if (b != null && b.Blueprint == bp) return b;
            }

            // 不设时长（永久），由 Sync 在层数归零/战斗结束时移除
            return GameHelper.ApplyBuff(unit, bp, null);
        }

        private static void RemoveBuff(UnitEntityData unit, BlueprintBuff bp)
        {
            if (bp == null) return;
            try
            {
                if (unit.Buffs == null) return;
                // 逐个判断再移除，避免 RemoveBuff 对不存在的 buff 报错
                List<Buff> toRemove = null;
                foreach (var b in unit.Buffs)
                {
                    if (b != null && b.Blueprint == bp)
                    {
                        (toRemove ?? (toRemove = new List<Buff>())).Add(b);
                    }
                }
                if (toRemove == null) return;
                foreach (var b in toRemove) b.Remove();
            }
            catch (Exception) { }
        }
    }
}
