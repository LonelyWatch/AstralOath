using System;
using System.Collections.Generic;
using System.Globalization;
using AstralOath.Client.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static class BindingMapEditorOperations
    {
        internal const string KeyField = "key";
        internal const string ReferenceField = "component";

        internal static SerializedProperty Entries(SerializedObject serialized) =>
            serialized.FindProperty("entries");

        internal static GameObject ReferenceOwner(Object reference)
        {
            if (reference is GameObject gameObject)
                return gameObject;
            return reference is Component component && component ? component.gameObject : null;
        }

        public static bool TryGetHierarchyBindings(GameObject gameObject, out string[] keys)
        {
            keys = Array.Empty<string>();
            if (!IsSceneObject(gameObject))
                return false;

            var binder = NearestBinder(gameObject);
            if (!binder)
                return PrefabRoot(gameObject);

            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            var matches = new List<string>();
            for (int index = 0; index < entries.arraySize; index++)
            {
                var entry = entries.GetArrayElementAtIndex(index);
                var reference = entry.FindPropertyRelative(ReferenceField).objectReferenceValue;
                if (ReferenceOwner(reference) == gameObject)
                    matches.Add(entry.FindPropertyRelative(KeyField).stringValue);
            }
            keys = matches.ToArray();
            return true;
        }

        public static bool Resize(BindingMap binder, int count)
        {
            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            count = Mathf.Max(0, count);
            int previousCount = entries.arraySize;
            if (count == previousCount)
                return false;

            entries.arraySize = count;
            // Unity copies the final element when growing arrays; new binding rows must be blank.
            for (int index = previousCount; index < count; index++)
            {
                var entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative(KeyField).stringValue = string.Empty;
                entry.FindPropertyRelative(ReferenceField).objectReferenceValue = null;
            }
            return Apply(serialized, "修改绑定总数");
        }

        public static bool Add(GameObject gameObject)
        {
            if (!IsSceneObject(gameObject))
                return false;

            var binder = NearestBinder(gameObject);
            var prefabRoot = binder ? null : PrefabRoot(gameObject);
            if (!binder && !prefabRoot)
                return false;

            const string undoName = "添加绑定";
            if (binder)
                return AddToBinder(binder, gameObject, undoName, startUndoGroup: true);

            // Creating the binder and its first entry is one Undo action.
            int group = BeginUndo(undoName);
            try
            {
                binder = Undo.AddComponent<BindingMap>(prefabRoot);
                return binder && AddToBinder(binder, gameObject, undoName, startUndoGroup: false);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        public static bool Remove(GameObject gameObject)
        {
            if (!IsSceneObject(gameObject))
                return false;

            var binder = NearestBinder(gameObject);
            if (!binder)
                return false;

            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            for (int index = entries.arraySize - 1; index >= 0; index--)
            {
                if (ReferenceOwner(entries.GetArrayElementAtIndex(index)
                        .FindPropertyRelative(ReferenceField).objectReferenceValue) == gameObject)
                    entries.DeleteArrayElementAtIndex(index);
            }
            return Apply(serialized, "移除对象绑定");
        }

        public static void AddEmpty(BindingMap binder)
        {
            using var serialized = new SerializedObject(binder);
            Append(Entries(serialized), string.Empty, null);
            Apply(serialized, "添加绑定");
        }

        public static void RemoveAt(BindingMap binder, int index)
        {
            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            if (index < 0 || index >= entries.arraySize)
                return;

            entries.DeleteArrayElementAtIndex(index);
            Apply(serialized, "移除绑定");
        }

        public static bool SetReference(BindingMap binder, int index, Object reference)
        {
            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            if (index < 0 || index >= entries.arraySize)
                return false;

            var field = entries.GetArrayElementAtIndex(index).FindPropertyRelative(ReferenceField);
            var gameObject = ReferenceOwner(reference);
            if (!gameObject || ReferenceOwner(field.objectReferenceValue) != gameObject)
                return false;

            field.objectReferenceValue = reference;
            return Apply(serialized, "切换绑定组件");
        }

        public static Object[] ReferenceChoices(GameObject gameObject)
        {
            var choices = new List<Object> { gameObject };
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component)
                    choices.Add(component);
            }
            return choices.ToArray();
        }

        private static bool IsSceneObject(GameObject gameObject) =>
            gameObject && gameObject.scene.IsValid() && !EditorUtility.IsPersistent(gameObject);

        private static BindingMap NearestBinder(GameObject gameObject)
        {
            for (var current = gameObject.transform; current; current = current.parent)
            {
                if (current.TryGetComponent<BindingMap>(out var binder))
                    return binder;
            }
            return null;
        }

        private static GameObject PrefabRoot(GameObject gameObject)
        {
            var stage = PrefabStageUtility.GetPrefabStage(gameObject);
            return stage != null && stage.IsPartOfPrefabContents(gameObject) ? stage.prefabContentsRoot : null;
        }

        private static bool AddToBinder(BindingMap binder, GameObject gameObject, string undoName, bool startUndoGroup)
        {
            using var serialized = new SerializedObject(binder);
            var entries = Entries(serialized);
            if (ContainsObject(entries, gameObject))
                return false;

            Append(entries, UniqueKey(entries, gameObject.name), DefaultReference(gameObject));
            return startUndoGroup ? Apply(serialized, undoName) : serialized.ApplyModifiedProperties();
        }

        private static bool ContainsObject(SerializedProperty entries, GameObject gameObject)
        {
            for (int index = 0; index < entries.arraySize; index++)
            {
                var reference = entries.GetArrayElementAtIndex(index).FindPropertyRelative(ReferenceField);
                if (ReferenceOwner(reference.objectReferenceValue) == gameObject)
                    return true;
            }
            return false;
        }

        private static string UniqueKey(SerializedProperty entries, string objectName)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.arraySize; index++)
                used.Add(entries.GetArrayElementAtIndex(index).FindPropertyRelative(KeyField).stringValue);

            string baseKey = string.IsNullOrWhiteSpace(objectName) ? "Binding" : objectName.Trim();
            string key = baseKey;
            for (int suffix = 2; used.Contains(key); suffix++)
                key = baseKey + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            return key;
        }

        private static Component DefaultReference(GameObject gameObject)
        {
            if (gameObject.TryGetComponent<GameButton>(out var button))
                return button;
            if (gameObject.TryGetComponent<GameText>(out var text))
                return text;
            // UI objects already have a RectTransform in place of Transform.
            return gameObject.transform;
        }

        private static void Append(SerializedProperty entries, string key, Object reference)
        {
            int index = entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative(KeyField).stringValue = key;
            entry.FindPropertyRelative(ReferenceField).objectReferenceValue = reference;
        }

        private static bool Apply(SerializedObject serialized, string undoName)
        {
            if (!serialized.hasModifiedProperties)
                return false;

            BeginUndo(undoName);
            return serialized.ApplyModifiedProperties();
        }

        private static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
    }
}
