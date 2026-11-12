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

        private Vector3 Framed(Vector3 anchor, float groundOffset) =>
            anchor + Vector3.up * (groundOffset * camera.orthographicSize / originalSize);

        private Vector3 FocusPoint(int location, Vector3 offset)
        {
            var point = Anchor(location) + offset;
            point.z = originalPosition.z;
            return point;
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
