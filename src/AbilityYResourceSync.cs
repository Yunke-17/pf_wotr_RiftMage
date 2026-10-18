using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Components;

namespace WizardArchetype
{
    /// <summary>
    /// 能力 Y 的使用次数同步器。
    ///
    /// ========== 为什么需要它 ==========
    ///
    /// 需求：次数 = ceil(IntMod / 2) + ceil(法师等级 / 2)，分别取整后相加。
    ///
    /// 引擎做不到：
    ///   BlueprintAbilityResource.GetMaxAmount（:65-150）支持的模式只有
    ///     BaseValue + LevelIncrease×职业等级
    ///     BaseValue + IncreasedByStat（加的是**原始**属性调整值，没有除法）
    ///     IncreasedByLevelStartPlusDivStep（仍是线性，无除法）
    ///   已核实原版奥能源泉用的就是 3 + 职业等级×1，**完全不看智力**。
    ///
    /// ⇒ 只能自己算：把资源上限设为 0，每次「休息后 / 升级后 / 加入战斗」时
    ///   直接把 Amount 写成公式值。
    ///
    /// ========== 为什么用这几个时机 ==========
    ///
    /// 需求是「每次休息后恢复」。但智力/等级变化也应该重算，否则
    ///   - 升级后加了法师等级，次数不涨（玩家会觉得坏了）
    ///   - 临时智力变化（装备）不会立即反映（这符合「不重算」的直觉）
    /// 因此选：
    ///   IUnitRestHandler         休息完成 → 重算并回满
    ///   IUnitLevelUpHandler      升级完成 → 重算并回满
    ///   IUnitCombatHandler       加入战斗 → 仅重算上限，不回满（避免战斗中刷次数）
    /// </summary>
    internal class AbilityYResourceSync : UnitFactComponentDelegate,
        IUnitRestHandler,
        IUnitLevelUpHandler,
        IUnitCombatHandler
    {
        public void HandleUnitRest(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;
            Sync(true);   // 休息完成 → 重算并回满
        }

        public void HandleUnitBeforeLevelUp(UnitEntityData unit)
        {
        }

        public void HandleUnitAfterLevelUp(UnitEntityData unit, Kingmaker.UnitLogic.Class.LevelUp.LevelUpController controller)
        {
            if (unit == null || unit != Owner) return;
            Sync(true);   // 升级后法师等级可能变化 → 重算并回满
        }

        public void HandleUnitJoinCombat(UnitEntityData unit)
        {
            if (unit == null || unit != Owner) return;
            Sync(false);   // 只校正上限，不回满
        }

        public void HandleUnitLeaveCombat(UnitEntityData unit)
        {
        }

        /// <summary>
        /// 取单位身上的 UnitAbilityResource。
        /// UnitAbilityResourceCollection.GetResource 是 private，用反射访问其内部字典。
        /// </summary>
        internal static UnitAbilityResource FindResource(
            Kingmaker.UnitLogic.UnitAbilityResourceCollection collection,
            Kingmaker.Blueprints.BlueprintScriptableObject blueprint)
        {
            if (collection == null || blueprint == null) return null;

            try
            {
                var field = typeof(Kingmaker.UnitLogic.UnitAbilityResourceCollection)
                    .GetField("m_Resources", System.Reflection.BindingFlags.Instance |
                                             System.Reflection.BindingFlags.NonPublic);
                var dict = field?.GetValue(collection) as
                    System.Collections.Generic.Dictionary<Kingmaker.Blueprints.BlueprintScriptableObject,
                        UnitAbilityResource>;
                if (dict == null) return null;

                UnitAbilityResource r;
                return dict.TryGetValue(blueprint, out r) ? r : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 把资源量写成公式值。
        ///
        /// ★ 必须直接写 UnitAbilityResource.Amount，不能用 Restore。
        ///   原因（UnitAbilityResourceCollection.cs:126-149）：
        ///     Restore(blueprint, amount) 的最终值是
        ///         Math.Min(resource.Amount + amount, resource.GetMaxAmount(owner))
        ///     而 GetMaxAmount 读的是**蓝图** m_MaxAmount（我们设为 BaseValue=0），
        ///     所以无论 amount 给多少，结果都被夹到 0 —— 这就是「休息后次数仍为 0」的原因。
        ///   直接写 Amount 才能表达「上限由本组件计算」的语义。
        ///
        ///   副作用：绕过 IUnitAbilityResourceHandler 通知，UI 可能不刷新。
        ///   因此写完手动 RaiseEvent 一次，保证行动栏计数更新。
        /// </summary>
        private void Sync(bool refill)
        {
            try
            {
                var res = AbilityYBlueprints.Resource;
                if (res == null || Owner == null) return;

                int max = AbilityY.GetMaxUses(Owner);
                var collection = Owner.Descriptor.Resources;
                if (collection == null) return;

                int current = collection.GetResourceAmount(res);
                int target = refill ? max : Math.Min(current, max);

                if (current == target)
                {
                    AbilityXScope.AcceptanceHook("X.YMaxUses", max);
                    return;
                }

                var unitRes = FindResource(collection, res);
                if (unitRes == null)
                {
                    // 资源还没挂到单位上（例如特性尚未生效）—— 先登记，下次再同步
                    collection.Add(res, false);
                    unitRes = FindResource(collection, res);
                    if (unitRes == null) return;
                }

                unitRes.Amount = target;

                // 手动通知 UI（因为绕过了 Restore/Spend 的内部事件）。
                // IUnitAbilityResourceHandler 不是 IUnitSubscriber，只能用全局重载。
                try
                {
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent<IUnitAbilityResourceHandler>(
                        h => h.HandleAbilityResourceChange(Owner, unitRes, current));
                }
                catch (Exception) { }

                Main.Log?.Log($"[能力Y] 次数同步：{current} → {target} / {max}" +
                              $"（INT 调整值 {Owner.Stats.Intelligence.BonusWithoutTemp}，" +
                              $"法师等级 {AbilityY.GetWizardLevel(Owner)}）");

                AbilityXScope.AcceptanceHook("X.YMaxUses", max);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[能力Y] 次数同步失败：" + e.Message);
            }
        }
    }
}
