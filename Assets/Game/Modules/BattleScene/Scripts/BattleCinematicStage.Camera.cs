using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AstralOath.Client.BattleScene
{
    public sealed partial class BattleCinematicStage
    {
        private sealed class HandoffState
        {
            internal Vector3 Position;
            internal float Size;
            internal Color Clear;
            internal bool Background;
            internal SpriteRenderer Backdrop;
            internal Vector3 BackdropPosition, BackdropScale;
            internal Bounds BackdropBounds, MapBounds;
            internal bool HasMapBounds;
        }
        private HandoffState pendingHandoff;

        // Transfer ownership once. The retiring stage may still receive source cues, but no longer
        // has authority to move the camera, show overlays or restore the incoming action's state.
        public void HandoffTo(BattleCinematicStage next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (next == this || next.camera != camera || next.background != background)
                throw new ArgumentException("Camera handoff requires another stage on the same scene.", nameof(next));
            if (next.running || next.pendingHandoff != null)
                throw new InvalidOperationException("The receiving stage already owns a presentation.");
            if (!running || !camera) return;
            var inherited = new HandoffState
            {
                Position = originalPosition, Size = originalSize, Clear = originalClear,
                Background = originalBackground, Backdrop = farBackdrop,
                BackdropPosition = backdropPosition, BackdropScale = backdropScale,
                BackdropBounds = backdropBounds, MapBounds = mapBounds, HasMapBounds = hasMapBounds
            };
            Vector3 position = camera.transform.position;
            float size = camera.orthographicSize;
            Color clear = camera.backgroundColor;
            bool visible = background && background.activeSelf;
            var backdrop = farBackdrop;
            Vector3 displayedPosition = backdrop ? backdrop.transform.position : Vector3.zero;
            Vector3 displayedScale = backdrop ? backdrop.transform.localScale : Vector3.one;
            Reset();
            camera.transform.position = position;
            camera.orthographicSize = size;
            camera.backgroundColor = clear;
            if (background) background.SetActive(visible);
            if (backdrop)
            {
                backdrop.transform.position = displayedPosition;
                backdrop.transform.localScale = displayedScale;
            }
            next.pendingHandoff = inherited;
        }

        private void AdoptBackdrop(HandoffState inherited)
        {
            farBackdrop = inherited.Backdrop;
            backdropPosition = inherited.BackdropPosition;
            backdropScale = inherited.BackdropScale;
            backdropBounds = inherited.BackdropBounds;
            mapBounds = inherited.MapBounds;
            hasMapBounds = inherited.HasMapBounds;
        }

        // Project fallback for actions without a source CAM/ZOOM return. Source cuts stay explicit.
        public void ReturnToBaseline(float seconds)
        {
            if (!running || !camera) return;
            float duration = Mathf.Max(0f, seconds);
            hasFocus = false;
            pan = (camera.transform.position - shakeOffset, 0, Vector3.zero, 0f, time, duration, "0.42,0,0.58,1");
            zoom = (camera.orthographicSize, originalSize, time, duration, "0.42,0,0.58,1");
            if (duration <= 0f) Tick(0f);
        }

        private sealed class CameraShake
        {
            internal ISkeletonPlayer Player;
            internal float Until;
            internal float Speed;
            internal float Facing;
        }
        private readonly List<CameraShake> cameraShakes = new List<CameraShake>();
        private Vector3 shakeOffset;
        private SpriteRenderer farBackdrop;
        private Vector3 backdropPosition;
        private Vector3 backdropScale;
        private Bounds backdropBounds;
        private BattlePreviewViewport viewport;
        private int viewportRevision;
        private float viewportBaseline;

        private void CaptureViewport()
        {
            viewport = camera.GetComponent<BattlePreviewViewport>();
            viewportRevision = viewport ? viewport.LayoutRevision : 0;
            viewportBaseline = viewport ? viewport.BaselineSize : 0f;
        }

        private void UpdateViewport()
        {
            if (!viewport || viewportRevision == viewport.LayoutRevision || viewportBaseline <= 0) return;
            float factor = viewport.BaselineSize / viewportBaseline;
            originalSize *= factor;
            requestedSize *= factor;
            if (zoom is { } scale)
                zoom = (scale.From * factor, scale.To * factor, scale.Start, scale.Duration, scale.Curve);
            CaptureViewport();
            viewport.ResizeBackdrop();
            CaptureBackdrop();
        }

        private Vector3 Framed(Vector3 anchor, float groundOffset) =>
            anchor + Vector3.up * (groundOffset * camera.orthographicSize / originalSize);

        private Vector3 FocusPoint(int location, Vector3 offset)
        {
            var point = Anchor(location) + offset;
            point.z = originalPosition.z;
            return point;
        }
        internal float SourceScreenScale => camera && originalSize > 0f ? camera.orthographicSize / originalSize : 1f;

        internal Vector3 SourceScreenPoint(Vector3 depthReference, Vector3 offset)
        {
            float depth = camera.WorldToViewportPoint(depthReference).z;
            Vector3 center = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, depth));
            // Keep the source camera shake visible in this coordinate space too.
            return center - shakeOffset + camera.transform.rotation * (offset * SourceScreenScale);
        }

        private float EffectiveSize()
        {
            if (!background || !background.activeSelf || !hasMapBounds) return requestedSize;
            return Mathf.Max(.01f, Mathf.Min(requestedSize, mapBounds.size.y * .5f, mapBounds.size.x / (2f * camera.aspect)));
        }
        private float GroundOffset(int location)
        {
            if (location == 0) return 0f;
            float Offset(Transform item) => item && groundOffsets.TryGetValue(item, out float offset) ? offset : 0f;
            if (location == 1 || location == 11) return Offset(caster);
            var live = targets.Where(item => item).ToArray();
            if (live.Length == 0) return Offset(caster);
            if (location == 31) return (Offset(caster) + Offset(live[0])) * .5f;
            if (location == 32 || location == 41 || location == 42) return live.Average(Offset);
            return Offset(live[0]);
        }

        private void CaptureBackdrop()
        {
            hasMapBounds = false;
            farBackdrop = null;
            if (!background) return;
            var layers = background.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(renderer => renderer.enabled && renderer.sprite).ToArray();
            foreach (var renderer in layers)
            {
                if (!hasMapBounds) { mapBounds = renderer.bounds; hasMapBounds = true; }
                else mapBounds.Encapsulate(renderer.bounds);
            }
            if (!hasMapBounds) return;
            // The authored initial frame is the accepted baseline, including the lower HUD-covered band.
            mapBounds.Encapsulate(originalPosition + new Vector3(originalSize * camera.aspect, originalSize, 10f));
            mapBounds.Encapsulate(originalPosition + new Vector3(-originalSize * camera.aspect, -originalSize, 10f));
            // Use a single continuous wide layer for horizontal coverage, not a union of sparse tree crowns.
            var wide = layers.OrderByDescending(renderer => renderer.bounds.size.x).First().bounds;
            mapBounds.SetMinMax(new Vector3(Mathf.Min(wide.min.x, originalPosition.x - originalSize * camera.aspect), mapBounds.min.y, mapBounds.min.z),
                new Vector3(Mathf.Max(wide.max.x, originalPosition.x + originalSize * camera.aspect), mapBounds.max.y, mapBounds.max.z));
            if (layers.Length <= 1) return;
            farBackdrop = layers.OrderBy(renderer => renderer.sortingOrder).First();
            backdropPosition = farBackdrop.transform.position;
            backdropScale = farBackdrop.transform.localScale;
            backdropBounds = farBackdrop.bounds;
        }

        private void ApplyCameraPose()
        {
            if (!running || !camera) return;
            camera.orthographicSize = EffectiveSize();
            if (hasFocus && !pan.HasValue)
                basePosition = Framed(FocusPoint(activeFocusLocation, activeFocusOffset), activeGroundOffset);
            Vector3 requested = basePosition + shakeOffset;
            if (background && background.activeSelf && hasMapBounds)
            {
                float x = camera.orthographicSize * camera.aspect;
                float y = camera.orthographicSize;
                requested.x = Mathf.Clamp(requested.x, mapBounds.min.x + x, mapBounds.max.x - x);
                requested.y = Mathf.Clamp(requested.y, mapBounds.min.y + y, mapBounds.max.y - y);
                // A distant, non-interactive background covers lateral pans; foreground keeps its world coordinates.
                if (farBackdrop)
                {
                    float factor = Mathf.Max(1f, x * 2.02f / backdropBounds.size.x,
                        (requested.y + y - backdropBounds.min.y) / backdropBounds.size.y);
                    farBackdrop.transform.localScale = backdropScale * factor;
                    Vector3 shift = new Vector3(requested.x - backdropBounds.center.x,
                        backdropBounds.size.y * (factor - 1f) * .5f, 0f);
                    farBackdrop.transform.position = backdropPosition + shift;
                }
            }
            camera.transform.position = requested;
        }

        public void PlayShake(JObject node, SkeletonPlayerSource source)
        {
            if (!running || !camera || source == null) return;
            float speed = Number(node, "timeScale", 1f);
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(node), "SHAKE timeScale must be positive.");
            var player = new SkeletonPlayerFactory().Create(source, camera.transform);
            try
            {
                string animation = player.Animations.FirstOrDefault(item => item.Name == "animation")?.Name ?? player.Animations.FirstOrDefault()?.Name;
                if (animation == null) throw new InvalidOperationException("Camera skeleton has no animation.");
                player.Play(animation, false);
                if (!player.TryGetBonePose("camera", out _)) throw new InvalidOperationException("Camera skeleton has no camera bone.");
                float duration = Number(node, "duration", -1f);
                float facing = caster && caster.TryGetComponent<BattlePreviewActor>(out var actor) ? actor.FacingDirection : 1f;
                cameraShakes.Add(new CameraShake { Player = player, Speed = speed, Facing = facing,
                    Until = time + (duration < 0f ? player.Duration / speed : duration / 1000f) });
            }
            catch { player.Dispose(); throw; }
        }

        private void TickShakes(float delta)
        {
            shakeOffset = Vector3.zero;
            foreach (var shake in cameraShakes.ToArray())
            {
                if (time >= shake.Until) { shake.Player.Dispose(); cameraShakes.Remove(shake); continue; }
                shake.Player.Tick(Mathf.Max(0f, delta) * shake.Speed);
                if (shake.Player.TryGetBonePose("camera", out var pose))
                    shakeOffset += new Vector3(pose.LocalPosition.x * shake.Facing, pose.LocalPosition.y, 0f);
            }
        }

        private void ReleaseCameraMotion()
        {
            foreach (var shake in cameraShakes) shake.Player.Dispose();
            cameraShakes.Clear(); shakeOffset = Vector3.zero; hasFocus = false;
            if (farBackdrop)
            {
                farBackdrop.transform.position = backdropPosition;
                farBackdrop.transform.localScale = backdropScale;
            }
            farBackdrop = null; hasMapBounds = false; groundOffsets.Clear();
        }
    }
}
