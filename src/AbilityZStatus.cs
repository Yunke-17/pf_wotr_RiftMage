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
    /// 能力 Z 的状态定义（用户 2026-10-20 最终版）。
    ///
    ///   使用 Z → 力竭 30 秒 + 疲劳 90 秒，**同时开始计时**
    ///   力竭 30s 先消失，疲劳继续到 90s
    ///
    /// ========== 为什么力竭要自建 ==========
    ///
    /// 原版 Exhausted.jbp 除了 AddCondition{Slowed/Exhausted} 外还挂了两个
    /// AddGenericStatBonus（属性惩罚）。自建时保留同样的条件，
    /// 但**不再挂任何后续动作** —— 疲劳由 AbilityZAction 直接施加 90 秒，
    /// 避免「力竭结束时再补一次疲劳」造成重复叠加或时长错乱。
    ///
    /// 疲劳直接复用原版 Fatigued（无联动动作，套用安全）。
    /// </summary>
    internal static class AbilityZStatus
    {
        internal const string GuidExhaustedBuff = "b1000000000000000000000000000043";

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
                "由「透支充能」造成，持续 30 秒。");

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

            // ★ 不再挂 AddFactContextActions。
            //   疲劳由 AbilityZAction 直接施加 90 秒（与力竭同时开始计时），
            //   这里若再加一次「力竭结束时施加疲劳」，会导致疲劳被重复叠加、
            //   总时长变成 90+90，与需求不符。

            var buff = new BlueprintBuff
            {
                name = "WizardArchetype_AbilityZ_Exhausted",
                AssetGuid = BlueprintGuid.Parse(GuidExhaustedBuff),
                Stacking = StackingType.Replace,
                Frequency = DurationRate.Rounds,
                Ranks = 0,
                IsClassFeature = false,
                ComponentsArray = new BlueprintComponent[] { slowed, exhausted },
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
