using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class UIViewArchitectureTests
    {
        private const string PrefabPath = "Assets/Game/Modules/FoundationDemo/Res/Prefab/FoundationWindow.prefab";

        [Test]
        public void BusinessViewIsAPlainClassRatherThanAPrefabComponent()
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(FoundationView)), Is.False,
                "Business views must inherit the plain UIView class; prefabs retain only the generic Binder.");
        }

        [Test]
        public void WindowPrefabContainsGenericBindingsAndNoBusinessViewScript()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BindingMap>(), Is.Not.Null);
            var scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(scripts.All(script => script != null), Is.True, "The migrated prefab must have no missing scripts.");
            Assert.That(scripts.Any(script => script.GetType() == typeof(FoundationView)), Is.False,
                "FoundationView must be created by the UIView registry, not attached to this prefab.");
        }
    }
}
