using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities.Components.Base;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 三个按钮的可用性限制（用户 2026-10-19 要求的「灰显」）。
    ///
    /// IAbilityCasterRestriction 是引擎原生接口：
    ///   IsCasterRestrictionPassed 返回 false ⇒ 按钮灰显且不可点
    /// 门控点在 AbilityData.CanBeCastByCaster（遍历 CasterRestrictions 那段）。
    ///
    /// 规则：
    ///   Up（+1 层）    : N &lt; +2 且（N == 0 或 N &gt; 0）—— 即未达上限、且不在负向
    ///   Down（-1 层）  : N &gt; -2 且（N == 0 或 N &lt; 0）
    ///   Cancel（取消） : N != 0
    ///
    /// 互斥的落地方式（用户原话）：
    ///   「当已存在某一种 buff，则另外一种不可使用，直到该 buff 清空」
    ///   ⇒ N > 0 时 Down 灰；N < 0 时 Up 灰。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f651")]
    internal class AbilityYRestriction : BlueprintComponent, IAbilityCasterRestriction
    {
        internal enum Kind
        {
            Up,
            Down,
            Cancel,
        }

        public Kind Allow;

        public bool IsCasterRestrictionPassed(UnitEntityData caster)
        {
            if (caster == null) return false;

            int n = AbilityY.GetN(caster);

            switch (Allow)
            {
                case Kind.Up:
                    // 已达 +2 上限，或当前是负向（互斥）→ 不可用
                    return n < AbilityY.MaxStack && n >= 0;

                case Kind.Down:
                    return n > -AbilityY.MaxStack && n <= 0;

                case Kind.Cancel:
                    return n != 0;

                default:
                    return true;
            }
        }

        public string GetAbilityCasterRestrictionUIText()
        {
            switch (Allow)
            {
                case Kind.Up:
                    return "已达 +2 层上限，或当前为反向层数（需先取消）";
                case Kind.Down:
                    return "已达 -2 层上限，或当前为反向层数（需先取消）";
                case Kind.Cancel:
                    return "当前没有施法参数修正";
                default:
                    return "";
            }
        }
    }
}
