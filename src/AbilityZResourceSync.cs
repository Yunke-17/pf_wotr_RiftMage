using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Z 的次数同步（§10.1）。
    ///
    /// 次数按**基础 CL** 分档：7–11 → 1；12–16 → 2；17+ → 3。
    ///
    /// 为什么不用蓝图原生分档：
    ///   引擎的 IncreasedByLevelStartPlusDivStep 按**职业等级**算，
    ///   而需求是**基础 CL**（Spellbook.CasterLevel，含神话合书）。
    ///   两者在合书后会不同（例如法师 10 级 + 天使合书 → CL 更高）。
    ///   因此与能力Y 一样，由本组件在休息/升级后写入公式值。
    ///
    /// 同时负责：CL 未达 7 时把上限压回 0（此时能力应灰显）。
    /// </summary>
    internal class AbilityZResourceSync : UnitFactComponentDelegate,
        IUnitRestHandler,
        IUnitLevelUpHandler,
        IUnitCombatHandler
    {
        public void HandleUnitRest(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;
            Sync(true);
        }

        public void HandleUnitBeforeLevelUp(UnitEntityData unit)
        {
        }

        public void HandleUnitAfterLevelUp(UnitEntityData unit, Kingmaker.UnitLogic.Class.LevelUp.LevelUpController controller)
        {
            if (unit == null || unit != Owner) return;
            Sync(true);
        }

        public void HandleUnitJoinCombat(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;
            Sync(false);
        }

        public void HandleUnitLeaveCombat(UnitEntityData unit)
        {
        }

        private void Sync(bool refill)
        {
            try
            {
                var res = AbilityZ.Resource;
                if (res == null || Owner == null) return;

                int max = AbilityZ.GetMaxUses(Owner);
                var collection = Owner.Descriptor.Resources;
                if (collection == null) return;

                int current = collection.GetResourceAmount(res);
                int target = refill ? max : Math.Min(current, max);

                if (current == target)
                {
                    AbilityXScope.AcceptanceHook("X.ZMaxUses", max);
                    return;
                }

                var unitRes = AbilityYResourceSync.FindResource(collection, res);
                if (unitRes == null)
                {
                    collection.Add(res, false);
                    unitRes = AbilityYResourceSync.FindResource(collection, res);
                    if (unitRes == null) return;
                }

                unitRes.Amount = target;

                try
                {
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent<IUnitAbilityResourceHandler>(
                        h => h.HandleAbilityResourceChange(Owner, unitRes, current));
                }
                catch (Exception) { }

                Main.Log?.Log($"[能力Z] 次数同步：{current} → {target} / {max}" +
                              $"（基础 CL {AbilityZ.GetBaseCasterLevel(Owner)}）");

                AbilityXScope.AcceptanceHook("X.ZMaxUses", max);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Z] 次数同步失败：" + e.Message);
            }
        }
    }
}
