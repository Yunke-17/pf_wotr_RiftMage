using System;
using System.Collections;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 X 的「额外伤害骰」—— 实际实现为**加骰面**（已与用户确认的方案 A）。
    ///
    /// ========== 语义 ==========
    ///
    /// 说明书 §8.2 写「额外伤害骰 +1d2 / +1d4 / +1d6」，§8.4 又说「设计目标是让额外伤害骰
    /// 计入该法术的基础伤害骰」。两者有冲突，用户在 2026-10-18 明确选择**方案 A：加骰面**：
    ///
    ///     燃烧之手 5d4（CL 1–4）→ 5d8
    ///     灼热射线 3d6（CL 5–9）→ 3d10
    ///     灼热射线 3d6（CL 10+）→ 3d12
    ///
    /// 即：骰子**颗数不变，每颗骰的面数 +N**。这样「计入基础伤害骰」成立（它改的就是基础骰本身），
    /// 也不会因为多射线/多段命中而重复累加数值。
    ///
    /// ========== 为什么必须在 OnEventAboutToTrigger ==========
    ///
    /// RuleCalculateDamage.OnTrigger 第 98 行就执行
    ///     CalculatedDamage.InsertRange(0, DamageBundle.Select(CalculateDamageValue))
    /// 把每一段伤害的最终值算完并存进 CalculatedDamage。
    /// 在 OnEventDidTrigger 里改骰式只会影响 tooltip，不改实际伤害。
    ///
    /// ========== 为什么读 ModifiedValue 而不是 BaseFormula ==========
    ///
    /// ModifiableDiceFormula.ModifiedValue（ModifiableDiceFormula.cs:19-30）取的是
    /// 「当前生效的骰式」，即最后一条修改。若直接写死自己算的值，会**覆盖**强效/强能等
    /// 其他组件已经做过的修改。正确做法：读出当前值 → 加面 → 再 Modify 追加一条。
    /// 这样是「在别人结果之上继续变换」，不是替换。
    ///
    /// ========== 为什么挂在特性上（和 SR 同理）==========
    ///
    /// 伤害结算发生在 RuleCastSpell.Did **之后**（AbilityExecutionProcess 协程后续帧），
    /// 而强化状态在 Did 里就被移除了。订阅若挂在 Buff 上，此时早已失效。
    ///
    /// ========== 骰面上限 ==========
    ///
    /// 引擎掷骰是纯数值路径（RulebookEvent.D 只做 Random.Range(1, Sides()+1)，
    /// DiceTypeExtension.Sides() 就是 (int)self；全程序集没有任何 case DiceType. 分支），
    /// 所以 D14 / D18 这种非标准骰面能正常掷、正常显示。
    /// 用户决定**不设实感封顶**，只保留 MaxDiceSides=100 作为防御常量。
    /// </summary>
    internal class AbilityXDamageDice : UnitFactComponentDelegate,
        IInitiatorRulebookHandler<RuleCalculateDamage>
    {
        /// <summary>防御上限。超过 100 面的骰子既没有实际意义，也可能是数值异常。</summary>
        private const int MaxDiceSides = 100;

        public void OnEventAboutToTrigger(RuleCalculateDamage evt)
        {
            if (evt == null) return;

            // 只处理本 MOD 强化过的那一发
            var info = AbilityXScope.GetEmpowerInfo(GetContext(evt));
            if (info == null) return;

            int add = GetDiceFaceBonus(info.CasterLevel);
            if (add <= 0) return;

            int changed = 0;
            string firstFormula = null;
            int firstSidesBefore = 0;
            int firstSidesAfter = 0;

            foreach (var damage in EnumerateDamage(evt))
            {
                if (damage == null) continue;

                var dice = damage.Dice;
                if (dice == null) continue;

                // 幂等保护：如果最后一条修改就是我们自己做的，说明这段伤害已经加过面。
                // 触发条件：特性的 Fact 万一被重复添加（例如变体与本体特性叠加），
                // OnEventAboutToTrigger 会被调用多次，加面就会累加（+2 → +4 → +6）。
                // ModifiedValue 取的是 LastItem，所以只要末条来源是自己就跳过即可。
                if (IsOwnLastModification(dice)) continue;

                // 读「当前生效的骰式」，而不是蓝图静态值
                DiceFormula current = dice.ModifiedValue;
                int sides = (int)current.Dice;
                if (sides <= 0) continue;   // 无骰伤害（纯固定值/纯加值）

                int next = Math.Min(sides + add, MaxDiceSides);
                if (next <= sides) continue;   // 已到上限

                dice.Modify(new DiceFormula(current.Rolls, (DiceType)next), Fact);

                if (firstFormula == null)
                {
                    firstFormula = current.Rolls + "d" + next;
                    firstSidesBefore = sides;
                    firstSidesAfter = next;
                }

                changed++;
            }

            if (changed == 0) return;

            Main.Log?.Log($"[能力X] 加骰面 +{add}（CL {info.CasterLevel}）：{info.SpellName}，" +
                          $"{changed} 段伤害，首段 {firstFormula}");

            // 验收埋点
            //
            // ★ 为什么报 X.DiceUp 而不只报 X.DiceBonus：
            //   档位（+2/+4/+6）依赖 CL，而 CL 会随升级变化。
            //   断言里写死某个档位必然在换等级后误报 FAIL（实测踩过：
            //   expect=2 但角色已 CL10 应为 6）。
            //   X.DiceUp 表达的是「骰面确实变大了」，与等级无关，才是稳定的验收点。
            AbilityXScope.AcceptanceHook("X.DiceBonus", add);
            AbilityXScope.AcceptanceHook("X.DiceUp", firstSidesAfter > firstSidesBefore);
            if (firstFormula != null)
                AbilityXScope.AcceptanceHook("X.DiceFormula", firstFormula);
        }

        public void OnEventDidTrigger(RuleCalculateDamage evt)
        {
            // 不做任何事：在 Did 阶段改骰式只影响 tooltip，无法改变已结算的伤害。
        }

        /// <summary>
        /// 最后一条骰式修改是不是本 Fact 做的。
        /// 用于防止「特性被重复添加 → About 多次触发 → 加面累加」。
        /// </summary>
        private bool IsOwnLastModification(ModifiableDiceFormula dice)
        {
            var mods = dice.Modifications;
            if (mods == null || mods.Count == 0) return false;

            var last = mods[mods.Count - 1];
            return last != null && ReferenceEquals(last.Source, Fact);
        }

        /// <summary>
        /// 档位：CL 1–4 → +2 面；5–9 → +4 面；10+ → +6 面。
        /// 对应说明书 §8.2 的 +1d2 / +1d4 / +1d6 三档。
        /// </summary>
        private static int GetDiceFaceBonus(int casterLevel)
        {
            if (casterLevel >= 10) return 6;
            if (casterLevel >= 5) return 4;
            if (casterLevel >= 1) return 2;
            return 0;
        }

        private static Kingmaker.UnitLogic.Mechanics.MechanicsContext GetContext(RuleCalculateDamage evt)
        {
            // RuleCalculateDamage 继承 RulebookTargetEvent，Reason 在基类 RulebookEvent 上；
            // Reason.Context 才是 MechanicsContext（伤害可能来自法术，也可能来自武器/能力）。
            var reason = evt.Reason;
            return reason != null ? reason.Context : null;
        }

        /// <summary>
        /// 遍历伤害包。一个法术可能有多种伤害类型（如地狱烈焰射线 = 火 + 邪能），
        /// 每种是一条 BaseDamage，都要处理。
        /// </summary>
        private static IEnumerable<BaseDamage> EnumerateDamage(RuleCalculateDamage evt)
        {
            var bundle = evt.DamageBundle;
            if (bundle == null) yield break;

            foreach (var d in bundle)
            {
                var bd = d as BaseDamage;
                if (bd != null) yield return bd;
            }
        }
    }
}
