using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Localization;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 空隙法师专属「幸运野兔」魔宠（用户 2026-10-20 需求）。
    ///
    /// ========== 为什么需要克隆而不是复用原版 ==========
    ///
    /// 原版野兔魔宠 97dff21a（HareFamiliarBondFeature）是**共享蓝图**，
    /// 法师本来就能通过「奥法契约」在 ArcaneBondSelection.m_AllFeatures 第 7 项选到它。
    /// 若直接改原版蓝图，会连带影响普通法师、女巫、萨满等所有能拿魔宠的职业。
    ///
    /// 因此这里**完整克隆一条独立链**：
    ///     Feature(合并特性) → Ability(可激活) → Buff(AddFamiliar + PrefabLink)
    /// 只挂在空隙法师变体上，原版蓝图一个字节都不动。
    ///
    /// ========== 数值为什么全放在特性上 ==========
    ///
    /// 用户要求「魔宠本身不给数值，只做载体」。所以这里的 Buff **只挂 AddFamiliar**，
    /// 不挂任何 AddStatBonus —— 先攻 / 察觉 / 穿透 / 双骰全部由
    /// WizardArchetypeFeatures 的合并特性提供，避免与魔宠自带加值重复计算。
    ///
    /// ========== PrefabLink 为什么要照抄原版 ==========
    ///
    /// AddFamiliar 是唯一在场景里生成跟随模型的组件（AddFamiliar.cs:44-67），
    /// 它读 FamiliarLink.AssetId 去 ResourcesLibrary 里取预制体。
    /// 我们不能凭空造资源，只能引用原版已打包的野兔预制体 b8ac96b2…。
    /// </summary>
    internal static class HareFamiliar
    {
        // GUID 段位：b100…0060 起（0060 已被占用的话往下顺延，见下方实际值）
        internal const string GuidAbility = "b1000000000000000000000000000060";
        internal const string GuidBuff    = "b1000000000000000000000000000061";

        /// <summary>原版野兔魔宠的预制体（HareFamiliarBuff.jbp 的 AddFamiliar.PrefabLink）。</summary>
        private const string HarePrefabAssetId = "b8ac96b2038460241bc59ae56bddac21";

        /// <summary>原版野兔魔宠图标的来源蓝图。</summary>
        private const string VanillaHareAbilityGuid = "1194129055923ae46a6b876ff6a30358";

        private const string KeyName = "WizardArchetype.HareFamiliar.Name";
        private const string KeyDesc = "WizardArchetype.HareFamiliar.Desc";

        internal static BlueprintActivatableAbility Ability { get; private set; }
        internal static BlueprintBuff Buff { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            if (Buff != null) return;

            CreateText();

            Buff = CreateBuff(cache);
            Ability = CreateAbility(cache, Buff);

            IconsAndFx.CopyIcon(cache, Ability, VanillaHareAbilityGuid);
            IconsAndFx.CopyIcon(cache, Buff, VanillaHareAbilityGuid);

            Main.Log?.Log("WizardArchetype: 已创建专属野兔魔宠（Buff + Ability）");
        }

        // ---------- Buff：只负责生成模型 ----------

        private static BlueprintBuff CreateBuff(BlueprintsCache cache)
        {
            var familiar = new AddFamiliar
            {
                name = "$AddFamiliar$HareFamiliar_0",
                PrefabLink = new FamiliarLink { AssetId = HarePrefabAssetId },
                HideInCapital = false,
            };

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_HareFamiliarBuff",
                AssetGuid = BlueprintGuid.Parse(GuidBuff),

                // 与原版野兔一致：死亡不消失、Replace 叠放
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = true,

                ComponentsArray = new BlueprintComponent[] { familiar },

                // 玩家棋子必定带 ParticlesSnapMap，到期移除时会读 FxOnRemove，为 null 会 NRE
                FxOnRemove = new PrefabLink(),
            };

            SetNameAndDesc(buff, KeyName, KeyDesc);

            foreach (var c in buff.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = buff;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidBuff), buff);
            return buff;
        }

        // ---------- Ability：可激活能力，默认开启 ----------

        /// <summary>
        /// 原版魔宠能力 IsOnByDefault = false，玩家要自己去能力栏点开。
        /// 这里改成 **true**：用户要的是「职业自带的魔宠」，不该再手动激活一次。
        ///
        /// Group 保持 Familiar，这样它和原版魔宠归在同一组、不会挤占普通动作栏。
        /// </summary>
        private static BlueprintActivatableAbility CreateAbility(BlueprintsCache cache, BlueprintBuff buff)
        {
            var ability = new BlueprintActivatableAbility
            {
                name = "WizardArchetype_HareFamiliarAbility",
                AssetGuid = BlueprintGuid.Parse(GuidAbility),
                Group = ActivatableAbilityGroup.Familiar,
                WeightInGroup = 1,
                IsOnByDefault = true,
                OnlyInCombat = false,
                DeactivateIfCombatEnded = false,
                DeactivateAfterFirstRound = false,
                DeactivateIfOwnerDisabled = false,
                DeactivateIfOwnerUnconscious = false,
                HiddenInUI = false,
                ActivationType = AbilityActivationType.Immediately,
            };

            // m_Buff 是私有字段（BlueprintActivatableAbility），只能反射写
            ReflectionFields.Set(ability, "m_Buff",
                BlueprintReference<BlueprintBuff>.CreateTyped<BlueprintBuffReference>(buff));

            SetNameAndDesc(ability, KeyName, KeyDesc);

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidAbility), ability);
            return ability;
        }

        // ---------- 文本 ----------

        private static void SetNameAndDesc(SimpleBlueprint bp, string nameKey, string descKey)
        {
            ReflectionFields.Set(bp, "m_DisplayName", new LocalizedString { Key = nameKey });
            ReflectionFields.Set(bp, "m_Description", new LocalizedString { Key = descKey });
        }

        private static void CreateText()
        {
            WizardArchetypeBlueprints.RegisterText(KeyName, "幸运野兔");

            // 魔宠只做载体，所以描述里明确写「不额外提供加值」，
            // 免得玩家以为漏了什么（原版野兔自带先攻+4/察觉+2，我们刻意不带）。
            WizardArchetypeBlueprints.RegisterText(KeyDesc,
                "一只由现实缝隙中钻出的契约野兔，会跟随在空隙法师身边。" +
                "它本身不提供任何" + Glossary.Link(Glossary.KInitiative, "先攻") +
                "或技能加值——它的意义在于作为空隙法师与时间裂隙之间的联系，" +
                "相关的增益均由「幸运野兔」特性本身提供。");
        }
    }
}
