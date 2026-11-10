using System;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    [InitializeOnLoad]
    internal static class BindingMapHierarchy
    {
        private const float ButtonWidth = 19f;
        private const float RightMargin = 21f;
        private static readonly GUIContent AddLabel = new GUIContent("+", "绑定此对象");
        private static GUIStyle keyStyle;

        static BindingMapHierarchy()
        {
            EditorApplication.hierarchyWindowItemOnGUI += Draw;
            Undo.undoRedoPerformed += EditorApplication.RepaintHierarchyWindow;
        }

        private static void Draw(int instanceId, Rect row)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || row.width < ButtonWidth + RightMargin)
                return;

            var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (!BindingMapEditorOperations.TryGetHierarchyBindings(gameObject, out string[] keys))
                return;

            // Leave space for Unity's prefab navigation arrow.
            var rect = new Rect(row.xMax - RightMargin - ButtonWidth, row.y, ButtonWidth, row.height);
            bool bound = keys.Length > 0;
            string names = bound ? string.Join(", ", Array.ConvertAll(keys,
                key => string.IsNullOrWhiteSpace(key) ? "未命名" : key)) : string.Empty;
            if (bound)
                DrawBindingNames(row, rect, gameObject.name, names);
            var button = bound ? new GUIContent("−", "移除此对象的绑定\n" + names) : AddLabel;
            if (!GUI.Button(rect, button, EditorStyles.miniButton))
                return;

            if (bound)
                BindingMapEditorOperations.Remove(gameObject);
            else
                BindingMapEditorOperations.Add(gameObject);

            EditorApplication.RepaintHierarchyWindow();
        }

        private static void DrawBindingNames(Rect row, Rect button, string objectName, string names)
        {
            if (keyStyle == null)
            {
                keyStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    richText = false
                };
            }
            keyStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.08f, 0.48f, 0.18f);

            // Keep the object's icon and name readable; clipped keys remain available in the tooltip.
            float nameEnd = row.x + 18f + EditorStyles.label.CalcSize(new GUIContent(objectName)).x + 6f;
            float available = button.x - 4f - nameEnd;
            if (available <= 0)
                return;

            var content = new GUIContent(names, names);
            float width = Mathf.Min(available, keyStyle.CalcSize(content).x);
            var rect = new Rect(button.x - 4f - width, row.y, width, row.height);
            GUI.Label(rect, content, keyStyle);
        }
    }
}
