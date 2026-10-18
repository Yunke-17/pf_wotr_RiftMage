using HarmonyLib;
using Kingmaker.UnitLogic;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 G —— 特殊状态不被「行动自如」等免疫来源抹掉。
    ///
    /// ========== 引擎事实（已逐行核实） ==========
    ///
    /// UnitState.cs:565
    ///     public bool HasCondition(UnitCondition condition)
    ///         => m_Conditions[(int)condition] > 0 && m_ConditionsImmunity[(int)condition] <= 0;
    ///
    /// m_Conditions 是纯计数数组，**不记录来源**：
    ///   AddCondition(condition, source, exceptions) 的 source 只用于刷图标（:498），
    ///   不参与任何判定。所以「按来源豁免」在引擎数据层根本不可行。
    ///
    /// ========== 本补丁只做一件事 ==========
    ///
    /// 当 HasCondition 因免疫（m_ConditionsImmunity > 0）返回 false，
    /// 而该单位确实挂着**本 MOD 施加的**对应特殊状态时，把结果改回 true。
    ///
    /// 为什么这样不会「暗改」原版：
    ///   - 判定依据是 SpecialConditionRegistry，只有我们自己的 Buff 会登记；
    ///   - 原版恍惚（怪物、法术）、先知「知命」、行动自如本身完全不受影响；
    ///   - 纯 Postfix，不短路、不改 IL、不影响其它 MOD 的补丁。
    ///
    /// ========== 局限（诚实记录） ==========
    ///
    /// 本补丁让**读取方**（移速计算、动作限制、施法检查等）看到「条件存在」。
    /// 但若有代码绕过 HasCondition 直接读 m_Conditions/m_ConditionsImmunity，
    /// 则不受本补丁影响。目前已知的速度/动作限制路径都走 HasCondition。
    /// </summary>
    [HarmonyPatch(typeof(UnitState), nameof(UnitState.HasCondition))]
    internal static class HasConditionBypassPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitState __instance, UnitCondition condition, ref bool __result)
        {
            if (__result || !Main.Enabled) return;

            try
            {
                // ① 特殊纠缠：仅在「引擎正在做施法专注判定」的那一瞬间，
                //    让 SpellCastingIsDifficult 成立，从而触发 RuleCheckConcentration。
                //    （特殊纠缠刻意不挂 Entangled，否则会减速且被行动自如免疫。）
                if (ForceConcentrationPatch.ShouldForceDifficult(__instance, condition))
                {
                    __result = true;
                    return;
                }

                // ★ 性能：HasCondition 是热路径（移速计算、动作限制、施法检查每帧都会问）。
                //   实测一轮战斗触发 79,223 次，因此这里必须尽早短路。
                //   只有在「因为免疫而不成立」时才有必要旁路 —— 条件计数为 0 的情况
                //   说明单位身上压根没有该条件，登记表里也不可能有，直接返回。
                if (!__instance.HasConditionImmunity(condition)) return;

                var unit = __instance.Owner != null ? __instance.Owner.Unit : null;
                if (unit == null) return;

                if (!SpecialConditionRegistry.Has(unit, condition)) return;

                __result = true;

                // 日志只记「第一次被放行」，绝不逐次打印（会刷爆 Player.log 且拖慢帧率）。
                if (Main.DebugLog && SpecialConditionRegistry.MarkLogged(unit, condition))
                {
                    Main.Log?.Log($"[特殊状态] 免疫旁路已生效：{unit.CharacterName} 的 {condition}" +
                                  "（此后同一状态不再重复记录）");
                }
            }
            catch (System.Exception)
            {
                // 旁路失败不能让战斗流程崩：保持原结果，且不刷日志。
            }
        }
    }
}
