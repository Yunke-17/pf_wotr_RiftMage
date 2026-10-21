using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Newtonsoft.Json;

namespace WizardArchetype
{
    /// <summary>
    /// 本地化文本加载（2026-10-21）。
    ///
    /// ========== 数据来源（按优先级） ==========
    ///
    ///   1. MOD 目录下 <c>Localization/&lt;语言&gt;.json</c> —— 可编辑、可翻译；
    ///   2. DLL 内嵌的同名 json —— 找不到外部文件时兜底（保证只装 DLL 也能跑）；
    ///   3. 代码里 RegisterText 传入的中文 —— 最后兜底。
    ///
    /// 语言由 <c>LocalizationManager.CurrentLocale</c> 决定：
    ///   zhCN → zhCN.json；其它语言 → enGB.json；enGB 缺失时回落 zhCN.json。
    ///
    /// ========== 为什么在 FlushText 时才加载 ==========
    ///
    /// 蓝图构建期（BlueprintsCache.Init）CurrentLocale 还没定下来，
    /// 而本地化包也要到 WaitForInit 才就绪 —— 两者同一时机，正好在 FlushText 里读。
    ///
    /// ========== 文件格式 ==========
    ///
    ///   { "WizardArchetype.AbilityX.Name": "空隙湮灭", ... }
    ///
    /// 文本里可以直接写 <c>{g|Encyclopedia:Key}词{/g}</c> 悬停标记。
    /// </summary>
    internal static class LocalizationText
    {
        private static Dictionary<string, string> s_Map;

        /// <summary>实际使用的语言包名（日志用）。</summary>
        internal static string Source { get; private set; } = "未加载";

        /// <summary>取一条文本；没有则用调用方给的兜底。</summary>
        internal static string Get(string key, string fallback)
        {
            EnsureLoaded();
            string text;
            if (s_Map != null && s_Map.TryGetValue(key, out text) && !string.IsNullOrEmpty(text))
                return text;
            return fallback;
        }

        private static void EnsureLoaded()
        {
            if (s_Map != null) return;

            string locale = ResolveLocale();
            // 规则：默认英文；只有当前语言是简体中文才用中文包。
            // 中文包缺失时回落英文（而不是让非中文玩家看到中文）。
            foreach (string name in new[] { locale, "enGB" })
            {
                var map = LoadFile(name) ?? LoadEmbedded(name);
                if (map == null || map.Count == 0) continue;

                s_Map = map;
                Source = name;
                Main.Log?.Info("[本地化] 已加载 " + name + ".json（" + map.Count +
                               " 条，游戏语言=" + locale + "）");
                return;
            }

            s_Map = new Dictionary<string, string>();
            Source = "代码内置";
            Main.Log?.Warning("[本地化] 未找到 Localization/*.json，回退到代码内置中文");
        }

        private static string ResolveLocale()
        {
            try
            {
                // 以"当前已加载的语言包"为准；它比 CurrentLocale 更早、更可靠。
                // 判定规则（用户 2026-10-21 定）：默认英文，只有检测到简体中文才切中文。
                var pack = LocalizationManager.CurrentPack;
                var locale = pack != null ? pack.Locale : LocalizationManager.CurrentLocale;
                return locale == Locale.zhCN ? "zhCN" : "enGB";
            }
            catch (Exception)
            {
                return "enGB";
            }
        }

        private static Dictionary<string, string> LoadFile(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(Main.ModPath)) return null;
                var path = Path.Combine(Main.ModPath, "Localization", name + ".json");
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[本地化] 读取 " + name + ".json 失败：" + e.Message);
                return null;
            }
        }

        private static Dictionary<string, string> LoadEmbedded(string name)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.EndsWith("." + name + ".json", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(res))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return Parse(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[本地化] 读取内嵌 " + name + " 失败：" + e.Message);
            }
            return null;
        }

        private static Dictionary<string, string> Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
        }
    }
}
