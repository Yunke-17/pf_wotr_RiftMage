using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.ElementsSystem;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 表现层总装（2026-10-21）：把"图标 / 光环 / 音效"集中成一张表。
    ///
    /// ========== 图标为什么是三元组 ==========
    ///
    /// 第一轮只给 Sprite 资源 GUID 时，ResourcesLibrary.TryGetResource&lt;Sprite&gt;
    /// 静默返回 null（26/26 全灭，见 Player.log）。所以每一项现在同时给：
    ///   · Sprite 资源 GUID   —— 资源库直取（首选）
    ///   · 源蓝图 GUID        —— 回退：读原版蓝图的 .Icon（野兔已验证可行）
    /// 两条路都失败时，IconsAndFx.Report 会打印 bundle 索引 / 缓存状态，便于定位。
    ///
    /// ========== 光环 ==========
    ///
    /// 用原版 Buff 的 FxOnStart（PrefabLink）。第一轮写进去了但用户反馈"看不到"，
    /// 所以第二轮加了 VerifyFxAssets：启动后主动加载一次 prefab 并打日志，
    /// 直接区分"没配上"和"配上了但加载不出来"。
    ///
    /// ========== 能力施法特效 ==========
    ///
    /// AbilitySpawnFx 需要一次性施法 prefab，观感必须进游戏看，这一轮仍未启用。
    /// 候选：Haste 的 OnApplyEffect 特效 3cf0849b9b4e4344a8b528ff62b28d4b（Anchor=SelectedTarget）；
    ///       时间停止区域特效 ab46363598ce88c4f8ff4519f38f770d（偏大）。
    /// </summary>
    internal static class Presentation
    {
        // ================= 图标资源（Sprite GUID） =================

        private const string IconSpellMasterFocus = "5f31e79573fa7ab43a1b4de6bb210182";
        private const string IconReservoirFeature = "9adc084dc47b0c44eaf3bd92e7732c34";
        private const string IconReservoirCL = "8bafdce16dc5eb944a7bb50adf5c9f0f";
        private const string IconReservoirDC = "a2bdfb72874bf314e843842c397ed22b";
        private const string IconReservoirBase = "016f3d82951105c4483bbda36d1e5f7e";
        private const string IconReservoir = "5c81b8937956a0a45b6575dcc144d57f";
        private const string IconTimeStop = "6bb07fab223b13c4baf87bee8208604c";
        private const string IconEdictOfPredetermination = "895c67e4fce6d6c4cb9fef8c37e40f7d";
        private const string IconImprovedInitiative = "812f653e135412c4d862a67fc9c68dab";
        private const string IconAlertness = "d868e0674e3789840a08e209211b5259";
        private const string IconCombatCasting = "5dbcab99c71c9724fb6750aae4f7bc25";
        private const string IconHareBond = "736cb7b0def6ff147b921c75c54a81a0";
        private const string IconSpellPenetration = "bc1f3a0b73f66074e89c446418a15bc9";
        private const string IconPerfectPrediction = "fe2e07682aa16494785b502da6b27f12";
        private const string IconQuickenSpell = "ed900c6a7b1c40341b9aa325c6068e37";
        private const string IconStaggered = "990cc5572d19fac4fa5839bb92514415";
        private const string IconEntangled = "6d9403c18a2ccfc4bbd0917562f51866";

        // ================= 图标来源蓝图（回退用） =================

        private const string BpTimeStop = "70e55e5b0ac84af78c084eb6cf47ae06";
        private const string BpSpellMasterFocus = "58d47da6361d465489bb9bcaa2f853c9";
        private const string BpEdictOfPredetermination = "3f205f55a6e7759449772feb34edc378";
        private const string BpImprovedInitiative = "797f25d709f559546b29e7bcb181cc74";
        private const string BpAlertness = "1c04fe9a13a22bc499ffac03e6f79153";
        private const string BpCombatCasting = "06964d468fde1dc4aa71a92ea04d930d";
        private const string BpQuickenSpell = "ef7ece7bb5bb66a41b256976b27f424e";
        private const string BpHareBond = "97dff21a036e80948b07097ad3df2b30";
        private const string BpSpellPenetration = "ee7dc126939e4d9438357fbd5980d459";
        private const string BpPerfectPrediction = "45094504ee3041f98ebf97ab1791e3d9";
        private const string BpReservoirFeature = "55db1859bd72fd04f9bd3fe1f10e4cbb";
        private const string BpReservoirBase = "91295893ae9fdfb4b8936a93eff019df";
        private const string BpReservoirCL = "0b10e8506f321f54eb4a4b39dd18d959";
        private const string BpReservoirDC = "36f81ca4bf1066e48a96866a32329c19";
        private const string BpReservoir = "45feafde36644d03b183110905cb85c7";
        private const string BpStaggered = "df3950af5a783bd4d91ab73eb8fa0fd3";
        private const string BpEntangled = "d1aea643c260c5e4ea66012876f2b7f5";

        // ================= 光环（PrefabLink AssetId） =================

        /// <summary>原版「缓慢」的持续光环（冷色、拖拽感）。</summary>
        private const string FxSlowedAura = "d6a8c6aead8ec1e4b9362cff1c2373a2";

        /// <summary>原版「困惑」的持续光环（紫雾、精神类）。备选：Haste 91ef30ab…、Daze 396af91a…</summary>
        private const string FxConfusionAura = "602fa850c4a94d84eb8aa1bcc0d008c7";

        // ================= 音效（Wwise 事件名，取自 VFX.bnk） =================

        private const string SndAeonDispel = "Mythic4lvlAeon_AeonDispel_Play";
        private const string SndAeonDispelAmbient = "Mythic4lvlAeon_AeonDispel00_Ambient_Play";
        private const string SndAbsoluteOrder = "Mythic5lvlAeon_AbosluteOrder00_Play";
        private const string SndUncertainty = "Mythic4lvlAeon_UncertanityPrinciple00_Spawn";
        private const string SndAbsoluteFocus = "Mythic1lvlAeon_AbsoluteFocus_Play";

        // 2026-10-21 第二轮：调谐 +1/-1 原来用的 Starlight In/Out（2.2/2.4s）太"大"，
        // 换成两个 1.7~1.8s 的 buff 类事件。
        private const string SndTuneUp = "Mythic4lvlAeon_UncertanityPrinciple00_CasterBuff";
        private const string SndTuneDown = "Mythic5lvlAeon_SphereOfOblivion_Out_Play";

        // ================= 表 =================

        private static readonly (string Guid, string Icon, string Source, string Label)[] s_Icons =
        {
            // —— 变体本体 ——
            ("a1b2c3d4e5f6478990a1b2c3d4e5f601", IconTimeStop, BpTimeStop, "变体：空隙法师"),

            // —— 能力X 空隙湮灭 ——
            (AbilityX.GuidFeature,  IconSpellMasterFocus, BpSpellMasterFocus, "空隙湮灭（特性）"),
            (AbilityX.GuidAbility,  IconSpellMasterFocus, BpSpellMasterFocus, "空隙湮灭（能力）"),
            (AbilityX.GuidBuff,     IconSpellMasterFocus, BpSpellMasterFocus, "空隙湮灭（状态）"),
            (AbilityX.GuidResource, IconSpellMasterFocus, BpSpellMasterFocus, "空隙湮灭（资源）"),

            // —— 开场时序 ——
            (OpeningTimingFeature.GuidFeature, IconEdictOfPredetermination, BpEdictOfPredetermination, "开场时序"),

            // —— 零散被动特性 ——
            (WizardArchetypeFeatures.GuidInitiativeInt,   IconImprovedInitiative, BpImprovedInitiative, "敏锐"),
            (WizardArchetypeFeatures.GuidPerceptionInt,   IconAlertness,          BpAlertness,          "敏锐推演"),
            (WizardArchetypeFeatures.GuidConcentration,   IconCombatCasting,      BpCombatCasting,      "空隙思维"),
            (WizardArchetypeFeatures.GuidQuickenCostNote, IconQuickenSpell,      BpQuickenSpell,       "精通瞬发"),

            // —— 幸运野兔系 ——
            (WizardArchetypeFeatures.GuidHareUnified,      IconHareBond,          BpHareBond,          "幸运野兔"),
            (WizardArchetypeFeatures.GuidSpellPen,         IconSpellPenetration,  BpSpellPenetration,  "幸运野兔：裂隙直觉"),
            (WizardArchetypeFeatures.GuidInitiativeDouble, IconPerfectPrediction, BpPerfectPrediction, "幸运野兔：时序预感"),

            // —— 能力Y 空隙调谐 ——
            (AbilityY.GuidFeature, IconReservoirFeature, BpReservoirFeature, "空隙调谐（特性）"),
            (AbilityY.GuidParent,  IconReservoirBase,    BpReservoirBase,    "空隙调谐（父能力）"),
            (AbilityY.GuidUp,      IconReservoirCL,      BpReservoirCL,      "提高施法者等级"),
            (AbilityY.GuidDown,    IconReservoirDC,      BpReservoirDC,      "提高难度等级"),
            (AbilityY.GuidCancel,  IconReservoir,        BpReservoir,        "解除调谐"),
            (AbilityYDisplay.GuidUpBuff,   IconReservoirCL, BpReservoirCL, "提高施法者等级（状态）"),
            (AbilityYDisplay.GuidDownBuff, IconReservoirDC, BpReservoirDC, "提高难度等级（状态）"),
            (AbilityY.GuidResource, IconReservoirFeature, BpReservoirFeature, "空隙调谐（资源）"),

            // —— 能力Z 空隙透支 ——
            (AbilityZ.GuidFeature,  IconReservoirBase, BpReservoirBase, "空隙透支（特性）"),
            (AbilityZ.GuidAbility,  IconReservoirBase, BpReservoirBase, "空隙透支（能力）"),
            (AbilityZ.GuidResource, IconReservoirBase, BpReservoirBase, "空隙透支（资源）"),

            // —— 两个时序状态 ——
            (SpecialStatuses.GuidSpecialStaggered,           IconStaggered, BpStaggered, "时序恍惚"),
            (SpecialEntangleBlueprints.GuidSpecialEntangled, IconEntangled, BpEntangled, "时序专注"),
        };

        private static readonly (string Guid, string Aura, string Label)[] s_BuffAuras =
        {
            (SpecialStatuses.GuidSpecialStaggered,           FxSlowedAura,    "时序恍惚"),
            (SpecialEntangleBlueprints.GuidSpecialEntangled, FxConfusionAura, "时序专注"),
            (AbilityX.GuidBuff, null, "空隙湮灭（清除恐惧占位）"),
        };

        private static readonly (string Guid, string Sound, string Key)[] s_AbilitySounds =
        {
            (AbilityX.GuidAbility, SndAeonDispel,        "AbilityX"),
            (AbilityY.GuidUp,      SndTuneUp,            "AbilityYUp"),
            (AbilityY.GuidDown,    SndTuneDown,          "AbilityYDown"),
            (AbilityY.GuidCancel,  SndAeonDispelAmbient, "AbilityYCancel"),
            (AbilityZ.GuidAbility, SndAbsoluteOrder,     "AbilityZ"),
        };

        private static readonly (string Guid, string Sound, string Key)[] s_BuffSounds =
        {
            (SpecialStatuses.GuidSpecialStaggered,           SndUncertainty,   "Staggered"),
            (SpecialEntangleBlueprints.GuidSpecialEntangled, SndAbsoluteFocus, "Entangled"),
        };

        // ================= 装配 =================

        internal static void Apply(BlueprintsCache cache)
        {
            if (cache == null) return;

            int icons = ApplyIcons(cache);
            ApplyBuffAuras(cache);
            ApplyAbilitySounds(cache);
            ApplyBuffSounds(cache);

            Main.Log?.Log("[表现层] 图标排队 " + icons + " 个；光环 " + s_BuffAuras.Length +
                          " 项、能力音效 " + s_AbilitySounds.Length + " 项、状态音效 " +
                          s_BuffSounds.Length + " 项已装配");
        }

        /// <summary>由 LocalizationTimingPatch 在图标加载之后调用：检查光环 prefab 能否加载。</summary>
        internal static void VerifyFxAssets()
        {
            foreach (var e in s_BuffAuras)
            {
                if (string.IsNullOrEmpty(e.Aura)) continue;
                IconsAndFx.VerifyFxPrefab(e.Aura, e.Label);
            }
        }

        private static int ApplyIcons(BlueprintsCache cache)
        {
            int n = 0;
            foreach (var e in s_Icons)
            {
                var bp = Load(cache, e.Guid);
                if (bp == null) continue;
                IconsAndFx.QueueIcon(bp, e.Icon, e.Source, e.Label);
                n++;
            }
            return n;
        }

        private static void ApplyBuffAuras(BlueprintsCache cache)
        {
            foreach (var e in s_BuffAuras)
            {
                var buff = Load(cache, e.Guid) as BlueprintBuff;
                if (buff == null) continue;

                IconsAndFx.SetFxOnStart(buff, e.Aura);
                IconsAndFx.SetFxOnRemove(buff, null);

                Main.Log?.Log("[表现层] 光环 " + e.Label + " ← " +
                              (string.IsNullOrEmpty(e.Aura) ? "（无）" : e.Aura));
            }
        }

        private static void ApplyAbilitySounds(BlueprintsCache cache)
        {
            foreach (var e in s_AbilitySounds)
            {
                var ability = Load(cache, e.Guid) as BlueprintAbility;
                if (ability == null) continue;
                AddAbilitySound(ability, e.Sound, e.Key);
            }
        }

        private static void ApplyBuffSounds(BlueprintsCache cache)
        {
            foreach (var e in s_BuffSounds)
            {
                var buff = Load(cache, e.Guid) as BlueprintBuff;
                if (buff == null) continue;
                AddBuffSound(buff, e.Sound, e.Key);
            }
        }

        /// <summary>把 PlaySoundAction 追加到能力的 AbilityEffectRunAction 里（幂等）。</summary>
        private static void AddAbilitySound(BlueprintAbility ability, string evt, string key)
        {
            AbilityEffectRunAction run = null;
            if (ability.ComponentsArray != null)
            {
                foreach (var c in ability.ComponentsArray)
                {
                    run = c as AbilityEffectRunAction;
                    if (run != null) break;
                }
            }

            if (run == null)
            {
                Main.Log?.Warning("[音效] " + ability.name + " 没有 AbilityEffectRunAction，跳过");
                return;
            }

            if (run.Actions == null) run.Actions = new ActionList();
            var old = run.Actions.Actions ?? new GameAction[0];

            foreach (var a in old)
            {
                var p = a as PlaySoundAction;
                if (p != null && p.SoundName == evt) return;
            }

            var list = new List<GameAction>(old);
            list.Add(new PlaySoundAction { name = "$PlaySoundAction$" + key, SoundName = evt });
            run.Actions.Actions = list.ToArray();

            Main.Log?.Log("[音效] " + ability.name + " ← " + evt);
        }

        /// <summary>
        /// 给 Buff 加一个 AddFactContextActions，激活时播放音效（幂等）。
        /// 挂 Buff 而不是"施放它的能力"，是因为两个状态有多条施加路径。
        /// </summary>
        private static void AddBuffSound(BlueprintBuff buff, string evt, string key)
        {
            var comps = buff.ComponentsArray ?? new BlueprintComponent[0];
            foreach (var c in comps)
            {
                var ctx = c as AddFactContextActions;
                if (ctx != null && ctx.name != null && ctx.name.Contains(key)) return;
            }

            var comp = new AddFactContextActions
            {
                name = "$AddFactContextActions$" + key,
                Activated = new ActionList
                {
                    Actions = new GameAction[]
                    {
                        new PlaySoundAction { name = "$PlaySoundAction$" + key, SoundName = evt },
                    },
                },
                Deactivated = new ActionList { Actions = new GameAction[0] },
                NewRound = new ActionList { Actions = new GameAction[0] },
            };
            comp.OwnerBlueprint = buff;

            var list = new List<BlueprintComponent>(comps);
            list.Add(comp);
            buff.ComponentsArray = list.ToArray();

            Main.Log?.Log("[音效] " + buff.name + " ← " + evt);
        }

        private static SimpleBlueprint Load(BlueprintsCache cache, string guid)
        {
            try
            {
                var bp = cache.Load(BlueprintGuid.Parse(guid));
                if (bp == null) Main.Log?.Warning("[表现层] 找不到蓝图：" + guid);
                return bp;
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[表现层] 读取蓝图失败 " + guid + "：" + e.Message);
                return null;
            }
        }
    }
}
