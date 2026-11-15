using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleOnlineArrival : IDisposable
    {
        private const string Animation = "animation";
        private const string EffectResources = "AstralOathBattleArrival/";
        private const float WideScale = 1.11f;
        private const float LeadSeconds = .06f;
        private const float RunSeconds = .64f;
        private const float StaggerSeconds = .105f;
        private const float CameraSeconds = .43f;
        private readonly BattlePreviewViewport viewport;
        private readonly Slot[] allies, enemies;
        private float time, cameraTime;
        private bool prepared, begun, disposed;

        private sealed class Slot
        {
            internal BattlePreviewActor Actor;
            internal Vector3 Origin, ShadowPosition;
            internal Transform Shadow;
            internal ISkeletonPlayer Effect;
            internal float RunStartX;
            internal bool Arrived;
        }

        internal bool IgnitionReady => begun && time >= .55f;
        internal bool ActorsReady => begun && enemies.All(slot => slot.Arrived);
        internal bool CameraBlending => ActorsReady && !Complete;
        internal bool Complete => ActorsReady && cameraTime >= CameraSeconds;
        internal bool EffectsComplete => Complete && allies.Concat(enemies).All(slot => slot.Effect?.Root.activeSelf != true);

        internal BattleOnlineArrival(Transform parent, BattlePreviewViewport viewport, IReadOnlyList<BattlePreviewActor> allies,
            IReadOnlyList<BattlePreviewActor> enemies)
        {
            this.viewport = viewport;
            var warning = LoadEffect("encounter_01", .0037f);
            var dust = LoadEffect("dust_landing", .0042f);
            var factory = new SkeletonPlayerFactory();
            var built = new List<Slot>();
            try
            {
                Slot Create(BattlePreviewActor actor, bool isAlly)
                {
                    var shadow = actor.PresentationShadow;
                    var slot = new Slot { Actor = actor, Origin = actor.transform.position, Shadow = shadow,
                        ShadowPosition = shadow ? shadow.position : actor.transform.position };
                    built.Add(slot);
                    var source = isAlly ? warning : dust;
                    slot.Effect = factory.Create(source, parent);
                    slot.Effect.Root.name = isAlly ? "PvP Arrival Warning " + actor.HeroId : "Battle Arrival Dust";
                    var renderer = slot.Effect.Root.GetComponent<MeshRenderer>();
                    renderer.sortingOrder = isAlly ? actor.PresentationDrawOrder + 100 : actor.PresentationDrawOrder - 1;
                    slot.Effect.Root.transform.position = slot.ShadowPosition;
                    if (!isAlly)
                    {
                        var scale = actor.transform.lossyScale;
                        slot.Effect.Root.transform.localScale = new Vector3(actor.FacingDirection * scale.x, scale.y, 1f);
                    }
                    slot.Effect.Root.SetActive(false);
                    return slot;
                }

                this.allies = allies.Select(actor => Create(actor, true)).ToArray();
                this.enemies = enemies.Select(actor => Create(actor, false)).ToArray();
            }
            catch
            {
                foreach (var slot in built) slot.Effect?.Dispose();
                throw;
            }
        }

        private static SkeletonPlayerSource LoadEffect(string name, float scale)
        {
            string resource = EffectResources + name;
            var skeleton = Resources.Load<TextAsset>(resource + ".spine");
            var atlas = Resources.Load<TextAsset>(resource + ".atlas");
            var texture = Resources.Load<Texture2D>(resource);
            if (!skeleton || !atlas || !texture)
                throw new InvalidOperationException("The online battle arrival effect is incomplete: " + name);
            return new SkeletonPlayerSource(skeleton.text, atlas.text,
                new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { name + ".sct", texture } },
                premultipliedAlpha: true, scale: scale);
        }

        internal void Prepare()
        {
            if (prepared) return;
            prepared = true;
            viewport.BeginOnlineEntry(WideScale);
            foreach (var slot in enemies)
            {
                slot.RunStartX = viewport.OnlineEntryRightEdge(slot.Origin) + Mathf.Max(1.4f, slot.Actor.Bounds.size.x);
                slot.Actor.BeginArrivalRun();
                Position(slot, 0f);
            }
        }

        internal void Begin()
        {
            if (begun) return;
            begun = true;
            foreach (var slot in allies)
            {
                PlaceWarning(slot);
                PlayEffect(slot);
            }
        }

        internal void Tick(float delta)
        {
            if (!begun || disposed) return;
            delta = Mathf.Max(0f, delta);
            time += delta;
            bool actorsWereReady = ActorsReady;
            for (int index = 0; index < enemies.Length; index++)
            {
                var slot = enemies[index];
                if (!slot.Arrived)
                {
                    float elapsed = time - LeadSeconds - index * StaggerSeconds;
                    if (elapsed < 0f) continue;
                    float progress = Mathf.Clamp01(elapsed / RunSeconds);
                    Position(slot, progress);
                    if (progress >= 1f)
                    {
                        slot.Arrived = true;
                        slot.Actor.EndArrivalPose();
                        PlayEffect(slot);
                    }
                    else
                        slot.Actor.AdvancePresentation(Mathf.Min(delta, elapsed));
                }
                TickEffect(slot, delta);
            }
            foreach (var slot in allies)
            {
                if (slot.Effect.Root.activeSelf) PlaceWarning(slot);
                TickEffect(slot, delta);
            }

            if (!actorsWereReady || Complete) return;
            cameraTime = Mathf.Min(cameraTime + delta, CameraSeconds);
            viewport.SetOnlineEntryScale(Mathf.SmoothStep(WideScale, 1f, cameraTime / CameraSeconds));
        }

        private static void Position(Slot slot, float progress)
        {
            float eased = Mathf.Lerp(progress, Mathf.SmoothStep(0f, 1f, progress), .26f);
            float offset = (slot.RunStartX - slot.Origin.x) * (1f - eased);
            var movement = Vector3.right * offset;
            slot.Actor.transform.position = slot.Origin + movement;
            if (slot.Shadow) slot.Shadow.position = slot.ShadowPosition + movement;
        }

        private static void PlaceWarning(Slot slot)
        {
            if (!slot.Actor.TryGetPresentationAnchor("top", out var anchor))
                anchor = new Vector3(slot.Origin.x, slot.Actor.Bounds.max.y, slot.Origin.z);
            slot.Effect.Root.transform.position = anchor + new Vector3(.57f, .13f);
        }

        private static void PlayEffect(Slot slot)
        {
            if (slot.Effect == null) return;
            slot.Effect.Root.SetActive(true);
            slot.Effect.Play(Animation, false);
        }

        private static void TickEffect(Slot slot, float delta)
        {
            if (slot.Effect?.Root.activeSelf != true) return;
            slot.Effect.Tick(delta);
            if (slot.Effect.Time >= slot.Effect.Duration) slot.Effect.Root.SetActive(false);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (prepared && viewport) viewport.SetOnlineEntryScale(1f);
            foreach (var slot in enemies)
            {
                if (prepared && !slot.Arrived && slot.Actor)
                {
                    Position(slot, 1f);
                    slot.Actor.EndArrivalPose();
                }
                slot.Effect?.Dispose();
            }
            foreach (var slot in allies) slot.Effect.Dispose();
        }
    }
}
