using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Properties;
using Kingmaker.EntitySystem.Properties.BaseGetter;
using Kingmaker.EntitySystem.Properties.Getters;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Properties;

namespace WizardArchetype
{
    /// <summary>
    /// 零代码特性：用原版组件拼装，仅在 ContextRankConfig / StatGetter 处走反射层。
    ///
    /// 需求对照（02 矩阵）：
    ///   B1  1级 幸运野兔             原版 97dff21a...
    ///   B4  3级 法术穿透 +2          【自建】不用原版专长，避免与专长重复叠加
    ///   B5  7级 先攻掷 2 骰取高       ModifyD20(Initiative, 2, TakeBest)
    ///   B7  先攻 + 智力调整值         AddContextStatBonus
    ///   B8  该加值 ≤ 法师职业等级     PropertyCalculator(Min) + ClassLevelGetter
    ///   B10 职业先攻 floor(L/2) 封顶   ContextRankConfig(ClassLevel, Div2, Max 10)
    ///   B12 感知检定用 max(INT, WIS)  三个感知技能都要覆盖
    ///   B14 专注封顶 +6               ConcentrationBonus + ContextRankConfig
    ///   D1  1级 技能专攻：知识(神秘)
    ///
    /// 感知影响的技能（CharacterStats.cs:85/89/91 逐个核对）：
    ///   SkillPerception   (察觉)      ← Wisdom
    ///   SkillLoreNature   (学识自然)  ← Wisdom
    ///   SkillLoreReligion (学识宗教)  ← Wisdom
    /// 另：SaveWill(意志豁免)也基于 Wisdom，但用户要求不影响，故不处理。
    /// </summary>
    internal static class WizardArchetypeFeatures
    {
        private const string HareBond = "97dff21a036e80948b07097ad3df2b30";
        private const string SkillFocusArcana = "cad1b9175e8c0e64583432a22134d33c";
        private const string QuickenSpellFeat = "ef7ece7bb5bb66a41b256976b27f424e";

        internal const string GuidInitiativeLevel  = "b1000000000000000000000000000001";
        internal const string GuidInitiativeInt    = "b1000000000000000000000000000002";
        internal const string GuidPerceptionInt    = "b1000000000000000000000000000003";
        internal const string GuidConcentration    = "b1000000000000000000000000000004";
        internal const string GuidInitiativeDouble = "b1000000000000000000000000000005";
        internal const string GuidSpellPen         = "b1000000000000000000000000000006";
        internal const string GuidWizardClassLevelProp = "b1000000000000000000000000000007";
        internal const string GuidQuickenCostNote = "b1000000000000000000000000000008";

        internal static List<(int Level, SimpleBlueprint Feature)> BuildAll(BlueprintsCache cache)
        {
            var result = new List<(int, SimpleBlueprint)>();

            // D2–D4：能力 X「强化施法」——1 级，自由动作，每次休息 1 次
            // 阶段 3 第 0 步：只验证蓝图注册与冒泡，不含任何自定义组件
            var abilityX = AbilityX.Create(cache);
            if (abilityX != null)
            {
                result.Add((1, abilityX));
            }

            AddOriginal(result, cache, 1, HareBond, "幸运野兔");
            AddSkillFocusArcana(result, cache);

            result.Add((1, MakeInitiativeFromLevel(cache)));
            result.Add((1, MakeInitiativeFromInt(cache)));
            result.Add((1, MakePerceptionFromInt(cache)));
            result.Add((1, MakeConcentration(cache)));
            result.Add((3, MakeSpellPenetration(cache)));

            // E1–E9：能力 Y「施法参数交换」—— 法师 3 级，迅捷动作
            // 蓝图在 AbilityX.Create 里已构建（见 AbilityYBlueprints.Create）
            var abilityY = AbilityYBlueprints.Feature;
            if (abilityY != null)
            {
                result.Add((3, abilityY));
            }

            // E10–E17：能力 Z「透支充能」—— 按基础 CL 解锁（7 起），
            // 因此挂在 1 级特性表里，靠可用次数为 0 时自动灰显来表达「尚未获得」。
            var abilityZ = AbilityZ.Feature;
            if (abilityZ != null)
            {
                result.Add((1, abilityZ));
            }

            // E18：法师 5 级自动获得「瞬发法术」原版专长
            AddOriginal(result, cache, 5, QuickenSpellFeat, "瞬发法术");

            // E19：5 级同时展示"瞬发减费"说明特性（纯 UI 标注，不挂任何组件）
            result.Add((5, MakeQuickenCostReducerNote(cache)));

            result.Add((7, MakeInitiativeDoubleRoll(cache)));

            return result;
        }

