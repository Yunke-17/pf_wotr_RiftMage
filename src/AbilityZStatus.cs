using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Localization;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的状态链（用户 2026-10-20 修正）：
    ///
    ///   使用 Z → 力竭 30 秒 → **力竭结束后**再疲劳 60 秒
    ///
    /// ========== 为什么不能直接用原版 Exhausted ==========
    ///
    /// 原版 Exhausted.jbp 只挂 AddCondition{Slowed/Exhausted}，
    /// 没有任何「结束后接疲劳」的动作。
    /// 若直接改原版蓝图，会影响所有原版来源（狂暴、法术等）——属于暗改。
    ///
    /// 因此自建一个「透支力竭」Buff：
    ///   - 条件效果与原版力竭一致（Slowed + Exhausted，等价于原版两个 AddCondition）
    ///   - 额外挂 AddFactContextActions{Deactivated: 施加 60 秒疲劳}
    ///     ⇒ 力竭到期/被移除时，自动接上疲劳
    ///
    /// 疲劳直接复用原版 Fatigued（它没有联动动作，套用安全）。
    ///
    /// ========== 为什么不用计时器轮询 ==========
    ///
    /// AddFactContextActions.OnDeactivate（:44-56）在 Fact 失效时**必然**执行，
    /// 覆盖「自然到期」和「被驱散/提前移除」两种情况，比自建倒计时可靠。
    /// </summary>
    internal static class AbilityZStatus
    {
        internal const string GuidExhaustedBuff = "b1000000000000000000000000000043";
        internal const string GuidFatiguedBuff = "b1000000000000000000000000000044";

        private const string VanillaFatigued = "e6f2fc5d73d88064583cb828801212f4";

        internal static BlueprintBuff ExhaustedBuff { get; private set; }
        internal static BlueprintBuff FatiguedBuff { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            // 疲劳：直接复用原版
            FatiguedBuff = cache.Load(BlueprintGuid.Parse(VanillaFatigued)) as BlueprintBuff;
            if (FatiguedBuff == null)
            {
                Main.Log?.Error("[能力Z] 原版 Fatigued 加载失败");
            }

            ExhaustedBuff = CreateExhausted(cache);
        }

        private static BlueprintBuff CreateExhausted(BlueprintsCache cache)
        {
            const string root = "WizardArchetype.AbilityZ.";
            WizardArchetypeBlueprints.RegisterText(root + "ZExhausted.Name", "透支力竭");
            WizardArchetypeBlueprints.RegisterText(root + "ZExhausted.Desc",
                "力竭：攻击检定、豁免检定、技能检定 -2，移动速度降低。\n" +
                "由「透支充能」造成，持续 30 秒；结束后转入 60 秒疲劳。");

            // 条件效果：与原版 Exhausted 一致（Slowed + Exhausted）
            var slowed = new BuffStatusCondition
            {
                name = "$BuffStatusCondition$ZExhausted_Slowed",
                Condition = UnitCondition.Slowed,
                SaveEachRound = false,
                SaveType = SavingThrowType.Unknown,
            };

            var exhausted = new BuffStatusCondition
            {
                name = "$BuffStatusCondition$ZExhausted_Exhausted",
                Condition = UnitCondition.Exhausted,
                SaveEachRound = false,
                SaveType = SavingThrowType.Unknown,
            };

            // 力竭结束后 → 施加 60 秒疲劳
            var applyFatigue = new ContextActionApplyBuff
            {
                name = "$ContextActionApplyBuff$ZExhausted_Fatigue",
                Permanent = false,
                UseDurationSeconds = true,      // 用秒级，避免轮次时长被战斗结束清掉
                DurationSeconds = 60f,          // 注意：这是 float 字段（ContextActionApplyBuff.cs:150）
                DurationValue = new ContextDurationValue
                {
                    Rate = DurationRate.Rounds,
                    DiceType = Kingmaker.RuleSystem.DiceType.Zero,
                    DiceCountValue = new ContextValue
                    {
                        ValueType = ContextValueType.Simple,
                        Value = 0,
                    },
                    BonusValue = new ContextValue
                    {
                        ValueType = ContextValueType.Simple,
                        Value = 0,
                    },
                },
            };
            ReflectionFields.Set(applyFatigue, "m_Buff",
                BlueprintReference<BlueprintBuff>.CreateTyped<BlueprintBuffReference>(FatiguedBuff));

            var contextActions = new AddFactContextActions
            {
                name = "$AddFactContextActions$ZExhausted",
                Activated = new ActionList { Actions = new GameAction[0] },
                Deactivated = new ActionList { Actions = new GameAction[] { applyFatigue } },
                NewRound = new ActionList { Actions = new GameAction[0] },
                Dispose = new ActionList { Actions = new GameAction[0] },
            };

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_AbilityZ_Exhausted",
                AssetGuid = BlueprintGuid.Parse(GuidExhaustedBuff),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = false,
                ComponentsArray = new BlueprintComponent[] { slowed, exhausted, contextActions },
                FxOnRemove = new Kingmaker.ResourceLinks.PrefabLink(),
            };

            ReflectionFields.Set(buff, "m_DisplayName",
                new LocalizedString { Key = root + "ZExhausted.Name" });
            ReflectionFields.Set(buff, "m_Description",
                new LocalizedString { Key = root + "ZExhausted.Desc" });

            foreach (var c in buff.ComponentsArray)
                if (c != null) c.OwnerBlueprint = buff;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidExhaustedBuff), buff);
            return buff;
        }
    }
}
