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
        private static readonly GUIContent RemoveLabel = new GUIContent("−", "移除此对象的绑定");

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
            if (!BindingMapEditorOperations.GetHierarchyState(gameObject, out bool bound))
                return;

            // Leave space for Unity's prefab navigation arrow.
            var rect = new Rect(row.xMax - RightMargin - ButtonWidth, row.y, ButtonWidth, row.height);
            if (!GUI.Button(rect, bound ? RemoveLabel : AddLabel, EditorStyles.miniButton))
                return;

            if (bound)
                BindingMapEditorOperations.Remove(gameObject);
            else
                BindingMapEditorOperations.Add(gameObject);

            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