        private static void AddOriginal(List<(int, SimpleBlueprint)> list,
            BlueprintsCache cache, int level, string guid, string label)
        {
            var bp = cache.Load(BlueprintGuid.Parse(guid));
            if (bp == null)
            {
                Main.Log?.Error($"WizardArchetype: 原版蓝图 {label} ({guid}) 加载失败");
                return;
            }
            list.Add((level, bp));
        }

        // ---------- B4：法术穿透 +2（自建，不借用原版专长） ----------
        private static SimpleBlueprint MakeSpellPenetration(BlueprintsCache cache)
        {
            var sp = new SpellPenetrationBonus
            {
                Value = new ContextValue { ValueType = ContextValueType.Simple, Value = 2 },
                Descriptor = ModifierDescriptor.UntypedStackable,
                CheckFact = false,
            };

            return NewFeature(cache, GuidSpellPen, "SpellPen",
                "法术穿透训练", "进行法术抗性检定时获得 +2 加值。",
                new BlueprintComponent[] { sp });
        }

        // ---------- B10：职业先攻 floor(L/2)，封顶 10 ----------
        private static SimpleBlueprint MakeInitiativeFromLevel(BlueprintsCache cache)
        {
            var rank = RankConfigs.Build(
                ContextRankBaseValueType.ClassLevel,
                ContextRankProgression.Div2,          // value / 2，见 ContextRankConfig.cs:564-565
                stepLevel: 1,
                classRef: WizardClassRef(),
                useMin: true, min: 0,
                useMax: true, max: 10);

            var bonus = new AddContextStatBonus
            {
                Stat = StatType.Initiative,
                Descriptor = ModifierDescriptor.UntypedStackable,
                Value = new ContextValue { ValueType = ContextValueType.Rank },
            };

            return NewFeature(cache, GuidInitiativeLevel, "InitiativeLevel",
                "职业先攻", "先攻获得等同法师等级一半的加值（最多 +10）。",
                new BlueprintComponent[] { bonus, rank });
        }

        // ---------- B7 + B8：先攻 + min(IntMod, 法师职业等级) ----------
        private static SimpleBlueprint MakeInitiativeFromInt(BlueprintsCache cache)
        {
            var comps = Main.UseCappedInitiative
                ? BuildCappedInitiativeComponents()
                : BuildDerivativeInitiativeComponents();

            return NewFeature(cache, GuidInitiativeInt, "InitiativeInt",
                "智力先攻",
                Main.UseCappedInitiative
                    ? "先攻获得智力调整值加值，但不超过法师职业等级。"
                    : "先攻获得智力调整值加值。",
                comps);
        }

        /// <summary>
        /// 回退方案：原版剑圣「疾电出鞘」（SwordSaintLightningDraw.jbp）的写法。
        /// 零反射，但智力为负时不生效（DerivativeStatBonus.cs:24 要求 Bonus > 0），且无封顶。
        /// </summary>
        private static BlueprintComponent[] BuildDerivativeInitiativeComponents()
        {
            return new BlueprintComponent[]
            {
                new DerivativeStatBonus
                {
                    BaseStat = StatType.Intelligence,
                    DerivativeStat = StatType.Initiative,
                    Descriptor = ModifierDescriptor.None,
                },
                new RecalculateOnStatChange { Stat = StatType.Intelligence },
            };
        }

