using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // One UI mesh for the source split sprite; never one GameObject per health segment.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleHealthSegments : MaskableGraphic
    {
        private Sprite sourceSprite;
        private int maximum = 1, sections = 10, healthInterval;
        public Sprite SourceSprite
        {
            get => sourceSprite;
            set { if (sourceSprite == value) return; sourceSprite = value; SetAllDirty(); }
        }
        public int MaximumHealth
        {
            get => maximum;
            set { if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); if (maximum == value) return; maximum = value; SetVerticesDirty(); }
        }
        public int DividerCount => healthInterval > 0 ? (maximum - 1) / healthInterval : Math.Max(0, sections - 1);
        public override Texture mainTexture => sourceSprite ? sourceSprite.texture : s_WhiteTexture;
        public void UseFixedSections(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (sections == count && healthInterval == 0) return;
            sections = count; healthInterval = 0; SetVerticesDirty();
        }
        public void UseHealthIntervals(int healthPerSection)
        {
            if (healthPerSection <= 0) throw new ArgumentOutOfRangeException(nameof(healthPerSection));
            if (healthInterval == healthPerSection) return;
            healthInterval = healthPerSection; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var rect = GetPixelAdjustedRect(); int count = DividerCount;
            if (count == 0 || rect.width <= 0 || rect.height <= 0) return;
            var uv = sourceSprite ? DataUtility.GetOuterUV(sourceSprite) : new Vector4(0, 0, 1, 1);
            float width = sourceSprite ? rect.height * sourceSprite.rect.width / sourceSprite.rect.height : 1f;
            var uiCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float pixels = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(uiCamera, rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0))),
                RectTransformUtility.WorldToScreenPoint(uiCamera, rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0))));
            int budget = Mathf.Clamp(Mathf.FloorToInt(pixels / 2f), 1, 256);
            int stride = healthInterval > 0 ? Math.Max(1, (int)Math.Ceiling(count / (double)budget)) : 1;
            // Overdense optional HP-value marks are decimated, not relabelled as a different HP unit.
            for (long divider = stride; divider <= count; divider += stride)
            {
                float ratio = healthInterval > 0 ? (float)(divider * healthInterval / (double)maximum) : (float)(divider / (double)sections);
                float x = rect.xMin + rect.width * ratio;
                bool major = Mathf.Abs(ratio - .5f) < .00001f;
                float markWidth = width * (major ? 1f : .75f);
                float top = rect.yMin + rect.height * (major ? 1f : .5f);
                float left = Mathf.Max(rect.xMin, x - markWidth * .5f), right = Mathf.Min(rect.xMax, x + markWidth * .5f);
                int first = helper.currentVertCount;
                helper.AddVert(new Vector3(left, rect.yMin, 0f), color, new Vector2(uv.x, uv.y));
                helper.AddVert(new Vector3(left, top, 0f), color, new Vector2(uv.x, uv.w));
                helper.AddVert(new Vector3(right, top, 0f), color, new Vector2(uv.z, uv.w));
                helper.AddVert(new Vector3(right, rect.yMin, 0f), color, new Vector2(uv.z, uv.y));
                helper.AddTriangle(first, first + 1, first + 2); helper.AddTriangle(first, first + 2, first + 3);
            }
        }
    }
}
