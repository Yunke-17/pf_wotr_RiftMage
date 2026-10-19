using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Localization;
using Kingmaker.UI.Common;

namespace WizardArchetype
{
    /// <summary>
    /// 自定义术语（Glossary）注入（用户 2026-10-20 需求）。
    ///
    /// ========== 原版机制 ==========
    ///
    /// 描述文本里写 `{g|Encyclopedia:Key}显示文字{/g}`，
    /// 鼠标悬停到「显示文字」上会弹出该术语的说明窗口（截图确认）。
    ///
    /// 解析链：
    ///   文本里的 g|Encyclopedia:Key
    ///     → UIUtility.GetGlossaryEntry(key)（UIUtility.cs:1374）
    ///     → GlossaryHolder.GetEntry(key)（GlossaryHolder.cs:16）
    ///     → GlossaryHolder.s_EntriesbyKey[key.ToLowerInvariant()]
    ///
    /// ========== 为什么要反射注入 ==========
    ///
    /// s_EntriesbyKey 是 **private static Dictionary**，
    /// 由 GlossaryHolder.Initialize() 在首次调用时从 LocalizedTexts 填充，
    /// 之后**只读**——没有公开的注册接口。
    ///
    /// 所以只能在 Initialize 之后（LocalizationManager.WaitForInit 时）
    /// 反射往字典里塞自定义条目。
    ///
    /// ========== 条目内容 ==========
    ///
    /// 我们注册 6 个术语，覆盖变体描述与能力描述里出现的【】名词。
    /// Name / Description 都是 LocalizedString，复用已有的本地化 key 体系。
    /// </summary>
    internal static class Glossary
    {
        // 术语 key（会拼进 {g|Encyclopedia:xxx}）
        internal const string KArchetype = "WizGapArchetype";
        internal const string KTimeFocus = "WizGapTimeFocus";
        internal const string KTimeRecoil = "WizGapTimeRecoil";
        internal const string KVoidAnnihilation = "WizGapVoidAnnihilation";
        internal const string KVoidHarmony = "WizGapVoidHarmony";
        internal const string KVoidOverdraw = "WizGapVoidOverdraw";

        // ---- 复用原版已有术语（不重复注册，直接引用其 key）----
        //
        // 原版 GlossaryHolder 里已经有的 key，我们只管用，不覆盖：
        //   Skills             技能检定
        //   Initiative         先攻
        //   Saving_Throw       豁免检定
        //   Spell_Resistance   法术抗力
        //   Concentration      专注
        //   Ability_Scores     属性值（智力/感知等）
        // 用常量集中管理，便于查错。

        /// <summary>原版：技能检定</summary>
        internal const string KSkills = "Skills";

        /// <summary>原版：先攻</summary>
        internal const string KInitiative = "Initiative";

        /// <summary>原版：豁免检定</summary>
        internal const string KSavingThrow = "Saving_Throw";

        /// <summary>原版：法术抗力</summary>
        internal const string KSpellResistance = "Spell_Resistance";

        /// <summary>原版：专注</summary>
        internal const string KConcentration = "Concentration";

        /// <summary>原版：难度等级（DC）</summary>
        internal const string KDC = "DC";

        /// <summary>自建：施法者等级（原版术语表里没有 CasterLevel 这个 key，已核对 zhCN.json）</summary>
        internal const string KCasterLevel = "WizGapCasterLevel";

        /// <summary>自建：法术穿透检定（原版没有精确对应的术语）</summary>
        internal const string KSpellPenetration = "WizGapSpellPenetration";

        /// <summary>自建：智力调整值</summary>
        internal const string KIntMod = "WizGapIntMod";

        /// <summary>自建：法师等级</summary>
        internal const string KWizardLevel = "WizGapWizardLevel";

        private static bool s_Injected;

