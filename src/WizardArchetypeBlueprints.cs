using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;

namespace WizardArchetype
{
    /// <summary>
    /// 变体蓝图构建与注册（裸 C#，不依赖 BlueprintCore）。
    ///
    /// 结构参照原版 SpellMasterArchetype.jbp：
    ///   ReplaceSpellbook / RemoveSpellbook 保持默认 → 保留法师法术书（A1）
    ///
    /// 与文档的对应：
    ///   A3  移除 1/5/15/20 级额外专长
    ///   A4  保留 10 级额外专长（不写进 RemoveFeatures 即可）
    ///   A5  移除专精学派
    ///   A7  移除奥法契约
    /// </summary>
    internal static class WizardArchetypeBlueprints
    {
        internal static readonly BlueprintGuid ArchetypeGuid =
            BlueprintGuid.Parse("a1b2c3d4e5f6478990a1b2c3d4e5f601");

        internal static readonly BlueprintGuid WizardClassGuid =
            BlueprintGuid.Parse("ba34257984f4c41408ce1dc2004e342e");

        // 原版特性 GUID（WizardProgression.jbp:54/59/62/82/104/126/148 已核对）
        private const string ExtraFeat     = "8c3102c2ff3b69444b139a98521a4899";
        private const string SchoolSelection = "5f838049069f1ac4d804ce0862ab5110";
        private const string ArcaneBond    = "03a1781486ba98043afddaabf6b7d8ff";

        private const string KeyName = "WizardArchetype.Archetype.Name";
        private const string KeyDesc = "WizardArchetype.Archetype.Desc";

        internal static BlueprintCharacterClass WizardClass { get; private set; }

        internal static BlueprintArchetype Archetype { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            RegisterText(KeyName, "时间法师");
            RegisterText(KeyDesc,
                "时间法师放弃了传统法师对专精学派的钻研，转而追求先手与施法节奏的掌控。\n" +
                "<b>职业特点</b>\n" +
                "· 以智力取代感知进行察觉与学识检定，并将智力调整值计入先攻。\n" +
                "· 随法师等级成长获得额外先攻加值与专注加值。\n" +
                "· <b>强化施法</b>：消耗一次充能，使下一发法师法术无视法术抗力、提升伤害骰面，\n" +
                "  并迫使敌方豁免检定多次取低。作为代价，会陷入短暂的恍惚。\n" +
                "· <b>施法参数交换</b>：在施法者等级与法术 DC 之间自由取舍。\n" +
                "· <b>透支充能</b>：以力竭与疲劳为代价，夺回一次强化施法的机会。\n" +
                "<b>代价</b>\n" +
                "每场战斗开始时，会根据先攻结果陷入特殊恍惚或特殊纠缠之一；\n" +
                "这些状态不会被「行动自如」免疫。\n" +
                "<b>失去</b>\n" +
                "1、5、15、20 级的额外专长；专精学派及其法术位与对立学派限制；奥法契约。\n" +
                "<b>保留</b>\n" +
                "10 级的额外专长，以及完整的法师法术书、法术准备与法术位成长。");

            var bp = new BlueprintArchetype
            {
                name = "WizardArchetypeArchetype",
                AssetGuid = ArchetypeGuid,
                LocalizedName = new LocalizedString { Key = KeyName },
                LocalizedDescription = new LocalizedString { Key = KeyDesc },
                LocalizedDescriptionShort = new LocalizedString { Key = KeyName },
                AddFeatures = new LevelEntry[0],
            };

            // 1 级：移除学派选择 + 奥法契约 + 额外专长
            bp.RemoveFeatures = new[]
            {
                MakeEntry(cache, 1,  SchoolSelection, ArcaneBond, ExtraFeat),
                MakeEntry(cache, 5,  ExtraFeat),
                MakeEntry(cache, 15, ExtraFeat),
                MakeEntry(cache, 20, ExtraFeat),
                // 注意：不写 10 级 —— 用户要求保留 10 级额外专长（A4）
            };

            Archetype = bp;
            Main.Log?.Log("WizardArchetype: 变体蓝图已构建");
            Main.SetArchetypeGuid(ArchetypeGuid);
        }

        /// <summary>注册进 BlueprintsCache 并挂到法师职业的变体列表。</summary>
        internal static void Register(BlueprintsCache cache)
        {
            if (Archetype == null) return;

            var wizard = cache.Load(WizardClassGuid) as BlueprintCharacterClass;
            if (wizard == null)
            {
                Main.Log?.Error("WizardArchetype: 找不到法师职业蓝图，变体未挂载");
                return;
            }
            WizardClass = wizard;

            // 构建全部特性（含原版挂载与自建蓝图）
            var features = WizardArchetypeFeatures.BuildAll(cache);

            // 按等级分组写进 AddFeatures
            Archetype.AddFeatures = features
                .GroupBy(f => f.Level)
                .OrderBy(g => g.Key)
                .Select(g => new LevelEntryBuilder(g.Key, g.Select(x => x.Feature)).Build())
                .ToArray();

            cache.AddCachedBlueprint(ArchetypeGuid, Archetype);

            var existing = wizard.Archetypes.Where(a => a != null).ToList();
            if (existing.Any(a => a.AssetGuid == ArchetypeGuid))
            {
                Main.Log?.Log("WizardArchetype: 变体已存在，跳过挂载");
                return;
            }

            var refs = existing
                .Select(a => BlueprintReference<BlueprintArchetype>.CreateTyped<BlueprintArchetypeReference>(a))
                .Concat(new[]
                {
                    BlueprintReference<BlueprintArchetype>.CreateTyped<BlueprintArchetypeReference>(Archetype),
                })
                .ToArray();

            // m_Archetypes 是私有字段（BlueprintCharacterClass.cs:705）
            Traverse.Create(wizard).Field("m_Archetypes").SetValue(refs);

            Main.Log?.Log($"WizardArchetype: 已挂到法师职业，变体数 = {refs.Length}，特性数 = {features.Count}");
        }

