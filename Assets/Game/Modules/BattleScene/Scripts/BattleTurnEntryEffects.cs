using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    // A model's ready clip owns the timing. The world effect stays at its sampled landing point.
    internal sealed class BattleTurnEntryEffects : IDisposable
    {
        private const string DustCue = "dust";
        private const string DustAnimation = "animation";
        private const float DustSizeInBattleUnits = .0055f;
        private readonly BattlePreviewActor[] actors;
        private readonly ISkeletonPlayer dust;
        private readonly MeshRenderer renderer;

        internal BattleTurnEntryEffects(Transform units, IEnumerable<BattlePreviewActor> actors,
            BattleHudPreview.SpineEffect asset)
        {
            if (asset == null || !asset.skeleton || !asset.atlas || !asset.page) return;
            this.actors = actors.Where(actor => actor.GetBattleAnimationEventTime("b_idle_ready", DustCue) >= 0f).ToArray();
            if (this.actors.Length == 0) return;

            dust = new SkeletonPlayerFactory().Create(asset.CreateSource(DustSizeInBattleUnits), units);
            dust.Root.name = "Battle Turn Entry Dust";
            renderer = dust.Root.GetComponent<MeshRenderer>();
            dust.Play(DustAnimation, false);
            dust.Root.SetActive(false);
            foreach (var actor in this.actors) actor.TurnEntryCue += OnCue;
        }

        private void OnCue(BattlePreviewActor actor, SkeletonPlaybackEvent cue)
        {
            if (cue.Name != DustCue || !actor.TryGetPresentationAnchor("shadow", out var landing)) return;
            var root = dust.Root.transform;
            var sourceScale = actor.transform.lossyScale;
            var parentScale = root.parent.lossyScale;
            root.position = landing;
            root.localScale = new Vector3(actor.FacingDirection * sourceScale.x / parentScale.x,
                sourceScale.y / parentScale.y, 1f);
            renderer.sortingOrder = actor.PresentationDrawOrder - 1;
            dust.Root.SetActive(true);
            dust.Play(DustAnimation, false);
        }

        internal void Tick(float delta)
        {
            if (dust == null || !dust.Root.activeSelf) return;
            dust.Tick(delta);
            if (dust.Time >= dust.Duration) dust.Root.SetActive(false);
        }

        public void Dispose()
        {
            if (dust == null) return;
            foreach (var actor in actors) actor.TurnEntryCue -= OnCue;
            dust.Dispose();
        }
    }
}