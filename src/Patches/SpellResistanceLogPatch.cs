using HarmonyLib;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 F —— 强化法术的 SR 检定不再进战斗日志。
    ///
    /// ========== 为什么要这个补丁 ==========
    ///
    /// 需求（说明书 §8.3 / 08 文档 2a）：
    ///   「被能力 X 强化的法术自动通过目标的法术抗力检定」，
    ///   验收标准是「对 SR 怪物施法，**战斗日志无 SR 检定**」。
    ///
    /// 实际现象（已实测，见附图）：
    ///   虽然 ForceIgnoreSpellResist 让法术生效了，但战斗日志仍然打出一条 SR 检定，
    ///   而且数值自相矛盾：
    ///       检定: 5（投骰 3 + 调整值 2） / 法术抗力(SR): 20 / 结果: 成功
    ///   5 < 20 却「成功」，是因为成功来自 ForceIgnoreSpellResist 而非骰值。
    ///   这不是 bug（结算确实对），是**纯显示问题**，但对玩家来说像是数值算错了。
    ///
    /// ========== 日志是哪来的 ==========
    ///
    /// 全程序集只有一处产生 SR 日志：
    ///   RulebookSpellResistanceCheckLogThread.HandleEvent(RuleSpellResistanceCheck)
    ///   （LogThreadService.cs:139 注册的实例）
    /// 它的逻辑：
    ///   if (!rule.HasResistanceRoll && !rule.TargetIsImmune) return;
    ///   ... AddMessage(rule.IsSpellResisted
    ///         ? (TargetIsImmune ? SpellImmunity : SpellResistanceNotPassed)
    ///         : SpellResistancePassed);
    /// ForceIgnoreSpellResist 使 IsSpellResisted == false ⇒ 走 SpellResistancePassed
    /// （就是「穿透了…的法术抗力」那条）。
    ///
    /// ========== 为什么只抑制「我们的」，不按标志一刀切 ==========
    ///
    /// 宗师之杖（MetamagicRodMechanics）同样会置 ForceIgnoreSpellResist。
    /// 若按标志判断，会把原版道具的日志一并改掉 —— 属于「暗改」，
    /// 违反本项目的硬性原则。因此只在 AbilityXScope 亲手标记过的施法上下文上抑制。
    ///
    /// ========== 用 Prefix 返回 false ==========
    ///
    /// HandleEvent 返回 void 且没有返回值可改，所以必须用 Prefix 返回 false 直接跳过。
    /// 这是**短路**而非修改返回值，但只作用于「本 MOD 自己置位的上下文」，
    /// 其他任何 SR 检定（包括原版神器、怪物、其他 MOD）完全不受影响。
    /// </summary>
    [HarmonyPatch(typeof(RulebookSpellResistanceCheckLogThread), "HandleEvent",
        new[] { typeof(RuleSpellResistanceCheck) })]
    internal static class SpellResistanceLogPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(RuleSpellResistanceCheck rule)
        {
            if (!Main.Enabled) return true;

            try
            {
                if (!AbilityXScope.IsSrBypassedByUs(rule)) return true;

                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力X] 已抑制 SR 战斗日志：{rule?.Ability?.name}（自动通过，不显示检定）");
                }

                // 验收埋点：证明「确实走到了抑制逻辑」
                AbilityXScope.AcceptanceHook("X.SRLogSuppressed", true);

                return false;   // 跳过原方法，不产生日志条目
            }
            catch (System.Exception e)
            {
                // 日志抑制失败绝不能让战斗流程崩掉：出异常就交回原方法
                Main.Log?.Warning("[能力X] SR 日志抑制异常，已放行原逻辑：" + e.Message);
                return true;
            }
        }
    }
}
