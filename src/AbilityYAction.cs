using System;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的具体动作：修改净层数 S。
    ///
    /// ========== 为什么继承 ContextAction ==========
    ///
    /// ContextAction 是 GameAction 的一种，能被 AbilityEffectRunAction 的
    /// ActionList 直接执行（与 ContextActionApplyBuff 同类）。
    /// 这样整个「点能力 → 改 S」链路都在蓝图里完成，不需要额外补丁。
    ///
    /// ========== 净层数规则（§9.3 / §9.4） ==========
    ///
    ///   ModeA  : S += 1（上限 +2）；达到上限时不可再选（见 CanUse 检查）
    ///   ModeB  : S -= 1（下限 -2）
    ///   Cancel : S = 0（§9.4，消耗 1 次使用次数，不返还）
    ///
    /// ========== 前置限制怎么做的 ==========
    ///
    /// 「达到 S=+2 后不能继续用模式 A」需要按运行期状态灰显按钮。
    /// 引擎的 Prerequisite 体系只能读静态/蓝图条件，读不到我们的 S，
    /// 因此这里在 RunAction 里做**运行时拒绝**：
    ///   - 已到上限时点击无效，并记日志说明；
    ///   - 使用次数已由 AbilityResourceLogic 扣除，所以拒绝时必须把次数**还回去**，
    ///     否则玩家会白白浪费一次。
    /// 灰显（按钮变灰）作为后续增强项，不阻塞功能。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f650")]
    internal class AbilityYAction : ContextAction
    {
        internal enum Kind
        {
            ModeA,
            ModeB,
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

            AbilityY.Track(unit);

            int s = AbilityY.GetS(unit);
            int before = s;

            switch (Action)
            {
                case Kind.ModeA:
                    if (s >= AbilityY.MaxS) { RejectAndRefund(unit, "已达上限 S=+2，不能再用模式 A"); return; }
                    s += 1;
                    break;

                case Kind.ModeB:
                    if (s <= -AbilityY.MaxS) { RejectAndRefund(unit, "已达下限 S=-2，不能再用模式 B"); return; }
                    s -= 1;
                    break;

                case Kind.Cancel:
                    if (s == 0) { RejectAndRefund(unit, "当前没有施法参数修正，无需取消"); return; }
                    s = 0;
                    break;
            }

            AbilityY.SetS(unit, s);

            Main.Log?.Log($"[能力Y] {Action}：S {before} → {s}");

            AbilityXScope.AcceptanceHook("X.YMode", Action.ToString());
            AbilityXScope.AcceptanceHook("X.YDelta", s - before);
        }

        /// <summary>
        /// 拒绝本次使用并把已扣的次数还回去。
        ///
        /// AbilityResourceLogic 在能力触发**之前**就已扣除资源，
        /// 所以运行时拒绝必须显式退还，否则玩家白掉一次。
        /// </summary>
        private static void RejectAndRefund(UnitEntityData unit, string reason)
        {
            Main.Log?.Log("[能力Y] 拒绝：" + reason);

            try
            {
                var res = AbilityYBlueprints.Resource;
                if (res != null)
                {
                    unit.Descriptor.Resources.Restore(res, 1);
                    Main.Log?.Log("[能力Y] 已退还 1 次使用次数");
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Y] 退还次数失败：" + e.Message);
            }

            AbilityXScope.AcceptanceHook("X.YRejected", reason);
        }
    }
}
