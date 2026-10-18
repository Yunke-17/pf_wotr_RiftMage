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

            var up = CreateVariant(cache, AbilityY.GuidUp, "Up",
                "+1 层：CL +1 / DC -2",
                "施法者等级 +1，法术 DC -2。可与同方向叠加，最多 +2 层。\n迅捷动作，仅战斗中可用。",
                AbilityYAction.Kind.Up, AbilityYRestriction.Kind.Up,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var down = CreateVariant(cache, AbilityY.GuidDown, "Down",
                "-1 层：CL -1 / DC +2",
                "施法者等级 -1，法术 DC +2。可与同方向叠加，最多 -2 层。\n迅捷动作，仅战斗中可用。",
                AbilityYAction.Kind.Down, AbilityYRestriction.Kind.Down,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var cancel = CreateVariant(cache, AbilityY.GuidCancel, "Cancel",
                "取消交换",
                "清除全部施法参数交换效果，层数归 0。\n自由动作，消耗 1 次使用次数。",
                AbilityYAction.Kind.Cancel, AbilityYRestriction.Kind.Cancel,
                UnitCommand.CommandType.Free, inCombatOnly: true);

            var parent = CreateParent(cache, up, down, cancel);
            Feature = CreateFeature(cache, parent);

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
            WizardArchetypeBlueprints.RegisterText(k + ".Name", "施法参数交换");
            WizardArchetypeBlueprints.RegisterText(k + ".Desc",
                "迅捷动作，仅战斗中可用。在施法者等级（CL）与法术 DC 之间交换。\n" +
                "每次 +1 层：CL +1 / DC -2；每次 -1 层：CL -1 / DC +2。\n" +
                "同方向最多 2 层，两个方向互斥。修正持续至战斗结束。\n" +
                "「取消」为自由动作，清除全部层数。");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceName", "施法参数交换次数");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceDesc",
                "每次休息后恢复：ceil(智力调整值 / 2) + ceil(法师等级 / 2)。");
        }
    }
}
