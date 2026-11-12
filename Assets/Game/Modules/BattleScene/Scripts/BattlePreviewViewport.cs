using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class BattlePreviewViewport : MonoBehaviour
    {
        private const float DesignAspect = 16f / 9f;
        private Camera stageCamera;
        private Vector2Int appliedSize;
        private Rect appliedSafeArea;

        private void OnEnable()
        {
            stageCamera = GetComponent<Camera>();
            appliedSize = default;
            ApplyViewport();
        }

        private void LateUpdate() => ApplyViewport();

        private void ApplyViewport()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            Rect safe = Screen.safeArea;
            if (size.x <= 0 || size.y <= 0 || safe.width <= 0 || safe.height <= 0 ||
                (size == appliedSize && safe == appliedSafeArea))
                return;

            var dimensions = new Vector2(Mathf.Min(safe.width, safe.height * DesignAspect),
                Mathf.Min(safe.height, safe.width / DesignAspect));
            Vector2 min = safe.center - dimensions * 0.5f;
            stageCamera.rect = new Rect(min.x / size.x, min.y / size.y, dimensions.x / size.x, dimensions.y / size.y);
            stageCamera.aspect = DesignAspect;
            appliedSize = size;
            appliedSafeArea = safe;
        }
    }
}
