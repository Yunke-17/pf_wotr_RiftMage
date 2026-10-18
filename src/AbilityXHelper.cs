using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X 的「合格法术」判定（需求 Q1 / 06 D5a）。
    ///
    /// 合格 = 排除戏法 + 法术书归属职业 == 法师
    ///
    /// 为什么用「法术书归属职业」而不用「法术清单」：
    ///   萨西隆变体等法师变体的法表是法师法表的一个子集，
    ///   若按法术清单判定会漏掉这些变体（06 D5c）。
    ///   按归属职业判定则天然覆盖：萨西隆、探法师等变体的法术书
    ///   的 m_CharacterClass 都指向法师职业 ba34257984f4c41408ce1dc2004e342e
    ///   （已在 C:\DSH\blueprints\Classes\Wizard\**\*Spellbook.jbp 上逐个核对）。
    ///
    /// 同理，博学士/秘术师等进阶职业若沿用法师法术书，也会自动合格（06 D5b）。
    /// </summary>
    internal static class AbilityXHelper
    {
        internal static bool IsEligible(AbilityData spell)
        {
            if (spell == null) return false;

            // 必须是法术（不是能力/物品使用等）
            if (spell.Blueprint == null || spell.Blueprint.Type != AbilityType.Spell) return false;

            // 必须有法术书 —— 没有法术书来源的不算
            var book = spell.Spellbook;
            if (book == null || book.Blueprint == null) return false;

            // 排除戏法（0 环）
            if (spell.SpellLevel <= 0) return false;

            // 法术书归属职业必须是法师
            var cls = book.Blueprint.CharacterClass;
            if (cls == null) return false;

            return cls.AssetGuid == WizardArchetypeBlueprints.WizardClassGuid;
        }
    }
}
