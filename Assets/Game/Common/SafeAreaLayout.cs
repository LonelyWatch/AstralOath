using UnityEngine;

namespace AstralOath.Client.Common
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaLayout : MonoBehaviour
    {
        private Rect applied;
        private Vector2Int dimensions;

        private void LateUpdate()
        {
            Rect safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (size.x <= 0 || size.y <= 0 || (applied == safe && dimensions == size))
                return;
            applied = safe;
            dimensions = size;
            var layout = (RectTransform)transform;
            layout.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            layout.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            layout.offsetMin = Vector2.zero;
            layout.offsetMax = Vector2.zero;
        }
    }
}