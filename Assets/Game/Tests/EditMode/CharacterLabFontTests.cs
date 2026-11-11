using System.Linq;
using AstralOath.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabFontTests
    {
        private const string FontPath = "Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset";

        [Test]
        public void SavedCharacterLabPrefabKeepsTheChineseFontAndItsMatchingMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterLabSceneBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Generate CharacterLab before validating its saved typography.");
            ValidateLabels(new[] { prefab });
        }

        [Test]
        public void SavedCharacterLabStartupSceneKeepsTheChineseFontAndItsMatchingMaterial()
        {
            var scene = SceneManager.GetSceneByPath(CharacterLabSceneBuilder.ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(CharacterLabSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try { ValidateLabels(scene.GetRootGameObjects()); }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ValidateLabels(GameObject[] roots)
        {
            var expectedFont = AssetDatabase.LoadAssetAtPath<Object>(FontPath);
            Assert.That(expectedFont, Is.Not.Null);
            var expectedMaterial = new SerializedObject(expectedFont).FindProperty("material").objectReferenceValue;
            Assert.That(expectedMaterial, Is.Not.Null);
            var labels = roots.SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(component => component && component.GetType().FullName == "TMPro.TextMeshProUGUI").ToArray();
            Assert.That(labels, Is.Not.Empty);
            foreach (var label in labels)
            {
                var saved = new SerializedObject(label);
                Assert.That(saved.FindProperty("m_fontAsset").objectReferenceValue, Is.EqualTo(expectedFont),
                    label.name + " saved TMP's Latin default instead of the Chinese font.");
                Assert.That(saved.FindProperty("m_sharedMaterial").objectReferenceValue, Is.EqualTo(expectedMaterial),
                    label.name + " saved a material from another font atlas.");
            }
        }
    }
}
