using System;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // Owned by the unit's presentation View. Never changes authoritative health or actor transforms.
    internal sealed class BattleUnitStatusPresentation : IDisposable
    {
        private readonly RectTransform marker, frame;
        private readonly Image health, trail, shield, injury, frameImage;
        private readonly Sprite normalFrame;
        private readonly bool ownsShield;
        private Image resourceFill;
        private readonly BattleHealthSegments segments;
        private readonly Transform actor;
        private readonly Func<Vector3> worldAnchor;
        private readonly Canvas canvas;
        private readonly Vector3 originalPosition, originalScale;
        private readonly float originalFill, referenceProjection;
        private float target, frontStart, trailStart, frontTime, trailTime;
        private float visibleHealth, visibleTrail;
        private double shieldTarget, shieldStart, visibleShield;
        private float shieldTime;
        private int maximum, uninjuredMaximum;
        private bool initialized, losingHealth, holdingDamageTrail, disposed;

        // Project presentation defaults. Original E7 millisecond timings are not yet proven.
        public float DamageSeconds { get; set; } = .16f;
        public float HealSeconds { get; set; } = .18f;
        public float TrailDelaySeconds { get; set; } = .15f;
        public float TrailSeconds { get; set; } = .50f;
        // Reference Canvas units, relative to the source top anchor. This is explicit calibration.
        public Vector2 AnchorOffset { get; set; }
        public Transform ScaleAnchor { get; set; }

        public BattleUnitStatusPresentation(RectTransform marker, Image health, Transform actor,
            Func<Vector3> worldAnchor, Camera camera, Sprite splitSprite, int sectionCount)
        {
            this.marker = marker ? marker : throw new ArgumentNullException(nameof(marker));
            this.health = health ? health : throw new ArgumentNullException(nameof(health));
            this.actor = actor ? actor : throw new ArgumentNullException(nameof(actor));
            this.worldAnchor = worldAnchor ?? throw new ArgumentNullException(nameof(worldAnchor));
            if (!camera) throw new ArgumentNullException(nameof(camera));
            if (sectionCount < 1) throw new ArgumentOutOfRangeException(nameof(sectionCount));
            frame = marker.Find("Frame") as RectTransform;
            if (!frame) throw new ArgumentException("The unit marker must contain a Frame.", nameof(marker));
            frameImage = frame.GetComponent<Image>();
            if (frameImage) normalFrame = frameImage.sprite;
            canvas = marker.GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas) throw new ArgumentException("The unit marker must belong to a Canvas.", nameof(marker));
            originalPosition = marker.localPosition; originalScale = marker.localScale; originalFill = health.fillAmount;
            referenceProjection = ProjectionInCanvasUnits(camera);
            var trailObject = new GameObject("Damage Trail", typeof(RectTransform), typeof(Image));
            trailObject.transform.SetParent(health.transform, false);
            trail = trailObject.GetComponent<Image>();
            trail.color = new Color32(231, 44, 53, 255);
            trail.raycastTarget = false;
            trail.enabled = false;
            trail.rectTransform.sizeDelta = Vector2.zero;
            SetHorizontalRange(trail.rectTransform, 0f, 0f, false);
            shield = health.transform.Find("Shield Fill")?.GetComponent<Image>();
            ownsShield = !shield;
            if (ownsShield)
            {
                shield = new GameObject("Shield Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                shield.transform.SetParent(health.transform, false);
            }
            shield.raycastTarget = false;
            shield.transform.SetSiblingIndex(trail.transform.GetSiblingIndex() + 1);
            SetShieldSprite(shield.sprite);
            shield.rectTransform.sizeDelta = Vector2.zero;
            SetHorizontalRange(shield.rectTransform, 0f, 0f, false);
            shield.gameObject.SetActive(false);

            injury = new GameObject("Injury", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            injury.transform.SetParent(health.transform, false);
            injury.rectTransform.sizeDelta = Vector2.zero;
            injury.color = new Color32(120, 72, 148, 255);
            injury.raycastTarget = false;
            injury.enabled = false;

            var segmentObject = new GameObject("Health Segments", typeof(RectTransform), typeof(BattleHealthSegments));
            segmentObject.transform.SetParent(health.transform, false);
            segments = segmentObject.GetComponent<BattleHealthSegments>();
            segments.rectTransform.sizeDelta = Vector2.zero;
            SetHorizontalRange(segments.rectTransform, 0f, 1f, false);
            segments.SourceSprite = splitSprite; segments.color = splitSprite ? Color.white : new Color(0f, 0f, 0f, 128f / 255f);
            segments.raycastTarget = false;
            segments.UseFixedSections(sectionCount);
        }

        public void UseFixedSections(int count) { if (!disposed) segments.UseFixedSections(count); }
        public void UseHealthIntervals(int healthPerSection) { if (!disposed) segments.UseHealthIntervals(healthPerSection); }

        public void ConfigureResource(Sprite frameSprite, Sprite fill, Sprite divider, int sections)
        {
            if (disposed || resourceFill || !frameImage || !frameSprite || !fill) return;
            frameImage.sprite = frameSprite;
            resourceFill = new GameObject("Personal Resource", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            resourceFill.transform.SetParent(health.transform, false);
            resourceFill.rectTransform.anchorMin = new Vector2(0, .5f);
            resourceFill.rectTransform.anchorMax = new Vector2(1, .5f);
            // hp_bar.csb places HP at y=16 with height 6 and the resource at y=11 with height 4.
            float sourcePixel = health.rectTransform.rect.height / 6f;
            resourceFill.rectTransform.sizeDelta = new Vector2(0, 4f * sourcePixel);
            resourceFill.rectTransform.anchoredPosition = new Vector2(0, -6f * sourcePixel);
            resourceFill.sprite = fill;
            resourceFill.type = Image.Type.Filled;
            resourceFill.fillMethod = Image.FillMethod.Horizontal;
            resourceFill.fillOrigin = health.fillOrigin;
            resourceFill.fillAmount = 0;
            resourceFill.raycastTarget = false;
            if (!divider || sections < 2) return;
            var marks = new GameObject("Resource Dividers", typeof(RectTransform), typeof(BattleHealthSegments))
                .GetComponent<BattleHealthSegments>();
            marks.transform.SetParent(resourceFill.transform, false);
            marks.rectTransform.anchorMin = Vector2.zero;
            marks.rectTransform.anchorMax = Vector2.one;
            marks.rectTransform.offsetMin = marks.rectTransform.offsetMax = Vector2.zero;
            marks.SourceSprite = divider;
            marks.UseFixedSections(sections);
            marks.raycastTarget = false;
        }

        public void UpdateResource(int value, int maximum)
        {
            if (!disposed && resourceFill)
                resourceFill.fillAmount = maximum > 0 ? Mathf.Clamp01(value / (float)maximum) : 0f;
        }

        public void SetHealthCapacity(int original, int effective)
        {
            if (disposed) return;
            uninjuredMaximum = Math.Max(original, effective);
            if (!initialized || maximum == effective || effective <= 0) return;
            // Percentages inside the playable region change with the cap, while their physical
            // positions and the existing red trail remain in original maximum-HP units.
            float ratio = maximum / (float)effective;
            target = Mathf.Min(1f, target * ratio);
            frontStart = Mathf.Min(1f, frontStart * ratio);
            trailStart = Mathf.Min(1f, trailStart * ratio);
            visibleHealth = Mathf.Min(1f, visibleHealth * ratio);
            visibleTrail = Mathf.Min(1f, visibleTrail * ratio);
            maximum = effective;
            segments.MaximumHealth = effective;
            RefreshBars();
        }

        public void SetShieldSprite(Sprite sprite)
        {
            if (disposed) return;
            shield.sprite = sprite;
            shield.color = sprite ? Color.white : new Color(220f / 255f, 220f / 255f, 220f / 255f, 1f);
        }

        public void UpdateHealth(long value, int max, bool immediate)
        {
            if (disposed) return;
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            float next = (float)(Math.Max(0L, Math.Min(value, max)) / (double)max);
            bool maximumChanged = maximum != max;
            if (maximumChanged) { maximum = max; segments.MaximumHealth = max; }
            if (next <= 0)
                shieldTarget = shieldStart = visibleShield = 0;
            if (!initialized || immediate || maximumChanged)
            {
                initialized = true; target = frontStart = trailStart = next;
                visibleHealth = visibleTrail = next; frontTime = trailTime = 0;
                losingHealth = holdingDamageTrail = false;
                RefreshBars();
                return;
            }
            if (Mathf.Approximately(next, target)) return; // A snapshot refresh must not restart an existing tween.
            holdingDamageTrail = false;
            losingHealth = next < target;
            target = next; frontStart = visibleHealth; frontTime = trailTime = 0;
            trailStart = losingHealth ? Mathf.Max(visibleTrail, visibleHealth) : visibleHealth;
            visibleTrail = trailStart;
            RefreshBars();
        }

        public void ApplyDamageHit(long value, int max, bool lastHit)
        {
            if (disposed) return;
            if (!initialized || maximum != max)
            {
                UpdateHealth(value, max, true);
                return;
            }

            float next = (float)(Math.Max(0L, Math.Min(value, max)) / (double)max);
            if (next < target)
            {
                // The green bar follows each actual loss. Keep the pre-hit edge through the hit sequence.
                visibleTrail = Mathf.Max(visibleTrail, Mathf.Max(visibleHealth, target));
                visibleHealth = target = frontStart = next;
                frontTime = trailTime = 0;
                losingHealth = holdingDamageTrail = true;
                if (next <= 0f) shieldTarget = shieldStart = visibleShield = 0;
            }
            if (lastHit && holdingDamageTrail)
            {
                holdingDamageTrail = false;
                trailStart = visibleTrail;
                trailTime = 0;
            }
            RefreshBars();
        }

        public void UpdateShield(long value, int max, bool immediate)
        {
            if (disposed) return;
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            double next = Math.Max(0L, value);
            if (!initialized || immediate || maximum != max)
            {
                shieldTarget = shieldStart = visibleShield = next; shieldTime = 0;
                RefreshBars();
                return;
            }
            if (next == shieldTarget) return;
            shieldTarget = next; shieldStart = visibleShield; shieldTime = 0;
        }

        public void Tick(float delta)
        {
            if (disposed || !initialized || delta <= 0) return;
            if (float.IsNaN(delta) || float.IsInfinity(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            frontTime += delta;
            float duration = losingHealth ? DamageSeconds : HealSeconds;
            float progress = duration <= 0 ? 1 : Mathf.Clamp01(frontTime / duration);
            visibleHealth = Mathf.Lerp(frontStart, target, Smooth(progress));
            if (losingHealth && !holdingDamageTrail)
            {
                trailTime += delta;
                float tailProgress = TrailSeconds <= 0 ? (trailTime >= TrailDelaySeconds ? 1 : 0) :
                    Mathf.Clamp01((trailTime - Mathf.Max(0, TrailDelaySeconds)) / TrailSeconds);
                visibleTrail = Mathf.Max(visibleHealth, Mathf.Lerp(trailStart, target, Smooth(tailProgress)));
            }
            else if (!losingHealth) visibleTrail = visibleHealth;

            if (visibleShield != shieldTarget)
            {
                shieldTime += delta;
                float shieldDuration = shieldTarget < shieldStart ? DamageSeconds : HealSeconds;
                float shieldProgress = shieldDuration <= 0 ? 1 : Mathf.Clamp01(shieldTime / shieldDuration);
                visibleShield = shieldProgress >= 1f ? shieldTarget : shieldStart + (shieldTarget - shieldStart) * Smooth(shieldProgress);
            }
            RefreshBars();
        }

        private void RefreshBars()
        {
            if (!initialized) return;
            // The source layout has separate HP and shield strips. Fit both into the fixed track,
            // preserving empty HP space until the combined visible amount exceeds maximum HP.
            float combined = visibleHealth + (float)(visibleShield / maximum);
            float effectiveTrack = maximum / (float)Mathf.Max(maximum, uninjuredMaximum);
            float healthTrack = effectiveTrack / Mathf.Max(1f, combined);
            float healthEnd = visibleHealth * healthTrack;
            float shieldEnd = combined * healthTrack;
            health.fillAmount = healthEnd;
            float trailEnd = visibleTrail * healthTrack;
            bool fromRight = health.fillOrigin == (int)Image.OriginHorizontal.Right;
            injury.enabled = effectiveTrack < 1f;
            if (injury.enabled) SetHorizontalRange(injury.rectTransform, effectiveTrack, 1f, fromRight);
            trail.enabled = trailEnd > healthEnd;
            SetHorizontalRange(trail.rectTransform, healthEnd, Mathf.Max(healthEnd, trailEnd), fromRight);
            SetHorizontalRange(segments.rectTransform, 0f, healthTrack, fromRight);
            bool showShield = target > 0 && shieldEnd > healthEnd;
            if (shield.gameObject.activeSelf != showShield)
                shield.gameObject.SetActive(showShield);
            if (showShield)
                SetHorizontalRange(shield.rectTransform, healthEnd, shieldEnd, fromRight);
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
                camera.WorldToScreenPoint(origin + (ScaleAnchor ? ScaleAnchor : actor).TransformVector(Vector3.up)));
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
        private static void SetHorizontalRange(RectTransform rect, float start, float end, bool fromRight)
        {
            Vector2 min = new Vector2(fromRight ? 1f - end : start, 0f);
            Vector2 max = new Vector2(fromRight ? 1f - start : end, 1f);
            if (rect.anchorMin != min) rect.anchorMin = min;
            if (rect.anchorMax != max) rect.anchorMax = max;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Remove(trail ? trail.gameObject : null); Remove(segments ? segments.gameObject : null);
            if (ownsShield) Remove(shield ? shield.gameObject : null);
            else if (shield) shield.gameObject.SetActive(false);
            Remove(resourceFill ? resourceFill.gameObject : null); Remove(injury ? injury.gameObject : null);
            if (frameImage) frameImage.sprite = normalFrame;
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
