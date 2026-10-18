using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X 的「作用域」：记住强化窗口里被瞄准的那一发法术，并在它成功施放后消耗状态。
    ///
    /// ========== 为什么需要这个组件 ==========
    ///
    /// 「强化下一发合格法术」这个语义，原版 <c>MetamagicOnNextSpell</c> 已经实现过一次，
    /// 但它有三处不能照抄（见 07 报告 §1.2）：
    ///
    ///   ① 效果只能是 AddMetamagic(enum) —— 我们的强化是「加骰面 + 豁免取低 + 无视 SR」，
    ///      没有对应枚举，所以效果组件要另外写，这里只负责「作用域」。
    ///   ② 它在 RuleCastSpell 的 About 阶段就 Buff.Remove() —— 那时成败还没判定。
    ///      RuleCastSpell.OnTrigger 的顺序是：ForceFail → 目标非法 → UMD → 法术失败率
    ///      → 奥术失败率 → Success = true → Spell.Cast()。
    ///      需求是「成功施法后才消耗」，因此必须等 Did + Success。
    ///   ③ CheckSpellLevel 只看戏法/环位，不看法术书归属职业 —— 术士兼职能蹭到。
    ///      这里改用 AbilityXHelper.IsEligible（Q1 判据）。
    ///
    /// ========== 两阶段设计（arm / consume）==========
    ///
    ///   arm     : RuleCalculateAbilityParams.About
    ///             这是「准备施放一个具体法术」时引擎必经的一步，拿到的是 AbilityData。
    ///             合格 → 记下 Data.Spell；不合格 → 清空。
    ///             （原版 MetamagicOnNextSpell 正是挂在同一个位置）
    ///
    ///   consume : RuleCastSpell.Did + Success + !IsUMDFailed
    ///             施法成功那一刻移除 Buff，窗口关闭。
    ///
    /// 为什么 arm 放在 RuleCalculateAbilityParams 而不是 RuleCastSpell：
    ///   RuleCastSpell 每发法术只出现一次，但 RuleCalculateAbilityParams 可能出现多次
    ///   （例如专注检定内部也会 trigger 一次）。多次 arm 同一个法术是无害的，
    ///   而 RuleCastSpell 才是「真正放出去了」的唯一信号。
    ///
    /// ========== Data 的存档语义 ==========
    ///
    ///   Data 来自 UnitFactComponentDelegate&lt;TData&gt;.ComponentRuntime.Data，
    ///   字段 m_Data 标了 [JsonProperty]（EntityFactComponentDelegate.cs:570），会随存档序列化。
    ///   如果「记住的法术」被持久化，读档后可能指向一个已经不存在的施法上下文。
    ///   因此 [JsonIgnore] 掉它 —— 强化窗口本身只有 3 轮，读档后重新瞄准即可，
    ///   不需要也不应该恢复「瞄准了哪个法术」。
    /// </summary>
    internal class AbilityXScope : UnitBuffComponentDelegate<AbilityXScope.ScopeData>,
        IInitiatorRulebookHandler<RuleCalculateAbilityParams>,
        IInitiatorRulebookHandler<RuleCastSpell>
    {
        /// <summary>
        /// 运行期状态。刻意不持久化（见类注释）。
        ///
        /// 注意类名不能叫 Data —— 基类 UnitFactComponentDelegate&lt;TData&gt; 已经有一个
        /// 名为 Data 的属性（ComponentRuntime.Data），嵌套类型同名会让 `Data.Spell`
        /// 解析到类型而不是实例属性，编译报 CS0120。
        /// </summary>
        public class ScopeData
        {
            /// <summary>当前被瞄准的那一发法术。null = 还没瞄准。</summary>
            [Newtonsoft.Json.JsonIgnore]
            public AbilityData Spell;
        }

        // ------------------------------------------------------------------
        // arm：记下「准备施放的是哪一发」
        // ------------------------------------------------------------------

        public void OnEventAboutToTrigger(RuleCalculateAbilityParams evt)
        {
            if (evt == null)
            {
                return;
            }

            // evt.AbilityData 是 RuleCalculateAbilityParams 暴露的 AbilityData（:285）。
            // 注意：某些路径（如按 Blueprint 构造）AbilityData 可能为空，此时无法瞄准。
            AbilityData spell = evt.AbilityData;
            if (spell == null)
            {
                return;   // 不要在这里清空 —— 无关的 params 计算不应该打断已瞄准的目标
            }

            if (AbilityXHelper.IsEligible(spell))
            {
                Data.Spell = spell;
                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力X] 已瞄准：{spell.Blueprint?.name}（环 {spell.SpellLevel}，CL {spell.Spellbook?.CasterLevel}）");
                }

                // 验收埋点：窗口已瞄准一发合格法术
                AcceptanceHook("X.Armed", true);
                AcceptanceHook("X.CasterLevel", spell.Spellbook != null ? spell.Spellbook.CasterLevel : 0);
            }
            else
            {
                // 不合格的法术不会占用窗口，但也不清掉已有的瞄准目标：
                // 玩家可能先放了几个戏法/术士法术，再放法师法术，强化应该留给后者。
                if (Main.DebugLog)
                {
                    Main.Log?.Log($"[能力X] 不合格，忽略：{spell.Blueprint?.name}（环 {spell.SpellLevel}，书 {(spell.Spellbook != null ? "有" : "无")}）");
                }

                // 验收埋点：区别对待「戏法/非法师法术」与「不是法术」——
                // 后者（物品触发、类法术能力）本来就不该进这条通道，报出来会污染断言。
                if (IsTrickOrOtherSpell(spell))
                {
                    AcceptanceHook("X.Ineligible", true);
                }
            }
        }

        public void OnEventDidTrigger(RuleCalculateAbilityParams evt)
        {
        }

        // ------------------------------------------------------------------
        // consume：施法成功才消耗
        // ------------------------------------------------------------------

        public void OnEventAboutToTrigger(RuleCastSpell evt)
        {
        }

        public void OnEventDidTrigger(RuleCastSpell evt)
        {
            if (evt == null || evt.Spell == null)
            {
                return;
            }

            // 失败不消耗：ForceFail / 目标非法 / UMD 失败 / 法术失败率 / 专注失败
            // 在引擎里都表现为 Success == false（RuleCastSpell.cs 的 OnTrigger 顺序）。
            if (!evt.Success)
            {
                return;
            }
            if (evt.IsUMDFailed)
            {
                return;
            }

            AbilityData aimed = Data.Spell;
            if (aimed == null)
            {
                return;
            }

            // ★ 必须用 .Equals()，不能用 == / ReferenceEquals。
            //
            // AbilityData 重写了 Equals（AbilityData.cs:1786-1804），
            // 比较的是 Blueprint + Caster + ConvertedFrom + SpellbookBlueprint + SpellLevel
            // + Fact + Metamagic 等内容；同一发法术在 arm 与 consume 阶段拿到的
            // 可能是不同实例但内容相等。用引用比较会漏判。
            if (!aimed.Equals(evt.Spell))
            {
                return;
            }

            // 防御：万一瞄准的法术其实不合格（理论上不会，arm 已经筛过）
            if (!AbilityXHelper.IsEligible(evt.Spell))
            {
                return;
            }

            Main.Log?.Log($"[能力X] 强化法术已成功施放，关闭窗口：{evt.Spell.Blueprint?.name}");
            Data.Spell = null;

            Buff?.Remove();

            AcceptanceHook("X.Consumed", true);
        }

        /// <summary>
        /// 判断这发「不合格」的法术，是否属于 Q1 要拦截的类别：
        ///   - 戏法（0 环）
        ///   - 来自非法师法术书的法术（术士/牧师/类法术等）
        ///
        /// 返回 false 表示：它压根不是法术（物品使用、类法术能力等），
        /// 那本来就不该进这条通道，不该算「拦截成功」。
        /// </summary>
        private static bool IsTrickOrOtherSpell(AbilityData spell)
        {
            if (spell == null || spell.Blueprint == null) return false;
            if (spell.Blueprint.Type != AbilityType.Spell) return false;

            // 戏法
            if (spell.SpellLevel <= 0) return true;

            // 有法术书但归属不是法师
            var book = spell.Spellbook;
            if (book == null || book.Blueprint == null) return false;
            var cls = book.Blueprint.CharacterClass;
            return cls != null && cls.AssetGuid != WizardArchetypeBlueprints.WizardClassGuid;
        }

        /// <summary>
        /// 埋点：把「字段 → 值」报给验收层。
        ///
        /// 用反射调用 ModDiag.Embedded.Expect，这样 WizardArchetype 不硬依赖 ModDiag ——
        /// 没装 ModDiag 时静默跳过，装了才生效。
        /// </summary>
        internal static void AcceptanceHook(string field, object value)
        {
            try
            {
                var t = System.Type.GetType("ModDiag.Embedded, ModDiag");
                if (t == null)
                {
                    return;
                }
                var m = t.GetMethod("Expect",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                m?.Invoke(null, new object[] { field, field, value });
            }
            catch (System.Exception)
            {
                // 验收埋点绝不影响游戏逻辑
            }
        }
    }
}