        /// <summary>
        /// 试验方案：PropertyCalculator 的 Min(IntMod, 法师职业等级)。
        ///
        /// 依据：PropertyCalculator.Min 从 getters[0] 起算（无 0 初值），
        /// 因此智力为负时能保留负值；这是 00 §4.4 手段 4，
        /// 原版 BloodlustBuff.jbp:73-129 用的是同一套结构。
        ///
        /// 上次失败原因是 ContextRankConfig / 组件缺 OwnerBlueprint，
        /// 已在 NewFeature 中统一回填。
        /// </summary>
        private static BlueprintComponent[] BuildCappedInitiativeComponents()
        {
            var calc = new PropertyCalculator
            {
                Operation = PropertyCalculator.OperationType.Min,
                TargetType = PropertyTargetType.CurrentEntity,
                Getters = new PropertyGetter[]
                {
                    RankConfigs.Stat(StatType.Intelligence, RankConfigs.StatValueTypeKind.BonusValue),
                    new ContextValueGetter
                    {
                        Value = MakeCustomPropertyValue(MakeWizardClassLevelProperty()),
                    },
                },
            };

            var calcComp = new PropertyCalculatorComponent
            {
                Name = ContextPropertyName.Value1,
                SaveToContext = PropertyCalculatorComponent.SaveToContextType.No,
                Value = calc,
            };

            // 关键：必须用 CasterNamedProperty，不能用 ContextProperty。
            //
            // ContextValue.cs:227-228  ContextProperty 分支只做 context[PropertyName]，
            //   即要求 PropertyCalculatorComponent 把值写进 context（SaveToContext 不能是 No），
            //   我们用的是 No，所以读到的永远是 0 —— 这就是上次"加值失效"的真正原因。
            // ContextValue.cs:209-210  CasterNamedProperty 分支走
            //   AssociatedBlueprint.GetPropertyValue(...)，
            //   直接在本蓝图里找同名 PropertyCalculatorComponent 现算，与 SaveToContext 无关。
            //
            // 原版对照：BloodlustBuff.jbp:59-68 正是
            //   "ValueType": "CasterNamedProperty", "PropertyName": "Value1"
            var bonus = new AddContextStatBonus
            {
                Stat = StatType.Initiative,
                Descriptor = ModifierDescriptor.UntypedStackable,
                Value = new ContextValue
                {
                    ValueType = ContextValueType.CasterNamedProperty,
                    PropertyName = ContextPropertyName.Value1,
                },
            };

            return new BlueprintComponent[] { calcComp, bonus };
        }

        /// <summary>
        /// 自建 BlueprintUnitProperty：取本变体的法师职业等级。
        ///
        /// PropertyGetter 体系里没有职业等级 getter，所以绕一层：
        ///   ContextValueGetter -> CasterCustomProperty -> 本蓝图 -> ClassLevelGetter
        /// ClassLevelGetter.cs:45 返回 unit.Progression.GetClassLevel(Class)，即职业等级。
        ///
        /// 注意 m_Class 是私有字段（ClassLevelGetter.cs:50），需反射写入。
        /// </summary>
        private static BlueprintUnitProperty MakeWizardClassLevelProperty()
        {
            if (s_WizardClassLevelProperty != null) return s_WizardClassLevelProperty;

            var getter = new ClassLevelGetter();
            ReflectionFields.Set(getter, "m_Class",
                BlueprintReference<BlueprintCharacterClass>
                    .CreateTyped<BlueprintCharacterClassReference>(
                        WizardArchetypeBlueprints.WizardClass));

            var prop = new BlueprintUnitProperty
            {
                name = "WizardArchetype_WizardClassLevel",
                AssetGuid = BlueprintGuid.Parse(GuidWizardClassLevelProp),
                BaseValue = 0,
                OperationOnComponents = BlueprintUnitProperty.MathOperation.Sum,
                ComponentsArray = new BlueprintComponent[] { getter },
            };

            s_WizardClassLevelProperty = prop;

            // 注册进缓存，供 ContextValue 按引用解析
            Kingmaker.Blueprints.ResourcesLibrary.BlueprintsCache
                ?.AddCachedBlueprint(BlueprintGuid.Parse(GuidWizardClassLevelProp), prop);

            Main.Log?.Log("WizardArchetype: 已创建职业等级属性蓝图");
            return prop;
        }

        private static BlueprintUnitProperty s_WizardClassLevelProperty;

