namespace WizardArchetype
{
    /// <summary>
    /// 悬停术语的引用工具。
    ///
    /// ========== 现状（2026-10-21 大幅精简） ==========
    ///
    /// 本类**不再注册任何自定义术语**。原因：
    ///
    ///   · 自建术语的悬停虽能显示文字，但右键「详细」打不开
    ///     （详细窗口走百科页面，mod 侧造不出可用的百科条目）；
    ///   · 给自建术语造百科页会污染百科系统的静态状态
    ///     （章节树 / 选择态 / 面包屑都会错乱）。
    ///
    /// 按用户要求：「我们自定义的条目去掉，直接描述」——
    /// 所有变体独有概念（空隙湮灭 / 空隙调谐 / 空隙透支 / 时序专注 / 时序恍惚）
    /// 都改为在各处描述里**直接写效果文字**，不再做粉色悬停。
    ///
    /// ========== 本类现在只做一件事 ==========
    ///
    /// 引用**原版已有**的术语，让描述里的通用名词（先攻、豁免、法术抗力…）
    /// 保持原版的悬停体验。这些 key 都确认存在于原版 zhCN.json 中
    /// （扫描过全部 11 万条文本里出现过的 {g|Encyclopedia:xxx}）。
    ///
    /// 为什么不自己注册：GlossaryHolder.s_EntriesbyKey 是 private static，
    /// 且 Initialize 会 Clear 一次；原版词条本来就在，直接引用即可。
    /// </summary>
    internal static class Glossary
    {
        // ---- 复用原版已有术语（不注册，直接引用其 key）----

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

        /// <summary>原版：体质（原版全库用 466 次，是标准写法）</summary>
        internal const string KConstitution = "Constitution";

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

        /// <summary>生成悬停标记：{g|Encyclopedia:Key}文字{/g}</summary>
        internal static string Link(string key, string text)
        {
            return "{g|Encyclopedia:" + key + "}" + text + "{/g}";
        }
    }
}
