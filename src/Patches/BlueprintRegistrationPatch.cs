using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;

namespace WizardArchetype.Patches
{
    /// <summary>
    /// 蓝图注册挂钩。
    ///
    /// BlueprintsCache.Init() 会打开 blueprints-pack.bbp 并建立索引，
    /// 必须在它之后才能 AddCachedBlueprint 和按 GUID Load 已有蓝图。
    /// 用 Postfix 而不是 Prefix，确保索引已就绪。
    ///
    /// 这是本 MOD 唯一的"启动期"补丁，其余补丁都是运行期逻辑。
    /// </summary>
    [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
    internal static class BlueprintRegistrationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BlueprintsCache __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                WizardArchetypeBlueprints.Create(__instance);
                WizardArchetypeBlueprints.Register(__instance);
            }
            catch (System.Exception e)
            {
                Main.Log?.Error("WizardArchetype: 蓝图注册失败 - " + e);
            }
        }
    }
}
