using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    internal static class GameUICreationMenu
    {
        [MenuItem("GameObject/UI/项目文本 (GameText)", false, 2002)]
        private static void CreateText(MenuCommand command) => Create(command, false);

        [MenuItem("GameObject/UI/项目按钮 (GameButton)", false, 2032)]
        private static void CreateButton(MenuCommand command) => Create(command, true);

        [MenuItem("GameObject/UI/通用确认按钮", false, 2033)]
        private static void CreateConfirm(MenuCommand command) => Create(command, true, CommonUiPrefabs.ButtonPath);

        private static void Create(MenuCommand command, bool button, string prefabPath = null)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string undoName = button ? "创建项目按钮" : "创建项目文本";
            Undo.SetCurrentGroupName(undoName);
            try
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                var parent = command.context as GameObject ?? Selection.activeGameObject;
                if (parent != null && (EditorUtility.IsPersistent(parent) ||
                    StageUtility.GetStageHandle(parent) != StageUtility.GetCurrentStageHandle()))
                    parent = null;

                // Preview-environment canvases must not become part of the prefab's hierarchy.
                if (stage != null && (parent == null || !stage.IsPartOfPrefabContents(parent)))
                    parent = stage.prefabContentsRoot;

                Scene scene = parent != null ? parent.scene : SceneManager.GetActiveScene();
                if (parent == null)
                    parent = FindCanvas(scene);
                if (parent == null || parent.GetComponentInParent<Canvas>(true) == null)
                    parent = CreateCanvas(parent, scene, undoName);
                if (stage == null)
                    EnsureEventSystem(scene, undoName);

                GameObject element = prefabPath == null
                    ? (button ? CreateButtonObject() : CreateTextObject())
                    : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
                SceneManager.MoveGameObjectToScene(element, scene);
                GameObjectUtility.SetParentAndAlign(element, parent);
                foreach (Transform child in element.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = parent.layer;
                GameObjectUtility.EnsureUniqueNameForSibling(element);
                Undo.RegisterCreatedObjectUndo(element, undoName);
                Selection.activeGameObject = element;
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        private static GameObject CreateTextObject()
        {
            var element = new GameObject("GameText", typeof(RectTransform), typeof(GameText));
            var text = element.GetComponent<GameText>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = TMP_Settings.defaultFontSize;
            text.text = "New Text";
            text.color = Color.white;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(200, 50);
            return element;
        }

        private static GameObject CreateButtonObject()
        {
            var element = new GameObject("GameButton", typeof(RectTransform), typeof(Image), typeof(GameButton));
            element.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 40);
            var image = element.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            element.GetComponent<GameButton>().targetGraphic = image;

            var label = CreateTextObject();
            GameObjectUtility.SetParentAndAlign(label, element);
            var text = label.GetComponent<GameText>();
            text.text = "Button";
            text.fontSize = 24;
            text.color = new Color(0.2f, 0.2f, 0.2f, 1);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return element;
        }

        private static GameObject FindCanvas(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isActiveAndEnabled && (canvas.hideFlags & HideFlags.HideInHierarchy) == 0)
                        return canvas.gameObject;
            return null;
        }

        private static GameObject CreateCanvas(GameObject parent, Scene scene, string undoName)
        {
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvas, scene);
            GameObjectUtility.SetParentAndAlign(canvas, parent);
            canvas.layer = LayerMask.NameToLayer("UI");
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GameObjectUtility.EnsureUniqueNameForSibling(canvas);
            Undo.RegisterCreatedObjectUndo(canvas, undoName);
            return canvas;
        }

        private static void EnsureEventSystem(Scene scene, string undoName)
        {
            if (StageUtility.GetCurrentStageHandle().FindComponentOfType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
            GameObjectUtility.EnsureUniqueNameForSibling(eventSystem);
            Undo.RegisterCreatedObjectUndo(eventSystem, undoName);
        }
    }
}
