using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal static class UiRenderSurface
    {
        // Keep both axes proportional on displays larger than a 4K render target.
        internal static int MaxDimension => Mathf.Min(4096, SystemInfo.maxTextureSize);

        internal static Vector2Int ScreenPixels(RawImage image)
        {
            var canvas = image.canvas ? image.canvas.rootCanvas : null;
            var camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var transform = image.rectTransform;
            var rect = transform.rect;
            Vector2 Project(float x, float y) =>
                RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(new Vector3(x, y)));
            var origin = Project(rect.xMin, rect.yMin);
            return new Vector2Int(Mathf.RoundToInt(Vector2.Distance(origin, Project(rect.xMax, rect.yMin))),
                Mathf.RoundToInt(Vector2.Distance(origin, Project(rect.xMin, rect.yMax))));
        }
    }
}
