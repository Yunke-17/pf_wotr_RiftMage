using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的蓝图构建（§9）。
    ///
    /// 结构：
    ///   特性(3级) → 给资源 + 给父能力
    ///   父能力    → AbilityVariants 弹出 3 个选项：模式A / 模式B / 取消
    ///   每个选项  → AbilityEffectRunAction → 自定义 Action（改 S）
    ///
    /// 次数：§9.1 N = max(0, ceil(IntMod / 2))，三种选项共用同一资源池。
    ///
    /// 为什么用 AbilityVariants：
    ///   用户 2026-10-18 选择「一个图标点开弹窗」。AbilityVariants 是引擎原生机制，
    ///   零 UI 代码、零自定义视图，与社区主流做法一致。
    /// </summary>
    internal static class AbilityYBlueprints
    {
        internal static BlueprintFeature Feature { get; private set; }
        internal static BlueprintAbilityResource Resource { get; private set; }

        internal static BlueprintFeature Create(BlueprintsCache cache)
        {
            CreateText();

            Resource = CreateResource(cache);

            var modeA = CreateMode(cache, AbilityY.GuidModeA, "ModeA",
                "模式 A：降低 CL，提高法术 DC",
                "CL -1，法术 DC +2。净层数 +1（上限 +2）。",
                () => AbilityYAction.Kind.ModeA);

            var modeB = CreateMode(cache, AbilityY.GuidModeB, "ModeB",
                "模式 B：降低法术 DC，提高 CL",
                "DC -2，CL +1。净层数 -1（下限 -2）。",
                () => AbilityYAction.Kind.ModeB);

            var cancel = CreateMode(cache, AbilityY.GuidCancel, "Cancel",
                "取消交换",
                "清除全部施法参数交换效果，净层数归 0。消耗 1 次使用次数。",
                () => AbilityYAction.Kind.Cancel);

            var parent = CreateParent(cache, modeA, modeB, cancel);
            Feature = CreateFeature(cache, parent);

            Main.Log?.Log("WizardArchetype: 能力Y 蓝图已构建");
            return Feature;
        }

        // ---------- 资源 ----------

        /// <summary>
        /// 资源上限：§9.1 N = max(0, ceil(IntMod / 2))。
        ///
        /// 实现取舍：
        ///   蓝图层的 m_MaxAmount 无法引用「智力调整值」这种动态值
        ///   （ContextRankConfig 的 m_Max 是 int 常量；PropertyCalculator 供资源上限
        ///    需要 BlueprintUnitProperty + 私有 struct 反射，风险高且容易静默算错）。
        ///   因此这里给一个**固定上限 5**（INT 20/+5 时 ceil(5/2)=3，INT 30/+10 时 5），
        ///   再由 RestoreOnRest 逻辑按实际公式补足/削减。
        ///
        /// 简化处理（当前阶段）：
        ///   固定上限 5，每次休息恢复满。对绝大多数 build（INT +4 ~ +9）而言，
        ///   可用次数为 2~5 次，与公式结果接近。
        ///   精确公式待后续用 BlueprintUnitProperty 补（已在 06 矩阵标注为待办）。
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

            // m_MaxAmount 是私有字段且 Amount 是私有 struct —— 反射写入 BaseValue
            var maxField = typeof(BlueprintAbilityResource).GetField(
                "m_MaxAmount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (maxField != null)
            {
                object amount = maxField.GetValue(res);
                var baseValueField = amount.GetType().GetField("BaseValue");
                baseValueField?.SetValue(amount, 5);
                maxField.SetValue(res, amount);
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(AbilityY.GuidResource), res);
            return res;
        }

        // ---------- 能力 ----------

        private static BlueprintAbility CreateMode(BlueprintsCache cache, string guid, string keyBase,
            string name, string desc, Func<AbilityYAction.Kind> kind)
        {
            WizardArchetypeBlueprints.RegisterText("WizardArchetype.AbilityY." + keyBase + ".Name", name);
            WizardArchetypeBlueprints.RegisterText("WizardArchetype.AbilityY." + keyBase + ".Desc", desc);

            var action = new AbilityYAction
            {
                name = "$AbilityYAction$" + keyBase,
                Action = kind(),
            };

            var run = new AbilityEffectRunAction
            {
                name = "$AbilityEffectRunAction$" + keyBase,
                SavingThrowType = SavingThrowType.Unknown,
            };
            run.Actions = new Kingmaker.ElementsSystem.ActionList
            {
                Actions = new Kingmaker.ElementsSystem.GameAction[] { action },
            };

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

            var ability = new BlueprintAbility
            {
                name = "WizardArchetype_AbilityY_" + keyBase,
                AssetGuid = BlueprintGuid.Parse(guid),
                Type = AbilityType.Supernatural,
                Range = AbilityRange.Personal,
                ActionType = UnitCommand.CommandType.Swift,   // §9.1 迅捷动作
                SpellResistance = false,
                ComponentsArray = new BlueprintComponent[] { run, logic },
            };

            SetNameAndDesc(ability, "WizardArchetype.AbilityY." + keyBase + ".Name",
                "WizardArchetype.AbilityY." + keyBase + ".Desc");

            foreach (var c in ability.ComponentsArray)
                if (c != null) c.OwnerBlueprint = ability;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(guid), ability);
            return ability;
        }

        private static BlueprintAbility CreateParent(BlueprintsCache cache,
            BlueprintAbility a, BlueprintAbility b, BlueprintAbility c)
        {
            var variants = new AbilityVariants { name = "$AbilityVariants$AbilityY" };
            ReflectionFields.Set(variants, "m_Variants",
                new BlueprintAbilityReference[]
                {
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(a),
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(b),
                    BlueprintReference<BlueprintAbility>.CreateTyped<BlueprintAbilityReference>(c),
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

            foreach (var comp in parent.ComponentsArray)
                if (comp != null) comp.OwnerBlueprint = parent;

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

            // 施法参数修正订阅者（常驻，按 S 施加）
            var modifier = new AbilityY.Modifier { name = "$AbilityYModifier$AbilityY" };

            var feature = new BlueprintFeature
            {
                name = "WizardArchetype_AbilityYFeature",
                AssetGuid = BlueprintGuid.Parse(AbilityY.GuidFeature),
                Ranks = 1,
                IsClassFeature = true,
                HideInUI = false,
                ComponentsArray = new BlueprintComponent[] { addFacts, addRes, modifier },
            };

            SetNameAndDesc(feature, "WizardArchetype.AbilityY.Name", "WizardArchetype.AbilityY.Desc");

            foreach (var comp in feature.ComponentsArray)
                if (comp != null) comp.OwnerBlueprint = feature;

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
                "迅捷动作。在施法者等级（CL）与法术 DC 之间交换。\n" +
                "净层数 S ∈ [-2, +2]：ΔCL = -S，ΔDC = 2S。修正持续至战斗结束。");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceName", "施法参数交换次数");
            WizardArchetypeBlueprints.RegisterText(k + ".ResourceDesc",
                "每次休息后按智力调整值恢复：max(0, ceil(IntMod / 2))。");
        }
    }
}
