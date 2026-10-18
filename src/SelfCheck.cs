using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace WizardArchetype
{
    /// <summary>
    /// 启动时静态自检：确认每个补丁的 Harmony 目标都能解析到。
    /// Harmony 的 PatchAll 在目标不存在时可能静默跳过，导致"看起来装了但没生效"。
    /// </summary>
    internal static class SelfCheck
    {
        internal static void Run()
        {
            Check(typeof(Kingmaker.UnitLogic.Abilities.MetamagicHelper),
                  "DefaultCost", "补丁D 瞬发减费");

            Check(typeof(Kingmaker.RuleSystem.Rules.RuleCollectMetamagic),
                  "AddMetamagic", "补丁E 越界判断");

            Check(typeof(Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Metamagic.SpellbookMetamagicSelectorVM),
                  "GetCost", "补丁D UI 上下文");

            Check(typeof(Kingmaker.Blueprints.JsonSystem.BlueprintsCache),
                  "Init", "蓝图注册");

            // 补丁 F：SR 战斗日志抑制。
            // 注意 HandleEvent 有两个重载（RuleSpellResistanceCheck / GameLogEventAbilityImmunity），
            // 必须按参数类型精确取，否则 AccessTools 会因歧义返回 null。
            CheckOverload(
                typeof(Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Combat
                    .RulebookSpellResistanceCheckLogThread),
                "HandleEvent",
                new[] { typeof(Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck) },
                "补丁F SR日志抑制");

            // 补丁 G：特殊状态免疫旁路
            CheckOverload(typeof(Kingmaker.UnitLogic.UnitState), "HasCondition",
                new[] { typeof(Kingmaker.UnitLogic.UnitCondition) },
                "补丁G 特殊状态免疫旁路");

            // 补丁 H：特殊纠缠强制专注检定（private 方法，按名取）
            Check(typeof(Kingmaker.UnitLogic.Commands.UnitUseAbility),
                  "MakeConcentrationCheckIfCastingIsDifficult", "补丁H 强制专注检定");

            // 补丁 I：CON < 3 禁施法（private 属性的 getter）
            Check(typeof(Kingmaker.UnitLogic.Abilities.AbilityData),
                  "get_CanBeCastByCaster", "补丁I 低体质禁施法");

            // 补丁 J：突破描述符类魔法免疫（private 方法，有重载，按参数精确取）
            CheckOverload(
                typeof(Kingmaker.UnitLogic.Parts.UnitPartSpellResistance),
                "CanApply",
                new[]
                {
                    typeof(Kingmaker.UnitLogic.Parts.UnitPartSpellResistance.SpellImmunity),
                    typeof(Kingmaker.UnitLogic.Mechanics.MechanicsContext),
                },
                "补丁J 免疫突破");

            // 补丁 J2：Buff 描述符免疫（另一条独立路径）
            CheckOverload(
                typeof(Kingmaker.UnitLogic.FactLogic.BuffDescriptorImmunity),
                "IsImmune",
                new[] { typeof(Kingmaker.UnitLogic.Mechanics.MechanicsContext) },
                "补丁J2 Buff描述符免疫");

            // 路线 C：反射层字段校验（缺失会记 Error，避免数值静默算错）
            ReflectionFields.RunSelfCheck();
        }

        /// <summary>按参数类型精确取重载，避免同名方法歧义。</summary>
        private static void CheckOverload(Type type, string method, Type[] args, string label)
        {
            if (type == null)
            {
                Main.Log.Warning($"[自检] {label}: 类型未找到");
                return;
            }

            var found = AccessTools.Method(type, method, args);
            if (found == null)
            {
                Main.Log.Warning($"[自检] {label}: {type.Name}.{method}({string.Join(", ", args.Select(a => a.Name))}) 未找到（补丁可能未生效）");
                return;
            }

            Main.Log.Log($"[自检] {label}: {type.Name}.{method}({string.Join(", ", args.Select(a => a.Name))}) OK");
        }

        private static void Check(Type type, string method, string label)
        {
            if (type == null)
            {
                Main.Log.Warning($"[自检] {label}: 类型未找到");
                return;
            }

            var found = AccessTools.Method(type, method);
            if (found == null)
            {
                Main.Log.Warning($"[自检] {label}: {type.Name}.{method} 未找到（补丁可能未生效）");
                return;
            }

            Main.Log.Log($"[自检] {label}: {type.Name}.{method} OK");
        }
    }
}
