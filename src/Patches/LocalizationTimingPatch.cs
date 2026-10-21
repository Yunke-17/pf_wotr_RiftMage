using HarmonyLib;
using Kingmaker.Localization;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 本地化文本注册时机补丁。
    ///
    /// 时序问题（GameStarter.cs）：
    ///   :205  loader.LoadPackTOC()        → 触发 BlueprintsCache.Init，本 MOD 在此构建蓝图
    ///   :229  while (LocalizationManager.CurrentPack == null) yield return null;
    ///   :233  LocalizationManager.WaitForInit()   → 本地化包此时才真正就绪
    ///
    /// 构建蓝图时 CurrentPack 还是 null，PutString 的写入会丢失，
    /// UI 上表现为显示 null 或裸 key。因此把文本缓存起来，等 WaitForInit 之后统一 flush。
    /// </summary>
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.WaitForInit))]
    internal static class LocalizationTimingPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!Main.Enabled) return;
            WizardArchetypeBlueprints.FlushText();

            // 图标第二轮：启动期（BlueprintRegistrationPatch）可能仍在 preloading，
            // Sprite 资源未必可取；本地化就绪时再试一次，仍未成功则由 Main.OnUpdate 兜底重试。
            IconsAndFx.ApplyQueued("本地化就绪后");

            // 光环 prefab 可加载性自检（第一轮"看不到光环"无法从日志判断原因）
            Presentation.VerifyFxAssets();

            // 说明：本 MOD 已不再注册自定义术语（改用直接描述），
            // 所以这里不需要再做 Glossary 注入。
        }
    }
}