        /// <summary>构造 LevelEntry 的小工具（SetFeatures 需要 IEnumerable&lt;BlueprintFeatureBase&gt;）。</summary>
        private readonly struct LevelEntryBuilder
        {
            private readonly int m_Level;
            private readonly IEnumerable<SimpleBlueprint> m_Features;

            internal LevelEntryBuilder(int level, IEnumerable<SimpleBlueprint> features)
            {
                m_Level = level;
                m_Features = features;
            }

            internal LevelEntry Build()
            {
                var entry = new LevelEntry { Level = m_Level };
                entry.SetFeatures(m_Features.OfType<BlueprintFeatureBase>());
                return entry;
            }
        }

        private static LevelEntry MakeEntry(BlueprintsCache cache, int level, params string[] featureGuids)
        {
            var entry = new LevelEntry { Level = level };

            var features = featureGuids
                .Select(g => LoadFeature(cache, g))
                .Where(f => f != null)
                .ToArray();

            entry.SetFeatures(features);
            return entry;
        }

        /// <summary>
        /// 从 BlueprintsCache 真实加载特性蓝图。
        /// 不能伪造 BlueprintFeature 实例 —— 引用只记 GUID，
        /// 但 AddFeatures/RemoveFeatures 在运行期会按引用取真实对象，
        /// 伪造对象会让"移除特性"完全失效。
        /// </summary>
        private static BlueprintFeatureBase LoadFeature(BlueprintsCache cache, string guid)
        {
            var bp = cache.Load(BlueprintGuid.Parse(guid));
            if (bp == null)
            {
                Main.Log?.Warning($"WizardArchetype: 特性蓝图 {guid} 加载失败");
            }
            return bp as BlueprintFeatureBase;
        }

        /// <summary>
        /// 待注册的本地化文本。
        ///
        /// 时机问题：本 MOD 的蓝图在 BlueprintsCache.Init()（GameStarter.cs:205）时构建，
        /// 而本地化包要到 GameStarter.cs:229-233 才就绪。此时 PutString 写入的是 null 包，
        /// 文本会全部丢失（表现为 UI 显示 null / key）。
        /// 因此先缓存到这里，等 LocalizationManager.WaitForInit 之后再 flush。
        /// </summary>
        private static readonly Dictionary<string, string> s_PendingText = new Dictionary<string, string>();
        private static bool s_TextFlushed;

        internal static void RegisterText(string key, string text)
        {
            if (s_TextFlushed)
            {
                PushText(key, text);
                return;
            }
            s_PendingText[key] = text;
        }

        /// <summary>本地化包就绪后调用，把积压文本写进包。</summary>
        internal static void FlushText()
        {
            if (s_TextFlushed) return;
            if (LocalizationManager.CurrentPack == null)
            {
                Main.Log?.Warning("WizardArchetype: 本地化包仍为空，文本暂不注册");
                return;
            }

            foreach (var kv in s_PendingText)
            {
                PushText(kv.Key, kv.Value);
            }

            s_TextFlushed = true;
            Main.Log?.Log($"WizardArchetype: 已注册 {s_PendingText.Count} 条本地化文本");

            VerifyText();
        }

        private static void PushText(string key, string text)
        {
            var pack = LocalizationManager.CurrentPack;
            if (pack == null) return;
            pack.PutString(key, text);
        }

        /// <summary>
        /// 自检：确认写入的文本能被取回。用于定位"特性名显示 null"。
        /// </summary>
        internal static void VerifyText()
        {
            var pack = LocalizationManager.CurrentPack;
            if (pack == null)
            {
                Main.Log?.Error("[本地化校验] CurrentPack 仍为 null");
                return;
            }

            int ok = 0, bad = 0;
            foreach (var kv in s_PendingText)
            {
                var got = pack.GetText(kv.Key, false);
                if (string.IsNullOrEmpty(got) || got.StartsWith("[unknown key")) bad++;
                else ok++;
            }

            Main.Log?.Log($"[本地化校验] 可读 {ok} 条，异常 {bad} 条（共 {s_PendingText.Count}）");

            if (bad > 0)
            {
                var sample = s_PendingText.Keys.FirstOrDefault(k =>
                    string.IsNullOrEmpty(pack.GetText(k, false)) || pack.GetText(k, false).StartsWith("[unknown key"));
                Main.Log?.Error($"[本地化校验] 示例异常 key = {sample}");
            }
        }
    }
}
