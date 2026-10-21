using System;
using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace WizardArchetype
{
    /// <summary>
    /// 图标与特效的底层工具（2026-10-21 重写，第二轮）。
    ///
    /// ========== 第一轮为什么只有声音生效 ==========
    ///
    /// 1. 声音是纯内存装配（给蓝图挂 PlaySoundAction），立刻生效；
    /// 2. 图标走 ResourcesLibrary.TryGetResource&lt;Sprite&gt;，**静默返回 null**
    ///    （日志里既没有 "Could not load resource" 也没有异常，说明命中了资源库里的
    ///    "已缓存但资源为空" 的条目，或该 sprite 不在 bundle 索引里）；
    /// 3. 光环虽然写进了 FxOnStart，但 prefab 是否可加载从未验证过。
    ///
    /// 所以这一轮加了两件事：
    ///   · **双路径取图标**：先按 Sprite GUID 直取；取不到就回退到"原版蓝图 + .Icon"，
    ///     后者是野兔魔宠已经验证可行的那条路（原版蓝图的 bundle 里自带图标）；
    ///   · **诊断日志**：失败时打印 IsAssetInBundle / IsLoaded / 源蓝图是否存在，
    ///     光环额外做一次 prefab 可加载性检查（Presentation.VerifyFxAssets）。
    ///
    /// ========== 时机 ==========
    ///
    /// 蓝图构建期（BlueprintsCache.Init）只登记，**不触发**资源加载
    /// （那个阶段可能仍在 preloading）。真正加载在 LocalizationManager.WaitForInit 之后，
    /// 之后由 Main.OnUpdate 每 2 秒重试，最多 20 秒并汇总日志。
    ///
    /// ========== 特效 ==========
    ///
    /// BlueprintBuff.FxOnStart —— **持续**挂在单位身上的特效（Buff.cs:445）；
    /// BlueprintBuff.FxOnRemove —— **移除瞬间**播一次（Buff.cs:397），与 FxOnStart 不是一对，
    /// 且为 null 时玩家棋子（带 ParticlesSnapMap）会 NRE，所以永远给空 PrefabLink。
    /// </summary>
    internal static class IconsAndFx
    {
        // ================= 图标 =================

        private sealed class PendingIcon
        {
            public SimpleBlueprint Target;
            public string SpriteGuid;
            public string SourceBlueprintGuid;
            public string Label;
        }

        private static readonly List<PendingIcon> s_Pending = new List<PendingIcon>();
        private static readonly Dictionary<string, Sprite> s_Sprites = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> s_Logged = new HashSet<string>();

        private static int s_Applied;
        private static int s_Pass;
        private static float s_NextRetry;
        private static float s_Deadline;
        private static bool s_Reported;

        /// <summary>
        /// 登记一个图标请求。
        /// <paramref name="sourceBlueprintGuid"/> 是"使用这张图标的原版蓝图"，
        /// 作为 Sprite 直取失败时的回退来源（可以为空）。
        /// </summary>
        internal static void QueueIcon(SimpleBlueprint target, string spriteGuid,
            string sourceBlueprintGuid, string label)
        {
            if (target == null)
            {
                Main.Log?.Warning("[图标] 目标蓝图为空：" + label);
                return;
            }
            if (string.IsNullOrEmpty(spriteGuid))
            {
                Main.Log?.Warning("[图标] 未配置图标：" + label);
                return;
            }
            s_Pending.Add(new PendingIcon
            {
                Target = target,
                SpriteGuid = spriteGuid,
                SourceBlueprintGuid = sourceBlueprintGuid,
                Label = label,
            });
        }

        /// <summary>
        /// 资源库是否仍在预载。
        ///
        /// ★ 第一轮 26 个图标全灭的根因就在这里：游戏启动时会对蓝图引用的 Sprite 做**异步预载**，
        /// 预载期间 ResourcesLibrary 里已经有该 assetId 的条目、但 Resource 还是 null，
        /// TryGetResource 直接返回这个 null（既不报错也不打日志）。
        /// 而预载期间 TryUnloadResource 会直接 return false —— 所以我们"清缓存再重试"也无效。
        /// 正确做法：等 Preloading == false 之后再加载（ResourcesLibrary.cs:34-42）。
        /// </summary>
        private static bool IsPreloading()
        {
            try { return ResourcesLibrary.Preloading; }
            catch (Exception) { return false; }
        }

        /// <summary>执行一轮图标赋值（可多次调用，幂等）。</summary>
        internal static void ApplyQueued(string phase)
        {
            if (s_Pending.Count == 0) return;

            if (IsPreloading())
            {
                Main.Log?.Log("[图标] " + phase + "：资源库仍在预载，推迟到预载结束后再加载");
                return;
            }

            s_Pass++;
            int before = s_Pending.Count;
            TryApplyAll(allowRetry: s_Pass > 1);
            Main.Log?.Log("[图标] " + phase + "：待处理 " + before + " 个，已生效 " + s_Applied +
                          " 个，剩余 " + s_Pending.Count + " 个");

            if (s_Pending.Count == 0) Report();
        }

        /// <summary>由 Main.OnUpdate 驱动的兜底重试（每 2 秒一次；预载期间不计时）。</summary>
        internal static void Tick()
        {
            if (s_Pending.Count == 0) return;
            if (IsPreloading()) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (s_Deadline <= 0f) s_Deadline = now + 120f;
            if (now < s_NextRetry) return;
            s_NextRetry = now + 2f;

            TryApplyAll(allowRetry: true);

            if (s_Pending.Count == 0) Report();
            else if (now >= s_Deadline) Report();
        }

        private static void TryApplyAll(bool allowRetry)
        {
            for (int i = s_Pending.Count - 1; i >= 0; i--)
            {
                var p = s_Pending[i];
                var sprite = GetSprite(p, allowRetry);
                if (sprite == null) continue;

                ReflectionFields.Set(p.Target, "m_Icon", sprite);
                s_Applied++;
                s_Pending.RemoveAt(i);
            }
        }

        /// <summary>
        /// 双路径取 Sprite：
        ///   路径 1 —— 按 Sprite 资源 GUID 从资源库直取；
        ///   路径 2 —— 回退到"原版蓝图的 .Icon"（蓝图 bundle 自带图标，野兔已验证）。
        /// </summary>
        private static Sprite GetSprite(PendingIcon p, bool allowRetry)
        {
            Sprite cached;
            if (s_Sprites.TryGetValue(p.SpriteGuid, out cached) && cached != null) return cached;

            // ---------- 路径 1 ----------
            try
            {
                var sprite = ResourcesLibrary.TryGetResource<Sprite>(p.SpriteGuid, true, true);

                // 资源库里已有条目但内容为空 ⇒ 预载没填上；删掉这个"空条目"再取一次。
                // 注意：**不能**用 TryUnloadResource —— 那会把已经被原版蓝图引用的 Sprite
                // 一起卸载，导致别的图标变白块。这里只删除空条目本身。
                if (sprite == null && ResourcesLibrary.IsLoaded(p.SpriteGuid))
                {
                    DropEmptyEntry(p.SpriteGuid);
                    sprite = ResourcesLibrary.TryGetResource<Sprite>(p.SpriteGuid, true, true);
                }

                if (sprite != null)
                {
                    s_Sprites[p.SpriteGuid] = sprite;
                    LogOnce(p.SpriteGuid, "[图标] 直取 Sprite 成功：" + p.Label +
                                          (allowRetry ? "（重试后）" : ""));
                    return sprite;
                }
            }
            catch (Exception e)
            {
                LogOnce(p.SpriteGuid + ":ex", "[图标] Sprite 直取异常 " + p.SpriteGuid + "：" + e.Message);
            }

            // ---------- 路径 2 ----------
            if (!string.IsNullOrEmpty(p.SourceBlueprintGuid))
            {
                try
                {
                    var bp = ResourcesLibrary.BlueprintsCache.Load(BlueprintGuid.Parse(p.SourceBlueprintGuid));
                    var fact = bp as BlueprintUnitFact;
                    if (fact != null && fact.Icon != null)
                    {
                        s_Sprites[p.SpriteGuid] = fact.Icon;
                        LogOnce(p.SpriteGuid, "[图标] 改用原版蓝图取图成功：" + p.Label +
                                              " ← " + p.SourceBlueprintGuid);
                        return fact.Icon;
                    }
                }
                catch (Exception e)
                {
                    LogOnce(p.SpriteGuid + ":bp", "[图标] 蓝图取图异常 " + p.SourceBlueprintGuid + "：" + e.Message);
                }
            }

            return null;
        }

        /// <summary>删掉资源库里"存在但内容为空"的条目（不动有效资源）。</summary>
        private static void DropEmptyEntry(string guid)
        {
            try
            {
                var dict = ResourcesLibrary.LoadedResources as System.Collections.IDictionary;
                if (dict == null) return;
                if (!dict.Contains(guid)) return;
                dict.Remove(guid);
                LogOnce(guid + ":drop", "[图标] 清理了空缓存条目：" + guid);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[图标] 清理空条目失败 " + guid + "：" + e.Message);
            }
        }

        private static void LogOnce(string key, string text)
        {
            if (s_Logged.Add(key)) Main.Log?.Log(text);
        }

        private static void Report()
        {
            if (s_Reported) return;
            s_Reported = true;

            if (s_Pending.Count == 0)
            {
                Main.Log?.Log("[图标] 完成：" + s_Applied + " 个图标全部生效");
                return;
            }

            Main.Log?.Error("[图标] 仍有 " + s_Pending.Count + " 个图标未解析（成功 " + s_Applied + " 个）");
            foreach (var p in s_Pending)
            {
                bool inBundle = false, loaded = false;
                try
                {
                    inBundle = ResourcesLibrary.IsAssetInBundle(p.SpriteGuid);
                    loaded = ResourcesLibrary.IsLoaded(p.SpriteGuid);
                }
                catch (Exception) { }

                Main.Log?.Error("[图标]   失败：" + p.Label + " ← " + p.SpriteGuid +
                                "（在bundle索引=" + inBundle + "，已缓存=" + loaded +
                                "，源蓝图=" + (p.SourceBlueprintGuid ?? "无") + "）");
            }
            s_Pending.Clear();
        }

        // ================= 特效 =================

        /// <summary>
        /// 设置"持续"特效。assetId 为空时写成 null（即不挂持续特效）。
        /// FxOnStart 为 null 是安全的（Buff.cs:465 对 null 有判空）。
        /// </summary>
        internal static void SetFxOnStart(BlueprintBuff buff, string assetId)
        {
            if (buff == null) return;
            try
            {
                PrefabLink link = string.IsNullOrEmpty(assetId) ? null : new PrefabLink { AssetId = assetId };
                ReflectionFields.Set(buff, "FxOnStart", link);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特效] 设置 FxOnStart 失败：" + e.Message);
            }
        }

        /// <summary>
        /// 设置"移除瞬间"特效。assetId 为空也必须给一个非 null 的 PrefabLink，
        /// 否则玩家棋子身上的 Buff 到期时会 NRE（Buff.cs:395-397）。
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

        /// <summary>检查一个特效预制体能否真的加载；返回是否成功（顺带预热到缓存里）。</summary>
        internal static bool VerifyFxPrefab(string assetId, string label)
        {
            if (string.IsNullOrEmpty(assetId)) return false;
            try
            {
                var go = ResourcesLibrary.TryGetResource<GameObject>(assetId, true, true);
                if (go != null)
                {
                    Main.Log?.Log("[特效] 预制体可加载：" + label + " ← " + assetId);
                    return true;
                }
                Main.Log?.Warning("[特效] 预制体加载不到：" + label + " ← " + assetId);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[特效] 预制体加载异常 " + label + "：" + e.Message);
            }
            return false;
        }

        // ================= 旧接口：从原版蓝图搬图标（仅野兔还在用） =================

        /// <summary>
        /// 把某个**原版蓝图**的图标复制到目标蓝图（来源必须是蓝图 GUID）。
        /// 目前只有野兔魔宠在用；其余走 QueueIcon。
        /// </summary>
        internal static void CopyIcon(BlueprintsCache cache, SimpleBlueprint target, string sourceBlueprintGuid)
        {
            if (cache == null || target == null || string.IsNullOrEmpty(sourceBlueprintGuid)) return;

            try
            {
                var src = cache.Load(BlueprintGuid.Parse(sourceBlueprintGuid)) as BlueprintUnitFact;
                if (src == null)
                {
                    Main.Log?.Warning("[图标] 来源蓝图不存在：" + sourceBlueprintGuid);
                    return;
                }

                Sprite icon = src.Icon;
                if (icon == null)
                {
                    Main.Log?.Warning("[图标] 来源蓝图无图标：" + sourceBlueprintGuid);
                    return;
                }

                ReflectionFields.Set(target, "m_Icon", icon);
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[图标] 复制失败：" + e.Message);
            }
        }
    }
}