        /// <summary>
        /// 构造引用自定义 BlueprintUnitProperty 的 ContextValue。
        ///
        /// ContextValue.CustomProperty 是只读属性（ContextValue.cs:33-44），
        /// 底层字段 m_CustomProperty 为 private（:394），故用反射写入。
        /// </summary>
        private static ContextValue MakeCustomPropertyValue(BlueprintUnitProperty prop)
        {
            var value = new ContextValue { ValueType = ContextValueType.CasterCustomProperty };
            ReflectionFields.Set(value, "m_CustomProperty",
                BlueprintReference<BlueprintUnitProperty>
                    .CreateTyped<BlueprintUnitPropertyReference>(prop));
            return value;
        }

        // ---------- B12：三个感知技能都用 max(INT, WIS) ----------
        private static SimpleBlueprint MakePerceptionFromInt(BlueprintsCache cache)
        {
            var targets = new[]
            {
                StatType.SkillPerception,    // 察觉
                StatType.SkillLoreNature,    // 学识（自然）
                StatType.SkillLoreReligion,  // 学识（宗教）
            };

            var comps = targets.Select(t => (BlueprintComponent)new ReplaceStatBaseAttribute
            {
                TargetStat = t,
                BaseAttributeReplacement = StatType.Intelligence,
                ReplaceIfHigher = true,
            }).ToArray();

            return NewFeature(cache, GuidPerceptionInt, "PerceptionInt",
                "智力感知",
                "察觉、学识（自然）、学识（宗教）检定使用智力调整值，若其高于感知调整值。",
                comps);
        }

        // ---------- B14：专注 + min(6, L+1) ----------
        private static SimpleBlueprint MakeConcentration(BlueprintsCache cache)
        {
            var rank = RankConfigs.Build(
                ContextRankBaseValueType.ClassLevel,
                ContextRankProgression.OnePlusDivStep,   // 1 + L/1 = L+1，见 :572-573
                stepLevel: 1,
                classRef: WizardClassRef(),
                useMax: true, max: 6);

            var bonus = new ConcentrationBonus
            {
                Value = new ContextValue { ValueType = ContextValueType.Rank },
                CheckFact = false,
            };

            return NewFeature(cache, GuidConcentration, "Concentration",
                "专注训练", "专注检定获得加值，最高 +6。",
                new BlueprintComponent[] { bonus, rank });
        }

        // ---------- B5：7 级 先攻掷 2 骰取高 ----------
        private static SimpleBlueprint MakeInitiativeDoubleRoll(BlueprintsCache cache)
        {
            // RollsAmount 语义是"额外追加几颗"，不含基础那颗：
            //   RuleRollDice.cs:86-91  先掷基础骰，再 for (i < abs(m_RerollAmount)) 追加
            // 所以"共 2 颗"应写 1 而不是 2（写 2 会变成 3 颗）。
            // 原版对照：OracleRevelationWarSight1.jbp 的双骰取高为 RollsAmount: 1。
            var mod = new ModifyD20
            {
                Rule = RuleType.Initiative,
                RollsAmount = 1,
                TakeBest = true,
            };

            return NewFeature(cache, GuidInitiativeDouble, "InitiativeDouble",
                "敏锐先攻", "先攻时掷两次 d20 并取较高结果。",
                new BlueprintComponent[] { mod });
        }

        // ---------- D1：技能专攻：知识（神秘）—— 直接挂原版蓝图 ----------
        private static void AddSkillFocusArcana(List<(int, SimpleBlueprint)> list, BlueprintsCache cache)
        {
            // 原版蓝图自带 Custom 分档（技能≤9 → +3，否则 +6），无需自建
            var bp = cache.Load(BlueprintGuid.Parse(SkillFocusArcana));
            if (bp == null)
            {
                Main.Log?.Error($"WizardArchetype: 技能专攻蓝图 {SkillFocusArcana} 加载失败");
                return;
            }
            list.Add((1, bp));
        }

