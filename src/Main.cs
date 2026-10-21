using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using UnityModManagerNet;
using UnityEngine;

namespace WizardArchetype
{
    /// <summary>
    /// MOD 入口 + 全局开关 + UMM 设置界面。
    /// </summary>
    public static class Main
    {
        internal static ModLog Log;
        internal static Settings ModSettings;

        private static UnityModManager.ModEntry s_Entry;
        private static float s_NextTitleRefresh;

        /// <summary>MOD 安装目录（unitymodmanager 提供），本地化文件从这里读。</summary>
        internal static string ModPath;

        public static bool Enabled { get; private set; }

        /// <summary>超魔越界判断修复（补丁 E），全局生效，默认开启。</summary>
        public static bool FixMetamagicLimit = true;

        /// <summary>
        /// 调试模式：输出详细诊断日志、启用验收埋点。
        /// 玩家默认关闭；在 UMM 的 MOD 设置里勾选即可开启。
        /// </summary>
        public static bool DebugLog = false;

        /// <summary>
        /// 智力先攻是否启用"封顶到法师职业等级"。
        /// true  = PropertyCalculator.Min(IntMod, 法师职业等级)，精确但依赖反射
        /// false = DerivativeStatBonus（原版剑圣路径），零反射但无封顶
        /// </summary>
        public static bool UseCappedInitiative = true;

        /// <summary>本 MOD 的法师变体 GUID，蓝图构建时写入。</summary>
        public static BlueprintGuid WizardArchetypeGuid = BlueprintGuid.Empty;

        /// <summary>
        /// 能力X 的代价（强化法术成功后 1 轮特殊恍惚）。
        /// 默认开启；关掉可用于隔离排查「恍惚副作用是否由本 MOD 引起」。
        /// </summary>
        public static bool EnableAbilityXCost = true;

        internal static void SetArchetypeGuid(BlueprintGuid guid)
        {
            WizardArchetypeGuid = guid;
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = new ModLog(modEntry.Logger);
            ModPath = modEntry.Path;

            try
            {
                s_Entry = modEntry;

                ModSettings = UnityModManager.ModSettings.Load<Settings>(modEntry);
                DebugLog = ModSettings.DebugMode;
                FixMetamagicLimit = ModSettings.FixMetamagicLimit;

                // UMM 标题：默认英文，检测到简体中文后切中文（OnUpdate 里会再刷一次，
                // 因为启动时本地化包还没加载，拿不到真实语言）。
                RefreshTitle();
                modEntry.OnGUI = ModSettings.Draw;
                modEntry.OnSaveGUI = ModSettings.Save;

                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(typeof(Main).Assembly);

                SelfCheck.Run();

                // 开场状态需要「延迟判定」（等先攻全部结算完），用 UMM 的每帧回调驱动。
                modEntry.OnUpdate = OnUpdate;

                Enabled = true;
                Log.Info("WizardArchetype loaded. 版本 " + modEntry.Info.Version +
                         "，调试模式 " + (DebugLog ? "开" : "关"));
                return true;
            }
            catch (Exception e)
            {
                Log?.Error("Failed to load: " + e);
                return false;
            }
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            if (!Enabled) return;

            try
            {
                // UMM 标题跟随游戏语言（每 2 秒最多刷新一次，语言包就绪后自动切中文）
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now >= s_NextTitleRefresh)
                {
                    s_NextTitleRefresh = now + 2f;
                    RefreshTitle();
                }

                // 图标兜底重试（预载结束后自动生效）
                IconsAndFx.Tick();

                OpeningStateDecider.Tick();
            }
            catch (Exception e)
            {
                Log?.Warning("OnUpdate 异常：" + e.Message);
            }
        }

