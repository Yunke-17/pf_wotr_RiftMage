using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.Localization;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace WizardArchetype
{
    /// <summary>
    /// 特殊纠缠（说明书 §6.3 / §6.4）。
    ///
    /// ========== 关键决策：不挂 UnitCondition.Entangled ==========
    ///
    /// 原版纠缠的真相（Root/SystemMechanics/Conditions/EntangledBuff.jbp）：
    /// 它的数据定义里**根本没有 UnitCondition.Entangled**，只有
    ///   AddStatBonus{-2 攻击} + AddStatBonus{-4 敏捷}
    /// 而「移动速度减半」来自**代码**（UnitEntityData.CalculateSpeedModifier 读 HasCondition(Entangled)）。
    ///
    /// 陷阱：若我们挂 Entangled 来触发限制，速度会减半（需求明确不要减速）；
    /// 若再用 *= 2 补回来，会把真实纠缠的减速也抵掉（两者读同一个 HasCondition）。
    ///
    /// ⇒ 本 MOD **完全不挂 Entangled**，只做「强制专注」：
    ///     - 天然不减速；
    ///     - 天然不被「行动自如」免疫（§6.3 / C7），因为压根没有那个条件；
    ///     - 也不必碰速度计算。
    ///
    /// 说明书 §6.3 只要求「具有普通纠缠的相关限制」中的施法专注，
    /// 经用户确认（2026-10-18）**不施加** -2 攻击 / -4 敏捷。
    /// </summary>
    internal static class SpecialEntangleBlueprints
    {
        internal const string GuidSpecialEntangled = "b1000000000000000000000000000021";

        private const string KeyName = "WizardArchetype.SpecialEntangled.Name";
        private const string KeyDesc = "WizardArchetype.SpecialEntangled.Desc";

        internal static BlueprintBuff SpecialEntangled { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            if (SpecialEntangled != null) return;

            WizardArchetypeBlueprints.RegisterText(KeyName, "特殊纠缠");
            WizardArchetypeBlueprints.RegisterText(KeyDesc,
                "施放法师法术时必须通过额外的专注检定。\n" +
                "不会降低移动速度，也不会被「行动自如」免疫或解除。");

            var concentration = new SpecialEntangleConcentration
            {
                name = "$SpecialEntangleConcentration$SpecialEntangled_0",
            };

            var combatTrigger = new CombatStateTrigger
            {
                name = "$CombatStateTrigger$SpecialEntangled_1",
                CombatStartActions = new ActionList { Actions = new GameAction[0] },
                CombatEndActions = new ActionList
                {
                    Actions = new GameAction[]
                    {
                        new ContextActionRemoveSelf { name = "$ContextActionRemoveSelf$SpecialEntangled_2" },
                    },
                },
            };

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_SpecialEntangled",
                AssetGuid = BlueprintGuid.Parse(GuidSpecialEntangled),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = false,
                ComponentsArray = new BlueprintComponent[] { concentration, combatTrigger },
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            ReflectionFields.Set(buff, "m_DisplayName", new LocalizedString { Key = KeyName });
            ReflectionFields.Set(buff, "m_Description", new LocalizedString { Key = KeyDesc });

            foreach (var c in buff.ComponentsArray)
            {
                if (c != null) c.OwnerBlueprint = buff;
            }

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidSpecialEntangled), buff);
            SpecialEntangled = buff;

            Main.Log?.Log("WizardArchetype: 已创建「特殊纠缠」蓝图");
        }
    }

    /// <summary>
    /// 特殊纠缠：每次施法都要过额外专注检定。
    ///
    /// 为什么写 CustomDC 而不是加条件：
    ///   RuleCheckConcentration.CustomDC 是 public int? { get; set; }（:36），
    ///   OnTrigger 在 :77 读它，而 OnEventAboutToTrigger **先于** OnTrigger 执行
    ///   （Rulebook.TriggerEventInternal 的顺序是 About → OnTrigger → Did），
    ///   所以写入时机安全。
    ///
    /// 为什么不用 ForbidSpellCasting / SpellCastingIsDifficult：
    ///   前者是「禁止施法」，与「堆专注克服」的设计目标冲突（用户明确排除）；
    ///   后者会由引擎自动加 15+环 的 DC，无法表达文档的分档公式。
    /// </summary>
    internal class SpecialEntangleConcentration : UnitBuffComponentDelegate,
        IInitiatorRulebookHandler<RuleCheckConcentration>
    {
        public void OnEventAboutToTrigger(RuleCheckConcentration evt)
        {
            if (evt == null || evt.Spell == null) return;

            // 只对法师法术书的法术生效（与 Q1 判据一致）
            if (!AbilityXHelper.IsEligible(evt.Spell)) return;

            int level = GetWizardLevel(Owner);
            int dc = GetConcentrationDC(level);

            evt.CustomDC = dc;

            Main.Log?.Log($"[特殊纠缠] 强制专注：{evt.Spell.Blueprint?.name}，法师等级 {level}，DC {dc}");
        }

        public void OnEventDidTrigger(RuleCheckConcentration evt)
        {
            if (evt == null) return;

            // §6.4：专注成功 → 法术正常施放，并立即移除特殊纠缠
            if (evt.Success)
            {
                Main.Log?.Log("[特殊纠缠] 专注通过，移除特殊纠缠");
                Buff?.Remove();
                return;
            }

            // §6.4：专注失败 → 法术失效（引擎原生处理：UnitUseAbility.ConcentrationCheckFailed）
            //              角色受到 1d2 点体质伤害；同一轮内最多结算一次
            Main.Log?.Log("[特殊纠缠] 专注失败，法术失效");
            ApplyConstitutionDamage();
        }

        /// <summary>
        /// 专注失败：1d2 点体质伤害，同一轮内最多一次（§6.4）。
        ///
        /// 用引擎的属性伤害管线 RuleDealStatDamage，而不是直接改属性值：
        ///   - 走规则系统 ⇒ 会正常参与伤害统计、免疫判定、战斗日志；
        ///   - 直接改 Stats 会绕过所有机制，属于暗改。
        ///
        /// 同轮保护：按「游戏时间 / 6 秒」估算轮次，同一轮内只结算一次。
        /// </summary>
        private void ApplyConstitutionDamage()
        {
            var unit = Owner;
            if (unit == null) return;

            int round = CurrentRound();

            if (round >= 0 && s_LastDamageRound.TryGetValue(unit, out RoundMark last) && last.Round == round)
            {
                Main.Log?.Log("[特殊纠缠] 本轮已结算过体质伤害，跳过");
                return;
            }

            if (round >= 0)
            {
                s_LastDamageRound.Remove(unit);
                s_LastDamageRound.Add(unit, new RoundMark { Round = round });
            }

            try
            {
                var rule = new Kingmaker.RuleSystem.Rules.RuleDealStatDamage(
                    unit,
                    unit,
                    Kingmaker.EntitySystem.Stats.StatType.Constitution,
                    new Kingmaker.RuleSystem.DiceFormula(1, Kingmaker.RuleSystem.DiceType.D2),
                    0);

                var game = Kingmaker.Game.Instance;
                if (game == null || game.Rulebook == null) return;

                game.Rulebook.TriggerEvent<Kingmaker.RuleSystem.Rules.RuleDealStatDamage>(rule);

                Main.Log?.Log($"[特殊纠缠] 专注失败代价：1d2 体质伤害（实际 {rule.Result}）");
                AbilityXScope.AcceptanceHook("X.EntangleConDamage", rule.Result);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特殊纠缠] 体质伤害结算失败：" + e.Message);
            }
        }

        /// <summary>每个单位上次结算体质伤害的轮次（CWT 的 value 必须是引用类型，用包装类）。</summary>
        private sealed class RoundMark { public int Round; }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            Kingmaker.EntitySystem.Entities.UnitEntityData, RoundMark> s_LastDamageRound =
            new System.Runtime.CompilerServices.ConditionalWeakTable<
                Kingmaker.EntitySystem.Entities.UnitEntityData, RoundMark>();

        private static int CurrentRound()
        {
            try
            {
                var game = Kingmaker.Game.Instance;
                if (game == null || game.Player == null) return -1;
                return (int)(game.Player.GameTime.TotalSeconds / 6.0);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// DC(L) = min(32, 25 + round(3(L-1)/7))，L 为法师等级（§6.3）。
        /// </summary>
        internal static int GetConcentrationDC(int wizardLevel)
        {
            if (wizardLevel < 1) wizardLevel = 1;
            int dc = 25 + (int)Math.Round(3.0 * (wizardLevel - 1) / 7.0, MidpointRounding.AwayFromZero);
            return Math.Min(32, dc);
        }

        private static int GetWizardLevel(Kingmaker.EntitySystem.Entities.UnitEntityData unit)
        {
            try
            {
                var wiz = WizardArchetypeBlueprints.WizardClass;
                if (unit == null || wiz == null) return unit != null ? 1 : 0;
                return unit.Progression.GetClassLevel(wiz);
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
