using UnityEngine;

namespace AstralOath.Client.Home
{
    // Both artifact galleries receive the same wheel, drag, touch pinch and double-tap gestures.
    internal sealed class ArtifactPaintingGestures
    {
        private readonly RectTransform image;
        private float zoom, previousPinch, lastTap;
        private int lastFrame = -1;
        private Vector2 dragOrigin;

        public ArtifactPaintingGestures(RectTransform image)
        {
            this.image = image;
            Reset();
        }

        public void Reset()
        {
            zoom = 1;
            previousPinch = 0;
            lastTap = -1;
            dragOrigin = Input.mousePosition;
            image.anchoredPosition = Vector2.zero;
            image.localScale = Vector3.one;
        }

        public void Tick()
        {
            if (!image || !image.gameObject.activeInHierarchy || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (Input.touchCount > 1)
            {
                float distance = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (previousPinch > 0) zoom = Mathf.Clamp(zoom * distance / previousPinch, 1, 4);
                previousPinch = distance;
            }
            else
            {
                previousPinch = 0;
                var mouse = (Vector2)Input.mousePosition;
                if (Input.GetMouseButtonDown(0))
                {
                    if (Time.unscaledTime - lastTap < .30f) Reset();
                    lastTap = Time.unscaledTime;
                    dragOrigin = mouse;
                }
                if (Input.GetMouseButton(0))
                {
                    var parent = (RectTransform)image.parent;
                    var canvas = image.GetComponentInParent<Canvas>().rootCanvas;
                    Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, mouse, camera, out var now) &&
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, dragOrigin, camera, out var last))
                        image.anchoredPosition += now - last;
                    dragOrigin = mouse;
                }
                zoom = Mathf.Clamp(zoom + Input.mouseScrollDelta.y * .12f, 1, 4);
            }
            image.localScale = Vector3.one * zoom;
        }
    }
}
