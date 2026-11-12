using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class DamageFeedbackGlyph : RawImage
    {
        private Color32 top = Color.white;
        private Color32 bottom = Color.white;

        internal Vector2 RestPosition { get; set; }

        internal void SetGradient(Color32 newTop, Color32 newBottom)
        {
            if (top.Equals(newTop) && bottom.Equals(newBottom))
                return;
            top = newTop;
            bottom = newBottom;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            base.OnPopulateMesh(vertices);
            var vertex = default(UIVertex);
            float center = GetPixelAdjustedRect().center.y;
            for (int index = 0; index < vertices.currentVertCount; index++)
            {
                vertices.PopulateUIVertex(ref vertex, index);
                vertex.color = vertex.position.y > center ? top : bottom;
                vertices.SetUIVertex(vertex, index);
            }
        }
    }
}
