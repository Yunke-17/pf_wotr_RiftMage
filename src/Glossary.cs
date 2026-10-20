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
        // 以下 key 均已确认存在于原版 zhCN.json 的实际使用中
        // （用正则扫描全部 11 万条文本里出现过的 {g|Encyclopedia:xxx}）：
        //   Caster_Level  施法者等级      Spell_Resistance 法术抗力
        //   Intelligence  智力            Ability_Scores   属性值
        //   Class_Level   职业等级        DC               难度等级
        //   Skills        技能检定        Initiative       先攻
        //   Concentration 专注            Spell            法术
        //   Saving_Throw  豁免检定
        //   Free_Action / Swift_Action / Full_Round_Action  动作类型

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

        /// <summary>原版：施法者等级（注意 key 是 Caster_Level，带下划线）</summary>
        internal const string KCasterLevel = "Caster_Level";

        /// <summary>原版：智力</summary>
        internal const string KIntelligence = "Intelligence";

        /// <summary>原版：属性值</summary>
        internal const string KAbilityScores = "Ability_Scores";

        /// <summary>原版：职业等级</summary>
        internal const string KClassLevel = "Class_Level";

        /// <summary>原版：法术</summary>
        internal const string KSpell = "Spell";

        /// <summary>原版：自由动作</summary>
        internal const string KFreeAction = "Free_Action";

        /// <summary>原版：迅捷动作</summary>
        internal const string KSwiftAction = "Swift_Action";

        /// <summary>原版：整轮动作</summary>
        internal const string KFullRoundAction = "Full_Round_Action";

        // ---- 以下为原版没有、需要自建的术语 ----

        /// <summary>自建：智力调整值（原版有「智力」，但没有"调整值"这一条）</summary>
        internal const string KIntMod = "WizGapIntMod";

        /// <summary>自建：法术穿透检定</summary>
        internal const string KSpellPenetration = "WizGapSpellPenetration";

        /// <summary>自建：法师等级（原版只有通用的「职业等级」）</summary>
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
                "当空隙法师进入战斗时，若其" + Link(KInitiative, "先攻") +
                "不低于所有敌人，现实会收紧对他法力回路的容许——他会陷入时序专注，持续 1 轮。" +
                "在此期间施放法师" + Link(KSpell, "法术") + "时，必须先通过一次" +
                Link(KConcentration, "专注检定") + "，其" + Link(KDC, "难度等级（DC）") +
                "等于 10 + ⌈" + Link(KWizardLevel, "法师等级") + " ÷ 2⌉ + 法术环位 × 2。" +
                "检定成功时，法术正常施放，且该状态立即解除；" +
                "检定失败时，法术失效，法师受到 1d2 点" +
                Link(KAbilityScores, "属性值") + "减损；" +
                "若其体质降至 3 以下，则在此期间无法继续施放任何法师法术。" +
                "与寻常纠缠不同，该状态不会降低" + Link(KInitiative, "先攻") + "或移动速度，" +
                "也无法被《行动自如》或类似效果预防与解除。");

            WizardArchetypeBlueprints.RegisterText(r + "TimeRecoil.Name", "时序反冲");
            WizardArchetypeBlueprints.RegisterText(r + "TimeRecoil.Desc",
                "当空隙法师进入战斗时其" + Link(KInitiative, "先攻") + "低于任一敌人，" +
                "或在其" + Link(KVoidAnnihilation, "空隙湮灭") + "所强化的" +
                Link(KSpell, "法术") + "完成施放之后，现实会加倍回推，令他陷入时序反冲。" +
                "其效果如同恍惚：每轮只能进行一个移动动作或一个标准动作，不能进行" +
                Link(KFullRoundAction, "整轮动作") +
                "，但可以正常使用" + Link(KFreeAction, "自由动作") + "与" +
                Link(KSwiftAction, "迅捷动作") + "。" +
                "该状态无法被《行动自如》或类似效果预防与解除。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidAnnihilation.Name", "空隙湮灭");
            WizardArchetypeBlueprints.RegisterText(r + "VoidAnnihilation.Desc",
                "空隙法师能够短暂撕开一条现实缝隙，令因果在其面前失效。" +
                "以一个" + Link(KFreeAction, "自由动作") + "激活后，" +
                "他在 3 轮内施放的下一个法师" + Link(KSpell, "法术") + "会获得强化：" +
                "伤害骰的骰面提高 2（5 级起为 4，10 级起为 6）；" +
                "敌方针对该法术的" + Link(KSavingThrow, "豁免检定") +
                "需掷 2 次取较低结果（5 级起 3 次，10 级起 4 次）；" +
                "该法术自动克服目标的" + Link(KSpellResistance, "法术抗力") +
                "，并可突破以描述符判定的魔法免疫。" +
                "强化完成后，法师会立即陷入 1 轮" + Link(KTimeRecoil, "时序反冲") + "。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidHarmony.Name", "空隙调谐");
            WizardArchetypeBlueprints.RegisterText(r + "VoidHarmony.Desc",
                "空隙法师可在战斗中拨动自身的法力相位。以一个" +
                Link(KSwiftAction, "迅捷动作") + "选择一种状态激活，效果持续至战斗结束。" +
                "选择【致密收束】时，其" + Link(KDC, "难度等级（DC）") + "提高 2 点，但" +
                Link(KCasterLevel, "施法者等级") + "降低 1 级；" +
                "选择【裂隙穿透】时，" + Link(KCasterLevel, "施法者等级") + "提高 1 级，但" +
                Link(KDC, "难度等级（DC）") + "降低 2 点。" +
                "同一种状态最多叠加 2 层，且两种状态互斥。");

            WizardArchetypeBlueprints.RegisterText(r + "VoidOverdraw.Name", "空隙透支");
            WizardArchetypeBlueprints.RegisterText(r + "VoidOverdraw.Desc",
                "空隙法师燃烧自身的现实锚点，使" +
                Link(KVoidAnnihilation, "空隙湮灭") + "恢复 1 次可用充能。" +
                "作为代价，他会同时获得 30 秒力竭与 90 秒疲劳，两者从一开始便同时计时：" +
                "30 秒后力竭结束，而疲劳仍会继续 60 秒。" +
                "只有当" + Link(KVoidAnnihilation, "空隙湮灭") +
                "已无可用充能时才可使用该能力。");

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

            Add(KSpellPenetration, r + "SpellPenetration.Name", r + "SpellPenetration.Desc");
            Add(KIntMod, r + "IntMod.Name", r + "IntMod.Desc");
            Add(KWizardLevel, r + "WizardLevel.Name", r + "WizardLevel.Desc");
            // 注意：不再注册 CasterLevel —— 现在直接引用原版的 Caster_Level
        }

        /// <summary>生成悬停标记：{g|Encyclopedia:Key}文字{/g}</summary>
        internal static string Link(string key, string text)
        {
            return "{g|Encyclopedia:" + key + "}" + text + "{/g}";
        }
    }
}
