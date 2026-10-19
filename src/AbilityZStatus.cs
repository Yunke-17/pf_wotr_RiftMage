using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 使用的状态（用户 2026-10-20 最终定稿）。
    ///
    /// ========== 两个状态都用原版 ==========
    ///
    ///   力竭 Exhausted  (46d1b9cc3d0fd36469a471b047d773a2)
    ///       条件：Slowed + Exhausted
    ///       效果：两个 AddGenericStatBonus（攻击/豁免/技能 -2、移速降低等）
    ///   疲劳 Fatigued   (e6f2fc5d73d88064583cb828801212f4)
    ///       条件：Fatigued
    ///
    /// 之前曾自建「透支力竭」，但自建版缺了原版的两个属性惩罚，
    /// 效果与原版力竭不一致。用户要求直接用原版，故删除自建蓝图。
    ///
    /// ========== 时长为什么是 30s + 90s ==========
    ///
    /// 用户设计意图：
    ///   力竭 30s 与疲劳 90s **同时开始计时**。
    ///   30 秒后力竭结束，疲劳还剩 60 秒继续生效。
    ///   ⇒ 表现为「力竭 30s，随后再疲劳 60s」，但只施加一次、无二次触发。
    ///
    ///   90 = 30（与力竭重叠）+ 60（力竭结束后的纯疲劳）
    ///
    /// 这样不需要任何「力竭结束时再补疲劳」的联动逻辑，
    /// 也就不会出现疲劳被重复叠加的问题。
    /// </summary>
    internal static class AbilityZStatus
    {
        internal const string VanillaExhausted = "46d1b9cc3d0fd36469a471b047d773a2";
        internal const string VanillaFatigued = "e6f2fc5d73d88064583cb828801212f4";

        /// <summary>力竭持续 30 秒。</summary>
        internal const float ExhaustedSeconds = 30f;

        /// <summary>疲劳持续 90 秒（30 秒与力竭重叠 + 60 秒纯疲劳）。</summary>
        internal const float FatiguedSeconds = 90f;

        internal static BlueprintBuff ExhaustedBuff { get; private set; }
        internal static BlueprintBuff FatiguedBuff { get; private set; }

        internal static void Create(BlueprintsCache cache)
        {
            ExhaustedBuff = cache.Load(BlueprintGuid.Parse(VanillaExhausted)) as BlueprintBuff;
            if (ExhaustedBuff == null)
            {
                Main.Log?.Error("[能力Z] 原版 Exhausted 加载失败（力竭效果将缺失）");
            }

            FatiguedBuff = cache.Load(BlueprintGuid.Parse(VanillaFatigued)) as BlueprintBuff;
            if (FatiguedBuff == null)
            {
                Main.Log?.Error("[能力Z] 原版 Fatigued 加载失败（疲劳效果将缺失）");
            }

            Main.Log?.Log("[能力Z] 状态：原版力竭 " + ExhaustedSeconds +
                          "s + 原版疲劳 " + FatiguedSeconds + "s（同时计时）");
        }
    }
}
