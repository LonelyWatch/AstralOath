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
        private readonly Dictionary<(BattlePreviewActor, BattleFeedbackCue), BattleNativeFeedbackEffect> effects =
            new Dictionary<(BattlePreviewActor, BattleFeedbackCue), BattleNativeFeedbackEffect>();
        private bool disposed;

        internal BattleActionFeedback(BattleSkillPresentationAsset[] assets)
            => library = new BattleNativeFeedbackEffect.Library(assets);

        internal bool PresentAction(BattleFlowEvent item, BattlePreviewActor actor)
        {
            if (disposed || item == null || item.Kind != BattleFlowEventKind.Action) return false;
            if (!cues.TryTake(item.Sequence, item.Kind, item.Origin, item.UseSoulBurn, out var cue, out bool burn)) return false;
            Show(cue, actor);
            return burn;
        }

        internal void PresentExtraTurn(BattleFlowEvent item, BattlePreviewActor actor)
        {
            if (disposed || item == null || item.Kind != BattleFlowEventKind.ExtraTurnGranted) return;
            if (cues.TryTake(item.Sequence, item.Kind, item.Origin, false, out var cue, out _)) Show(cue, actor);
        }

        private void Show(BattleFeedbackCue cue, BattlePreviewActor actor)
        {
            if (cue == BattleFeedbackCue.None || !actor) return;
            // P2 08:05.2 and 08:06.9 show the chain lightning at counter-action onset.
            // stse_counter is a buff-add icon, not this action cue. Dual/extra attack
            // retain the brief chain reuse pending their own video occurrence.
            string source = BattleFeedbackCueTracker.SourceFor(cue);
            if (!library.Contains(source)) return; // Older isolated fixtures can omit feedback assets.
            var key = (actor, cue);
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
            Place(actor, effect);
        }

        internal void Tick(float delta)
        {
            if (disposed) return;
            foreach (var pair in effects)
            {
                if (!pair.Value.Active) continue;
                if (!pair.Key.Item1) { pair.Value.Stop(); continue; }
                Place(pair.Key.Item1, pair.Value);
                pair.Value.Tick(delta);
            }
        }

        private static void Place(BattlePreviewActor actor, BattleNativeFeedbackEffect effect)
        {
            var body = actor.PresentationRenderer;
            if (!body || !effect.Root) return;
            // P2 counters and ONE MORE TURN are centered on the body, not the
            // status/top bone. Native CFX offsets (including addturn y=-100) stay intact.
            effect.Root.transform.position = actor.Bounds.center;
            var parentScale = effect.Root.transform.parent.lossyScale;
            var bodyScale = body.transform.lossyScale;
            effect.Root.transform.localScale = new Vector3(Mathf.Abs(bodyScale.x) / Mathf.Max(.000001f, Mathf.Abs(parentScale.x)),
                Mathf.Abs(bodyScale.y) / Mathf.Max(.000001f, Mathf.Abs(parentScale.y)), 1f);
            effect.SetSorting(body.sortingLayerID, body.sortingOrder + 5);
            // Never queue stale flashes through a full cut-in; visual time still expires.
            effect.Root.SetActive(effect.Active && BattlePreviewActor.IsBodyVisible(body));
        }

        internal void Clear(ulong lastEventSequence = 0)
        {
            cues.Clear(lastEventSequence);
            foreach (var effect in effects.Values) effect.Stop();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var effect in effects.Values) effect.Dispose();
            effects.Clear();
        }
    }
}
