using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.Localization;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X「强化施法」—— 阶段 3 第 0 步：最小可观测性。
    ///
    /// 本步刻意【不含任何自定义组件】，全部用原版现成件拼装，目的只有一个：
    /// 先证明"四个蓝图能注册、能出现在特性列表、能用、状态能挂上、休息能恢复"。
    /// 上次能力 X 失败就是因为一次写了太多东西，失败时无法定位。
    ///
    /// 对应文档：08-能力X实现方案.md §5「第 0 步」
    /// 需求：D2（法师 1 级、自由动作）、D3（每次休息 1 次）、D4（3 轮状态）
    /// </summary>
    internal static class AbilityX
    {
        // GUID 段位沿用现有 b100…，1–8 已被 WizardArchetypeFeatures 占用
        internal const string GuidFeature   = "b1000000000000000000000000000010";  // 特性
        internal const string GuidResource  = "b1000000000000000000000000000011";  // 资源
        internal const string GuidAbility   = "b1000000000000000000000000000012";  // 能力
        internal const string GuidBuff      = "b1000000000000000000000000000013";  // 状态

        private const string KeyBase = "AbilityX";

        internal static BlueprintFeature Feature { get; private set; }
        internal static BlueprintAbilityResource Resource { get; private set; }
        internal static BlueprintAbility Ability { get; private set; }
        internal static BlueprintBuff Buff { get; private set; }

        /// <summary>
        /// 构建并注册四个蓝图。
        /// 必须在 WizardArchetypeBlueprints.Register 里、BuildAll 之前调用，
        /// 因为特性列表需要拿到 Feature 实例。
        /// </summary>
        internal static BlueprintFeature Create(BlueprintsCache cache)
        {
            CreateText();

            // 特殊恍惚蓝图（能力X 代价用，也供阶段4的战斗开场状态复用）
            SpecialStatuses.Create(cache);
            // 特殊纠缠蓝图（战斗开场状态用）
            SpecialEntangleBlueprints.Create(cache);
            // 能力Y 蓝图（3 级特性）
            AbilityYBlueprints.Create(cache);
            // 能力Z 蓝图（基础 CL 7 起）
            AbilityZ.Create(cache);

            // 1) 资源（每次休息 1 次）
            Resource = CreateResource(cache);

            // 2) 状态（3 轮，本步不挂任何组件）
            Buff = CreateBuff(cache);

            // 3) 能力（自由动作，消耗资源，挂状态）
            Ability = CreateAbility(cache);

            // 4) 特性（给能力 + 给资源）
            Feature = CreateFeature(cache);

            // 5) 图标 / 光环 / 音效统一由 Presentation.Apply 在全部蓝图构建完之后装配

            Main.Log?.Log("WizardArchetype: 能力X 蓝图已构建");
            return Feature;
        }

        // ---------- 资源 ----------

        /// <summary>
        /// «每次休息 1 次» 的实现方式：
        /// BlueprintAbilityResource.m_MaxAmount 是私有字段，且其类型 Amount 是私有嵌套 struct，
        /// 没有公开构造器，只能取默认实例后逐字段写（参考 Original OverwhelmingSoul 的配置：
        /// BaseValue=1，其余全默认）。
        /// </summary>
        private static BlueprintAbilityResource CreateResource(BlueprintsCache cache)
        {
            var res = new BlueprintAbilityResource
            {
                name = "WizardArchetype_AbilityXResource",
                AssetGuid = BlueprintGuid.Parse(GuidResource),
                LocalizedName = new LocalizedString { Key = "WizardArchetype." + KeyBase + ".ResourceName" },
                LocalizedDescription = new LocalizedString { Key = "WizardArchetype." + KeyBase + ".ResourceDesc" },
            };

            // 取私有嵌套 struct 的默认实例，只改 BaseValue
            var maxField = typeof(BlueprintAbilityResource).GetField(
                "m_MaxAmount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (maxField == null)
            {
                Main.Log?.Error("WizardArchetype: BlueprintAbilityResource.m_MaxAmount 字段缺失，能力X 资源上限无法设置");
                return res;
            }

            object amount = maxField.GetValue(res);   // 值类型装箱
            var baseValueField = amount.GetType().GetField("BaseValue");
            if (baseValueField == null)
            {
                Main.Log?.Error("WizardArchetype: Amount.BaseValue 字段缺失，能力X 资源上限无法设置");
                return res;
            }

            baseValueField.SetValue(amount, 1);
            maxField.SetValue(res, amount);

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidResource), res);
            return res;
        }

        // ---------- 状态 ----------

        /// <summary>
        /// 强化施法状态，3 轮。
        /// 本步【不挂任何效果组件】——只验证状态本身能不能挂上、到期、手动移除。
        /// </summary>
        private static BlueprintBuff CreateBuff(BlueprintsCache cache)
        {
            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_AbilityXBuff",
                AssetGuid = BlueprintGuid.Parse(GuidBuff),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = true,
                ComponentsArray = new BlueprintComponent[0],

                // ★ 必须给 FxOnRemove 一个非 null 实例，否则会在到期移除时崩溃。
                //
                // 依据（Kingmaker.UnitLogic.Buffs.Buff.OnRemove 的 IL，偏移 0x00B0）：
                //   0x00AA  call    UnitFact.get_Blueprint()
                //   0x00B0  ldfld   BlueprintBuff::FxOnRemove    ← 为 null
                //   0x00B7  callvirt WeakResourceLink.Load(...)  ← 对 null 调用 → NullReferenceException
                //
                // 为什么原版大多数 Buff 的 FxOnRemove 也是 null 却没事：
                // 只有当单位有 UnitEntityView 且带 ParticlesSnapMap 时（Buff.TrySpawnParticleEffect，
                // Buff.cs:447），才会给 m_ParticleEffectOwner 赋值；一旦有值，移除时必然走到上面那句。
                // 玩家棋子一定有 ParticlesSnapMap，所以玩家身上的 Buff 必须设 FxOnRemove。
                //
                // 这里刻意【不设 AssetId】：WeakResourceLink.Load 在 AssetId 为空时会直接
                // return default（WeakResourceLink.2.cs:54-57），既不崩溃也【没有视觉特效】。
                // 等效果组件齐全、需要正反馈时再换成一个真正的特效。
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            // ★ 合格法术施放后移除（自建组件，判定见 AbilityXHelper.IsEligible）
            //
            // 曾用过原版 AddAbilityUseTrigger + ContextActionRemoveSelf，但它的
            // CheckAbilityType 只能判断「是不是法术」，无法表达 Q1 要求的
            // 「法术书归属职业 == 法师」，会误吃戏法与术士/牧师法术。
            //
            // 第 1 步（作用域）：AbilityXScope 同时负责 arm（瞄准哪一发）与 consume（成功后移除）。
            //   造抄原版 MetamagicOnNextSpell 的两阶段骨架，但改成 Did + Success 守卫。
            //   后续效果组件（加骰面 / 豁免取低 / 无视 SR / 代价恍惚）将读它的 Data.Spell。
            var scope = new AbilityXScope
            {
                name = "$AbilityXScope$AbilityX_0",
            };

            buff.ComponentsArray = new BlueprintComponent[] { scope };

            // 组件 name 必须唯一非空（05 踩坑 #1），赋值后必须回填 OwnerBlueprint（05 踩坑 #2）
            foreach (var c in buff.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = buff;
            }
            SetNameAndDesc(buff, "WizardArchetype." + KeyBase + ".BuffName",
                "WizardArchetype." + KeyBase + ".BuffDesc");

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidBuff), buff);
            return buff;
        }

        // ---------- 能力 ----------

        private static BlueprintAbility CreateAbility(BlueprintsCache cache)
        {
            // 挂状态的动作：3 轮
            var applyBuff = new ContextActionApplyBuff
            {
                name = "$ContextActionApplyBuff$AbilityX_0",
                Permanent = false,
                UseDurationSeconds = false,
                DurationValue = new Kingmaker.UnitLogic.Mechanics.ContextDurationValue
                {
                    Rate = DurationRate.Rounds,
                    DiceType = Kingmaker.RuleSystem.DiceType.Zero,
                    // 固定 3 轮：骰数为 0，加值 3（ContextValue.cs 的 Simple 类型直接取 Value）
                    DiceCountValue = new Kingmaker.UnitLogic.Mechanics.ContextValue
                    {
                        ValueType = Kingmaker.UnitLogic.Mechanics.ContextValueType.Simple,
                        Value = 0,
                    },
                    BonusValue = new Kingmaker.UnitLogic.Mechanics.ContextValue
                    {
                        ValueType = Kingmaker.UnitLogic.Mechanics.ContextValueType.Simple,
                        Value = 3,
                    },
                },
            };
            ReflectionFields.Set(applyBuff, "m_Buff",
                BlueprintReference<BlueprintBuff>.CreateTyped<BlueprintBuffReference>(Buff));

            var runAction = new AbilityEffectRunAction
            {
                name = "$AbilityEffectRunAction$AbilityX_1",
                SavingThrowType = Kingmaker.EntitySystem.Stats.SavingThrowType.Unknown,
            };
            // ActionList 没有单参数构造器，只能建空列表后 Add
            runAction.Actions = new Kingmaker.ElementsSystem.ActionList();
            runAction.Actions.Actions = new Kingmaker.ElementsSystem.GameAction[] { applyBuff };

            var resourceLogic = new AbilityResourceLogic
            {
                name = "$AbilityResourceLogic$AbilityX_2",
                Amount = 1,
                CostIsCustom = false,
                ResourceCostIncreasingFacts = new List<BlueprintUnitFactReference>(),
                ResourceCostDecreasingFacts = new List<BlueprintUnitFactReference>(),
            };
            ReflectionFields.Set(resourceLogic, "m_RequiredResource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));
            ReflectionFields.Set(resourceLogic, "m_IsSpendResource", true);

            // 状态已存在时禁用能力（原版「法术大师·专注法术」同款）
            // 依据：AbilityCasterHasNoFacts.IsCasterRestrictionPassed 在施法者已拥有这些 Fact 时返回 false
            var noFacts = new Kingmaker.UnitLogic.Abilities.Components.CasterCheckers.AbilityCasterHasNoFacts
            {
                name = "$AbilityCasterHasNoFacts$AbilityX_3",
            };
            ReflectionFields.Set(noFacts, "m_Facts",
                new BlueprintUnitFactReference[]
                {
                    BlueprintReference<BlueprintUnitFact>.CreateTyped<BlueprintUnitFactReference>(Buff),
                });

            var ability = new BlueprintAbility
            {
                name = "WizardArchetype_AbilityX",
                AssetGuid = BlueprintGuid.Parse(GuidAbility),
                Type = AbilityType.Supernatural,
                Range = AbilityRange.Personal,
                ActionType = UnitCommand.CommandType.Free,
                SpellResistance = false,
                ComponentsArray = new BlueprintComponent[] { runAction, resourceLogic, noFacts },
            };

            // ★ 2026-10-21（用户反馈「持续时间 null / 豁免检定 null」）：
            //
            // 这两个是 BlueprintAbility 上的 **LocalizedString 字段**（:1081/:1084）。
            // 纯 C# 构造时若不给值就是 null，UI 会原样打印 "<null>"。
            // 原版能力的做法是给一个 **空 key 的 LocalizedString 实例**（不是 null），
            // 这样 UI 判定为「没有该字段」而整行隐藏。
            //
            // 本能力是自由动作、自身目标、无时长、无豁免，正应该走这条路。
            ability.LocalizedDuration = new LocalizedString();
            ability.LocalizedSavingThrow = new LocalizedString();
            SetNameAndDesc(ability, "WizardArchetype." + KeyBase + ".Name",
                "WizardArchetype." + KeyBase + ".Desc");

            // 回填 OwnerBlueprint（05 踩坑 #2）
            foreach (var c in ability.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = ability;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidAbility), ability);
            return ability;
        }

        // ---------- 特性 ----------

        private static BlueprintFeature CreateFeature(BlueprintsCache cache)
        {
            var addFacts = new AddFacts
            {
                name = "$AddFacts$AbilityX_0",
            };
            ReflectionFields.Set(addFacts, "m_Facts",
                new BlueprintUnitFactReference[]
                {
                    BlueprintReference<BlueprintUnitFact>.CreateTyped<BlueprintUnitFactReference>(Ability),
                });

            var addResource = new AddAbilityResources
            {
                name = "$AddAbilityResources$AbilityX_1",
                UseThisAsResource = false,
                Amount = 0,
                RestoreAmount = false,
                RestoreOnLevelUp = false,
            };
            ReflectionFields.Set(addResource, "m_Resource",
                BlueprintReference<BlueprintAbilityResource>.CreateTyped<BlueprintAbilityResourceReference>(Resource));

            // ★ 无视 SR 的订阅者必须挂在**特性**上，不能挂在强化状态 Buff 上。
            //
            // 原因：SR 检定发生在 RuleCastSpell.Did 之后的帧里（AbilityExecutionProcess 协程），
            // 而强化状态在 Did 里就被移除了 —— 订阅随 Buff 一起失效，
            // 等 SR 检定来临时收不到通知，IgnoreSpellResistance 就设不上。
            // 特性是常驻的，能覆盖整个时间窗。
            var srBypass = new AbilityXSrBypass
            {
                name = "$AbilityXSrBypass$AbilityX_2",
            };

            // 加骰面（D8）：同样必须挂在特性上 —— 伤害结算在 RuleCastSpell.Did 之后的帧里，
            // 那时强化状态 Buff 已被移除，挂 Buff 上的订阅收不到通知。
            var diceBonus = new AbilityXDamageDice
            {
                name = "$AbilityXDamageDice$AbilityX_3",
            };

            // 豁免多骰取低（D9）：同样挂在特性上，理由同上。
            var savingThrow = new AbilityXSavingThrow
            {
                name = "$AbilityXSavingThrow$AbilityX_4",
            };

            // ★ 2026-10-20：战斗开场「按先攻二选一」的 SpecialOpeningState 已迁出，
            //   改挂到独立的「开场时序」特性上（见 OpeningTimingFeature）。
            //   理由：那条规则和空隙湮灭本身无关，混在这条描述里让玩家看不出规则来源。

            var feature = new BlueprintFeature
            {
                name = "WizardArchetype_AbilityXFeature",
                AssetGuid = BlueprintGuid.Parse(GuidFeature),
                Ranks = 1,
                IsClassFeature = true,
                HideInUI = false,
                ComponentsArray = new BlueprintComponent[]
                {
                    addFacts, addResource, srBypass, diceBonus, savingThrow,
                },
            };
            SetNameAndDesc(feature, "WizardArchetype." + KeyBase + ".Name",
                "WizardArchetype." + KeyBase + ".Desc");

            foreach (var c in feature.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = feature;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidFeature), feature);
            return feature;
        }

        // ---------- 本地化 ----------

        /// <summary>
        /// 写显示名与描述。
        ///
        /// BlueprintAbility / BlueprintFeature / BlueprintBuff 都继承 BlueprintUnitFact，
        /// 而 m_DisplayName / m_Description 是该类的【私有字段】（BlueprintUnitFact.cs:113/119），
        /// 没有公开 setter，只能反射写 —— 这正是 05 踩坑 #3 记录过的路径，
        /// 也是 Verify.exe 会校验的两个字段。
        /// </summary>
        private static void SetNameAndDesc(SimpleBlueprint bp, string nameKey, string descKey)
        {
            ReflectionFields.Set(bp, "m_DisplayName", new LocalizedString { Key = nameKey });
            ReflectionFields.Set(bp, "m_Description", new LocalizedString { Key = descKey });
        }

        private static void CreateText()
        {
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".Name",
                "空隙湮灭");
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".Desc",
                "空隙法师能够短暂撕开一条现实缝隙，令因果在其面前失效。" +
                "以一个" + Glossary.Link(Glossary.KFreeAction, "自由动作") +
                "激活后，法师在此后 3 轮内施放的下一个" +
                Glossary.Link(Glossary.KSpell, "法术") + "将获得强化。\n" +
                "被强化的法术会随" + Glossary.Link(Glossary.KCasterLevel, "施法者等级") +
                "获得不同程度的增益：" +
                "伤害骰的骰面提高 2（5 级起为 4，10 级起为 6）；" +
                "敌方针对该法术的" + Glossary.Link(Glossary.KSavingThrow, "豁免") +
                "检定需要掷 2 次并取较低结果（5 级起为 3 次，10 级起为 4 次）；" +
                "该法术还会自动克服目标的" + Glossary.Link(Glossary.KSpellResistance, "法术抗力") +
                "，并可突破以描述符判定的魔法免疫。\n" +
                "只有当施放的是来自法师法术书的非戏法法术时，强化才会生效。" +
                "若 3 轮内未施放合格法术，状态会消失，且本次使用次数不会返还。\n" +
                "作为代价，法术施放完成的瞬间，法师自身会立即陷入 1 轮" +
                "时序恍惚（如同恍惚：每轮只能进行一个移动动作或一个标准动作，移动速度减半）。\n" +
                "每次休息后恢复 1 次使用次数。");
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".BuffName",
                "空隙湮灭");
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".BuffDesc",
                "现实缝隙已经撕开。你下一次施放的法师法术会获得强化：" +
                "伤害骰面提高，" + Glossary.Link(Glossary.KSavingThrow, "豁免") +
                "检定多次取低，自动克服" + Glossary.Link(Glossary.KSpellResistance, "法术抗力") +
                "，并可突破描述符类免疫。" +
                "强化完成后，你会立即陷入 1 轮时序恍惚" +
                "（每轮只能进行一个移动动作或一个标准动作）。");
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".ResourceName",
                "空隙湮灭");
            WizardArchetypeBlueprints.RegisterText("WizardArchetype." + KeyBase + ".ResourceDesc",
                "每次休息后恢复 1 次。");
        }
    }
}
