using System;
using System.Collections;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class UIViewRuntimeTests
    {
        private GameObject prefab;
        private GameObject parent;
        private GameObject label;
        private BindingMap bindings;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Test Window");
            parent = new GameObject("View Root");
            label = new GameObject("Label");
            label.transform.SetParent(prefab.transform, false);
            bindings = prefab.AddComponent<BindingMap>();
            SetEntries(("label", label), ("transform", label.transform));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void BindingsPreserveGameObjectAndComponentReferenceTypes()
        {
            Assert.That(bindings.Require<GameObject>("label"), Is.SameAs(label));
            Assert.That(bindings.Require<Transform>("transform"), Is.SameAs(label.transform));
            var error = Assert.Throws<InvalidOperationException>(() => bindings.Require<Transform>("label"));
            Assert.That(error.Message, Does.Contain("label").And.Contain("Transform").And.Contain("GameObject"));
        }

        [Test]
        public void BindingsRejectDuplicateKeys()
        {
            SetEntries(("label", label), ("label", label.transform));
            var error = Assert.Throws<InvalidOperationException>(bindings.Validate);
            Assert.That(error.Message, Does.Contain("label").And.Contain("Duplicate"));
        }

        [Test]
        public void BindingsRejectMissingAndOutOfSubtreeObjects()
        {
            SetEntries(("missing", null));
            var missing = Assert.Throws<InvalidOperationException>(bindings.Validate);
            Assert.That(missing.Message, Does.Contain("missing"));
            SetEntries(("outside", parent));
            var outside = Assert.Throws<InvalidOperationException>(bindings.Validate);
            Assert.That(outside.Message, Does.Contain("outside").And.Contain("Test Window"));
        }

        [Test]
        public void RegistryRejectsDuplicateTypesAndAddresses()
        {
            var registry = Registered();
            Assert.Throws<InvalidOperationException>(() => registry.Register<TestView>());
            Assert.Throws<InvalidOperationException>(() => registry.Register<ConflictingView>());
        }

        [Test]
        public void ViewUsesRegisteredPrefabAndOwnerReleasesInstanceBeforeAsset()
        {
            var assets = new TestAssets(Task.FromResult(prefab), () =>
                Assert.That(parent.transform.childCount, Is.Zero));
            using var owner = new BindingScope(default);
            var view = Registered().CreateAsync<TestView>(assets, parent.transform, owner).GetAwaiter().GetResult();
            Assert.That(assets.Address, Is.EqualTo("test/window"));
            Assert.That(view.Root, Is.Not.SameAs(prefab));
            Assert.That(view.Root.transform.parent, Is.SameAs(parent.transform));
            Assert.That(view.Label, Is.SameAs(view.Root.transform.GetChild(0).gameObject));
            Assert.That(view.Root.activeSelf, Is.False);
            view.Show();
            Assert.That(view.Root.activeSelf, Is.True);
            view.Hide();
            Assert.That(view.Root.activeSelf, Is.False);

            owner.Dispose();
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(assets.Released, Is.EqualTo(1));
        }

        [Test]
        public void InvalidPrefabReleasesAssetAndInstance()
        {
            Object.DestroyImmediate(bindings);
            var assets = new TestAssets(Task.FromResult(prefab));
            using var owner = new BindingScope(default);
            var creation = Registered().CreateAsync<TestView>(assets, parent.transform, owner);
            var error = Assert.Throws<InvalidOperationException>(() => creation.GetAwaiter().GetResult());
            Assert.That(error.Message, Does.Contain("BindingMap"));
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(assets.Released, Is.EqualTo(1));
            owner.Dispose();
            Assert.That(assets.Released, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OwnerDisposalReleasesAssetAndRejectsLatePrefab()
        {
            var pending = new TaskCompletionSource<GameObject>();
            var assets = new TestAssets(pending.Task);
            using var owner = new BindingScope(default);
            var creation = Registered().CreateAsync<TestView>(assets, parent.transform, owner);
            Assert.That(creation.IsCompleted, Is.False);
            owner.Dispose();
            Assert.That(assets.Released, Is.EqualTo(1));
            pending.SetResult(prefab);
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (!creation.IsCompleted && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(creation.IsCanceled, Is.True);
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(assets.Released, Is.EqualTo(1));
        }

        private void SetEntries(params (string key, Object value)[] values)
        {
            var data = new SerializedObject(bindings);
            var entries = data.FindProperty("entries");
            entries.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("key").stringValue = values[i].key;
                entry.FindPropertyRelative("component").objectReferenceValue = values[i].value;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UIViewRegistry Registered()
        {
            var registry = new UIViewRegistry();
            registry.Register<TestView>();
            return registry;
        }

        public sealed class TestView : UIView
        {
            public override string PrefabPath => "test/window";
            public GameObject Label => Bindings.Require<GameObject>("label");
            public override void Validate() => _ = Label;
        }

        public sealed class ConflictingView : UIView
        {
            public override string PrefabPath => "test/window";
        }

        private sealed class TestAssets : IAssetService
        {
            private readonly Task<GameObject> result;
            private readonly Action onRelease;

            internal TestAssets(Task<GameObject> result, Action onRelease = null)
            {
                this.result = result;
                this.onRelease = onRelease;
            }

            internal string Address { get; private set; }
            internal int Released { get; private set; }

            public async Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                Address = address;
                scope.Add(() =>
                {
                    Released++;
                    onRelease?.Invoke();
                });
                return await result as T;
            }
        }
    }
}
