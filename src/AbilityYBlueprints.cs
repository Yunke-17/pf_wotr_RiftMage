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
                "【裂隙穿透】",
                "施法者等级 CL +1，但法术豁免 DC -2。\n" +
                "可与同方向再叠加 1 层（最多 2 层）。激活期间【致密收束】不可用。\n" +
                "迅捷动作，仅可在战斗中使用。",
                AbilityYAction.Kind.Up, AbilityYRestriction.Kind.Up,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var down = CreateVariant(cache, AbilityY.GuidDown, "Down",
                "【致密收束】",
                "法术豁免 DC +2，但施法者等级 CL -1。\n" +
                "可与同方向再叠加 1 层（最多 2 层）。激活期间【裂隙穿透】不可用。\n" +
                "迅捷动作，仅可在战斗中使用。",
                AbilityYAction.Kind.Down, AbilityYRestriction.Kind.Down,
                UnitCommand.CommandType.Swift, inCombatOnly: true);

            var cancel = CreateVariant(cache, AbilityY.GuidCancel, "Cancel",
                "解除调谐",
                "提前取消当前的【致密收束】或【裂隙穿透】效果，层数归 0。\n" +
                "自由动作，消耗 1 次使用次数。",
                AbilityYAction.Kind.Cancel, AbilityYRestriction.Kind.Cancel,
                UnitCommand.CommandType.Free, inCombatOnly: true);

            var parent = CreateParent(cache, up, down, cancel);
            Feature = CreateFeature(cache, parent);

            // 图标（用户 2026-10-20 新增需求）：特性 + 父能力 + 三变体
            IconsAndFx.ApplyAbilityY(cache, Feature, parent, up, down, cancel);
            // 层数显示 Buff 的图标
            IconsAndFx.ApplyAbilityYBuffs(cache, AbilityYDisplay.UpBuff, AbilityYDisplay.DownBuff);

            Main.Log?.Log("WizardArchetype: 能力Y 蓝图已构建（+1层 / -1层 / 取消，含图标）");
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
            WizardArchetypeBlueprints.RegisterText(k + ".Name", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(k + ".Desc",
                "空隙法师可在战斗中拨动自身的法力相位。\n" +
                "使用<b>迅捷动作</b>选择以下一种状态激活，效果持续至战斗结束：\n" +
                "· <b>【致密收束】</b>：" + Glossary.Link(Glossary.KDC, "法术 DC") +
                " +2，但" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") + " -1\n" +
                "· <b>【裂隙穿透】</b>：" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                " +1，但" + Glossary.Link(Glossary.KDC, "法术 DC") + " -2\n" +
                "<b>限制</b>\n" +
                "· 同一状态最高叠加 2 层\n" +
                "· 当应用其中一种状态时，另一种状态暂时不可用\n" +
                "· 仅作用于法师法术书的法术\n" +
                "<b>取消</b>\n" +
                "战斗中可消耗<b>自由动作</b>并扣除 1 次使用次数，提前取消【致密收束】或【裂隙穿透】的效果。\n" +
                "<b>使用次数</b>\n" +
                "每次休息后恢复：⌈" + Glossary.Link(Glossary.KIntMod, "智力调整值") +
                " ÷ 2⌉ + ⌈" + Glossary.Link(Glossary.KWizardLevel, "法师等级") + " ÷ 2⌉。");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceName", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceDesc",
                "每次休息后恢复：⌈智力调整值 ÷ 2⌉ + ⌈法师等级 ÷ 2⌉。");
        }
    }
}
