using System;
using System.Collections.Generic;
using AstralOath.Shared.Battle;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    // Generic action acknowledgements live outside skill stages and their successor handoffs.
    internal sealed class BattleActionFeedback : IDisposable
    {
        private readonly BattleFeedbackCueTracker cues = new BattleFeedbackCueTracker();
        private readonly BattleNativeFeedbackEffect.Library library;
        private readonly AudioSource chainSound;
        private readonly Dictionary<(BattlePreviewActor Actor, string Source, string Bone), BattleNativeFeedbackEffect> effects =
            new Dictionary<(BattlePreviewActor, string, string), BattleNativeFeedbackEffect>();
        private bool disposed;

        internal BattleActionFeedback(BattleSkillPresentationAsset[] assets, Transform soundHost, AudioClip chainFlash)
        {
            library = new BattleNativeFeedbackEffect.Library(assets);
            if (chainFlash && soundHost)
            {
                chainSound = soundHost.gameObject.AddComponent<AudioSource>();
                chainSound.playOnAwake = false;
                chainSound.clip = chainFlash;
            }
        }

        internal bool PresentAction(BattleFlowEvent item, BattlePreviewActor actor)
        {
            if (disposed || item == null || item.Kind != BattleFlowEventKind.Action) return false;
            if (!cues.TryTake(item.Sequence, item.Kind, item.Origin, item.UseSoulBurn, out var cue, out bool burn)) return false;
            Show(BattleFeedbackCueTracker.SourceFor(cue), actor);
            return burn;
        }

        internal void PresentExtraTurn(BattleFlowEvent item, BattlePreviewActor actor)
        {
            if (disposed || item == null || item.Kind != BattleFlowEventKind.ExtraTurnGranted) return;
            if (cues.TryTake(item.Sequence, item.Kind, item.Origin, false, out var cue, out _))
                Show(BattleFeedbackCueTracker.SourceFor(cue), actor);
        }

        internal void PresentHealing(BattleFlowEvent item, BattlePreviewActor recipient)
        {
            if (!disposed && recipient && item?.Kind == BattleFlowEventKind.Healing && item.Amount > 0 && cues.TryMark(item.Sequence))
                Show(BattleNativeFeedbackEffect.HealingSource, recipient, "target");
        }

        internal void PresentStatusGain(BattleFlowEvent item, BattlePreviewActor recipient)
        {
            if (disposed || !recipient || item?.Kind != BattleFlowEventKind.StatusApplied || item.Status == null) return;
            // cs_player.cs_effect and cs_eff_bone describe the gain animation separately from the HUD icon.
            (string source, string bone) = item.Status.Kind switch
            {
                BattleStatusKind.AttackUp or BattleStatusKind.GreaterAttackUp => ("stse_attackpower_up", "top"),
                BattleStatusKind.DefenseUp or BattleStatusKind.CriticalResistanceUp or BattleStatusKind.EffectivenessUp
                    or BattleStatusKind.SkillNullifier => ("stse_defense_up", "top"),
                BattleStatusKind.SpeedUp or BattleStatusKind.GreaterSpeedUp => ("stse_speed_up", "top"),
                BattleStatusKind.Immunity => ("stse_immune_weak", "top"),
                BattleStatusKind.Counter => ("stse_counter", "top"),
                BattleStatusKind.EvasionUp => ("stse_evade", "top"),
                BattleStatusKind.Immortality => ("stse_immortality", "top"),
                BattleStatusKind.Reflect => ("stse_defense_reflect", "top"),
                BattleStatusKind.CriticalChanceUp => ("stse_critup", "shadow"),
                BattleStatusKind.ContinuousHealing => (BattleNativeFeedbackEffect.HealingSource, "target"),
                _ => (null, null)
            };
            if (source != null && cues.TryMark(item.Sequence)) Show(source, recipient, bone);
        }

        private void Show(string source, BattlePreviewActor actor, string bone = null)
        {
            if (source == null || !actor) return;
            // P2 08:05.2 and 08:06.9 show the chain lightning at counter-action onset.
            // stse_counter is a buff-add icon, not this action cue. Dual/extra attack
            // retain the brief chain reuse pending their own video occurrence.
            if (!library.Contains(source)) return; // Older isolated fixtures can omit feedback assets.
            var key = (actor, source, bone);
            if (!effects.TryGetValue(key, out var effect))
            {
                var renderer = actor.PresentationRenderer;
                // P2 counter rays span the battle viewport; the earlier .003
                // reduction produced only a 1.5-character-height ring.
                const float pixelScale = .01f;
                effect = new BattleNativeFeedbackEffect(library, source, actor.transform, pixelScale,
                    renderer ? renderer.sortingLayerID : 0, renderer ? renderer.sortingOrder + 5 : 60);
                effects.Add(key, effect);
            }
            effect.Play();
            if (source == "stse_chain" && chainSound) chainSound.PlayOneShot(chainSound.clip);
            Place(actor, effect, bone);
        }

        internal void Tick(float delta)
        {
            if (disposed) return;
            foreach (var pair in effects)
            {
                if (!pair.Value.Active) continue;
                if (!pair.Key.Actor) { pair.Value.Stop(); continue; }
                Place(pair.Key.Actor, pair.Value, pair.Key.Bone);
                pair.Value.Tick(delta);
            }
        }

        private static void Place(BattlePreviewActor actor, BattleNativeFeedbackEffect effect, string bone)
        {
            var body = actor.PresentationRenderer;
            if (!body || !effect.Root) return;
            // P2 counters and ONE MORE TURN are centered on the body, not the
            // status/top bone. Native CFX offsets (including addturn y=-100) stay intact.
            effect.Root.transform.position = bone != null && actor.TryGetPresentationAnchor(bone, out var anchor)
                ? anchor : actor.Bounds.center;
            var parentScale = effect.Root.transform.parent.lossyScale;
            var bodyScale = body.transform.lossyScale;
            effect.Root.transform.localScale = new Vector3(Mathf.Abs(bodyScale.x) / Mathf.Max(.000001f, Mathf.Abs(parentScale.x)),
                Mathf.Abs(bodyScale.y) / Mathf.Max(.000001f, Mathf.Abs(parentScale.y)), 1f);
            effect.SetActorSorting(body);
            // Never queue stale flashes through a full cut-in; visual time still expires.
            effect.Root.SetActive(effect.Active && BattlePreviewActor.IsBodyVisible(body));
        }

        internal void Clear(ulong lastEventSequence = 0)
        {
            cues.Clear(lastEventSequence);
            if (chainSound) chainSound.Stop();
            foreach (var effect in effects.Values) effect.Stop();
        }

        internal void SetPaused(bool paused)
        {
            if (!chainSound) return;
            if (paused) chainSound.Pause();
            else chainSound.UnPause();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (chainSound)
            {
                chainSound.Stop();
                UnityEngine.Object.Destroy(chainSound);
            }
            foreach (var effect in effects.Values) effect.Dispose();
            effects.Clear();
        }
    }
}
