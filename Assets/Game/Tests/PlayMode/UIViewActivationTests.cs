using System;
using System.Collections;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class UIViewActivationTests
    {
        private GameObject prefab;
        private GameObject parent;
        private TestAssets assets;

        [SetUp]
        public void SetUp()
        {
            ActivationProbe.Activations = 0;
            prefab = new GameObject("Active Window", typeof(RectTransform), typeof(BindingMap), typeof(ActivationProbe));
            var layout = (RectTransform)prefab.transform;
            layout.anchorMin = new Vector2(0.2f, 0.35f);
            layout.anchorMax = new Vector2(0.7f, 0.85f);
            layout.pivot = new Vector2(0.3f, 0.8f);
            layout.sizeDelta = new Vector2(320, 160);
            layout.anchoredPosition3D = new Vector3(90, -45, 3);
            parent = new GameObject("Window Parent", typeof(RectTransform));
            ((RectTransform)parent.transform).sizeDelta = new Vector2(1400, 900);
            assets = new TestAssets(prefab);
            Assert.That(ActivationProbe.Activations, Is.EqualTo(1), "The source probe should start enabled.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(parent);
            Object.Destroy(prefab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActivePrefabCloneEnablesOnlyWhenShown()
        {
            var registry = new UIViewRegistry();
            registry.Register<TestView>();
            using var owner = new BindingScope(default);
            var view = registry.CreateAsync<TestView>(assets, parent.transform, owner).GetAwaiter().GetResult();

            Assert.That(prefab.activeSelf, Is.True);
            Assert.That(view.Root.activeSelf, Is.False);
            Assert.That(view.Root.transform.parent, Is.SameAs(parent.transform));
            Assert.That(ActivationProbe.Activations, Is.EqualTo(1), "The clone should not enable during creation.");
            var layout = (RectTransform)view.Root.transform;
            Assert.That(layout.anchorMin, Is.EqualTo(new Vector2(0.2f, 0.35f)));
            Assert.That(layout.anchorMax, Is.EqualTo(new Vector2(0.7f, 0.85f)));
            Assert.That(layout.pivot, Is.EqualTo(new Vector2(0.3f, 0.8f)));
            Assert.That(layout.sizeDelta, Is.EqualTo(new Vector2(320, 160)));
            Assert.That(layout.anchoredPosition3D, Is.EqualTo(new Vector3(90, -45, 3)));
            view.Show();
            Assert.That(ActivationProbe.Activations, Is.EqualTo(2));

            owner.Dispose();
            yield return null;
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(assets.Released, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RejectedActivePrefabCloneNeverEnablesAndIsDestroyed()
        {
            var registry = new UIViewRegistry();
            registry.Register<RejectedView>();
            using var owner = new BindingScope(default);
            var creation = registry.CreateAsync<RejectedView>(assets, parent.transform, owner);

            Assert.Throws<InvalidOperationException>(() => creation.GetAwaiter().GetResult());
            yield return null;
            Assert.That(prefab.activeSelf, Is.True);
            Assert.That(ActivationProbe.Activations, Is.EqualTo(1), "Validation failure should never enable the clone.");
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(Object.FindObjectsOfType<ActivationProbe>(true),
                Is.EquivalentTo(new[] { prefab.GetComponent<ActivationProbe>() }));
            Assert.That(assets.Released, Is.EqualTo(1));
        }

        public sealed class ActivationProbe : MonoBehaviour
        {
            internal static int Activations;
            private void OnEnable() => Activations++;
        }

        public sealed class TestView : UIView
        {
            public override string PrefabPath => "test/window";
        }

        public sealed class RejectedView : UIView
        {
            public override string PrefabPath => "test/rejected-window";
            public override void Validate() => throw new InvalidOperationException("Rejected view.");
        }

        private sealed class TestAssets : IAssetService
        {
            private readonly GameObject prefab;

            internal TestAssets(GameObject prefab) => this.prefab = prefab;

            internal int Released { get; private set; }

            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                scope.Add(() => Released++);
                return Task.FromResult(prefab as T);
            }
        }
    }
}
