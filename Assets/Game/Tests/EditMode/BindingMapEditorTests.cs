using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using static AstralOath.Editor.BindingMapEditorOperations;

namespace AstralOath.Client.Tests
{
    public sealed class BindingMapEditorTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();
        private string prefabPath;

        [TearDown]
        public void TearDown()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == prefabPath)
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }

            foreach (var root in roots)
            {
                if (root)
                    Object.DestroyImmediate(root);
            }
            roots.Clear();
            if (prefabPath != null)
                AssetDatabase.DeleteAsset(prefabPath);
            prefabPath = null;
        }

        [Test]
        public void UnityUsesTheCustomBindingMapInspector()
        {
            var binder = CreateRoot().AddComponent<BindingMap>();
            var inspector = UnityEditor.Editor.CreateEditor(binder);
            try
            {
                Assert.That(inspector.GetType().Name, Is.EqualTo("BindingMapEditor"));
            }
            finally
            {
                Object.DestroyImmediate(inspector);
            }
        }

        [Test]
        public void AddPrefersGameButtonAndKeepsOneRowPerObjectWithDistinctKeys()
        {
            var binder = CreateRoot().AddComponent<BindingMap>();
            var first = CreateChild(binder.transform, "Action", typeof(RectTransform), typeof(GameButton));
            var second = CreateChild(binder.transform, "Action", typeof(RectTransform));
            var third = CreateChild(binder.transform, "Action");

            Assert.That(Add(first), Is.True);
            Assert.That(Add(second), Is.True);
            Assert.That(Add(first), Is.False);
            Assert.That(Add(third), Is.True);

            Assert.That(Read(binder), Is.EqualTo(new[]
            {
                ("Action", (Object)first.GetComponent<GameButton>()),
                ("Action_2", second.transform),
                ("Action_3", third.transform)
            }));
        }

        [Test]
        public void HierarchyAddAndRemoveCanBeUndoneAndRedoneIndependently()
        {
            var binder = CreateRoot().AddComponent<BindingMap>();
            var child = CreateChild(binder.transform, "Target");

            Assert.That(Add(child), Is.True);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(Read(binder), Is.Empty);
            Undo.PerformRedo();
            Assert.That(Read(binder), Is.EqualTo(new[] { ("Target", (Object)child.transform) }));

            Assert.That(Remove(child), Is.True);
            Undo.FlushUndoRecordObjects();
            Assert.That(Read(binder), Is.Empty);
            Undo.PerformUndo();
            Assert.That(Read(binder), Is.EqualTo(new[] { ("Target", (Object)child.transform) }));
            Undo.PerformRedo();
            Assert.That(Read(binder), Is.Empty);
        }

        [Test]
        public void HierarchyUsesNearestBinderAndIgnoresUnownedSceneObjects()
        {
            var outer = CreateRoot().AddComponent<BindingMap>();
            var inner = CreateChild(outer.transform, "Nested").AddComponent<BindingMap>();
            var leaf = CreateChild(inner.transform, "Leaf");
            var sibling = CreateChild(outer.transform, "Sibling");
            var unrelated = CreateRoot();

            Assert.That(Add(unrelated), Is.False);
            Assert.That(unrelated.GetComponent<BindingMap>(), Is.Null);
            Assert.That(Add(leaf), Is.True);
            Assert.That(Add(sibling), Is.True);
            Assert.That(Read(outer), Is.EqualTo(new[] { ("Sibling", (Object)sibling.transform) }));
            Assert.That(Read(inner), Is.EqualTo(new[] { ("Leaf", (Object)leaf.transform) }));

            Assert.That(Remove(leaf), Is.True);
            Assert.That(Read(inner), Is.Empty);
            Assert.That(Read(outer), Is.EqualTo(new[] { ("Sibling", (Object)sibling.transform) }));
        }

        [Test]
        public void TypeSelectionStoresTheExactComponentOrGameObject()
        {
            var binder = CreateRoot().AddComponent<BindingMap>();
            var child = CreateChild(binder.transform, "Target");
            var first = child.AddComponent<BoxCollider>();
            var second = child.AddComponent<BoxCollider>();
            Assert.That(Add(child), Is.True);
            Assert.That(ReferenceChoices(child), Is.EqualTo(new Object[] { child, child.transform, first, second }));

            Assert.That(SetReference(binder, 0, second), Is.True);
            Assert.That(Read(binder), Is.EqualTo(new[] { ("Target", (Object)second) }));
            Assert.That(SetReference(binder, 0, child), Is.True);
            Assert.That(Read(binder), Is.EqualTo(new[] { ("Target", (Object)child) }));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(Read(binder), Is.EqualTo(new[] { ("Target", (Object)second) }));
        }

        [Test]
        public void InspectorCanUndoAddingAndDeletingARow()
        {
            var binder = CreateRoot().AddComponent<BindingMap>();
            AddEmpty(binder);
            Assert.That(Read(binder), Has.Length.EqualTo(1));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(Read(binder), Is.Empty);
            Undo.PerformRedo();
            Assert.That(Read(binder), Has.Length.EqualTo(1));

            RemoveAt(binder, 0);
            Undo.FlushUndoRecordObjects();
            Assert.That(Read(binder), Is.Empty);
            Undo.PerformUndo();
            Assert.That(Read(binder), Has.Length.EqualTo(1));
        }

        [Test]
        public void PrefabStageCreatesRootBinderInOneUndoAndPersistsBindings()
        {
            var root = CreateRoot();
            CreateChild(root.transform, "Target");
            SavePrefab(root);

            var stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(stage, Is.Not.Null);
            var stageRoot = stage.prefabContentsRoot;
            var child = stageRoot.transform.Find("Target").gameObject;
            Assert.That(Add(child), Is.True);
            Undo.FlushUndoRecordObjects();
            Assert.That(Read(stageRoot.GetComponent<BindingMap>()),
                Is.EqualTo(new[] { ("Target", (Object)child.transform) }));

            Undo.PerformUndo();
            Assert.That(stageRoot.GetComponent<BindingMap>(), Is.Null);
            Undo.PerformRedo();
            Assert.That(Read(stageRoot.GetComponent<BindingMap>()),
                Is.EqualTo(new[] { ("Target", (Object)child.transform) }));

            Assert.That(SetReference(stageRoot.GetComponent<BindingMap>(), 0, child), Is.True);
            Assert.That(stage.scene.isDirty, Is.True);
            PrefabUtility.SaveAsPrefabAsset(stageRoot, prefabPath, out bool saved);
            Assert.That(saved, Is.True);
            stage.ClearDirtiness();
            StageUtility.GoToMainStage();

            var reloaded = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var reloadedChild = reloaded.transform.Find("Target").gameObject;
                Assert.That(Read(reloaded.GetComponent<BindingMap>()),
                    Is.EqualTo(new[] { ("Target", (Object)reloadedChild) }));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(reloaded);
            }
        }

        [Test]
        public void HierarchyAddCreatesPrefabInstanceOverrideWithoutEditingTheSource()
        {
            var root = CreateRoot();
            root.AddComponent<BindingMap>();
            CreateChild(root.transform, "Target");
            var asset = SavePrefab(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            roots.Add(instance);
            var binder = instance.GetComponent<BindingMap>();

            Assert.That(Add(instance.transform.Find("Target").gameObject), Is.True);
            Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.True);
            Assert.That(Read(asset.GetComponent<BindingMap>()), Is.Empty);
            Assert.That(Read(binder), Has.Length.EqualTo(1));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.False);
            Assert.That(Read(binder), Is.Empty);
        }

        private GameObject CreateRoot()
        {
            var root = new GameObject("Binding Map Test");
            roots.Add(root);
            return root;
        }

        private static GameObject CreateChild(Transform parent, string name, params Type[] components)
        {
            var child = new GameObject(name, components);
            child.transform.SetParent(parent, false);
            return child;
        }

        private GameObject SavePrefab(GameObject root)
        {
            prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/BindingMapEditorTest.prefab");
            var asset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Assert.That(asset, Is.Not.Null);
            return asset;
        }

        private static (string key, Object value)[] Read(BindingMap binder)
        {
            Assert.That(binder, Is.Not.Null);
            using var serialized = new SerializedObject(binder);
            var entries = serialized.FindProperty("entries");
            var result = new (string, Object)[entries.arraySize];
            for (int index = 0; index < entries.arraySize; index++)
            {
                var entry = entries.GetArrayElementAtIndex(index);
                result[index] = (entry.FindPropertyRelative("key").stringValue,
                    entry.FindPropertyRelative("component").objectReferenceValue);
            }
            return result;
        }

    }
}
