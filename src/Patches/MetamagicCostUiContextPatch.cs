using HarmonyLib;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Metamagic;
using Kingmaker.UnitLogic;
using UniRx;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 补丁 D 的 UI 上下文配套。
    ///
    /// MetamagicHelper.DefaultCost 是静态扩展方法，拿不到"谁在查看法术书"。
    /// 规则结算路径可以从事件栈拿到 Initiator，但 UI 显示路径不在规则事件里跑，
    /// 事件栈是空的。不补这里就会出现"面板显示 +4、实际扣 +1"。
    ///
    /// 做法：在 SpellbookMetamagicSelectorVM.GetCost 进出时设置/清空一个线程静态的
    /// 当前角色，供 DefaultCost 补丁读取。这样显示和结算走同一个 DefaultCost，
    /// 不会各算一套导致漂移。
    /// </summary>
    [HarmonyPatch(typeof(SpellbookMetamagicSelectorVM), "GetCost")]
    internal static class MetamagicCostUiContextPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            IReactiveProperty<UnitDescriptor> ___m_Unit,
            IReactiveProperty<Spellbook> ___m_Spellbook)
        {
            MetamagicCost.s_CurrentSelectorUnit = ___m_Unit?.Value?.Unit;
            MetamagicCost.s_CurrentSelectorSpellbook = ___m_Spellbook?.Value;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            MetamagicCost.s_CurrentSelectorUnit = null;
            MetamagicCost.s_CurrentSelectorSpellbook = null;
        }
    }
}
