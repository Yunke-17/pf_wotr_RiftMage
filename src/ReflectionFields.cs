using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Properties;
using Kingmaker.EntitySystem.Properties.BaseGetter;
using Kingmaker.EntitySystem.Properties.Getters;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 唯一需要反射的地方。
    ///
    /// 原因：ContextRankConfig（ContextRankConfig.cs:771-945）与 StatGetter
    /// （StatGetter.cs:76-87）的字段全部是 private，且没有任何公开构造器或 setter。
    /// 原版蓝图靠 JSON 反序列化直接写这些字段；纯 C# 构建只能用反射。
    ///
    /// 路线 C 的核心：启动期做字段存在性校验。
    /// 反射最大的风险是"字段名变了但不报错，只是数值静默算错"，
    /// 而本 MOD 的验收靠游戏内实测，静默失效极难发现。
    /// 因此 RunSelfCheck() 会在加载时逐个确认字段存在，缺失即记 Error。
    /// </summary>
    internal static class ReflectionFields
    {
        // ContextRankConfig 私有字段
        internal const string CR_BaseValueType = "m_BaseValueType";
        internal const string CR_Progression   = "m_Progression";
        internal const string CR_StepLevel     = "m_StepLevel";
        internal const string CR_UseMin        = "m_UseMin";
        internal const string CR_Min           = "m_Min";
        internal const string CR_UseMax        = "m_UseMax";
        internal const string CR_Max           = "m_Max";
        internal const string CR_Class         = "m_Class";

        // StatGetter 私有字段
        internal const string SG_Stat          = "m_Stat";
        internal const string SG_StatValueType = "m_StatValueType";
        internal const string SG_SpecificMod   = "m_SpecificModifier";

        private static readonly (Type Type, string Field, string Label)[] s_Required =
        {
            (typeof(ContextRankConfig), CR_BaseValueType, "ContextRankConfig.m_BaseValueType"),
            (typeof(ContextRankConfig), CR_Progression,   "ContextRankConfig.m_Progression"),
            (typeof(ContextRankConfig), CR_StepLevel,     "ContextRankConfig.m_StepLevel"),
            (typeof(ContextRankConfig), CR_UseMin,        "ContextRankConfig.m_UseMin"),
            (typeof(ContextRankConfig), CR_Min,           "ContextRankConfig.m_Min"),
            (typeof(ContextRankConfig), CR_UseMax,        "ContextRankConfig.m_UseMax"),
            (typeof(ContextRankConfig), CR_Max,           "ContextRankConfig.m_Max"),
            (typeof(ContextRankConfig), CR_Class,         "ContextRankConfig.m_Class"),
            (typeof(StatGetter),        SG_Stat,          "StatGetter.m_Stat"),
            (typeof(StatGetter),        SG_StatValueType, "StatGetter.m_StatValueType"),
            (typeof(StatGetter),        SG_SpecificMod,   "StatGetter.m_SpecificModifier"),
        };

        private static bool s_Verified;

        /// <summary>
        /// 启动期校验。任何字段缺失都会记 Error，而不是等到数值算错才发现。
        /// </summary>
        internal static bool RunSelfCheck()
        {
            int missing = 0;

            foreach (var (type, field, label) in s_Required)
            {
                var f = type.GetField(field,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null)
                {
                    Main.Log?.Error($"[反射校验] 缺失字段 {label} —— 游戏版本可能已变更，相关特性会算错");
                    missing++;
                }
            }

            s_Verified = missing == 0;

            if (s_Verified)
            {
                Main.Log?.Log($"[反射校验] 全部 {s_Required.Length} 个私有字段存在，反射层可用");
            }
            else
            {
                Main.Log?.Error($"[反射校验] 共 {missing} 个字段缺失");
            }

            return s_Verified;
        }

        internal static void Set(object target, string field, object value)
        {
            var t = target.GetType();
            FieldInfo f = null;

            // 逐级向上找，避免字段声明在父类时 GetField 直接返回 null
            while (t != null && f == null)
            {
                f = t.GetField(field,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                t = t.BaseType;
            }

            if (f == null)
            {
                Main.Log?.Error($"[反射校验] 写入失败：{target.GetType().Name}.{field} 不存在");
                return;
            }

            try
            {
                f.SetValue(target, value);
            }
            catch (Exception e)
            {
                Main.Log?.Error($"[反射校验] 写入异常 {target.GetType().Name}.{field}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 用强类型参数构造 ContextRankConfig / StatGetter，内部走反射。
    /// 避免各处散落反射调用。
    /// </summary>
    internal static class RankConfigs
    {
        internal static ContextRankConfig Build(
            ContextRankBaseValueType baseValue,
            ContextRankProgression progression,
            int stepLevel,
            BlueprintCharacterClassReference classRef,
            bool useMin = false, int min = 0,
            bool useMax = false, int max = 0)
        {
            var cfg = new ContextRankConfig();

            ReflectionFields.Set(cfg, ReflectionFields.CR_BaseValueType, baseValue);
            ReflectionFields.Set(cfg, ReflectionFields.CR_Progression, progression);
            ReflectionFields.Set(cfg, ReflectionFields.CR_StepLevel, stepLevel);
            ReflectionFields.Set(cfg, ReflectionFields.CR_UseMin, useMin);
            ReflectionFields.Set(cfg, ReflectionFields.CR_Min, min);
            ReflectionFields.Set(cfg, ReflectionFields.CR_UseMax, useMax);
            ReflectionFields.Set(cfg, ReflectionFields.CR_Max, max);

            if (classRef != null)
            {
                var arr = Array.CreateInstance(typeof(BlueprintCharacterClassReference), 1);
                arr.SetValue(classRef, 0);
                ReflectionFields.Set(cfg, ReflectionFields.CR_Class, arr);
            }

            return cfg;
        }

        /// <summary>
        /// 构造 StatGetter。m_Stat / m_StatValueType / m_SpecificModifier 均为私有字段
        /// （StatGetter.cs:76-87），其中 m_StatValueType 的类型还是私有嵌套枚举，
        /// 必须按名字反射解析。
        /// </summary>
        internal static StatGetter Stat(StatType stat, StatValueTypeKind kind,
            ModifierDescriptor specific = ModifierDescriptor.None)
        {
            var getter = new StatGetter();
            ReflectionFields.Set(getter, ReflectionFields.SG_Stat, stat);
            ReflectionFields.Set(getter, ReflectionFields.SG_SpecificMod, specific);

            var enumType = typeof(StatGetter).GetNestedType("ValueType", BindingFlags.NonPublic);
            if (enumType == null)
            {
                Main.Log?.Error("[反射校验] StatGetter.ValueType 嵌套枚举缺失");
            }
            else
            {
                ReflectionFields.Set(getter, ReflectionFields.SG_StatValueType,
                    Enum.Parse(enumType, kind.ToString()));
            }

            return getter;
        }

        /// <summary>对应 StatGetter.ValueType 私有枚举的三个成员。</summary>
        internal enum StatValueTypeKind
        {
            BonusValue,
            ModifiedValue,
            BaseValue,
        }
    }
}
