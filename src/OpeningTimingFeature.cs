using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Localization;

namespace WizardArchetype
{
    /// <summary>
    /// 「开场时序」—— 战斗首轮状态的**说明特性**（用户 2026-10-20 新增需求）。
    ///
    /// ========== 为什么单独做一个特性 ==========
    ///
    /// 之前 SpecialOpeningState 这个组件是挂在能力X「空隙湮灭」的特性上的，
    /// 导致「按先攻决定首轮陷入时序专注 / 时序恍惚」这条规则
    /// 只能从空隙湮灭的说明里间接看到，职业特性列表里没有任何交代。
    ///
    /// 现在拆成独立特性：
    ///   · 特性目录里有一条明确的「开场时序」，描述完整的首轮规则；
    ///   · SpecialOpeningState 组件也迁到这里（见 Migration 注释）；
    ///   · 能力X 只保留和湮灭本身相关的内容。
    ///
    /// ========== 为什么是纯说明特性 ==========
    ///
    /// 规则的实际执行在 SpecialOpeningState + OpeningStateDecider 里
    /// （延迟到先攻结算完再二选一），不是蓝图组件能表达的。
    /// 本特性只负责「让玩家在职业面板里看到这条规则」。
    /// </summary>
    internal static class OpeningTimingFeature
    {
        internal const string GuidFeature = "b1000000000000000000000000000050";

        private const string KeyName = "WizardArchetype.OpeningTiming.Name";
        private const string KeyDesc = "WizardArchetype.OpeningTiming.Desc";

        internal static BlueprintFeature Feature { get; private set; }

        internal static BlueprintFeature Create(BlueprintsCache cache)
        {
            if (Feature != null) return Feature;

            CreateText();

            // 规则执行者：加入战斗时登记，先攻结算完后二选一施加 1 轮状态。
            // ★ 2026-10-20 从这里开始由「开场时序」特性承载
            //   （此前挂在能力X「空隙湮灭」的特性上）。
            //
            //   组件 name 必须稳定：EntityFact 以 SourceBlueprintComponentName 为键，
            //   读档时按名字反查。换了名字会让旧存档里这条组件的状态对不上，
            //   所以即使从别的特性迁过来也沿用原来的名字。
            var openingState = new SpecialOpeningState
            {
                name = "$SpecialOpeningState$AbilityX_5",
            };

            var feature = new BlueprintFeature
            {
                name = "WizardArchetype_OpeningTiming",
                AssetGuid = BlueprintGuid.Parse(GuidFeature),
                Ranks = 1,
                IsClassFeature = true,
                HideInUI = false,

                ComponentsArray = new BlueprintComponent[] { openingState },
            };

            ReflectionFields.Set(feature, "m_DisplayName", new LocalizedString { Key = KeyName });
            ReflectionFields.Set(feature, "m_Description", new LocalizedString { Key = KeyDesc });

            // 组件必须回填 OwnerBlueprint（见 WizardArchetypeFeatures.NewFeature 的说明）
            openingState.OwnerBlueprint = feature;

            cache.AddCachedBlueprint(BlueprintGuid.Parse(GuidFeature), feature);
            Feature = feature;

            Main.Log?.Log("WizardArchetype: 已创建「开场时序」特性（含 SpecialOpeningState 组件）");
            return feature;
        }

        private static void CreateText()
        {
            WizardArchetypeBlueprints.RegisterText(KeyName, "开场时序");

            // 与 SpecialOpeningState 的实际行为逐条对应：
            //   我方先攻 >= 敌方最高 → 时序专注（专注检定压力）
            //   我方先攻 <  敌方最高 → 时序恍惚（行动受限）
            // 两者都是 1 轮，都不可被行动自如免疫。
            //
            // ★ 2026-10-21（用户要求）：时序专注 / 时序恍惚**不再做悬停术语**，
            //   改为在这里直接写清效果。原因：这两个是状态名而非可查阅的百科概念，
            //   做成粉色悬停反而让文案变碎；直接把规则写进正文更清楚。
            WizardArchetypeBlueprints.RegisterText(KeyDesc,
                "空隙法师在战斗打响的瞬间永远慢现实半拍。" +
                "进入战斗时，他会依据双方" + Glossary.Link(Glossary.KInitiative, "先攻") +
                "的相对快慢，在首轮陷入以下两种状态之一，持续 1 轮：\n" +
                "若其先攻不低于所有敌人的最高先攻，他会陷入时序专注——" +
                "施放法师法术时，必须先通过一次专注检定，" +
                "其 DC 等于 10 + ⌈职业等级 / 2⌉ + 法术环位 × 2；" +
                "检定成功则法术正常施放、该状态立即解除，" +
                "失败则法术失效，并受到 1d2 点体质伤害（同一轮内最多结算一次）；" +
                "若其体质降至 3 以下，则在此期间无法继续施放任何法师法术。\n" +
                "若其先攻低于任一敌人，他会陷入时序恍惚——" +
                "其效果如同恍惚：每轮只能进行一个移动动作或一个标准动作，" +
                "不能进行整轮动作，但可以正常使用自由动作与迅捷动作。\n" +
                // 依据：UnitEntityData.CalculateSpeedModifier（:2529）对 Staggered 有 *= 0.5f，
                // 所以时序恍惚（挂的就是 Staggered）会减半移速；时序专注不挂任何条件，不减移速。
                "时序恍惚会使移动速度减半，时序专注则不影响移动速度；" +
                "两者都无法被《行动自如》或类似效果预防与解除。");
        }
    }
}
