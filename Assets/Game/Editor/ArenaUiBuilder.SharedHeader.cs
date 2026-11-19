using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string HeaderBackPrefab = Common + "Prefab/HeaderBackButton.prefab";
        private const string HeaderTokenPrefab = Common + "Prefab/TopBarToken.prefab";

        private static void GenerateHeaderControls()
        {
            var root = Root("HeaderBackButton");
            // The original 250-wide image has a chevron at x=23..53. Only the first
            // 66 units are the hit target so the title and help remain independent.
            var button = Button(root, null, Color.clear);
            var face = Area(root, "Native back", 0, 0, 250f / 66, 1);
            var image = Paint(face, CommonArt("cm_hud_top_btn_back"), Color.white);
            button.targetGraphic = image;
            PressSprites(button, image.sprite, CommonArt("cm_hud_top_btn_back_p"));
            button.FeedbackSound = GameUiFeedback.Sound.Back;
            Bind(root, ("button", button));
            Save(root.gameObject, HeaderBackPrefab);

            var token = Root("TopBarToken");
            var group = token.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = group.childControlWidth = true;
            group.childForceExpandHeight = group.childForceExpandWidth = false;
            group.spacing = -3f;
            var icon = Paint(Area(token, "Icon", 0, 0, 1, 1), null, Color.white);
            icon.preserveAspect = true;
            var iconBox = icon.gameObject.AddComponent<LayoutElement>();
            iconBox.preferredWidth = iconBox.preferredHeight = 40.32f * HomeDesignScale;
            var plus = Picture(icon.rectTransform, "Plus", CommonArt("cm_hud_top_btn_plus"), .48f, -.02f, .69f, .69f, true);
            var amount = Text(token, "Balance", null, 26f * .8f * HomeDesignScale, 0, 0, 1, 1);
            amount.enableWordWrapping = false;
            amount.overflowMode = TextOverflowModes.Overflow;
            Bind(token, ("icon", icon), ("value", amount), ("plus", plus.gameObject));
            Save(token.gameObject, HeaderTokenPrefab);
        }

        private static GameButton SharedHeaderBack(RectTransform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(HeaderBackPrefab), parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(66f, 66f) * HomeDesignScale;
            rect.anchoredPosition = Vector2.zero;
            return go.GetComponent<BindingMap>().Require<GameButton>("button");
        }

        private static void BuildSummonCurrencies(RectTransform parent, List<(string Key, Object Target)> bindings)
        {
            var row = HomeRightRect(parent, "Summoning currencies", -310, 33, 0, 50, new Vector2(1, .5f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.spacing = 4f * HomeDesignScale;
            row.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var prefab = Load<GameObject>(HeaderTokenPrefab);
            var slots = new[] { ("transmitBalance", "token_hero2"), ("moonBalance", "token_ticket_moon"),
                ("mysticBalance", "token_ticket_special"), ("balance", "token_ticket_rare"), ("skyBalance", "token_crystal") };
            foreach (var (key, sprite) in slots)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row);
                var map = go.GetComponent<BindingMap>();
                map.Require<Image>("icon").sprite = SummonArt(sprite);
                map.Require<GameObject>("plus").SetActive(key != "transmitBalance");
                bindings.Add((key, map.Require<GameText>("value")));
            }
        }
    }
}
