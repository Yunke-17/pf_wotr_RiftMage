using System;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.Sound;
using Kingmaker.UnitLogic.Mechanics.Actions;
using UnityEngine;

namespace WizardArchetype
{
    /// <summary>
    /// 播放一个 Wwise 事件（2026-10-21 新增）。
    ///
    /// ========== 为什么不用原版 ContextActionPlaySound ==========
    ///
    /// 原版那个（Kingmaker.Assets.UnitLogic.Mechanics.Actions.ContextActionPlaySound.cs:27-37）
    /// 直接写：
    ///     TargetWrapper t = base.Target ?? base.Context.MaybeCaster;
    ///     GameObject go = t.Unit.View.gameObject;      // ← Unit 为 null 时 NRE
    /// 对"点地"类目标（Target.Unit == null）会抛空引用。我们只需要"在施法者身上播"，
    /// 所以自己写一个，目标永远取施法者，并对 View 判空。
    ///
    /// 事件名来自 VFX.bnk（见 SoundbanksInfo.xml），例如：
    ///     Mythic1lvlAeon_AbsoluteFocus_Play      （Aeon「绝对专注」，2.7s，25m 衰减）
    ///     Mythic4lvlAeon_UncertanityPrinciple00_Spawn（1.8s）
    ///
    /// ⚠️ 事件名写错时 SoundEventsManager.PostEvent 不会报错，只是静默返回 0，
    ///    所以这里每次都打一条日志，方便在 Player.log 里核对"到底有没有播"。
    /// </summary>
    [TypeId("a1b2c3d4e5f6478990a1b2c3d4e5f660")]
    internal class PlaySoundAction : ContextAction
    {
        public string SoundName;

        public override string GetCaption()
        {
            return "PlaySound(" + SoundName + ")";
        }

        public override void RunAction()
        {
            try
            {
                if (string.IsNullOrEmpty(SoundName)) return;

                var unit = (Target != null ? Target.Unit : null);
                if (unit == null && Context != null) unit = Context.MaybeCaster;
                if (unit == null)
                {
                    Main.Log?.Warning("[音效] 找不到施法者，跳过：" + SoundName);
                    return;
                }

                var view = unit.View;
                if (view == null)
                {
                    Main.Log?.Warning("[音效] 施法者没有 View，跳过：" + SoundName);
                    return;
                }

                var go = view.gameObject;
                if (go.GetComponent<AkGameObj>() == null) go.AddComponent<AkGameObj>();

                // 返回 0 表示"事件不存在 / 对应 bank 未加载"，Wwise 不会报错，只能自己查。
                uint playingId = SoundEventsManager.PostEvent(SoundName, go, false);
                if (playingId == 0)
                {
                    Main.Log?.Warning("[音效] 事件未播放（名字写错或 bank 未加载）：" + SoundName);
                }
                else
                {
                    Main.Log?.Log("[音效] " + SoundName + "（id=" + playingId + "）");
                }
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[音效] 播放失败 " + SoundName + "：" + e.Message);
            }
        }
    }
}
