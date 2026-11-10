using System;
using System.Collections.Generic;
using System.Globalization;
using AstralOath.Client.Common;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    [CustomEditor(typeof(BindingMap))]
    internal sealed class BindingMapEditor : UnityEditor.Editor
    {
        private const float Gap = 3f;
        private const float DeleteWidth = 19f;
        private static readonly GUIContent DeleteLabel = new GUIContent("−", "移除此行绑定");

        public override void OnInspectorGUI()
        {
            var binder = (BindingMap)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("绑定列表", EditorStyles.boldLabel);
            var entries = BindingMapEditorOperations.Entries(serializedObject);
            int requestedCount = Mathf.Max(0, EditorGUILayout.DelayedIntField("绑定总数", entries.arraySize));
            bool resize = requestedCount != entries.arraySize;
            int removeIndex = -1;
            int typeIndex = -1;
            Object typeReference = null;

            for (int index = 0; index < entries.arraySize; index++)
            {
                var entry = entries.GetArrayElementAtIndex(index);
                var key = entry.FindPropertyRelative(BindingMapEditorOperations.KeyField);
                var reference = entry.FindPropertyRelative(BindingMapEditorOperations.ReferenceField);
                var line = EditorGUILayout.GetControlRect(false);
                SplitRow(line, out Rect keyRect, out Rect objectRect, out Rect typeRect, out Rect deleteRect);

                EditorGUI.PropertyField(keyRect, key, GUIContent.none);
                EditorGUI.PropertyField(objectRect, reference, GUIContent.none);
                var choice = DrawTypePopup(typeRect, reference.objectReferenceValue);
                if (choice)
                {
                    typeIndex = index;
                    typeReference = choice;
                }

                if (GUI.Button(deleteRect, DeleteLabel, EditorStyles.miniButton))
                    removeIndex = index;
            }

            bool add = GUILayout.Button("+ 添加绑定");
            bool changed = serializedObject.ApplyModifiedProperties();
            if (resize)
            {
                changed |= BindingMapEditorOperations.Resize(binder, requestedCount);
            }
            else if (removeIndex >= 0)
            {
                BindingMapEditorOperations.RemoveAt(binder, removeIndex);
                changed = true;
            }
            else if (add)
            {
                BindingMapEditorOperations.AddEmpty(binder);
                changed = true;
            }
            else if (typeIndex >= 0)
            {
                changed |= BindingMapEditorOperations.SetReference(binder, typeIndex, typeReference);
            }

            if (changed)
            {
                serializedObject.Update();
                entries = BindingMapEditorOperations.Entries(serializedObject);
                EditorApplication.RepaintHierarchyWindow();
            }
            DrawValidation(entries, binder);
        }

        private static void SplitRow(Rect row, out Rect key, out Rect reference, out Rect type, out Rect delete)
        {
            float available = Mathf.Max(0, row.width - DeleteWidth - 3 * Gap);
            float keyWidth = Mathf.Floor(available * 0.29f);
            float typeWidth = Mathf.Floor(available * 0.28f);
            float referenceWidth = available - keyWidth - typeWidth;

            key = new Rect(row.x, row.y, keyWidth, row.height);
            reference = new Rect(key.xMax + Gap, row.y, referenceWidth, row.height);
            type = new Rect(reference.xMax + Gap, row.y, typeWidth, row.height);
            delete = new Rect(type.xMax + Gap, row.y, DeleteWidth, row.height);
        }

        private static Object DrawTypePopup(Rect rect, Object reference)
        {
            var owner = BindingMapEditorOperations.ReferenceOwner(reference);
            if (!owner)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.Popup(rect, 0, new[] { "类型" });
                return null;
            }

            var choices = BindingMapEditorOperations.ReferenceChoices(owner);
            var labels = new GUIContent[choices.Length];
            var counts = new Dictionary<Type, int>();
            for (int index = 0; index < choices.Length; index++)
            {
                var type = choices[index].GetType();
                counts.TryGetValue(type, out int count);
                counts[type] = ++count;
                string label = count == 1 ? type.Name : $"{type.Name} ({count.ToString(CultureInfo.InvariantCulture)})";
                labels[index] = new GUIContent(label, type.FullName);
            }

            int selected = Array.IndexOf(choices, reference);
            int next = EditorGUI.Popup(rect, selected, labels);
            return next >= 0 && next != selected ? choices[next] : null;
        }

        private static void DrawValidation(SerializedProperty entries, BindingMap binder)
        {
            var firstRows = new Dictionary<string, int>(StringComparer.Ordinal);
            var issues = new List<string>();

            for (int index = 0; index < entries.arraySize; index++)
            {
                var entry = entries.GetArrayElementAtIndex(index);
                string key = entry.FindPropertyRelative(BindingMapEditorOperations.KeyField).stringValue;
                var reference = entry.FindPropertyRelative(BindingMapEditorOperations.ReferenceField).objectReferenceValue;
                string row = string.IsNullOrWhiteSpace(key) ? $"第 {index + 1} 行" : $"第 {index + 1} 行（{key}）";

                if (string.IsNullOrWhiteSpace(key))
                    issues.Add($"{row} 缺少 Key。");
                else if (firstRows.TryGetValue(key, out int first))
                    issues.Add($"{row} 与第 {first} 行的 Key 重复。");
                else
                    firstRows.Add(key, index + 1);

                var owner = BindingMapEditorOperations.ReferenceOwner(reference);
                if (!reference)
                    issues.Add($"{row} 缺少对象引用。");
                else if (!owner)
                    issues.Add($"{row} 需要 GameObject 或 Component 引用。");
                else if (!owner.transform.IsChildOf(binder.transform))
                    issues.Add($"{row} 的对象 {owner.name} 位于 Binder 子树之外。");
            }

            if (issues.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Error);
        }
    }
}
