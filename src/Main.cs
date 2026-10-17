using System;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using UnityModManagerNet;

namespace WizardArchetype
{
    public static class Main
    {
        internal static UnityModManager.ModEntry.ModLogger Log;

        public static bool Enabled { get; private set; }

        /// <summary>超魔越界判断修复（补丁 E），全局生效，默认开启。</summary>
        public static bool FixMetamagicLimit = true;

        /// <summary>打印瞬发减费的计算细节，用于排查为何未生效。</summary>
        public static bool DebugLog = true;

        /// <summary>
        /// 智力先攻是否启用"封顶到法师职业等级"。
        /// true  = PropertyCalculator.Min(IntMod, 法师职业等级)，精确但依赖反射
        /// false = DerivativeStatBonus（原版剑圣路径），零反射但无封顶
        /// 回退时改这一处即可。
        ///
        /// 封顶基准 = 法师职业等级（不是角色总等级）。2026-10-18 用户确认以代码为准，
        /// 忽略机制说明书 §5.1「C 为角色总等级」的表述。
        /// </summary>
        public static bool UseCappedInitiative = true;

        /// <summary>本 MOD 的法师变体 GUID，蓝图构建时写入。</summary>
        public static BlueprintGuid WizardArchetypeGuid = BlueprintGuid.Empty;

        internal static void SetArchetypeGuid(BlueprintGuid guid)
        {
            WizardArchetypeGuid = guid;
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Log = modEntry.Logger;

            try
            {
                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(typeof(Main).Assembly);

                SelfCheck.Run();

                Enabled = true;
                Log.Log("WizardArchetype loaded.");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Failed to load: " + e);
                return false;
            }
        }

        /// <summary>
        /// 判断某单位是否为本 MOD 的法师变体。
        /// 变体 GUID 在蓝图注册后才有效，此外恒为 false，减费补丁保持惰性。
        /// </summary>
        public static bool IsWizardArchetype(UnitEntityData unit)
        {
            if (unit == null) return false;
            if (WizardArchetypeGuid == BlueprintGuid.Empty) return false;

            var classes = unit.Descriptor?.Progression?.Classes;
            if (classes == null) return false;

            foreach (var cls in classes)
            {
                if (cls?.Archetypes == null) continue;
                foreach (var a in cls.Archetypes)
                {
                    if (a != null && a.AssetGuid == WizardArchetypeGuid) return true;
                }
            }
            return false;
        }
    }
}
