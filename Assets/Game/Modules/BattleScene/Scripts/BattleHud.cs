using System;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    public sealed class BattleHud : UIBaseView
    {
        public override string PrefabPath => "battle/battle-hud";

        public override void Validate()
        {
            foreach (string key in new[] { "skill1", "skill2", "skill3", "pause", "speed", "auto", "soulBurn", "startPreview" })
                Bindings.Require<GameButton>(key);
            foreach (string key in new[] { "wave", "activeName", "activeHp", "soulCount" })
                Bindings.Require<GameText>(key);
            Bindings.Require<Image>("orderHourglass");
            Bindings.Require<Image>("activePortrait");
            Bindings.Require<Image>("activeHpFill");
            Bindings.Require<Image>("soulFill");
            for (int slot = 1; slot <= 3; slot++)
                Bindings.Require<Image>("skillSelected" + slot);
        }

        // Compatibility display for authored HUD prefabs: only generic UI components are added.
        // The Binder owns listeners and the panel lifetime; this View only creates and shows UI.
        internal static PauseControls CreatePauseControls(BindingMap map, Func<string, string> label) =>
            new PauseControls(map, label);

        internal sealed class PauseControls : IDisposable
        {
            // Original CUTIN renders at 30000. Pause remains accessible during full-screen skills.
            private const int PauseOrder = 31000;
            private readonly GameObject root;
            internal GameButton Continue { get; }
            internal GameButton Restart { get; }

            internal PauseControls(BindingMap map, Func<string, string> label)
            {
                var ownerCanvas = map.GetComponentInParent<Canvas>();
                var pause = map.Require<GameButton>("pause");
                pause.targetGraphic.raycastTarget = true;
                // These generic components belong to the authored HUD object, survive a restart,
                // and are released with that object; no feature component is attached to a prefab.
                RaiseCanvas(pause.gameObject, ownerCanvas, PauseOrder);
                var panel = Rect(map.transform, "Pause Panel", Vector2.zero, Vector2.zero);
                root = panel.gameObject;
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.one;
                panel.offsetMin = panel.offsetMax = Vector2.zero;
                RaiseCanvas(root, ownerCanvas, PauseOrder + 1);
                var shield = root.AddComponent<Image>();
                shield.color = new Color(0f, 0f, 0f, 0.68f);
                shield.raycastTarget = true;
                var card = Rect(panel, "Background", Vector2.zero, new Vector2(580f, 370f)).gameObject.AddComponent<Image>();
                card.color = new Color32(25, 32, 43, 250);
                card.raycastTarget = false;
                var font = map.Require<GameText>("activeName");
                Caption(panel, "Title", label("battle.pause.title"), font, new Vector2(0f, 115f), 36f);
                Continue = Button(panel, "Continue", label("battle.pause.continue"), font, new Vector2(0f, 15f));
                Restart = Button(panel, "Restart", label("battle.pause.restart"), font, new Vector2(0f, -95f));
                root.SetActive(false);
            }

            internal void Show(bool visible)
            {
                if (visible) root.transform.SetAsLastSibling();
                root.SetActive(visible);
            }

            private static void RaiseCanvas(GameObject item, Canvas owner, int order)
            {
                var canvas = item.GetComponent<Canvas>();
                if (!canvas) canvas = item.AddComponent<Canvas>();
                canvas.worldCamera = owner.worldCamera;
                canvas.overrideSorting = true;
                canvas.sortingLayerID = owner.sortingLayerID;
                canvas.sortingOrder = order;
                if (!item.GetComponent<GraphicRaycaster>()) item.AddComponent<GraphicRaycaster>();
            }

            private static GameButton Button(Transform parent, string name, string caption, GameText font, Vector2 position)
            {
                var rect = Rect(parent, name, position, new Vector2(400f, 84f));
                var background = rect.gameObject.AddComponent<Image>();
                background.color = new Color32(69, 81, 98, 255);
                var button = rect.gameObject.AddComponent<GameButton>();
                button.targetGraphic = background;
                Caption(rect, "Caption", caption, font, Vector2.zero, 30f);
                return button;
            }

            private static void Caption(Transform parent, string name, string caption, GameText font, Vector2 position, float size)
            {
                var text = Rect(parent, name, position, new Vector2(480f, 66f)).gameObject.AddComponent<GameText>();
                text.font = font.font;
                text.fontSharedMaterial = font.fontSharedMaterial;
                text.fontSize = size;
                text.alignment = TextAlignmentOptions.Center;
                text.text = caption;
                text.raycastTarget = false;
                text.enableWordWrapping = false;
            }

            private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
            {
                var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                rect.SetParent(parent, false);
                rect.gameObject.layer = parent.gameObject.layer;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                return rect;
            }

            public void Dispose()
            {
                if (!root) return;
                root.SetActive(false);
                root.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
