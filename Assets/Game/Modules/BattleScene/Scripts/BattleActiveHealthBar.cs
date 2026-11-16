using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // The active-unit HP strip uses a slanted, beveled fill. Its geometry is
    // separate from the head bars; partial health keeps both slanted ends.
    public sealed class BattleActiveHealthBar : Image
    {
        private static readonly float[] Rows = { 0f, .14f, .28f, .76f, .9f, 1f };
        private static readonly float[] Columns = { 0f, .47f, 1f };
        private static readonly Color32[] Edge =
        {
            new Color32(155, 189, 70, 255), new Color32(125, 178, 40, 255),
            new Color32(100, 155, 20, 255), new Color32(117, 168, 34, 255),
            new Color32(142, 187, 51, 255), new Color32(174, 202, 92, 255)
        };
        private static readonly Color32[] Center =
        {
            new Color32(87, 112, 42, 255), new Color32(64, 103, 18, 255),
            new Color32(55, 92, 12, 255), new Color32(62, 102, 14, 255),
            new Color32(81, 111, 35, 255), new Color32(108, 132, 57, 255)
        };

        public override Texture mainTexture => Texture2D.whiteTexture;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;

            float slope = Mathf.Min(rect.width * .25f, rect.height * .36f);
            float rim = rect.height * .13f;
            AddTrack(mesh, Rect.MinMaxRect(rect.xMin - rim, rect.yMin - rim, rect.xMax + rim, rect.yMax + rim),
                slope, new Color32(39, 40, 43, 255), new Color32(65, 65, 67, 255));
            AddTrack(mesh, rect, slope, new Color32(12, 12, 12, 255), new Color32(6, 6, 6, 255));
            if (fillAmount <= 0f) return;

            int start = mesh.currentVertCount;
            float width = (rect.width - slope) * fillAmount;
            for (int row = 0; row < Rows.Length; row++)
            {
                float y = Rows[row];
                for (int column = 0; column < Columns.Length; column++)
                {
                    var tint = column == 1 ? Center[row] : Edge[row];
                    mesh.AddVert(new Vector3(rect.xMin + width * Columns[column] + slope * y,
                        rect.yMin + rect.height * y, 0f), (Color32)((Color)tint * color), Vector2.zero);
                }
            }

            for (int row = 1; row < Rows.Length; row++)
            for (int column = 1; column < Columns.Length; column++)
            {
                int bottom = start + (row - 1) * Columns.Length + column - 1;
                int top = bottom + Columns.Length;
                mesh.AddTriangle(bottom, top, top + 1);
                mesh.AddTriangle(bottom, top + 1, bottom + 1);
            }
        }

        private void AddTrack(VertexHelper mesh, Rect bounds, float slope, Color32 bottom, Color32 top)
        {
            int start = mesh.currentVertCount;
            var lower = (Color32)((Color)bottom * color);
            var upper = (Color32)((Color)top * color);
            mesh.AddVert(new Vector3(bounds.xMin, bounds.yMin), lower, Vector2.zero);
            mesh.AddVert(new Vector3(bounds.xMin + slope, bounds.yMax), upper, Vector2.zero);
            mesh.AddVert(new Vector3(bounds.xMax, bounds.yMax), upper, Vector2.zero);
            mesh.AddVert(new Vector3(bounds.xMax - slope, bounds.yMin), lower, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
