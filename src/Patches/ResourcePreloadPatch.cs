using System;
using HarmonyLib;
using Kingmaker.Blueprints;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 资源预载结束的挂钩（2026-10-21 第二轮新增）。
    ///
    /// 背景：游戏启动时会对蓝图引用的 Sprite 做异步预载（ResourcesLibrary.cs:83-96），
    /// 预载期间 TryGetResource 会返回"已缓存但内容为 null"的条目，且 TryUnloadResource
    /// 直接 return false —— 表现为图标全部静默失败（第一轮 26/26 就是这样）。
    ///
    /// 所以在 StopPreloadingMode 之后立刻补一次图标加载，不等 OnUpdate 轮询。
    /// 该方法在场景切换时会被多次调用，这里只在有未完成图标时才做事，开销可忽略。
    /// </summary>
    [HarmonyPatch(typeof(ResourcesLibrary), nameof(ResourcesLibrary.StopPreloadingMode))]
    internal static class ResourcePreloadPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!Main.Enabled) return;

            try
            {
                IconsAndFx.ApplyQueued("资源预载结束");
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[图标] 预载结束后应用失败：" + e.Message);
            }
        }
    }
}
