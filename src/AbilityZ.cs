using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Blueprints.TurnBasedModifiers;
using Kingmaker.Designers;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Abilities.Components.CasterCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z「透支充能」（说明书 §10）。
    ///
    /// ========== 规则 ==========
    ///
    ///   解锁：基础 CL ≥ 7
    ///   次数：CL 7–11 → 1 次；12–16 → 2 次；17+ → 3 次（每次休息恢复）
    ///   动作：整轮动作
    ///   条件：① 战斗中 ② 有可用次数 ③ 能力X 无充能 ④ 无未到期强化状态
    ///   效果：消耗 1 次 Z → 能力X 恢复 1 次（不超 1）
    ///         同时获得 30 秒力竭 + 60 秒疲劳
    ///
    /// ========== 为什么不用原版 Fatigued/Exhausted 的解除联动 ==========
    ///
    /// 原版这两个 Buff 自带 AddCondition 与「力竭解除后转疲劳」的联动。
    /// 需求是「两者同时开始计时，力竭结束后疲劳继续」——
    /// 如果复用原版联动，力竭到期会再次施加疲劳（覆盖我们的 60 秒计时）。
    ///
    /// 做法：**自建两个独立 Buff**，只挂 AddCondition（Exhausted / Fatigued），
    /// 不带任何解除联动，时长用秒级（DurationSeconds），
    /// 从而满足「不因战斗结束提前清除」（§10.3 / E17）。
    /// </summary>
    internal static class AbilityZ
    {
        internal const string GuidFeature = "b1000000000000000000000000000040";
        internal const string GuidResource = "b1000000000000000000000000000041";
        internal const string GuidAbility = "b1000000000000000000000000000042";
        internal const string GuidExhaustedBuff = "b1000000000000000000000000000043";
        internal const string GuidFatiguedBuff = "b1000000000000000000000000000044";

        /// <summary>解锁所需基础 CL（§10.1）。</summary>
        internal const int UnlockCasterLevel = 7;

        internal static BlueprintFeature Feature { get; private set; }
        internal static BlueprintAbilityResource Resource { get; private set; }
        internal static BlueprintAbility Ability { get; private set; }

        internal static BlueprintBuff ExhaustedBuff { get; private set; }
        internal static BlueprintBuff FatiguedBuff { get; private set; }

        internal static BlueprintFeature Create(BlueprintsCache cache)
        {
            CreateText();

            ExhaustedBuff = CreateConditionBuff(cache, GuidExhaustedBuff, "ZExhausted",
                "透支（力竭）",
                "力竭：攻击检定、豁免检定、技能检定 -2，移动速度降低。\n" +
                "由「透支充能」造成，持续 30 秒，不因战斗结束提前清除。",
                UnitCondition.Exhausted);

            FatiguedBuff = CreateConditionBuff(cache, GuidFatiguedBuff, "ZFatigued",
                "透支（疲劳）",
                "疲劳：攻击检定、豁免检定、技能检定 -1，无法奔跑或冲锋。\n" +
                "由「透支充能」造成，持续 60 秒，不因战斗结束提前清除。",
                UnitCondition.Fatigued);

            Resource = CreateResource(cache);
            Ability = CreateAbility(cache);
            Feature = CreateFeature(cache);

            Main.Log?.Log("WizardArchetype: 能力Z 蓝图已构建");
            return Feature;
        }

        // ---------- 条件 Buff ----------

        private static BlueprintBuff CreateConditionBuff(BlueprintsCache cache, string guid, string keyBase,
            string name, string desc, UnitCondition condition)
        {
            const string root = "WizardArchetype.AbilityZ.";
            WizardArchetypeBlueprints.RegisterText(root + keyBase + ".Name", name);
            WizardArchetypeBlueprints.RegisterText(root + keyBase + ".Desc", desc);

            var add = new Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition
            {
                name = "$BuffStatusCondition$" + keyBase,
                Condition = condition,
                SaveEachRound = false,
                SaveType = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown,
            };

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_" + keyBase,
                AssetGuid = BlueprintGuid.Parse(guid),
                Stacking = StackingType.Replace,
                // Frequency 只影响「每轮/每分钟」类效果；实际时长由 AddBuff 的 TimeSpan 决定。
                // DurationRate 没有 Seconds 枚举值（Rounds/Minutes/TenMinutes/Hours/Days）。
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = false,
                ComponentsArray = new BlueprintComponent[] { add },
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            ReflectionFields.Set(buff, "m_DisplayName", new LocalizedString { Key = root + keyBase + ".Name" });
            ReflectionFields.Set(buff, "m_Description", new LocalizedString { Key = root + keyBase + ".Desc" });

            foreach (var c in buff.ComponentsArray)
                if (c != null) c.OwnerBlueprint = buff;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(guid), buff);
            return buff;
        }

        // ---------- 资源：按 CL 分档 ----------

        /// <summary>
        /// CL 7–11 → 1；12–16 → 2；17+ → 3。
        ///
        /// 用蓝图原生能力表达：
        ///   IncreasedByLevelStartPlusDivStep + StartingLevel=7 + LevelStep=5 + PerStepIncrease=1
        /// 即「从 7 级起，每 5 级 +1」。
        /// 但这里的「等级」是**职业等级**，而需求是**基础 CL**（含神话合书）。
        /// 因此仍由 AbilityZResourceSync 在休息后按真实 CL 校正，蓝图值只作打底。
        /// </summary>
        private static BlueprintAbilityResource CreateResource(BlueprintsCache cache)
        {
            var res = new BlueprintAbilityResource
            {
                name = "WizardArchetype_AbilityZResource",
                AssetGuid = BlueprintGuid.Parse(GuidResource),
                LocalizedName = new LocalizedString { Key = "WizardArchetype.AbilityZ.ResourceName" },
                LocalizedDescription = new LocalizedString { Key = "WizardArchetype.AbilityZ.ResourceDesc" },
            };

            var maxField = typeof(BlueprintAbilityResource).GetField(
                "m_MaxAmount", System.Reflection.BindingFlags.Instance |
                               System.Reflection.BindingFlags.NonPublic);
            if (maxField != null)
            {
                object amount = maxField.GetValue(res);
                amount.GetType().GetField("BaseValue")?.SetValue(amount, 0);
                maxField.SetValue(res, amount);
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidResource), res);
            return res;
        }

        /// <summary>按基础 CL 计算可用次数（§10.1）。</summary>
        internal static int GetMaxUses(UnitEntityData unit)
        {
            int cl = GetBaseCasterLevel(unit);
            if (cl < UnlockCasterLevel) return 0;
            if (cl >= 17) return 3;
            if (cl >= 12) return 2;
            return 1;
        }

        /// <summary>
        /// 基础 CL = 法师法术书的 CL（Spellbook.CasterLevel）。
        /// 与能力X 档位用的是同一个定义（§2「基础施法者等级」）。
        /// </summary>
        internal static int GetBaseCasterLevel(UnitEntityData unit)
        {
            try
            {
                if (unit == null) return 0;

                var wiz = WizardArchetypeBlueprints.WizardClass;
                if (wiz == null) return 0;

                // 取该单位的法师法术书
                foreach (var book in unit.Spellbooks)
                {
                    if (book == null || book.Blueprint == null) continue;
                    if (book.Blueprint.CharacterClass != wiz) continue;
                    return book.CasterLevel;
                }
            }
            catch (Exception) { }

            return 0;
        }

        // ---------- 能力 ----------

        private static BlueprintAbility CreateAbility(BlueprintsCache cache)
        {
            var comps = new List<BlueprintComponent>();

            // 效果：恢复能力X 1 次 + 施加 30s 力竭 / 60s 疲劳
            var run = new AbilityEffectRunAction
            {
                name = "$AbilityEffectRunAction$AbilityZ",
                SavingThrowType = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown,
            };
            run.Actions = new ActionList
            {
                Actions = new GameAction[]
                {
                    new AbilityZAction { name = "$AbilityZAction$AbilityZ" },
                },
            };
            comps.Add(run);

            // 消耗 1 次
            var logic = new AbilityResourceLogic
            {
                name = "$AbilityResourceLogic$AbilityZ",
                Amount = 1,
                CostIsCustom = false,
                ResourceCostIncreasingFacts = new List<BlueprintUnitFactReference>(),
                ResourceCostDecreasingFacts = new List<BlueprintUnitFactReference>(),
            };
            ReflectionFields.Set(logic, "m_RequiredResource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));
            ReflectionFields.Set(logic, "m_IsSpendResource", true);
            comps.Add(logic);

            // 条件：战斗中
            comps.Add(new AbilityCasterInCombat { name = "$AbilityCasterInCombat$AbilityZ" });

            // 条件：能力X 无可用次数（§10.2 ③）
            comps.Add(new AbilityZRequirement { name = "$AbilityZRequirement$AbilityZ" });

            var ability = new BlueprintAbility
            {
                name = "WizardArchetype_AbilityZ",
                AssetGuid = BlueprintGuid.Parse(GuidAbility),
                Type = AbilityType.Supernatural,
                Range = AbilityRange.Personal,
                // 整轮动作（§10.3 / E14）
                ActionType = UnitCommand.CommandType.Standard,
                SpellResistance = false,
                ComponentsArray = comps.ToArray(),
            };

            // m_IsFullRoundAction 由 AbilityIsFullRoundInTurnBased 组件控制（回合制）
            var fullRound = new AbilityIsFullRoundInTurnBased
            {
                name = "$AbilityIsFullRoundInTurnBased$AbilityZ",
                FullRoundIfTurnBased = true,
            };
            comps.Add(fullRound);
            ability.ComponentsArray = comps.ToArray();

            ReflectionFields.Set(ability, "m_DisplayName",
                new LocalizedString { Key = "WizardArchetype.AbilityZ.Name" });
            ReflectionFields.Set(ability, "m_Description",
                new LocalizedString { Key = "WizardArchetype.AbilityZ.Desc" });

            foreach (var c in ability.ComponentsArray)
                if (c != null) c.OwnerBlueprint = ability;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidAbility), ability);
            return ability;
        }

        private static BlueprintFeature CreateFeature(BlueprintsCache cache)
        {
            var addFacts = new AddFacts { name = "$AddFacts$AbilityZ" };
            ReflectionFields.Set(addFacts, "m_Facts",
                new BlueprintUnitFactReference[]
                {
                    BlueprintReference<BlueprintUnitFact>.CreateTyped<BlueprintUnitFactReference>(Ability),
                });

            var addRes = new AddAbilityResources
            {
                name = "$AddAbilityResources$AbilityZ",
                UseThisAsResource = false,
                Amount = 0,
                RestoreAmount = false,
                RestoreOnLevelUp = false,
            };
            ReflectionFields.Set(addRes, "m_Resource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));

            var sync = new AbilityZResourceSync { name = "$AbilityZResourceSync$AbilityZ" };

            var feature = new BlueprintFeature
            {
                name = "WizardArchetype_AbilityZFeature",
                AssetGuid = BlueprintGuid.Parse(GuidFeature),
                Ranks = 1,
                IsClassFeature = true,
                HideInUI = false,
                ReapplyOnLevelUp = true,
                ComponentsArray = new BlueprintComponent[] { addFacts, addRes, sync },
            };

            ReflectionFields.Set(feature, "m_DisplayName",
                new LocalizedString { Key = "WizardArchetype.AbilityZ.Name" });
            ReflectionFields.Set(feature, "m_Description",
                new LocalizedString { Key = "WizardArchetype.AbilityZ.Desc" });

            foreach (var c in feature.ComponentsArray)
                if (c != null) c.OwnerBlueprint = feature;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidFeature), feature);
            return feature;
        }

        private static void CreateText()
        {
            const string k = "WizardArchetype.AbilityZ";
            WizardArchetypeBlueprints.RegisterText(k + ".Name", "透支充能");
            WizardArchetypeBlueprints.RegisterText(k + ".Desc",
                "整轮动作，仅战斗中可用。\n" +
                "恢复「强化施法」1 次可用次数（不超过 1 次）。\n" +
                "同时获得 30 秒力竭与 60 秒疲劳。\n" +
                "仅在强化施法已无充能且无未到期强化状态时可用。");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceName", "透支充能次数");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceDesc",
                "基础 CL 7 起获得：7-11 → 1 次，12-16 → 2 次，17+ → 3 次。每次休息恢复。");
        }
    }
}
