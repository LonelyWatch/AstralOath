using System;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // Owned by the unit's presentation View. Never changes authoritative health or actor transforms.
    internal sealed class BattleUnitStatusPresentation : IDisposable
    {
        private readonly RectTransform marker, frame;
        private readonly Image health, trail;
        private readonly Sprite fallbackTrailSprite;
        private readonly BattleHealthSegments segments;
        private readonly Transform actor;
        private readonly Func<Vector3> worldAnchor;
        private readonly Canvas canvas;
        private readonly Vector3 originalPosition, originalScale;
        private readonly float originalFill, referenceProjection;
        private float target, frontStart, trailStart, frontTime, trailTime;
        private int maximum;
        private bool initialized, losingHealth, disposed;

        // Project presentation defaults. Original E7 millisecond timings are not yet proven.
        public float DamageSeconds { get; set; } = .16f;
        public float HealSeconds { get; set; } = .18f;
        public float TrailDelaySeconds { get; set; } = .12f;
        public float TrailSeconds { get; set; } = .28f;
        // Reference Canvas units, relative to the source top anchor. This is explicit calibration.
        public Vector2 AnchorOffset { get; set; }

        public BattleUnitStatusPresentation(RectTransform marker, Image health, Transform actor,
            Func<Vector3> worldAnchor, Camera camera, Sprite damageSprite, Sprite splitSprite, int sectionCount)
        {
            this.marker = marker ? marker : throw new ArgumentNullException(nameof(marker));
            this.health = health ? health : throw new ArgumentNullException(nameof(health));
            this.actor = actor ? actor : throw new ArgumentNullException(nameof(actor));
            this.worldAnchor = worldAnchor ?? throw new ArgumentNullException(nameof(worldAnchor));
            if (!camera) throw new ArgumentNullException(nameof(camera));
            if (sectionCount < 1) throw new ArgumentOutOfRangeException(nameof(sectionCount));
            frame = marker.Find("Frame") as RectTransform;
            if (!frame) throw new ArgumentException("The unit marker must contain a Frame.", nameof(marker));
            canvas = marker.GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas) throw new ArgumentException("The unit marker must belong to a Canvas.", nameof(marker));
            originalPosition = marker.localPosition; originalScale = marker.localScale; originalFill = health.fillAmount;
            referenceProjection = ProjectionInCanvasUnits(camera);
            var trailObject = new GameObject("Damage Trail", typeof(RectTransform), typeof(Image));
            trailObject.transform.SetParent(marker, false);
            trail = trailObject.GetComponent<Image>(); CopyRect(health.rectTransform, trail.rectTransform);
            if (!damageSprite)
                fallbackTrailSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            trail.sprite = damageSprite ? damageSprite : fallbackTrailSprite;
            // A missing source asset is a diagnostic fallback, never an opaque white damage bar.
            trail.color = damageSprite ? Color.white : new Color(129f / 255f, 50f / 255f, 45f / 255f, 1f);
            trail.raycastTarget = false;
            trail.type = Image.Type.Filled; trail.fillMethod = Image.FillMethod.Horizontal;
            trail.fillOrigin = health.fillOrigin; trail.fillAmount = originalFill;
            trail.transform.SetSiblingIndex(health.transform.GetSiblingIndex());
            var segmentObject = new GameObject("Health Segments", typeof(RectTransform), typeof(BattleHealthSegments));
            segmentObject.transform.SetParent(marker, false);
            segments = segmentObject.GetComponent<BattleHealthSegments>(); CopyRect(health.rectTransform, segments.rectTransform);
            segments.SourceSprite = splitSprite; segments.color = splitSprite ? Color.white : new Color(0f, 0f, 0f, 128f / 255f);
            segments.raycastTarget = false;
            segments.transform.SetSiblingIndex(health.transform.GetSiblingIndex() + 1);
            segments.UseFixedSections(sectionCount);
        }

        public void UseFixedSections(int count) { if (!disposed) segments.UseFixedSections(count); }
        public void UseHealthIntervals(int healthPerSection) { if (!disposed) segments.UseHealthIntervals(healthPerSection); }

        public void UpdateHealth(long value, int max, bool immediate)
        {
            if (disposed) return;
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            float next = (float)(Math.Max(0L, Math.Min(value, max)) / (double)max);
            bool maximumChanged = maximum != max;
            if (maximumChanged) { maximum = max; segments.MaximumHealth = max; }
            if (!initialized || immediate || maximumChanged)
            {
                initialized = true; target = frontStart = trailStart = next;
                health.fillAmount = trail.fillAmount = next; frontTime = trailTime = 0;
                losingHealth = false;
                return;
            }
            if (Mathf.Approximately(next, target)) return; // A snapshot refresh must not restart an existing tween.
            losingHealth = next < target;
            target = next; frontStart = health.fillAmount; frontTime = trailTime = 0;
            if (losingHealth)
                trailStart = trail.fillAmount = Mathf.Max(trail.fillAmount, health.fillAmount);
            else
                trailStart = trail.fillAmount = health.fillAmount; // Healing cancels a stale red damage tail.
        }

        public void Tick(float delta)
        {
            if (disposed || !initialized || delta <= 0) return;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            frontTime += delta; trailTime += delta;
            float duration = losingHealth ? DamageSeconds : HealSeconds;
            float progress = duration <= 0 ? 1 : Mathf.Clamp01(frontTime / duration);
            health.fillAmount = Mathf.Lerp(frontStart, target, Smooth(progress));
            if (losingHealth)
            {
                float tailProgress = TrailSeconds <= 0 ? (trailTime >= TrailDelaySeconds ? 1 : 0) :
                    Mathf.Clamp01((trailTime - Mathf.Max(0, TrailDelaySeconds)) / TrailSeconds);
                trail.fillAmount = Mathf.Max(health.fillAmount, Mathf.Lerp(trailStart, target, Smooth(tailProgress)));
            }
            else trail.fillAmount = health.fillAmount;
        }

        public void LateTick(Camera camera)
        {
            if (disposed || !marker || !actor || !camera) return;
            float relative = ProjectionInCanvasUnits(camera) / referenceProjection;
            if (!float.IsFinite(relative) || relative <= .000001f) return;
            marker.localScale = originalScale * relative; // Positive uniform scale keeps text readable after facing flips.
            Vector3 projected = camera.WorldToScreenPoint(worldAnchor());
            var parent = marker.parent as RectTransform;
            if (!parent) return;
            var offset = new Vector3(AnchorOffset.x * originalScale.x * relative,
                AnchorOffset.y * originalScale.y * relative, 0);
            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                // ScreenSpaceCamera updates its root world pose during camera rendering, after
                // LateUpdate. Work entirely within the root Canvas so that later movement does
                // not translate/scale an already projected marker a second time.
                Rect pixels = ScreenRect();
                Rect rect = ((RectTransform)canvas.transform).rect;
                if (pixels.width <= 0f || pixels.height <= 0f) return;
                var local = new Vector3(rect.xMin + (projected.x - pixels.xMin) * rect.width / pixels.width,
                    rect.yMin + (projected.y - pixels.yMin) * rect.height / pixels.height, 0);
                var desiredLocal = parent.InverseTransformPoint(canvas.transform.TransformPoint(local));
                var frameLocal = parent.InverseTransformPoint(frame.position);
                desiredLocal.z = frameLocal.z;
                marker.localPosition += desiredLocal + offset - frameLocal;
            }
            else
            {
                if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, projected, UiCamera(), out var desired)) return;
                desired += parent.TransformVector(offset);
                marker.position += desired - frame.position;
            }
        }

        private float ProjectionInCanvasUnits(Camera camera)
        {
            Vector3 origin = worldAnchor();
            float worldPixels = Vector2.Distance(camera.WorldToScreenPoint(origin),
                camera.WorldToScreenPoint(origin + actor.TransformVector(Vector3.up)));
            var parent = marker.parent;
            float canvasPixels;
            if (canvas.renderMode != RenderMode.WorldSpace)
            {
                Rect pixels = ScreenRect();
                Rect rect = ((RectTransform)canvas.transform).rect;
                var localUp = canvas.transform.InverseTransformVector(parent.TransformVector(Vector3.up));
                canvasPixels = new Vector2(localUp.x * pixels.width / Mathf.Max(.000001f, rect.width),
                    localUp.y * pixels.height / Mathf.Max(.000001f, rect.height)).magnitude;
            }
            else
                canvasPixels = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(UiCamera(), parent.position),
                    RectTransformUtility.WorldToScreenPoint(UiCamera(), parent.TransformPoint(Vector3.up)));
            return Mathf.Max(.000001f, worldPixels / Mathf.Max(.000001f, canvasPixels));
        }
        private Rect ScreenRect()
        {
            var uiCamera = UiCamera();
            // Camera rendering to a texture covers the entire texture, ignoring camera.rect.
            if (uiCamera && uiCamera.targetTexture)
                return new Rect(0, 0, uiCamera.targetTexture.width, uiCamera.targetTexture.height);
            return uiCamera ? uiCamera.pixelRect : canvas.pixelRect;
        }
        private Camera UiCamera() => canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        private static float Smooth(float t) => t * t * (3f - 2f * t);
        private static void CopyRect(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax; target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta; target.anchoredPosition3D = source.anchoredPosition3D;
            target.localScale = source.localScale; target.localRotation = source.localRotation;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Remove(trail ? trail.gameObject : null); Remove(segments ? segments.gameObject : null);
            if (fallbackTrailSprite)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(fallbackTrailSprite);
                else UnityEngine.Object.DestroyImmediate(fallbackTrailSprite);
            }
            if (health) health.fillAmount = originalFill;
            if (marker) { marker.localPosition = originalPosition; marker.localScale = originalScale; }
        }
        private static void Remove(GameObject item)
        {
            if (!item) return;
            item.SetActive(false); item.transform.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
