using System.IO;
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
            Assert.That(typeof(FoundationWindow).BaseType, Is.EqualTo(typeof(UIBaseView)));
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(FoundationWindow)), Is.False,
                "Business views must inherit the plain UIBaseView class; prefabs retain only the generic Binder.");
        }

        [Test]
        public void WindowViewAndBinderNamesMatchThePrefab()
        {
            string prefabName = Path.GetFileNameWithoutExtension(PrefabPath);
            const string modulePath = "Assets/Game/Modules/FoundationDemo";
            Assert.That(typeof(FoundationWindow).Name, Is.EqualTo(prefabName));
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>($"{modulePath}/Scripts/{prefabName}.cs"),
                Is.Not.Null, "The View source file must use the prefab name.");
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>($"{modulePath}/Lua/{prefabName}Binder.lua.txt"),
                Is.Not.Null, "The business Binder must use the prefab name plus Binder.");
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>($"{modulePath}/Lua/{prefabName}Binder.exports.txt"),
                Is.Not.Null, "The Binder export contract must use the same stem.");
        }

        [Test]
        public void WindowPrefabContainsGenericBindingsAndNoBusinessViewScript()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BindingMap>(), Is.Not.Null);
            var scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(scripts.All(script => script != null), Is.True, "The migrated prefab must have no missing scripts.");
            Assert.That(scripts.Any(script => script.GetType() == typeof(FoundationWindow)), Is.False,
                "FoundationWindow must be created by the UIBaseView registry, not attached to this prefab.");
        }
    }
}
