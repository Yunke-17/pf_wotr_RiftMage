using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.ElementsSystem;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Abilities.Components.CasterCheckers;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的蓝图构建（§9 + 用户 2026-10-19 修正）。
    ///
    ///   特性(3级)
    ///     ├─ AddFacts         → 父能力
    ///     ├─ AddAbilityResources → 资源
    ///     ├─ AbilityY.Modifier       ← CL/DC 修正订阅者
    ///     └─ AbilityYResourceSync    ← 次数公式同步
    ///
    ///   父能力
    ///     └─ AbilityVariants → [+1 层, -1 层, 取消]
    ///
    ///   三个子能力各自带：
    ///     AbilityEffectRunAction  → AbilityYAction
    ///     AbilityResourceLogic    → 扣 1 次
    ///     AbilityYRestriction     → 灰显条件（互斥 + 上限）
    ///     AbilityCasterInCombat   → 仅战斗中可用（取消除外）
    /// </summary>
    internal static class AbilityYBlueprints
    {
        internal static BlueprintFeature Feature { get; private set; }
        internal static BlueprintAbilityResource Resource { get; private set; }

        internal static BlueprintFeature Create(BlueprintsCache cache)
        {
            CreateText();
            Resource = CreateResource(cache);

            // 层数显示用 Buff（Buff 栏可见）
            AbilityYDisplay.Create(cache);

            // ★ 2026-10-20（用户要求）：去掉「裂隙穿透 / 致密收束」这组自造名称，
            //   改为照奥能源泉的直白写法 ——
            //   原版写的是「使法术的施法者等级提高1级，或者难度等级（DC）提高1点」，
            //   直接把效果名当按钮名用，不另起花样名。
            //   机制不变（仍是可叠 2 层、正负互斥、可解除），只改称呼与描述。
            var up = CreateVariant(cache, AbilityY.GuidUp, "Up",
                "提高施法者等级",
                "本场战斗内，法师法术的" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                "提高 1 级，但" + Glossary.Link(Glossary.KDC, "DC") + "降低 2 点。\n" +
                "可再使用一次叠加同类效果（最多 2 层）。生效期间无法选择「提高难度等级」。\n" +
                "迅捷动作，仅可在战斗中使用。",
                AbilityYAction.Kind.Up, AbilityYRestriction.Kind.Up,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var down = CreateVariant(cache, AbilityY.GuidDown, "Down",
                "提高难度等级",
                "本场战斗内，法师法术的" + Glossary.Link(Glossary.KDC, "DC") +
                "提高 2 点，但" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") + "降低 1 级。\n" +
                "可再使用一次叠加同类效果（最多 2 层）。生效期间无法选择「提高施法者等级」。\n" +
                "迅捷动作，仅可在战斗中使用。",
                AbilityYAction.Kind.Down, AbilityYRestriction.Kind.Down,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var cancel = CreateVariant(cache, AbilityY.GuidCancel, "Cancel",
                "解除调谐",
                "立即取消当前的施法者等级与难度等级调整，层数归 0。\n" +
                "自由动作，消耗 1 次使用次数。",
                AbilityYAction.Kind.Cancel, AbilityYRestriction.Kind.Cancel,
                UnitCommand.CommandType.Free, inCombatOnly: true);

            var parent = CreateParent(cache, up, down, cancel);
            Feature = CreateFeature(cache, parent);

            // 图标 / 音效统一由 Presentation.Apply 装配（见 Presentation.cs 的表）

            Main.Log?.Log("WizardArchetype: 能力Y 蓝图已构建（+1层 / -1层 / 取消）");
            return Feature;
        }

        // ---------- 资源 ----------

        /// <summary>
        /// 资源上限由 AbilityYResourceSync 在运行期写入（公式含除法，蓝图做不到）。
        /// 这里只给 BaseValue = 0 打底，避免初始值干扰。
        /// </summary>
        private static BlueprintAbilityResource CreateResource(BlueprintsCache cache)
        {
            var res = new BlueprintAbilityResource
            {
                name = "WizardArchetype_AbilityYResource",
                AssetGuid = BlueprintGuid.Parse(AbilityY.GuidResource),
                LocalizedName = new LocalizedString { Key = "WizardArchetype.AbilityY.ResourceName" },
                LocalizedDescription = new LocalizedString { Key = "WizardArchetype.AbilityY.ResourceDesc" },
            };

            var maxField = typeof(BlueprintAbilityResource).GetField(
                "m_MaxAmount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (maxField != null)
            {
                object amount = maxField.GetValue(res);
                amount.GetType().GetField("BaseValue")?.SetValue(amount, 0);
                maxField.SetValue(res, amount);
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(AbilityY.GuidResource), res);
            return res;
        }

        // ---------- 子能力 ----------

        private static BlueprintAbility CreateVariant(BlueprintsCache cache, string guid, string keyBase,
            string name, string desc,
            AbilityYAction.Kind actionKind,
            AbilityYRestriction.Kind restrictKind,
            UnitCommand.CommandType commandType,
            bool inCombatOnly)
        {
            const string keyRoot = "WizardArchetype.AbilityY.";
            WizardArchetypeBlueprints.RegisterText(keyRoot + keyBase + ".Name", name);
            WizardArchetypeBlueprints.RegisterText(keyRoot + keyBase + ".Desc", desc);

            var comps = new List<BlueprintComponent>();

            var action = new AbilityYAction
            {
                name = "$AbilityYAction$" + keyBase,
                Action = actionKind,
            };

            var run = new AbilityEffectRunAction
            {
                name = "$AbilityEffectRunAction$" + keyBase,
                SavingThrowType = SavingThrowType.Unknown,
            };
            run.Actions = new ActionList { Actions = new GameAction[] { action } };
            comps.Add(run);

            var logic = new AbilityResourceLogic
            {
                name = "$AbilityResourceLogic$" + keyBase,
                Amount = 1,
                CostIsCustom = false,
                ResourceCostIncreasingFacts = new List<BlueprintUnitFactReference>(),
                ResourceCostDecreasingFacts = new List<BlueprintUnitFactReference>(),
            };
            ReflectionFields.Set(logic, "m_RequiredResource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));
            ReflectionFields.Set(logic, "m_IsSpendResource", true);
            comps.Add(logic);

            // 灰显：互斥 + 上限 + 无层数时不能取消
            comps.Add(new AbilityYRestriction
            {
                name = "$AbilityYRestriction$" + keyBase,
                Allow = restrictKind,
            });

            // 仅战斗中可用（取消也是——战斗外层数本就为 0）
            if (inCombatOnly)
            {
                comps.Add(new AbilityCasterInCombat
                {
                    name = "$AbilityCasterInCombat$" + keyBase,
                });
            }

            var ability = new BlueprintAbility
            {
                name = "WizardArchetype_AbilityY_" + keyBase,
                AssetGuid = BlueprintGuid.Parse(guid),
                Type = AbilityType.Supernatural,
                Range = AbilityRange.Personal,
                ActionType = commandType,
                SpellResistance = false,
                ComponentsArray = comps.ToArray(),
            };

            SetNameAndDesc(ability, keyRoot + keyBase + ".Name", keyRoot + keyBase + ".Desc");

            // 空 key 的 LocalizedString（不是 null）：UI 判定为「无此字段」并整行隐藏。
            // 不给的话 UI 会显示 "<null>"（2026-10-21 用户反馈）。
            ability.LocalizedDuration = new LocalizedString();
            ability.LocalizedSavingThrow = new LocalizedString();

            foreach (var c in ability.ComponentsArray)
                if (c != null) c.OwnerBlueprint = ability;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(guid), ability);
            return ability;
        }

        private static BlueprintAbility CreateParent(BlueprintsCache cache,
            BlueprintAbility up, BlueprintAbility down, BlueprintAbility cancel)
        {
            var variants = new AbilityVariants { name = "$AbilityVariants$AbilityY" };
            ReflectionFields.Set(variants, "m_Variants",
                new BlueprintAbilityReference[]
                {
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(up),
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(down),
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(cancel),
                });

            var parent = new BlueprintAbility
            {
                name = "WizardArchetype_AbilityY",
                AssetGuid = BlueprintGuid.Parse(AbilityY.GuidParent),
                Type = AbilityType.Supernatural,
                Range = AbilityRange.Personal,
                ActionType = UnitCommand.CommandType.Swift,
                SpellResistance = false,
                ComponentsArray = new BlueprintComponent[] { variants },
            };

            SetNameAndDesc(parent, "WizardArchetype.AbilityY.Name", "WizardArchetype.AbilityY.Desc");

            parent.LocalizedDuration = new LocalizedString();
            parent.LocalizedSavingThrow = new LocalizedString();

            foreach (var c in parent.ComponentsArray)
                if (c != null) c.OwnerBlueprint = parent;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(AbilityY.GuidParent), parent);
            return parent;
        }

        private static BlueprintFeature CreateFeature(BlueprintsCache cache, BlueprintAbility parent)
        {
            var addFacts = new AddFacts { name = "$AddFacts$AbilityY" };
            ReflectionFields.Set(addFacts, "m_Facts",
                new BlueprintUnitFactReference[]
                {
                    BlueprintReference<BlueprintUnitFact>.CreateTyped<BlueprintUnitFactReference>(parent),
                });

            var addRes = new AddAbilityResources
            {
                name = "$AddAbilityResources$AbilityY",
                UseThisAsResource = false,
                Amount = 0,
                RestoreAmount = false,
                RestoreOnLevelUp = false,
            };
            ReflectionFields.Set(addRes, "m_Resource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));

            var modifier = new AbilityY.Modifier { name = "$AbilityYModifier$AbilityY" };
            var sync = new AbilityYResourceSync { name = "$AbilityYResourceSync$AbilityY" };

            var feature = new BlueprintFeature
            {
                name = "WizardArchetype_AbilityYFeature",
                AssetGuid = BlueprintGuid.Parse(AbilityY.GuidFeature),
                Ranks = 1,
                IsClassFeature = true,
                HideInUI = false,
                ComponentsArray = new BlueprintComponent[] { addFacts, addRes, modifier, sync },
            };

            SetNameAndDesc(feature, "WizardArchetype.AbilityY.Name", "WizardArchetype.AbilityY.Desc");

            foreach (var c in feature.ComponentsArray)
                if (c != null) c.OwnerBlueprint = feature;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(AbilityY.GuidFeature), feature);
            return feature;
        }

        // ---------- 辅助 ----------

        private static void SetNameAndDesc(SimpleBlueprint bp, string nameKey, string descKey)
        {
            ReflectionFields.Set(bp, "m_DisplayName", new LocalizedString { Key = nameKey });
            ReflectionFields.Set(bp, "m_Description", new LocalizedString { Key = descKey });
        }

        private static void CreateText()
        {
            const string k = "WizardArchetype.AbilityY";
            WizardArchetypeBlueprints.RegisterText(k + ".Name", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(k + ".Desc",
                "空隙法师可在战斗中拨动自身的法力相位。以一个" +
                Glossary.Link(Glossary.KSwiftAction, "迅捷动作") +
                "选择提高" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                "或提高" + Glossary.Link(Glossary.KDC, "DC") + "，效果持续至战斗结束。\n" +
                "选择「提高施法者等级」时，其法师法术的施法者等级提高 1 级，但 DC 降低 2 点；" +
                "选择「提高难度等级」时，其法师法术的 DC 提高 2 点，但施法者等级降低 1 级。\n" +
                "同类效果最多可叠加 2 层；当其中一种效果生效时，另一种暂时无法选择。" +
                "该效果只作用于来自法师法术书的" + Glossary.Link(Glossary.KSpell, "法术") + "。\n" +
                "战斗中，空隙法师还可消耗一个" +
                Glossary.Link(Glossary.KFreeAction, "自由动作") +
                "与 1 次使用次数，提前解除已生效的调整。\n" +
                "每次休息后，使用次数会恢复至 ⌈" +
                Glossary.Link(Glossary.KIntelligence, "智力") + "调整值 / 2⌉ + ⌈" +
                Glossary.Link(Glossary.KClassLevel, "职业等级") + " / 2⌉。");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceName", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceDesc",
                "每次休息后恢复：⌈" + Glossary.Link(Glossary.KIntelligence, "智力") +
                "调整值 / 2⌉ + ⌈" + Glossary.Link(Glossary.KClassLevel, "职业等级") + " / 2⌉。");
        }
    }
}
