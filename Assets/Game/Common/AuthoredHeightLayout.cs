using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AuthoredHeightLayout : UIBehaviour, ILayoutSelfController
    {
        [SerializeField] private RectTransform content;
        [SerializeField, Min(.001f)] private float authoredHeight = 45f;

        public float AuthoredHeight => authoredHeight;

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyScale();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            ApplyScale();
        }

        public void SetLayoutHorizontal() => ApplyScale();
        public void SetLayoutVertical() => ApplyScale();

        private void ApplyScale()
        {
            if (!content || authoredHeight <= 0)
                return;
            float scale = ((RectTransform)transform).rect.height / authoredHeight;
            content.localScale = new Vector3(scale, scale, 1);
        }
    }
}
