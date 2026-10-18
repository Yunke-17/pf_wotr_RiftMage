using System;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Designers;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的效果（§10.3）：
    ///   ① 消耗 1 次 Z（由 AbilityResourceLogic 完成）
    ///   ② 能力 X 恢复 1 次（不超 1）
    ///   ③ 同时获得 30 秒力竭 + 60 秒疲劳
    ///
    /// 三者都用引擎 API 完成，不直接改数值：
    ///   - 资源恢复走 UnitAbilityResource.Amount（与能力Y 次数同步同一手法，
    ///     因为 Restore 会被蓝图上限夹住）
    ///   - 状态施加走 GameHelper.ApplyBuff + 秒级时长
    ///
    /// 为什么力竭/疲劳用**秒级**：§10.3「不因战斗结束而提前清除」，
    /// 轮次时长会在战斗结束时被清（引擎行为），秒级不会。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f652")]
    internal class AbilityZAction : ContextAction
    {
        public override string GetCaption()
        {
            return "Ability Z: 透支充能";
        }

        public override void RunAction()
        {
            var unit = Target != null ? Target.Unit : null;
            if (unit == null) return;

            RestoreAbilityX(unit);
            ApplyConditions(unit);
        }

        /// <summary>把能力X 的资源恢复为 1 次（不超过 1）。</summary>
        private static void RestoreAbilityX(UnitEntityData unit)
        {
            try
            {
                var res = AbilityX.Resource;
                if (res == null) return;

                var collection = unit.Descriptor.Resources;
                if (collection == null) return;

                int current = collection.GetResourceAmount(res);
                if (current >= 1)
                {
                    Main.Log?.Log("[能力Z] 能力X 已有充能，不恢复（上限 1）");
                    return;
                }

                // 直接写 Amount：Restore 会被蓝图上限（BaseValue=1）夹住，
                // 而我们要表达的是「恢复到 1」这一确定结果。
                var unitRes = AbilityYResourceSync.FindResource(collection, res);
                if (unitRes == null) return;
                unitRes.Amount = 1;

                try
                {
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent<IUnitAbilityResourceHandler>(
                        h => h.HandleAbilityResourceChange(unit, unitRes, current));
                }
                catch (Exception) { }

                Main.Log?.Log($"[能力Z] 能力X 充能：{current} → 1");
                AbilityXScope.AcceptanceHook("X.ZRestored", true);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Z] 恢复能力X 失败：" + e.Message);
            }
        }

        /// <summary>施加 30 秒力竭 + 60 秒疲劳（同时开始计时）。</summary>
        private static void ApplyConditions(UnitEntityData unit)
        {
            try
            {
                var exhausted = AbilityZ.ExhaustedBuff;
                var fatigued = AbilityZ.FatiguedBuff;

                if (exhausted != null)
                {
                    // AddBuff 在 BuffCollection 上（UnitDescriptor.Buffs），不在 UnitDescriptor 上。
                    // 秒级时长：直接传 TimeSpan，避免用轮次（轮次会在战斗结束时被清）。
                    unit.Descriptor.Buffs.AddBuff(exhausted, unit, TimeSpan.FromSeconds(30));
                }
                if (fatigued != null)
                {
                    unit.Descriptor.Buffs.AddBuff(fatigued, unit, TimeSpan.FromSeconds(60));
                }

                Main.Log?.Log("[能力Z] 获得 30 秒力竭 + 60 秒疲劳");
                AbilityXScope.AcceptanceHook("X.ZConditions", true);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Z] 施加强化状态失败：" + e.Message);
            }
        }
    }
}
