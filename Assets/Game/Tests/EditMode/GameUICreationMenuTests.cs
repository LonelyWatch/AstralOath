using System;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class GameUICreationMenuTests
    {
        private const string TextMenu = "GameObject/UI/项目文本 (GameText)";
        private const string ButtonMenu = "GameObject/UI/项目按钮 (GameButton)";
        private Scene scene;
        private Scene previousScene;
        private string scenePath;
        private string prefabPath;
        private Object previousSelection;

        [SetUp]
        public void SetUp()
        {
            previousScene = SceneManager.GetActiveScene();
            previousSelection = Selection.activeObject;
            scene = OpenTemporaryEmptyScene(out scenePath);
            SceneManager.SetActiveScene(scene);
            Selection.activeObject = null;
            Undo.IncrementCurrentGroup();
        }

        [TearDown]
        public void TearDown()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == prefabPath)
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (prefabPath != null)
                AssetDatabase.DeleteAsset(prefabPath);
            prefabPath = null;
            if (scenePath != null)
                AssetDatabase.DeleteAsset(scenePath);
            scenePath = null;
            Selection.activeObject = previousSelection;
        }

        [Test]
        public void TextMenuCreatesOnlyGameTextUnderTheContextWithDefaultFontAndUsableLayout()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            var parent = new GameObject("Container", typeof(RectTransform));
            parent.transform.SetParent(canvas.transform, false);
            parent.layer = LayerMask.NameToLayer("UI");

            GameObject created = Create(TextMenu, parent);

            Assert.That(created.transform.parent, Is.EqualTo(parent.transform));
            AssertGameText(created);
            var text = created.GetComponent<TextMeshProUGUI>();
            Assert.That(text.rectTransform.rect.width, Is.GreaterThan(0));
            Assert.That(text.rectTransform.rect.height, Is.GreaterThan(0));
            Assert.That(text.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(created.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(created.layer, Is.EqualTo(parent.layer));
        }

        [Test]
        public void ButtonMenuCreatesOneGameButtonWithImageTargetAndStretchedGameTextChild()
        {
            var parent = new GameObject("Canvas", typeof(Canvas));
            GameObject created = Create(ButtonMenu, parent);

            Assert.That(created.transform.parent, Is.EqualTo(parent.transform));
            Assert.That(created.GetComponents<Button>(), Has.Length.EqualTo(1));
            Assert.That(created.GetComponent<Button>(), Is.TypeOf<GameButton>());
            Assert.That(created.GetComponent<Button>().targetGraphic, Is.EqualTo(created.GetComponent<Image>()));
            Assert.That(created.GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(created.GetComponent<RectTransform>().rect.width, Is.GreaterThan(0));
            Assert.That(created.GetComponent<RectTransform>().rect.height, Is.GreaterThan(0));
            Assert.That(created.transform.childCount, Is.EqualTo(1));
            var label = created.transform.GetChild(0).gameObject;
            AssertGameText(label);
            var rect = label.GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(label.GetComponent<TextMeshProUGUI>().alignment, Is.EqualTo(TextAlignmentOptions.Center));
        }

        [Test]
        public void NoCanvasCreatesCanvasAndEventSystemAndUndoesTheWholeCreationInOneStep()
        {
            GameObject created = Create(ButtonMenu, null);
            var canvas = created.GetComponentInParent<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.gameObject.scene, Is.EqualTo(scene));
            Assert.That(canvas.GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(ComponentsInScene<EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(ComponentsInScene<EventSystem>()[0].GetComponent<StandaloneInputModule>(), Is.Not.Null);

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(scene.GetRootGameObjects(), Is.Empty);
            Undo.PerformRedo();
            Assert.That(ComponentsInScene<Canvas>(), Has.Length.EqualTo(1));
            Assert.That(ComponentsInScene<EventSystem>(), Has.Length.EqualTo(1));
            Assert.That(ComponentsInScene<GameButton>(), Has.Length.EqualTo(1));
            Assert.That(ComponentsInScene<GameText>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void PlainContextGetsCanvasWithoutDuplicatingAnExistingEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var parent = new GameObject("UI Owner");
            var created = Create(TextMenu, parent);
            Assert.That(created.transform.parent.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(created.transform.parent.parent, Is.EqualTo(parent.transform));
            Assert.That(ComponentsInScene<EventSystem>(), Has.Length.EqualTo(1));
        }

        [Test]
        public void ExistingEventSystemInAnotherLoadedSceneIsReused()
        {
            Scene otherScene = default;
            string otherScenePath = null;
            try
            {
                otherScene = OpenTemporaryEmptyScene(out otherScenePath);
                SceneManager.SetActiveScene(scene);
                var existing = new GameObject("ExistingEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(existing, otherScene);

                var created = Create(TextMenu, null);

                Assert.That(created.scene, Is.EqualTo(scene));
                Assert.That(ComponentsInScene<EventSystem>(), Is.Empty);
                Assert.That(otherScene.GetRootGameObjects(), Is.EquivalentTo(new[] { existing }));
            }
            finally
            {
                if (otherScene.IsValid() && otherScene.isLoaded)
                    EditorSceneManager.CloseScene(otherScene, true);
                if (otherScenePath != null)
                    AssetDatabase.DeleteAsset(otherScenePath);
            }
        }

        [Test]
        public void RepeatedCreationUsesUniqueNamesAndUndoKeepsTheExistingParent()
        {
            var parent = new GameObject("Canvas", typeof(Canvas));
            var first = Create(TextMenu, parent);
            var second = Create(TextMenu, parent);
            Assert.That(first.name, Is.Not.EqualTo(second.name));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(parent, Is.Not.Null);
            Assert.That(parent.transform.childCount, Is.EqualTo(1));
            Assert.That(parent.transform.GetChild(0).gameObject, Is.EqualTo(first));
            Undo.PerformRedo();
            Assert.That(parent.transform.childCount, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PrefabModeCreatesInsideThePrefabWithoutChangingTheMainScene(bool useContext)
        {
            var mainCanvas = new GameObject("Main Scene Canvas", typeof(Canvas));
            var source = new GameObject("Window", typeof(RectTransform));
            prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/GameUICreationMenuTest.prefab");
            Assert.That(PrefabUtility.SaveAsPrefabAsset(source, prefabPath), Is.Not.Null);
            Object.DestroyImmediate(source);
            var mainRoots = scene.GetRootGameObjects();
            var stage = PrefabStageUtility.OpenPrefab(prefabPath);
            Assert.That(stage, Is.Not.Null);
            Selection.activeObject = null;

            var created = Create(ButtonMenu, useContext ? stage.prefabContentsRoot : null);

            Assert.That(stage.IsPartOfPrefabContents(created), Is.True);
            Assert.That(created.scene, Is.EqualTo(stage.scene));
            Assert.That(created.transform.IsChildOf(stage.prefabContentsRoot.transform), Is.True);
            Assert.That(mainCanvas.transform.childCount, Is.Zero);
            Assert.That(scene.GetRootGameObjects(), Is.EquivalentTo(mainRoots));
            Assert.That(ComponentsInScene<EventSystem>(), Is.Empty);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(stage.prefabContentsRoot.GetComponentsInChildren<GameButton>(true), Is.Empty);
        }

        private static Scene OpenTemporaryEmptyScene(out string path)
        {
            // Open a saved copy through the Editor API used by the other scene asset tests.
            // Only the copy is modified; existing editor scenes and the template remain untouched.
            path = "Assets/GameUICreationMenuTestScene-" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.That(AssetDatabase.CopyAsset("Assets/Settings/Scenes/URP2DSceneTemplate.unity", path), Is.True);
            var temporaryScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            foreach (var root in temporaryScene.GetRootGameObjects())
                Object.DestroyImmediate(root);
            return temporaryScene;
        }

        private T[] ComponentsInScene<T>() where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        private static void AssertGameText(GameObject created)
        {
            Assert.That(created.GetComponents<TextMeshProUGUI>(), Has.Length.EqualTo(1));
            Assert.That(created.GetComponent<TextMeshProUGUI>(), Is.TypeOf<GameText>());
            var text = created.GetComponent<TextMeshProUGUI>();
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "Project TMP default font must be configured.");
            Assert.That(text.font, Is.EqualTo(TMP_Settings.defaultFontAsset));
            Assert.That(text.raycastTarget, Is.False);
            Assert.That(text.text, Is.Not.Empty);
            Assert.That(text.font.HasCharacters(text.text, out uint[] _, true), Is.True,
                "The default label must be supported by the selected font.");
        }

        private static GameObject Create(string menuPath, GameObject context)
        {
            // Discover the actual menu callback so missing implementation is an assertion failure,
            // and the context matches a Hierarchy right-click rather than only the current selection.
            var menuType = Type.GetType("AstralOath.Editor.GameUICreationMenu, AstralOath.Client.Editor");
            Assert.That(menuType, Is.Not.Null, "Project UI creation menu has not been implemented.");
            var method = menuType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(candidate => candidate.GetCustomAttributes<MenuItem>()
                    .Any(item => item.menuItem == menuPath && !item.validate));
            Assert.That(method, Is.Not.Null, "Expected Hierarchy UI menu is missing: " + menuPath);
            method.Invoke(null, new object[] { new MenuCommand(context) });
            Assert.That(Selection.activeGameObject, Is.Not.Null);
            return Selection.activeGameObject;
        }
    }
}
