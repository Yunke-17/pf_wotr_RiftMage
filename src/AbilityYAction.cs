using System;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的具体动作：修改层数 N。
    ///
    /// ========== 方向（用户 2026-10-19 确认） ==========
    ///
    ///   以「+CL」为基准：
    ///     Up   ：N += 1 → ΔCL = +N，ΔDC = -2N     （+1 层 = CL+1 / DC-2）
    ///     Down ：N -= 1 → ΔCL = +N，ΔDC = -2N     （-1 层 = CL-1 / DC+2）
    ///     Cancel：N = 0
    ///
    /// ========== 前置校验已经在灰显里做了 ==========
    ///
    /// 互斥与上限由 AbilityYRestriction（IAbilityCasterRestriction）处理：
    /// 不可用时按钮直接灰显，玩家点不到。
    /// 所以这里**不需要**再拒绝并退还次数 —— 那是上一版的设计，
    /// 现在改成了「先灰显，点不到」，比「点了再退还」干净。
    ///
    /// 但保留一层防御：若因竞态真的越界，只记日志、不改数值（次数已在
    /// AbilityResourceLogic 里扣掉，此处不退还，因为灰显已保证正常路径不会发生）。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f650")]
    internal class AbilityYAction : ContextAction
    {
        internal enum Kind
        {
            Up,
            Down,
            Cancel,
        }

        public Kind Action;

        public override string GetCaption()
        {
            return "Ability Y: " + Action;
        }

        public override void RunAction()
        {
            var unit = Target != null ? Target.Unit : null;
            if (unit == null) return;

            int before = AbilityY.GetN(unit);
            int after = before;

            switch (Action)
            {
                case Kind.Up:
                    if (before >= AbilityY.MaxStack)
                    {
                        Main.Log?.Warning($"[能力Y] 越界防御：N={before} 已达上限，忽略 +1");
                        return;
                    }
                    after = before + 1;
                    break;

                case Kind.Down:
                    if (before <= -AbilityY.MaxStack)
                    {
                        Main.Log?.Warning($"[能力Y] 越界防御：N={before} 已达下限，忽略 -1");
                        return;
                    }
                    after = before - 1;
                    break;

                case Kind.Cancel:
                    if (before == 0)
                    {
                        Main.Log?.Warning("[能力Y] 越界防御：N=0，无需取消");
                        return;
                    }
                    after = 0;
                    break;
            }

            AbilityY.SetN(unit, after);

            Main.Log?.Log($"[能力Y] {Action}：N {before} → {after}" +
                          $"（ΔCL {after:+0;-0}，ΔDC {-2 * after:+0;-0}）");

            AbilityXScope.AcceptanceHook("X.YMode", Action.ToString());
            AbilityXScope.AcceptanceHook("X.YDelta", after - before);
        }
    }
}
