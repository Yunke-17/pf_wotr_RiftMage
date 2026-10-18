using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X 的「合格法术」判定（需求 Q1 / 06 D5a）。
    ///
    /// 合格 = 排除戏法 + 法术书归属职业 == 法师
    ///
    /// ========== 为什么用「法术书归属职业」而不用「法术清单」 ==========
    ///
    /// 萨西隆等法师变体的法表是法师法表的一个子集，
    /// 若按法术清单判定会漏掉这些变体（06 D5c）。
    /// 按归属职业判定则天然覆盖：萨西隆 7 个学派法术书的 m_CharacterClass
    /// 都指向法师职业 ba34257984f4c41408ce1dc2004e342e
    /// （已用 StaticCheck/check_static.py 逐个数过，见 StaticCheck/REPORT.md）。
    ///
    /// 同理，博学士/秘术师等进阶职业若沿用法师法术书，也会自动合格（06 D5b）。
    ///
    /// ========== 2026-10-18 修正：SpellbookBlueprint 回退 ==========
    ///
    /// 实测发现：同一个法师法术（MagicMissile / StunningBarrier），
    /// 有时判合格、有时判不合格。原因是 Arm 阶段的 AbilityData 可能还没有绑定
    /// Spellbook 实例（Spellbook 为 null），此时只能靠 SpellbookBlueprint 判断。
    ///
    /// 所以判定链改成「两条路，任一成立即合格」：
    ///   ① spell.Spellbook.Blueprint.CharacterClass == 法师     （实例可用时）
    ///   ② spell.SpellbookBlueprint.CharacterClass == 法师      （实例不可用时的回退）
    /// </summary>
    internal static class AbilityXHelper
    {
        internal static bool IsEligible(AbilityData spell)
        {
            string why;
            return IsEligible(spell, out why);
        }

        /// <summary>
        /// 带失败原因的版本。诊断日志与验收埋点都用它，
        /// 这样日志里能直接看到「是哪一条判据把它挡住了」。
        /// </summary>
        internal static bool IsEligible(AbilityData spell, out string why)
        {
            why = null;

            if (spell == null) { why = "spell=null"; return false; }

            // 必须是法术（不是能力/物品使用等）
            if (spell.Blueprint == null) { why = "Blueprint=null"; return false; }
            if (spell.Blueprint.Type != AbilityType.Spell)
            {
                why = "不是法术(type=" + spell.Blueprint.Type + ")";
                return false;
            }

            // 排除戏法（0 环）
            if (spell.SpellLevel <= 0)
            {
                why = "戏法(环 " + spell.SpellLevel + ")";
                return false;
            }

            // 法术书归属职业必须是法师。
            // 两条路：优先实例，实例缺失时回退蓝图。
            var classFromInstance = TryGetClassFromInstance(spell);
            if (classFromInstance != null)
            {
                bool ok = classFromInstance.AssetGuid == WizardArchetypeBlueprints.WizardClassGuid;
                if (!ok)
                {
                    why = "法术书归属职业=" + (classFromInstance.name ?? "?") +
                          "（" + classFromInstance.AssetGuid + "），不是法师";
                }
                return ok;
            }

            var classFromBlueprint = TryGetClassFromBlueprint(spell);
            if (classFromBlueprint != null)
            {
                bool ok = classFromBlueprint.AssetGuid == WizardArchetypeBlueprints.WizardClassGuid;
                if (!ok)
                {
                    why = "法术书蓝图归属职业=" + (classFromBlueprint.name ?? "?") +
                          "（" + classFromBlueprint.AssetGuid + "），不是法师";
                }
                return ok;
            }

            why = "拿不到法术书归属职业（Spellbook=" + (spell.Spellbook != null ? "有" : "无") +
                  ", SpellbookBlueprint=" + (spell.SpellbookBlueprint != null ? "有" : "无") + "）";
            return false;
        }

        private static BlueprintCharacterClass TryGetClassFromInstance(AbilityData spell)
        {
            var book = spell.Spellbook;
            if (book == null || book.Blueprint == null) return null;
            return book.Blueprint.CharacterClass;
        }

        private static BlueprintCharacterClass TryGetClassFromBlueprint(AbilityData spell)
        {
            var bp = spell.SpellbookBlueprint;
            if (bp == null) return null;
            return bp.CharacterClass;
        }
    }
}
