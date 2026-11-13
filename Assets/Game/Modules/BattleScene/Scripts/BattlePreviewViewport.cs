using System.Collections.Generic;
using AstralOath.Client.Common;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(Camera))]
    public sealed class BattlePreviewViewport : MonoBehaviour
    {
        private const float DesignAspect = 16f / 9f;
        private const float MaximumHorizontalExpansion = 1.12f;
        private static readonly Vector2 DesignSize = new Vector2(1920f, 1080f);
        private static readonly Rect FullViewport = new Rect(0, 0, 1, 1);
        private Camera stageCamera;
        private float designSize, appliedScale;
        private Vector2Int appliedSize;
        private Rect appliedSafeArea;
        private Canvas canvas;
        private RectTransform hud, top, rail, active, soul, skills, detailsClose;
        private readonly List<(RectTransform Item, Vector2 Position)> topRight = new List<(RectTransform, Vector2)>();
        private Backdrop sky, ground;
        private Vector2 railPosition, railScale, layoutScreen;
        internal float BaselineSize { get; private set; }
        internal int LayoutRevision { get; private set; }

        private sealed class Backdrop
        {
            internal readonly Transform Transform;
            internal readonly Bounds Bounds;
            internal readonly Vector3 Position, Scale;
            internal Backdrop(SpriteRenderer renderer)
            {
                Transform = renderer.transform;
                Bounds = renderer.bounds;
                Position = Transform.position;
                Scale = Transform.localScale;
            }
        }

        private void OnEnable()
        {
            if (!stageCamera)
            {
                stageCamera = GetComponent<Camera>();
                designSize = stageCamera.orthographicSize;
                BaselineSize = designSize;
            }
            appliedSize = default;
            appliedScale = 0f;
            ApplyViewport();
        }

        private void Update() => ApplyViewport();
        private void LateUpdate() => ApplyViewport();

        private void ApplyViewport()
        {
            // Offscreen capture tools set their own size and projection for the render.
            if (stageCamera.targetTexture) return;
            ApplyScreen(new Vector2Int(Screen.width, Screen.height), Screen.safeArea);
        }

        internal void ApplyScreen(Vector2Int size, Rect safe)
        {
            if (size.x <= 0 || size.y <= 0) return;
            safe = ClampSafeArea(safe, size);
            if (size != appliedSize)
            {
                LayoutRevision++;
                float aspect = size.x / (float)size.y;
                float previousBaseline = BaselineSize;
                BaselineSize = designSize * Mathf.Min(1f, DesignAspect * MaximumHorizontalExpansion / aspect);
                if (aspect < DesignAspect) BaselineSize = designSize * DesignAspect / aspect;
                stageCamera.rect = FullViewport;
                stageCamera.aspect = aspect;
                if (stageCamera.orthographic && previousBaseline > 0)
                    stageCamera.orthographicSize *= BaselineSize / previousBaseline;
                ResizeBackdrop();
            }

            if (FindHud())
            {
                // Screen Space Camera updates its logical rect and plane transform at render.
                // The displayed frame always uses the scale computed from the screen.
                Vector2 pixelsToCanvas = Vector2.one / Mathf.Max(.001f, canvas.scaleFactor);
                float canvasScale = Mathf.Max(.001f, canvas.scaleFactor);
                if (size != appliedSize || safe != appliedSafeArea || canvasScale != appliedScale)
                {
                    LayoutHud(size, safe, pixelsToCanvas);
                    appliedScale = canvasScale;
                }
                LayoutReadinessClose();
            }
            appliedSize = size;
            appliedSafeArea = safe;
        }

        private static Rect ClampSafeArea(Rect safe, Vector2Int size)
        {
            float left = Mathf.Clamp(safe.xMin, 0, size.x);
            float bottom = Mathf.Clamp(safe.yMin, 0, size.y);
            float right = Mathf.Clamp(safe.xMax, 0, size.x);
            float top = Mathf.Clamp(safe.yMax, 0, size.y);
            return right > left && top > bottom
                ? Rect.MinMaxRect(left, bottom, right, top) : new Rect(0, 0, size.x, size.y);
        }

        private bool FindHud()
        {
            if (hud) return true;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var candidate = root.GetComponent<Canvas>();
                if (!candidate || candidate.worldCamera != stageCamera || !root.GetComponent<BattleHudPreview>()) continue;
                var map = root.GetComponentInChildren<BindingMap>(true);
                if (!map) return false;
                canvas = candidate;
                hud = (RectTransform)map.transform;
                top = FindGroup("Top Bar");
                rail = FindGroup("Combat Readiness");
                active = FindGroup("Active Unit");
                soul = FindGroup("Soul Gauge");
                skills = FindGroup("Skills");
                topRight.Clear();
                if (top)
                    foreach (RectTransform item in top)
                        if (item.anchoredPosition.x > DesignSize.x * .5f)
                            topRight.Add((item, item.anchoredPosition));
                appliedScale = 0;
                return true;
            }
            return false;
        }

        private RectTransform FindGroup(string name) => hud.Find(name) as RectTransform;

        private void LayoutHud(Vector2Int size, Rect safe, Vector2 pixelsToCanvas)
        {
            layoutScreen = Vector2.Scale(new Vector2(size.x, size.y), pixelsToCanvas);
            var layoutSafe = new Rect(Vector2.Scale(safe.position, pixelsToCanvas), Vector2.Scale(safe.size, pixelsToCanvas));
            float designToPixels = Mathf.Min(safe.width / DesignSize.x, safe.height / DesignSize.y);
            var groupScale = pixelsToCanvas * designToPixels;
            var safeMin = layoutSafe.min;
            var safeMax = layoutSafe.max;
            float designWidth = DesignSize.x * groupScale.x;
            float topY = safeMax.y - DesignSize.y * groupScale.y;
            float right = safeMax.x - designWidth;
            railPosition = new Vector2(safeMin.x, topY);
            railScale = groupScale;
            SetGroup(top, railPosition, groupScale);
            float topSpan = (right - safeMin.x) / groupScale.x;
            foreach (var (item, position) in topRight)
                if (item) item.anchoredPosition = position + Vector2.right * topSpan;
            SetGroup(rail, railPosition, groupScale);
            SetGroup(active, safeMin, groupScale);
            SetGroup(soul, new Vector2(layoutSafe.center.x - designWidth * .5f, safeMin.y), groupScale);
            SetGroup(skills, new Vector2(right, safeMin.y), groupScale);
            LayoutReadinessClose();
        }

        private static void SetGroup(RectTransform group, Vector2 origin, Vector2 scale)
        {
            if (!group) return;
            group.anchorMin = group.anchorMax = group.pivot = Vector2.zero;
            group.sizeDelta = DesignSize;
            group.anchoredPosition = origin;
            group.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        private void LayoutReadinessClose()
        {
            if (!rail || railScale.x <= 0 || railScale.y <= 0) return;
            // The rail is docked to the safe area. Its close surface still covers the physical screen.
            if (!detailsClose)
                detailsClose = rail.Find("Live Readiness/Readiness Details/Close Surface") as RectTransform;
            if (!detailsClose) return;
            var origin = new Vector2(-railPosition.x / railScale.x, -railPosition.y / railScale.y);
            var size = new Vector2(layoutScreen.x / railScale.x, layoutScreen.y / railScale.y);
            if (detailsClose.anchorMax == Vector2.zero && detailsClose.anchoredPosition == origin && detailsClose.sizeDelta == size) return;
            detailsClose.anchorMin = detailsClose.anchorMax = detailsClose.pivot = Vector2.zero;
            detailsClose.anchoredPosition = origin;
            detailsClose.sizeDelta = size;
        }

        internal void ResizeBackdrop()
        {
            if (sky == null || !sky.Transform || ground == null || !ground.Transform)
                foreach (var root in gameObject.scene.GetRootGameObjects())
                {
                    var skyLayer = root.transform.Find("Sky");
                    var groundLayer = root.transform.Find("Ground");
                    if (!skyLayer || !groundLayer || !skyLayer.TryGetComponent<SpriteRenderer>(out var skyRenderer) ||
                        !groundLayer.TryGetComponent<SpriteRenderer>(out var groundRenderer)) continue;
                    sky = new Backdrop(skyRenderer);
                    ground = new Backdrop(groundRenderer);
                    break;
                }
            if (sky == null || !sky.Transform || ground == null || !ground.Transform) return;
            float width = 2 * BaselineSize * stageCamera.aspect;
            // Extend only continuous sky/ground, keeping the horizon and actor feet in world space.
            ResizeLayer(sky, width, Mathf.Max(sky.Bounds.size.y, BaselineSize - sky.Bounds.min.y), true);
            ResizeLayer(ground, width, Mathf.Max(ground.Bounds.size.y, ground.Bounds.max.y + BaselineSize), false);
        }

        private static void ResizeLayer(Backdrop layer, float width, float height, bool fixBottom)
        {
            float x = Mathf.Max(1, width / layer.Bounds.size.x);
            float y = Mathf.Max(1, height / layer.Bounds.size.y);
            layer.Transform.localScale = Vector3.Scale(layer.Scale, new Vector3(x, y, 1));
            layer.Transform.position = layer.Position + Vector3.up * (height - layer.Bounds.size.y) * (fixBottom ? .5f : -.5f);
        }
    }
}
