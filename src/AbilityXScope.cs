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

            bool sameAsAimed = Data.Spell != null && Data.Spell.Equals(spell);
            bool firstSight = !IsSameReported(spell);

            string why;
            bool eligible = AbilityXHelper.IsEligible(spell, out why);

            if (Main.DebugLog && firstSight)
            {
                // 同一发法术在一次施法里会被 arm 几十次（实测一次 BurningHands 触发 24 次），
                // 日志只打第一次，避免刷屏。
                Main.Log?.Log($"[能力X] 判定 {spell.Blueprint?.name}：{(eligible ? "合格" : "不合格 — " + why)}" +
                              $"（环 {spell.SpellLevel}，CL {spell.Spellbook?.CasterLevel}，" +
                              $"书实例 {(spell.Spellbook != null ? (spell.Spellbook.Blueprint != null ? spell.Spellbook.Blueprint.name : "?") : "无")}，" +
                              $"书蓝图 {(spell.SpellbookBlueprint != null ? spell.SpellbookBlueprint.name : "无")}）");
            }

            if (eligible)
            {
                if (sameAsAimed)
                {
                    return;   // 同一发重复 arm，静默跳过
                }

                Data.Spell = spell;
                Main.Log?.Log($"[能力X] 已瞄准：{spell.Blueprint?.name}（环 {spell.SpellLevel}，CL {spell.Spellbook?.CasterLevel}）");

                // 验收埋点：窗口已瞄准一发合格法术
                AcceptanceHook("X.Armed", true);
                AcceptanceHook("X.CasterLevel", spell.Spellbook != null ? spell.Spellbook.CasterLevel : 0);
                return;
            }

            // 不合格：不占用窗口，也不清掉已有的瞄准目标
            // （玩家可能先放几个戏法/术士法术，再放法师法术，强化应留给后者）。
            // 验收埋点：只对「本该被拦下的法术」上报，避免物品/类法术能力污染断言。
            if (firstSight && IsTrickOrOtherSpell(spell))
            {
                AcceptanceHook("X.Ineligible", true);
                AcceptanceHook("X.IneligibleReason", why ?? "unknown");
            }
        }

        /// <summary>
        /// 同一次施法内对同一发法术只报告一次。
        ///
        /// 实测：一次 BurningHands 施法会触发 RuleCalculateAbilityParams 约 24 次。
        /// 用「键 + 时间窗」双重去重：
        ///   - 键相同且距上次报告 &lt; 50ms  → 视为同一次施法的重复触发
        ///   - 否则重新报告（玩家真的又放了一次同一个法术）
        /// 单用键比对不够：交替放 A、B、A 时会被误判。
        /// </summary>
        private string _lastSeenKey;
        private float _lastSeenTime = -999f;

        private bool IsSameReported(AbilityData spell)
        {
            string key = (spell.Blueprint != null ? spell.Blueprint.name : "?") + "|" +
                         spell.SpellLevel + "|" +
                         (spell.SpellbookBlueprint != null ? spell.SpellbookBlueprint.name : "-");

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (key == _lastSeenKey && now - _lastSeenTime < 0.05f)
            {
                _lastSeenTime = now;
                return true;
            }

            _lastSeenKey = key;
            _lastSeenTime = now;
            return false;
        }

        public void OnEventDidTrigger(RuleCalculateAbilityParams evt)
        {
        }

        // ------------------------------------------------------------------
        // consume + 无视 SR（同一阶段，因为都要先确认「是这一发」）
        // ------------------------------------------------------------------

        /// <summary>
        /// 无视法术抗力（D10 / 说明书 §8.3）。
        ///
        /// 原版范式：宗师之杖 MetamagicRodMechanics.cs:99-106
        ///     if (ForceIgnoreSpellResist &amp;&amp; evt.Context != null)
        ///         evt.Context.ForceIgnoreSpellResist = true;
        ///
        /// SR 判定链第一项就是 !context.SourceAbilityContext.ForceIgnoreSpellResist
        /// （RuleSpellResistanceCheck.cs:50-66），而 SourceAbilityContext 是
        /// MechanicsContext.Root as AbilityExecutionContext —— 正是 RuleCastSpell.Context
        /// 那个只读字段（RuleCastSpell.cs:129 构造时创建）。同一个对象，改得上。
        ///
        /// 时序：RuleCastSpell.OnTrigger 末尾的 Spell.Cast() 只**启动**
        /// AbilityExecutionProcess 协程，真正的 SR 检定在后续帧的 Tick() 里
        /// （AbilityExecutionProcess.cs:402）。所以 About/Did 阶段写都来得及。
        ///
        /// ★ 为什么写在 Scope 里而不是单独一个组件：
        ///   独立组件拿不到本 Scope 的运行时 Data.Spell ——
        ///   EntityFact.Components 是每个 Fact 实例各自的（EntityFact.cs:118 用
        ///   SourceBlueprintComponentName 作键），而 SourceBlueprintComponent 是共享的蓝图对象。
        ///   两个组件之间无法共享「当前瞄准哪一发」，只能放在同一个组件里。
        /// </summary>
        private void TryIgnoreSpellResistance(RuleCastSpell evt)
        {
            if (evt == null || evt.Spell == null || evt.Context == null) return;

            AbilityData aimed = Data.Spell;
            if (aimed == null) return;
            if (!aimed.Equals(evt.Spell)) return;

            // 已被别人（宗师之杖等）设过就不重复处理
            if (evt.Context.ForceIgnoreSpellResist) return;

            evt.Context.ForceIgnoreSpellResist = true;

            // 记录「这个施法上下文被我们强化了」。
            // 后续的 SR 静默通过、伤害加骰面都靠这条标记识别自己该不该生效。
            //
            // 用弱引用表：AbilityExecutionContext 是短命对象，强引用会漏内存。
            // net48 的 ConditionalWeakTable 没有 AddOrUpdate，用 GetValue(工厂) 即可。
            s_Empowered.GetValue(evt.Context, _ => new EmpowerInfo
            {
                CasterLevel = evt.Spell.Spellbook != null ? evt.Spell.Spellbook.CasterLevel : 0,
                SpellName = evt.Spell.Blueprint != null ? evt.Spell.Blueprint.name : null,
                RoundIndex = CurrentRoundIndex(),
            });

            Main.Log?.Log($"[能力X] 无视 SR 已置位：{evt.Spell.Blueprint?.name}");
        }

        // ------------------------------------------------------------------
        // 强化标记：SR 静默通过 + 伤害加骰面 共用
        // ------------------------------------------------------------------

        /// <summary>一次被强化的施法所携带的信息。</summary>
        internal sealed class EmpowerInfo
        {
            /// <summary>该法术书的 CL（决定加骰面档位 / 豁免骰数档位）。</summary>
            public int CasterLevel;
            public string SpellName;

            /// <summary>
            /// 施法时所在的战斗轮次（GameTime / 6 秒）。-1 表示拿不到。
            /// 用于实现 §8.5「后续回合独立触发的持续效果豁免不继承多骰取低」。
            /// </summary>
            public int RoundIndex = -1;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            Kingmaker.UnitLogic.Abilities.AbilityExecutionContext, EmpowerInfo> s_Empowered =
            new System.Runtime.CompilerServices.ConditionalWeakTable<
                Kingmaker.UnitLogic.Abilities.AbilityExecutionContext, EmpowerInfo>();

        /// <summary>取强化信息；不是我们强化的返回 null。</summary>
        internal static EmpowerInfo GetEmpowerInfo(Kingmaker.UnitLogic.Mechanics.MechanicsContext ctx)
        {
            if (ctx == null) return null;

            var abilityCtx = ctx as Kingmaker.UnitLogic.Abilities.AbilityExecutionContext
                             ?? ctx.SourceAbilityContext;
            if (abilityCtx == null) return null;

            EmpowerInfo info;
            return s_Empowered.TryGetValue(abilityCtx, out info) ? info : null;
        }

        /// <summary>施法时所在的轮次（GameTime / 6 秒）。拿不到返回 -1。</summary>
        private static int CurrentRoundIndex()
        {
            try
            {
                var game = Kingmaker.Game.Instance;
                if (game == null || game.Player == null) return -1;
                return (int)(game.Player.GameTime.TotalSeconds / 6.0);
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        /// <summary>该 SR 检定是否由本 MOD 的强化施法触发。</summary>
        internal static bool IsSrBypassedByUs(Kingmaker.RuleSystem.Rules.RuleSpellResistanceCheck rule)
        {
            return rule != null && GetEmpowerInfo(rule.Context) != null;
        }

        public void OnEventAboutToTrigger(RuleCastSpell evt)
        {
            // 预置一次：覆盖「即时生效、不走协程」的路径。
            // 此时会话尚未判定成败，但只对本单位本窗口内瞄准的那一发置位，不会误伤。
            TryIgnoreSpellResistance(evt);
        }

        public void OnEventDidTrigger(RuleCastSpell evt)
        {
            // 再置一次（幂等），与原版 MetamagicRodMechanics 的时机一致。
            TryIgnoreSpellResistance(evt);

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

            // 该发的 SR 置位结果一并上报（置位在 Did 开头已完成）
            if (evt.Context != null)
            {
                AcceptanceHook("X.SRIgnored", evt.Context.ForceIgnoreSpellResist);
            }

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
        ///
        /// ★ 为什么不用 Type.GetType("ModDiag.Embedded, ModDiag")：
        ///   Unity 的 Mono 下 UMM 加载 MOD 的方式不走标准程序集解析，
        ///   带程序集名的 Type.GetType 会返回 null（实测：埋点一次都没到验收层）。
        ///   必须遍历 AppDomain 已加载程序集去找。
        /// </summary>
        private static System.Reflection.MethodInfo s_ExpectMethod;
        private static bool s_ExpectLookupDone;

        internal static void AcceptanceHook(string field, object value)
        {
            try
            {
                if (!s_ExpectLookupDone)
                {
                    s_ExpectLookupDone = true;
                    s_ExpectMethod = FindExpectMethod();
                    if (s_ExpectMethod == null)
                    {
                        Main.Log?.Log("[能力X] 未找到 ModDiag.Embedded.Expect，验收埋点已禁用（不影响功能）");
                    }
                    else
                    {
                        Main.Log?.Log("[能力X] 验收埋点已接通 → ModDiag.Embedded.Expect");
                    }
                }

                if (s_ExpectMethod == null)
                {
                    return;
                }

                s_ExpectMethod.Invoke(null, new object[] { field, field, value });
            }
            catch (System.Exception e)
            {
                // 验收埋点绝不影响游戏逻辑
                Main.Log?.Warning("[能力X] 验收埋点异常：" + e.GetType().Name + "：" + e.Message);
            }
        }

        private static System.Reflection.MethodInfo FindExpectMethod()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("ModDiag.Embedded", false);
                if (t == null) continue;

                var m = t.GetMethod("Expect",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m != null) return m;
            }
            return null;
        }
    }
}
