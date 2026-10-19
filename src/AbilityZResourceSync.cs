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

            // 旧存档补发（用户 2026-10-20 要求「只显示一个」后引入）：
            // Z 现在只挂在 1 级，而成长表 GiveFeaturesForPreviousLevels = False，
            // 升级时不会补发低等级特性。所以已升过级的旧角色第一次升级后
            // 在这里补挂一次（AddFact 对已存在的 blueprint 只做 AddRank，不会重复）。
            EnsureFeaturePresent(unit);

            Sync(true);
        }

        /// <summary>确保单位身上有「空隙透支」特性（旧存档补发用）。</summary>
        private static void EnsureFeaturePresent(UnitEntityData unit)
        {
            try
            {
                var bp = AbilityZ.Feature;
                if (bp == null || unit == null) return;

                if (unit.Descriptor.Progression.Features.HasFact(bp)) return;

                var wiz = WizardArchetypeBlueprints.WizardClass;
                if (wiz == null) return;

                // 只补给使用本变体的法师
                int lvl = unit.Progression.GetClassLevel(wiz);
                if (lvl < 1) return;

                unit.Descriptor.Progression.Features.AddFeature(bp);
                Main.Log?.Log("[能力Z] 旧存档补发「空隙透支」特性");
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Z] 补发特性失败：" + e.Message);
            }
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