        // ---------- E19：瞬发减费的说明特性（纯标注） ----------
        private static SimpleBlueprint MakeQuickenCostReducerNote(BlueprintsCache cache)
        {
            // 这个特性不挂任何组件，只用于在职业特性列表里说明减费效果。
            // 实际减费由 MetamagicDefaultCostPatch 在 MetamagicHelper.DefaultCost 处完成。
            return NewFeature(cache, GuidQuickenCostNote, "QuickenCostNote",
                "精通瞬发",
                "瞬发法术的环位消耗随法师等级降低：" +
                "13 级起 +3，15 级起 +2，17 级起 +1。" +
                "<br>该效果可与「偏好超魔：瞬发」叠加，最终消耗最低为 0。",
                new BlueprintComponent[0]);
        }

        // ---------- 辅助 ----------
        private static BlueprintCharacterClassReference WizardClassRef()
        {
            return BlueprintReference<BlueprintCharacterClass>
                .CreateTyped<BlueprintCharacterClassReference>(WizardArchetypeBlueprints.WizardClass);
        }

        private static SimpleBlueprint NewFeature(BlueprintsCache cache, string guid, string keyBase,
            string textName, string textDesc, BlueprintComponent[] components)
        {
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + keyBase + ".Name", textName);
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + keyBase + ".Desc", textDesc);

            // 关键：每个组件的 name 必须唯一且非空，且【跨版本稳定】。
            //
            // 依据：
            //   1. EntityFact.cs:118  Components.ToDictionary(i => i.SourceBlueprintComponentName)
            //      —— name 为 null 或重复会抛 "Value cannot be null. Parameter name: key"，
            //         发生在存档序列化时，表现为玩家无法保存。
            //   2. EntityFactComponent.cs:123  SourceBlueprintComponentName = component.name
            //   3. EntityFact.cs:139  读档时按 name 反查蓝图组件
            //      —— 名字变了会导致旧存档的组件状态对不上。
            //
            // 因此用 "$类型名$特性Key_序号" 这种确定性格式，不用 Guid.NewGuid()。
            for (int i = 0; i < components.Length; i++)
            {
                if (string.IsNullOrEmpty(components[i].name))
                {
                    components[i].name = $"${components[i].GetType().Name}${keyBase}_{i}";
                }
            }

            var feat = new BlueprintFeature
            {
                name = "WizardArchetype_" + keyBase,
                AssetGuid = BlueprintGuid.Parse(guid),
                Ranks = 1,
                IsClassFeature = true,

                // ★ 关键：升级时必须重算上下文。
                //
                // 不带这个标志的后果（实测）：旧存档里先攻/专注的加值存在，
                // 但**升级后不更新**、**洗点后也不更新** —— 因为 ContextRankConfig
                // 的结果缓存在 MechanicsContext 里，而 EntityFact.Reapply()
                // （EntityFact.cs:552-568，内部调 maybeContext.Recalculate()）
                // 只有在 BlueprintFeature.ReapplyOnLevelUp == true 时才会被升级流程触发
                // （BlueprintFeature.cs:147 该字段默认 false）。
                //
                // 本 MOD 的「职业先攻」「专注训练」「智力先攻（封顶到职业等级）」
                // 都依赖 ContextRankConfig / PropertyCalculator 现算，所以必须开启。
                ReapplyOnLevelUp = true,
            };

            ReflectionFields.Set(feat, "m_DisplayName",
                new LocalizedString { Key = "WizardArchetype." + keyBase + ".Name" });
            ReflectionFields.Set(feat, "m_Description",
                new LocalizedString { Key = "WizardArchetype." + keyBase + ".Desc" });

            feat.ComponentsArray = components;

            // 关键：回填 OwnerBlueprint。
            //
            // BlueprintComponent.OwnerBlueprint（BlueprintComponent.cs:33）在原版里由
            // JSON 反序列化时自动赋值（:80 OnEnable）。
            // 纯 C# 创建不会走那条路径，导致 OwnerBlueprint == null。
            // 后果（WrathPatches 会报 "Non-matching OwnerBlueprint NULL"）：
            //   ContextRankConfig 在 m_BaseValueType = ClassLevel 时，需要通过
            //   OwnerBlueprint 反查所属职业来算等级；为 null 就算不出等级，
            //   表现为"先攻/专注加值完全不生效"。
            foreach (var c in components)
            {
                if (c != null) c.OwnerBlueprint = feat;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(guid), feat);
            return feat;
        }
    }
}
