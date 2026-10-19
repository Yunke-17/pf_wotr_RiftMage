using System;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace WizardArchetype
{
    /// <summary>
    /// 图标与特效的统一配置（用户 2026-10-20 新增需求）。
    ///
    /// ========== 图标 ==========
    ///
    /// `BlueprintUnitFact.m_Icon` 是 **private Sprite 字段**（BlueprintUnitFact.cs:128），
    /// 蓝图里以 `{"guid": "...", "fileid": 21300000}` 形式引用资源。
    /// 纯 C# 构建时必须反射写入一个 `Sprite` 实例 —— 但 Sprite 是 UnityEngine.Object，
    /// 无法凭空 new 出来。
    ///
    /// 做法：**加载原版蓝图，把它的 Icon 引用搬过来**。
    /// 原版蓝图的 m_Icon 在加载后已经是有效的 Sprite（由 ResourcesLibrary 解析），
    /// 直接取它的实例赋给我们的蓝图即可。
    ///
    /// 这样做的优点：
    ///   - 零外部资源依赖（不需要 Unity 工程 / AssetBundle）
    ///   - 不会因为资源 GUID 变更而失效（走的是已加载对象）
    ///
    /// ========== 特效 ==========
    ///
    /// `BlueprintBuff.FxOnStart` / `FxOnRemove` 是 `PrefabLink`，
    /// 其 `AssetId` 是 public 字段（WeakResourceLink.cs:55），可直接赋值字符串。
    /// 复用原版特效资源 GUID 即可，无需打包。
    ///
    /// ⚠️ 注意：`FxOnRemove` 为 null 会在玩家棋子身上崩溃（见 AbilityX.cs 的注释），
    /// 所以即使不想要特效也必须给一个空 PrefabLink；现在给它真正的资源。
    /// </summary>
    internal static class IconsAndFx
    {
        // ---------- 原版图标来源（已核对存在）----------

        /// <summary>法术大师「专注法术」图标 —— 用于能力X。</summary>
        private const string IconSpellMasterFocusAbility =
            "5f31e79573fa7ab43a1b4de6bb210182";

        /// <summary>奥能源泉特性图标 —— 用于能力Y（同样是「资源池」概念）。</summary>
        private const string IconArcaneReservoirFeature =
            "9adc084dc47b0c44eaf3bd92e7732c34";

        /// <summary>奥能源泉能力图标 —— 用于能力Z。</summary>
        private const string IconArcaneReservoirAbility =
            "8bafdce16dc5eb944a7bb50adf5c9f0f";

        // ---------- 原版特效来源 ----------

        /// <summary>恐惧术的起始特效 —— 用于「特殊恍惚」（同属精神类负面状态）。</summary>
        private const string FxFearStart = "72e115990e2d31c40b9573f3d50ec60a";

        /// <summary>
        /// 把某个原版蓝图的图标复制到目标蓝图。
        /// 找不到来源时静默跳过（图标缺失不影响功能）。
        /// </summary>
        internal static void CopyIcon(BlueprintsCache cache, SimpleBlueprint target, string sourceGuid)
        {
            if (cache == null || target == null || string.IsNullOrEmpty(sourceGuid)) return;

            try
            {
                var src = cache.Load(BlueprintGuid.Parse(sourceGuid)) as BlueprintUnitFact;
                if (src == null)
                {
                    Main.Log?.Warning($"[图标] 来源蓝图不存在：{sourceGuid}");
                    return;
                }

                Sprite icon = src.Icon;
                if (icon == null)
                {
                    Main.Log?.Warning($"[图标] 来源蓝图无图标：{sourceGuid}");
                    return;
                }

                ReflectionFields.Set(target, "m_Icon", icon);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[图标] 复制失败：" + e.Message);
            }
        }

        /// <summary>给 Buff 设置起始特效（无资源时静默跳过）。</summary>
        internal static void SetFxOnStart(BlueprintBuff buff, string assetId)
        {
            if (buff == null || string.IsNullOrEmpty(assetId)) return;
            try
            {
                var link = new PrefabLink { AssetId = assetId };
                ReflectionFields.Set(buff, "FxOnStart", link);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特效] 设置 FxOnStart 失败：" + e.Message);
            }
        }

        /// <summary>
        /// 给 Buff 设置移除特效。
        /// 即使 assetId 为空也要给一个非 null 的 PrefabLink —— 否则玩家棋子身上会 NRE。
        /// </summary>
        internal static void SetFxOnRemove(BlueprintBuff buff, string assetId)
        {
            if (buff == null) return;
            try
            {
                var link = new PrefabLink();
                if (!string.IsNullOrEmpty(assetId)) link.AssetId = assetId;
                ReflectionFields.Set(buff, "FxOnRemove", link);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特效] 设置 FxOnRemove 失败：" + e.Message);
            }
        }

        // ---------- 各模块的图标配置入口 ----------

        /// <summary>能力X：特性/能力/状态统一用「专注法术」图标。</summary>
        internal static void ApplyAbilityX(BlueprintsCache cache,
            SimpleBlueprint feature, SimpleBlueprint ability, BlueprintBuff buff)
        {
            CopyIcon(cache, feature, IconSpellMasterFocusAbility);
            CopyIcon(cache, ability, IconSpellMasterFocusAbility);
            CopyIcon(cache, buff, IconSpellMasterFocusAbility);

            // 状态挂上时播放恐惧术的起始特效（精神类强化）
            SetFxOnStart(buff, FxFearStart);
        }

        /// <summary>能力Y：三个变体 + 父能力 + 特性，统一用「奥能源泉」图标。</summary>
        internal static void ApplyAbilityY(BlueprintsCache cache,
            SimpleBlueprint feature, params SimpleBlueprint[] others)
        {
            CopyIcon(cache, feature, IconArcaneReservoirFeature);
            foreach (var o in others)
            {
                CopyIcon(cache, o, IconArcaneReservoirFeature);
            }
        }

        /// <summary>能力Y 的层数显示 Buff（CL 强化 / DC 强化）。</summary>
        internal static void ApplyAbilityYBuffs(BlueprintsCache cache,
            BlueprintBuff upBuff, BlueprintBuff downBuff)
        {
            CopyIcon(cache, upBuff, IconArcaneReservoirFeature);
            CopyIcon(cache, downBuff, IconArcaneReservoirFeature);
        }

        /// <summary>能力Z：特性/能力用「奥能源泉能力」图标。</summary>
        internal static void ApplyAbilityZ(BlueprintsCache cache,
            SimpleBlueprint feature, SimpleBlueprint ability)
        {
            CopyIcon(cache, feature, IconArcaneReservoirAbility);
            CopyIcon(cache, ability, IconArcaneReservoirAbility);
        }

        /// <summary>特殊恍惚 / 特殊纠缠：用恐惧术特效 + 通用负面状态图标。</summary>
        internal static void ApplySpecialStatus(BlueprintsCache cache, BlueprintBuff buff)
        {
            if (buff == null) return;
            // 用「恐惧」的起始特效作为精神类负面状态的视觉提示
            SetFxOnStart(buff, FxFearStart);
        }
    }
}
