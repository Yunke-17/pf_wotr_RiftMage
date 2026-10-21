using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
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
        // 2026-10-20：HareBond(97dff21a 原版野兔魔宠) 已不再直接挂载 ——
        // 改为自建专属魔宠 HareFamiliar，原版蓝图保持不动。
        private const string SkillFocusArcana = "cad1b9175e8c0e64583432a22134d33c";
        private const string QuickenSpellFeat = "ef7ece7bb5bb66a41b256976b27f424e";

        internal const string GuidInitiativeLevel  = "b1000000000000000000000000000001";
        internal const string GuidInitiativeInt    = "b1000000000000000000000000000002";
        internal const string GuidPerceptionInt    = "b1000000000000000000000000000003";
        internal const string GuidConcentration    = "b1000000000000000000000000000004";

        /// <summary>3 级「法术穿透 +2」子特性（不再直接出现在特性列表，由合并特性按等级授予）。</summary>
        internal const string GuidInitiativeDouble = "b1000000000000000000000000000005";

        /// <summary>7 级「先攻双骰取高」子特性（同上）。</summary>
        internal const string GuidSpellPen         = "b1000000000000000000000000000006";
        internal const string GuidWizardClassLevelProp = "b1000000000000000000000000000007";
        internal const string GuidQuickenCostNote = "b1000000000000000000000000000008";

        /// <summary>
        /// 合并后的「幸运野兔」单一特性（2026-10-20）。
        /// 原来的 1 级原版魔宠 97dff21a / 3 级穿透 / 7 级双骰三条已合并到这里。
        /// </summary>
        internal const string GuidHareUnified = "b1000000000000000000000000000009";

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

            // 战斗首轮状态「开场时序」—— 1 级。
            // 规则本身由 SpecialOpeningState 组件执行（2026-10-20 从能力X 迁来），
            // 本特性同时提供职业面板上的文字说明。
            var openingTiming = OpeningTimingFeature.Create(cache);
            if (openingTiming != null)
            {
                result.Add((1, openingTiming));
            }

            AddSkillFocusArcana(result, cache);

            // 敏锐：职业先攻 + 智力先攻合并为**一个**特性
            result.Add((1, MakeInsight(cache)));
            result.Add((1, MakePerceptionFromInt(cache)));
            result.Add((1, MakeConcentration(cache)));

            // 幸运野兔系：1 级主特性 + 3/7 级进阶（用前置链串起来）
            BuildHareChain(result, cache);

            // E1–E9：能力 Y「空隙调谐」—— 法师 3 级，迅捷动作
            // 蓝图在 AbilityX.Create 里已构建（见 AbilityYBlueprints.Create）
            var abilityY = AbilityYBlueprints.Feature;
            if (abilityY != null)
            {
                result.Add((3, abilityY));
            }

            // E10–E17：能力 Z「空隙透支」—— 按基础 CL 解锁（7 起）。
            //
            // ★ 「只显示一个」的处理（用户 2026-10-20）：
            //   之前挂 1/7/12/17 四个等级，特性列表里会出现 4 个同名条目。
            //   现在改为：
            //     · 只用**一个** LevelEntry（1 级），保证列表只出现一次；
            //     · 为了让已升级的旧存档也能拿到，在 HandleUnitAfterLevelUp 里
            //       检测并补发（见 AbilityZResourceSync）。
            //   这样既不重复显示，也不依赖成长表的追溯设置。
            var abilityZ = AbilityZ.Feature;
            if (abilityZ != null)
            {
                result.Add((1, abilityZ));
            }

            // E18：法师 5 级自动获得「瞬发法术」原版专长
            AddOriginal(result, cache, 5, QuickenSpellFeat, "瞬发法术");

            // E19：5 级同时展示"瞬发减费"说明特性（纯 UI 标注，不挂任何组件）
            result.Add((5, MakeQuickenCostReducerNote(cache)));

            // ★ 2026-10-20 起：幸运野兔的 3 级 / 7 级效果不再作为独立 LevelEntry 挂载，
            //   而是由合并后的单一特性通过 AddFeatureOnClassLevel 按职业等级自动授予
            //   （见 BuildHareChain）。所以这里不再有 MakeInitiativeDoubleRoll 的挂载点。

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
                "幸运野兔：裂隙直觉",
                "契约野兔能嗅到现实褶皱中的空隙。" +
                "自职业等级 3 级起，空隙法师在为穿透目标的" +
                Glossary.Link(Glossary.KSpellResistance, "法术抗力") +
                "进行" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                "检定时，获得 +2 加值。",
                new BlueprintComponent[] { sp });
        }

        // ---------- 敏锐：职业先攻 + 智力先攻（合并为一个特性）----------

        /// <summary>
        /// 用户 2026-10-20：把「职业先攻」与「智力先攻」合并成**一个**特性「敏锐」。
        ///
        /// 两个来源：
        ///   ① 法师等级 ÷ 2（Div2，封顶 10）
        ///   ② min(智力调整值, 法师等级)
        /// 都用 AddContextStatBonus 加到 Initiative，各自独立计算、互相叠加。
        /// </summary>
        private static SimpleBlueprint MakeInsight(BlueprintsCache cache)
        {
            // ① 职业等级先攻：floor(L/2)，封顶 10
            var classRank = RankConfigs.Build(
                ContextRankBaseValueType.ClassLevel,
                ContextRankProgression.Div2,          // value / 2，见 ContextRankConfig.cs:564-565
                stepLevel: 1,
                classRef: WizardClassRef(),
                useMin: true, min: 0,
                useMax: true, max: 10);

            var classBonus = new AddContextStatBonus
            {
                Stat = StatType.Initiative,
                Descriptor = ModifierDescriptor.UntypedStackable,
                Value = new ContextValue { ValueType = ContextValueType.Rank },
            };

            // ② 智力先攻：min(IntMod, 法师等级)
            var intComps = Main.UseCappedInitiative
                ? BuildCappedInitiativeComponents()
                : BuildDerivativeInitiativeComponents();

            var all = new List<BlueprintComponent> { classBonus, classRank };
            all.AddRange(intComps);

            string desc =
                "空隙法师的神经反射被异化，使其总能在现实闭合之前先一步行动。" +
                "在" + Glossary.Link(Glossary.KInitiative, "先攻") +
                "检定中获得相当于" + Glossary.Link(Glossary.KClassLevel, "职业等级") +
                "一半的加值（最多 +10）。" +
                (Main.UseCappedInitiative
                    ? "此外，还会将" + Glossary.Link(Glossary.KIntelligence, "智力") +
                      "调整值计入先攻检定，但该部分加值不会超过职业等级。"
                    : "此外，还会将" + Glossary.Link(Glossary.KIntelligence, "智力") +
                      "调整值计入先攻检定。");

            // 复用 GuidInitiativeInt 作为合并后的特性 GUID（另一个不再使用）
            return NewFeature(cache, GuidInitiativeInt, "Insight",
                "敏锐", desc, all.ToArray());
        }

        // ---------- 幸运野兔：单一特性 + 按等级自动升阶 ----------

        /// <summary>
        /// 用户 2026-10-20 最终形态：三条幸运野兔**合并为一条**。
        ///
        /// ========== 结构 ==========
        ///
        ///   幸运野兔（1 级，单一 GUID = GuidHareUnified）
        ///     ├─ AddFacts → 专属野兔魔宠能力（HareFamiliar，只生成模型，不给数值）
        ///     ├─ SpellPenetrationBonus +2   ── 3 级起生效
        ///     ├─ ModifyD20(Initiative, 2取高) ── 7 级起生效
        ///     └─ 两个 AddFeatureOnClassLevel，把上面两个效果组件
        ///        按职业等级「挂进 / 摘下」一个子特性
        ///
        /// ========== 为什么用 AddFeatureOnClassLevel ==========
        ///
        /// 它是原版现成组件（AddFeatureOnClassLevel.cs:92-114），语义正是
        /// 「职业等级 ≥ Level 时把 Feature 加给 Owner，降下来就摘掉」，
        /// 并且订阅了 IOwnerGainLevelHandler（:86），升级时会重新判定。
        ///
        /// 关键优势：它**支持指定 Archetypes**（:64-70）。
        /// 原版是共享蓝图，我们给它写 m_Archetypes = 本变体，
        /// 就只会对空隙法师生效，不会影响其他职业的同名组件。
        ///
        /// 这样特性列表里只有「幸运野兔」一条，但 3 级 / 7 级的效果仍会逐级到账。
        ///
        /// ========== 为什么不直接挂三个组件 + ContextRankConfig ==========
        ///
        /// ContextRankConfig 只能缩放**数值**（加值大小），
        /// 而「3 级才让法术穿透生效、7 级才让双骰生效」是**开关**语义，
        /// 加值为 0 时 SpellPenetrationBonus 仍会挂一个 +0 的空修改器，
        /// 在 UI 上会显示成「法术穿透 +0」，很怪。所以用「子特性挂/摘」更干净。
        /// </summary>
        private static void BuildHareChain(List<(int Level, SimpleBlueprint Feature)> result, BlueprintsCache cache)
        {
            // 专属魔宠（克隆原版，不动原版蓝图）
            HareFamiliar.Create(cache);

            // 3 级效果：法术穿透 +2（独立子特性，供 AddFeatureOnClassLevel 引用）
            var spellPen = MakeSpellPenetration(cache) as BlueprintFeature;

            // 7 级效果：先攻掷 2 取高（独立子特性）
            var doubleRoll = MakeInitiativeDoubleRoll(cache) as BlueprintFeature;

            var comps = new List<BlueprintComponent>();

            // ① 专属魔宠
            if (HareFamiliar.Ability != null)
            {
                var addFamiliar = new AddFacts { name = "$AddFacts$HareUnified_0" };
                ReflectionFields.Set(addFamiliar, "m_Facts",
                    new BlueprintUnitFactReference[]
                    {
                        BlueprintReference<BlueprintUnitFact>
                            .CreateTyped<BlueprintUnitFactReference>(HareFamiliar.Ability),
                    });
                comps.Add(addFamiliar);
            }
            else
            {
                Main.Log?.Error("[幸运野兔] 专属魔宠创建失败，合并特性将不含魔宠");
            }

            // ② 3 级起生效：法术穿透
            if (spellPen != null)
            {
                comps.Add(MakeLevelGate(cache, spellPen, 3, "$AddFeatureOnClassLevel$HarePen"));
            }
            else
            {
                Main.Log?.Error("[幸运野兔] 法术穿透子特性创建失败");
            }

            // ③ 7 级起生效：先攻双骰取高
            if (doubleRoll != null)
            {
                comps.Add(MakeLevelGate(cache, doubleRoll, 7, "$AddFeatureOnClassLevel$HareInit"));
            }
            else
            {
                Main.Log?.Error("[幸运野兔] 先攻双骰子特性创建失败");
            }

            // ④ 旧存档清理：摘掉合并前遗留的旧特性实例（见 HareLegacyCleanup）
            comps.Add(new HareLegacyCleanup { name = "$HareLegacyCleanup$HareUnified_4" });

            var unified = NewFeature(cache, GuidHareUnified, "HareUnified",
                "幸运野兔",
                BuildHareUnifiedDescription(),
                comps.ToArray());

            result.Add((1, unified));
        }

        /// <summary>
        /// 造一个「职业等级 ≥ level 时授予 feature」的开关组件。
        ///
        /// m_Class / m_Feature / m_Archetypes 全是私有字段，只能反射写。
        /// ★ m_Archetypes 必须填本变体 —— 否则这个组件对「所有拿法师职业的人」生效，
        ///   普通法师也会在 3 级白得法术穿透 +2。
        /// </summary>
        private static AddFeatureOnClassLevel MakeLevelGate(
            BlueprintsCache cache, BlueprintFeature feature, int level, string componentName)
        {
            var gate = new AddFeatureOnClassLevel
            {
                name = componentName,
                Level = level,
                BeforeThisLevel = false,
            };

            // m_Class：法师职业
            ReflectionFields.Set(gate, "m_Class",
                BlueprintReference<BlueprintCharacterClass>
                    .CreateTyped<BlueprintCharacterClassReference>(
                        WizardArchetypeBlueprints.WizardClass));

            // m_Feature：要授予的子特性
            ReflectionFields.Set(gate, "m_Feature",
                BlueprintReference<BlueprintFeature>
                    .CreateTyped<BlueprintFeatureReference>(feature));

            // m_Archetypes：限定本变体（关键）
            if (WizardArchetypeBlueprints.Archetype != null)
            {
                var arr = Array.CreateInstance(typeof(BlueprintArchetypeReference), 1);
                arr.SetValue(
                    BlueprintReference<BlueprintArchetype>
                        .CreateTyped<BlueprintArchetypeReference>(WizardArchetypeBlueprints.Archetype),
                    0);
                ReflectionFields.Set(gate, "m_Archetypes", arr);
            }
            else
            {
                Main.Log?.Error("[幸运野兔] 变体蓝图尚未构建，等级开关无法限定 archetype");
            }

            return gate;
        }

        /// <summary>合并特性的描述：一条里说清魔宠 + 三级成长。</summary>
        private static string BuildHareUnifiedDescription()
        {
            // 同一段内同术语只挂首次：先攻 ×1、法术抗力 ×1、施法者等级 ×1
            return "一只契约野兔从现实缝隙中钻出，成为空隙法师的魔宠。" +
                   "它本身不提供任何加值，而是作为法师与时间裂隙之间的联系而存在。\n" +
                   "自职业等级 3 级起，空隙法师在为穿透目标的" +
                   Glossary.Link(Glossary.KSpellResistance, "法术抗力") +
                   "进行" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                   "检定时获得 +2 加值。\n" +
                   "自职业等级 7 级起，野兔会在时间真正流逝之前数次颤动双耳，" +
                   "使空隙法师进行" + Glossary.Link(Glossary.KInitiative, "先攻") +
                   "检定时掷两次 d20 并取较高结果。该优势只作用于先攻检定，" +
                   "不改变其他检定或攻击的掷骰方式。";
        }

        // ---------- （已合并进 MakeInsight，保留此注释便于追溯）----------
        // 原 MakeInitiativeFromLevel（职业先攻独立特性）与
        // 原 MakeInitiativeFromInt（智力先攻独立特性）
        // 已于 2026-10-20 合并为单一特性「敏锐」。

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

            // ★ 智力变化时必须重算。
            //
            // PropertyCalculator 的结果是在特性 **激活那一刻** 算出来并 AddModifier 进去的
            // （AddContextStatBonus.OnTurnOn → CalculateBaseValue），
            // 之后智力再怎么变（装备、buff、喝药）都不会自动刷新。
            //
            // RecalculateOnStatChange 订阅属性变化并触发 Fact.Reapply()
            // （→ Deactivate/Activate → 重跑 OnTurnOn → 重新取智力）。
            // 这正是用户要求的「包括临时给的智力 buff 或装备给的 buff」。
            var recalc = new RecalculateOnStatChange { Stat = StatType.Intelligence };

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

            return new BlueprintComponent[] { calcComp, bonus, recalc };
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
                "敏锐推演",
                "空隙法师以严密的因果推演取代了模糊的直觉。" +
                "当他进行" + Glossary.Link(Glossary.KSkills, "技能") +
                "检定（察觉、学识：自然、学识：宗教）时，" +
                "若其" + Glossary.Link(Glossary.KIntelligence, "智力") + "高于感知，" +
                "则以智力调整值替代感知调整值。",
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
                "空隙思维",
                "空隙法师学会了在现实断裂的间隙中稳定自身的法力回路。" +
                "其" + Glossary.Link(Glossary.KConcentration, "专注") +
                "检定获得相当于" + Glossary.Link(Glossary.KClassLevel, "职业等级") +
                "+1 的加值，但该加值最高为 +6。",
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
                "幸运野兔：时序预感",
                "自职业等级 7 级起，契约野兔会在时间真正流逝之前数次颤动双耳。" +
                "空隙法师进行" + Glossary.Link(Glossary.KInitiative, "先攻") +
                "检定时获得优势：掷两次 d20 并取较高结果。" +
                "该优势只作用于先攻检定，不改变其他检定或攻击的掷骰方式。",
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
                "空隙法师以本能驱动施法，使" + Glossary.Link(Glossary.KSpell, "法术") +
                "的瞬发成本随" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") + "降低：" +
                "13 级起为 +3，15 级起为 +2，17 级起为 +1。" +
                "该效果可与「偏好超魔：瞬发」叠加，最终环位消耗最低为 0。",
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