        internal static void Inject()
        {
            if (s_Injected) return;
            s_Injected = true;

            try
            {
                // s_Entries 已由 CreateText() 在蓝图构建阶段填充
                if (s_Entries.Count == 0)
                {
                    Main.Log?.Warning("[术语] 条目为空（CreateText 未被调用？），跳过注入");
                    return;
                }

                var field = typeof(GlossaryHolder).GetField("s_EntriesbyKey",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var dict = field?.GetValue(null) as Dictionary<string, GlossaryEntry>;
                if (dict == null)
                {
                    Main.Log?.Warning("[术语] 找不到 GlossaryHolder.s_EntriesbyKey，悬停提示不可用");
                    return;
                }

                int added = 0;
                foreach (var e in s_Entries)
                {
                    string k = e.Key.ToLowerInvariant();
                    if (dict.ContainsKey(k)) continue;   // 不覆盖原版
                    dict[k] = e;
                    added++;
                }

                Main.Log?.Log($"[术语] 已注入 {added} 个自定义术语（悬停提示可用）");
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[术语] 注入失败：" + e.Message);
            }
        }

        private static readonly List<GlossaryEntry> s_Entries = new List<GlossaryEntry>();
        private static bool s_TextCreated;

        private static void Add(string key, string nameKey, string descKey)
        {
            s_Entries.Add(new GlossaryEntry
            {
                Key = key,
                Name = new LocalizedString { Key = nameKey },
                Description = new LocalizedString { Key = descKey },
            });
        }

        /// <summary>
        /// 注册术语的文本。必须在本地化 flush 之前调用（与其它 RegisterText 同时机）。
        /// </summary>
        internal static void CreateText()
        {
            if (s_TextCreated) return;   // 防重复（蓝图可能被多次构建）
            s_TextCreated = true;

            const string r = "WizardArchetype.Glossary.";

            WizardArchetypeBlueprints.RegisterText(r + "Archetype.Name", "空隙法师");
            WizardArchetypeBlueprints.RegisterText(r + "Archetype.Desc",
                "专注于捕捉刹那之间现实缝隙的法师变体。\n" +
                "以神经加速与肉体反冲为代价，换取破除因果的爆发与近乎本能的极速施法。");

            WizardArchetypeBlueprints.RegisterText(r + "TimeFocus.Name", "时序专注");
            WizardArchetypeBlueprints.RegisterText(r + "TimeFocus.Desc",
                "进入战斗时若先攻不低于所有敌人，会陷入此状态，持续 1 轮。\n" +
                "期间施放法师法术必须通过一次专注检定：\n" +
                "<b>DC = 10 + ⌈法师等级 ÷ 2⌉ + 法术环位 × 2</b>\n" +
                "成功：法术正常施放，状态立即解除。\n" +
                "失败：法术失效，受到 1d2 点体质减损；若体质低于 3，期间无法继续施法。\n" +
                "该状态无法被《行动自如》解除。");

            WizardArchetypeBlueprints.RegisterText(r + "TimeRecoil.Name", "时序反冲");
            WizardArchetypeBlueprints.RegisterText(r + "TimeRecoil.Desc",
                "进入战斗时若先攻低于任一敌人，或在【空隙湮灭】强化法术结算后，会陷入此状态。\n" +
                "如同恍惚：每轮只能进行一个移动动作或一个标准动作，不能进行整轮动作。\n" +
                "该状态无法被《行动自如》或类似效果预防与解除。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidAnnihilation.Name", "空隙湮灭");
            WizardArchetypeBlueprints.RegisterText(r + "VoidAnnihilation.Desc",
                "撕开一条现实缝隙，使你在 3 轮内施放的下一个法师法术破除因果：\n" +
                "· 伤害骰面 +2/+4/+6（按基础施法者等级）\n" +
                "· 敌方豁免检定掷 2/3/4 次取最低\n" +
                "· 自动通过法术抗力检定，并突破描述符类魔法免疫\n" +
                "代价：法术结算完成后立即陷入 1 轮【时序反冲】。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidHarmony.Name", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(r + "VoidHarmony.Desc",
                "在战斗中拨动法力相位，选择一种状态持续至战斗结束：\n" +
                "· 【致密收束】：法术 DC +2，施法者等级 -1\n" +
                "· 【裂隙穿透】：施法者等级 +1，法术 DC -2\n" +
                "同一状态最多 2 层；两种状态互斥。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidOverdraw.Name", "空隙透支");
            WizardArchetypeBlueprints.RegisterText(r + "VoidOverdraw.Desc",
                "燃烧自身的现实锚点，使【空隙湮灭】恢复 1 次充能。\n" +
                "代价：获得 30 秒力竭与 90 秒疲劳（同时开始计时）。\n" +
                "仅当【空隙湮灭】已无充能时可用。");

            Add(KArchetype, r + "Archetype.Name", r + "Archetype.Desc");
            Add(KTimeFocus, r + "TimeFocus.Name", r + "TimeFocus.Desc");
            Add(KTimeRecoil, r + "TimeRecoil.Name", r + "TimeRecoil.Desc");
            Add(KVoidAnnihilation, r + "VoidAnnihilation.Name", r + "VoidAnnihilation.Desc");
            Add(KVoidHarmony, r + "VoidHarmony.Name", r + "VoidHarmony.Desc");
            Add(KVoidOverdraw, r + "VoidOverdraw.Name", r + "VoidOverdraw.Desc");

            // ---- 以下三个是原版没有、但本 MOD 反复用到的概念 ----

            WizardArchetypeBlueprints.RegisterText(r + "SpellPenetration.Name", "法术穿透检定");
            WizardArchetypeBlueprints.RegisterText(r + "SpellPenetration.Desc",
                "施法者为克服目标的法术抗力（" +
                "{g|Encyclopedia:" + KSpellResistance + "}法术抗力{/g}）而进行的检定。\n" +
                "法术穿透检定 = d20 + 施法者等级 + 各类穿透加值，对抗目标的法术抗力数值。\n" +
                "成功则法术对目标正常生效，失败则法术被抵抗。");

            WizardArchetypeBlueprints.RegisterText(r + "IntMod.Name", "智力调整值");
            WizardArchetypeBlueprints.RegisterText(r + "IntMod.Desc",
                "由智力属性换算得到的调整值：（智力 - 10）÷ 2，向下取整。\n" +
                "智力 10–11 → +0；12–13 → +1；14–15 → +2；以此类推。\n" +
                "装备、增益与临时效果提供的智力加值会一并计入。");

            WizardArchetypeBlueprints.RegisterText(r + "WizardLevel.Name", "法师等级");
            WizardArchetypeBlueprints.RegisterText(r + "WizardLevel.Desc",
                "角色在「法师」职业（含本变体）上投入的等级，\n" +
                "不等于角色总等级。兼职时两者会不同。\n" +
                "本职业多数特性的档位按法师等级而非总等级计算。");

            WizardArchetypeBlueprints.RegisterText(r + "CasterLevel.Name", "施法者等级");
            WizardArchetypeBlueprints.RegisterText(r + "CasterLevel.Desc",
                "决定法术强度、可施放环位与诸多法术效果的等级数值。\n" +
                "通常等于对应职业的等级；神话道途「合书」后会额外提高。\n" +
                "施法者等级越高，法术的持续时间、伤害骰数量与穿透能力往往越强。");

            Add(KSpellPenetration, r + "SpellPenetration.Name", r + "SpellPenetration.Desc");
            Add(KIntMod, r + "IntMod.Name", r + "IntMod.Desc");
            Add(KWizardLevel, r + "WizardLevel.Name", r + "WizardLevel.Desc");
            Add(KCasterLevel, r + "CasterLevel.Name", r + "CasterLevel.Desc");
        }

        /// <summary>生成悬停标记：{g|Encyclopedia:Key}文字{/g}</summary>
        internal static string Link(string key, string text)
        {
            return "{g|Encyclopedia:" + key + "}" + text + "{/g}";
        }
    }
}