        /// <summary>
        /// 判断某单位是否为本 MOD 的法师变体。
        /// 变体 GUID 在蓝图注册后才有效，此外恒为 false，减费补丁保持惰性。
        /// </summary>
        public static bool IsWizardArchetype(UnitEntityData unit)
        {
            if (unit == null) return false;
            if (WizardArchetypeGuid == BlueprintGuid.Empty) return false;

            var classes = unit.Descriptor?.Progression?.Classes;
            if (classes == null) return false;

            foreach (var cls in classes)
            {
                if (cls?.Archetypes == null) continue;
                foreach (var a in cls.Archetypes)
                {
                    if (a != null && a.AssetGuid == WizardArchetypeGuid) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 界面语言：默认英文，只有游戏当前语言是简体中文才用中文。
        /// 启动时本地化包可能还没加载，因此拿不到就按英文处理。
        /// </summary>
        internal static bool IsChineseUi()
        {
            try
            {
                var pack = LocalizationManager.CurrentPack;
                if (pack != null) return pack.Locale == Locale.zhCN;
                return LocalizationManager.CurrentLocale == Locale.zhCN;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>UMM 列表里显示的标题：Rift Mage / 空隙法师。</summary>
        private static void RefreshTitle()
        {
            try
            {
                if (s_Entry?.Info == null) return;
                string want = IsChineseUi() ? "空隙法师" : "Rift Mage";
                if (s_Entry.Info.DisplayName != want) s_Entry.Info.DisplayName = want;
            }
            catch (Exception) { }
        }

        private static bool? s_ExternalMetamagicAssembly;

        /// <summary>
        /// 独立的「超魔越界修复 / MetamagicFix」MOD 当前是否在生效。
        ///
        /// 两个 MOD 都带同一份超魔补丁：同时启用会让同一次判定被补两次。
        /// 约定是**独立 MOD 优先** —— 它装了并且开着，本 MOD 就让位。
        ///
        /// 实现上：
        ///   · 按程序集名找 "MetamagicFix"（找到与否缓存一次）；
        ///   · 找到后再读它的 <c>Main.FixEnabled</c>（每次现读，这样它在设置里关掉时本 MOD 会自动接回）。
        /// 只在补丁真正执行时调用，那时所有 MOD 都已加载完毕，与加载顺序无关。
        /// </summary>
        internal static bool HasExternalMetamagicFix()
        {
            if (!s_ExternalMetamagicAssembly.HasValue)
            {
                bool found = false;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm?.GetName()?.Name == "MetamagicFix") { found = true; break; }
                    }
                }
                catch (Exception) { }
                s_ExternalMetamagicAssembly = found;
                if (found)
                {
                    Log?.Info("[超魔] 检测到独立 MOD「MetamagicFix」：本 MOD 内置的超魔补丁默认让位（只保留一份）");
                }
            }

            if (!s_ExternalMetamagicAssembly.Value) return false;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm?.GetName()?.Name != "MetamagicFix") continue;
                    var type = asm.GetType("MetamagicFix.Main");
                    var prop = type?.GetProperty("FixEnabled");
                    if (prop == null) return true;
                    var value = prop.GetValue(null, null);
                    return value is bool b && b;
                }
            }
            catch (Exception) { }

            return true;
        }

        /// <summary>UMM 设置项。</summary>
        public class Settings : UnityModManager.ModSettings
        {
            public bool DebugMode;

            public override void Save(UnityModManager.ModEntry modEntry)
            {
                Save(this, modEntry);
            }

            public bool FixMetamagicLimit = true;

            public void Draw(UnityModManager.ModEntry modEntry)
            {
                bool zh = Main.IsChineseUi();
                GUILayout.BeginVertical("box");

                string debugLabel = zh ? "调试模式" : "Debug mode";
                bool debug = GUILayout.Toggle(DebugMode, debugLabel);
                if (debug != DebugMode)
                {
                    DebugMode = debug;
                    DebugLog = debug;
                    Log?.Info("Debug mode: " + (debug ? "on" : "off"));
                }

                // 装了独立 MOD「MetamagicFix」时由它接管这份补丁，这里就不再显示开关。
                if (!Main.HasExternalMetamagicFix())
                {
                    string fixLabel = zh ? "修复超魔越界" : "Fix metamagic limit";
                    bool fix = GUILayout.Toggle(FixMetamagicLimit, fixLabel);
                    if (fix != FixMetamagicLimit)
                    {
                        FixMetamagicLimit = fix;
                        Main.FixMetamagicLimit = fix;
                        Log?.Info("Metamagic limit fix: " + (fix ? "on" : "off"));
                    }
                }

                GUILayout.EndVertical();
            }
        }
    }

    /// <summary>
    /// 日志包装：Log 只在调试模式输出，Warning/Error 始终输出。
    ///
    /// 这样全项目现有的 Main.Log?.Log(...) 调用点不用逐个改，
    /// 默认给玩家的日志就只剩警告与错误。
    /// </summary>
    internal sealed class ModLog
    {
        private readonly UnityModManager.ModEntry.ModLogger m_Inner;

        internal ModLog(UnityModManager.ModEntry.ModLogger inner)
        {
            m_Inner = inner;
        }

        /// <summary>调试日志（受调试模式控制）。</summary>
        public void Log(string message)
        {
            if (Main.DebugLog) m_Inner.Log(message);
        }

        /// <summary>始终输出的提示（启动摘要等）。</summary>
        public void Info(string message)
        {
            m_Inner.Log(message);
        }

        public void Warning(string message)
        {
            m_Inner.Warning(message);
        }

        public void Error(string message)
        {
            m_Inner.Error(message);
        }
    }
}
