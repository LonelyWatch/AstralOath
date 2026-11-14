using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleOnlineArrival : IDisposable
    {
        private const float DropHeight = 5.8f;
        private const float DropSeconds = .48f;
        private const float StaggerSeconds = .035f;
        private readonly Slot[] slots;
        private float time;
        private bool prepared, begun, disposed;

        private sealed class Slot
        {
            internal BattlePreviewActor Actor;
            internal Vector3 Origin, ShadowPosition;
            internal Transform Shadow;
            internal ISkeletonPlayer Dust;
            internal bool Landed;
        }

        internal bool Complete => begun && slots.All(slot => slot.Landed);

        internal BattleOnlineArrival(Transform parent, IEnumerable<BattlePreviewActor> actors,
            BattleHudPreview.SpineEffect dustAsset)
        {
            var source = dustAsset != null && dustAsset.skeleton && dustAsset.atlas && dustAsset.page ? dustAsset.CreateSource(.0055f) : null;
            var factory = new SkeletonPlayerFactory();
            var built = new List<Slot>();
            try
            {
                foreach (var actor in actors)
                {
                    var shadow = actor.PresentationShadow;
                    var slot = new Slot { Actor = actor, Origin = actor.transform.position,
                        Shadow = shadow, ShadowPosition = shadow ? shadow.position : actor.transform.position };
                    built.Add(slot);
                    if (source == null) continue;
                    slot.Dust = factory.Create(source, parent);
                    slot.Dust.Root.name = "Battle Arrival Dust";
                    slot.Dust.Root.transform.position = slot.ShadowPosition;
                    var scale = actor.transform.lossyScale;
                    slot.Dust.Root.transform.localScale = new Vector3(actor.FacingDirection * scale.x, scale.y, 1f);
                    slot.Dust.Root.GetComponent<MeshRenderer>().sortingOrder = actor.PresentationDrawOrder - 1;
                    slot.Dust.Root.SetActive(false);
                }
            }
            catch
            {
                foreach (var slot in built) slot.Dust?.Dispose();
                throw;
            }
            slots = built.ToArray();
        }

        internal void HoldAboveStage()
        {
            if (prepared) return;
            prepared = true;
            foreach (var slot in slots)
            {
                slot.Actor.BeginArrivalPose();
                Position(slot, 0f);
            }
        }

        internal void Begin() => begun = true;

        internal void Tick(float delta)
        {
            if (!begun) return;
            time += Mathf.Max(0f, delta);
            for (int index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                if (!slot.Landed)
                {
                    // Corresponding fronts, lowers, uppers and rears land on the same beat on either screen.
                    float progress = Mathf.Clamp01((time - index % 4 * StaggerSeconds) / DropSeconds);
                    Position(slot, progress);
                    if (progress >= 1f)
                    {
                        slot.Landed = true;
                        slot.Actor.EndArrivalPose();
                        if (slot.Dust != null)
                        {
                            slot.Dust.Root.SetActive(true);
                            slot.Dust.Play("animation", false);
                        }
                    }
                }
                if (slot.Dust?.Root.activeSelf == true)
                {
                    slot.Dust.Tick(delta);
                    if (slot.Dust.Time >= slot.Dust.Duration) slot.Dust.Root.SetActive(false);
                }
            }
        }

        internal bool EffectsComplete => Complete && slots.All(slot => slot.Dust?.Root.activeSelf != true);

        private static void Position(Slot slot, float progress)
        {
            slot.Actor.transform.position = slot.Origin + Vector3.up * (DropHeight * (1f - progress * progress));
            if (slot.Shadow) slot.Shadow.position = slot.ShadowPosition;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var slot in slots)
            {
                if (prepared && !slot.Landed && slot.Actor)
                {
                    Position(slot, 1f);
                    slot.Actor.EndArrivalPose();
                }
                slot.Dust?.Dispose();
            }
        }
    }
}
