using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.FoundationDemo;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string GmPath = "Assets/Game/Modules/FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab";

        [MenuItem("Tools/游戏主页/生成开发版 GM 窗口")]
        public static void GenerateDevelopmentGmWindow()
        {
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows.ToDictionary(row => row.key, row => row.text);
            Save(BuildDevelopmentGmWindow(), GmPath);
            ConfigureCollectors();
            UIViewRegistry.ValidatePrefab<DevelopmentGmWindow>(Load<GameObject>(GmPath));
        }

        private static GameObject BuildDevelopmentGmWindow()
        {
            var root = Root("DevelopmentGmWindow");
            var keys = new List<(string Key, Object Target)>();
            var entry = Area(root, "GM entry", 0, 0, 0, 0);
            entry.anchorMin = entry.anchorMax = new Vector2(1, .67f);
            entry.pivot = new Vector2(1, .5f);
            entry.anchoredPosition = new Vector2(-8, 0);
            entry.sizeDelta = new Vector2(86, 74);
            keys.Add(("entry", Button(entry, null, Color.clear)));
            Picture(entry, "Help glyph", Art("cm_hud_top_btn_help"), .16f, 0, .68f, .68f, true);
            Text(entry, "GM", "gm.short", 21, 0, .64f, 1, .36f, Color.white, TextAlignmentOptions.Center);

            var panel = Area(root, "GM overlay", 0, 0, 1, 1);
            Paint(panel, null, new Color(0, 0, 0, .68f), true);
            keys.Add(("panel", panel.gameObject));
            var modal = Area(panel, "Dialog", 0, .09f, 1, .82f);
            Paint(modal, DraftSprite("renew_popup_bg_2_alpha"), Color.white, true, true);
            Text(modal, "Title", "gm.title", 40, .23f, .018f, .54f, .099f, Color.white, TextAlignmentOptions.Center);
            Rule(modal, "Title line", .21f, .117f, .58f, .0025f);

            GameButton Command(RectTransform parent, string key, float x, float y, float w, float h,
                string sprite = "_btn_small_black")
            {
                var b = Action(parent, key, "gm." + key, 25, x, y, w, h, out var caption, CommonArt(sprite), Color.white);
                keys.Add((key, b));
                if (key == "submit") keys.Add(("submitLabel", caption));
                return b;
            }

            Command(modal, "resources", .19f, .14f, .19f, .09f);
            Command(modal, "inventory", .405f, .14f, .19f, .09f);
            Command(modal, "diagnostics", .62f, .14f, .19f, .09f);
            var editor = Area(modal, "Selected module", .145f, .25f, .71f, .53f);
            keys.Add(("editor", editor.gameObject));
            var kinds = Area(editor, "Inventory kinds", 0, 0, 1, .15f);
            keys.Add(("kinds", kinds.gameObject));
            Command(kinds, "hero", 0, 0, .30f, 1);
            Command(kinds, "artifact", .35f, 0, .30f, 1);
            Command(kinds, "equipment", .70f, 0, .30f, 1);

            keys.Add(("filter", Input(editor, "Filter", "gm.filter", 64, 0, .19f, .55f, .15f)));
            var items = Scroll(editor, "Items", 0, .37f, .55f, .63f);
            keys.Add(("items", items));
            var row = Action(editor, "Item template", null, 24, 0, 0, .45f, .1f, out _, CommonArt("_btn_small_black"), Color.white);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 55;
            row.gameObject.SetActive(false);
            keys.Add(("row", row));
            keys.Add(("selection", Text(editor, "Selection", null, 29, .59f, .20f, .41f, .24f,
                Color.white, TextAlignmentOptions.Center, true)));
            keys.Add(("amountLabel", Text(editor, "Amount label", null, 23, .63f, .45f, .33f, .11f,
                Mist, TextAlignmentOptions.MidlineLeft)));
            var amount = Input(editor, "Amount", "gm.amount", 10, .63f, .57f, .33f, .18f);
            amount.contentType = TMP_InputField.ContentType.IntegerNumber;
            keys.Add(("amount", amount));
            Command(editor, "submit", .63f, .78f, .33f, .20f, "_btn_green").FeedbackSound = GameUiFeedback.Sound.Confirm;

            keys.Add(("report", Text(modal, "Diagnostics", null, 27, .17f, .26f, .66f, .39f,
                Color.white, TextAlignmentOptions.Center, true)));
            Command(modal, "copy", .41f, .66f, .18f, .10f);
            keys.Add(("status", Text(modal, "Status", null, 23, .10f, .78f, .80f, .075f, Mist, TextAlignmentOptions.Center, true)));
            Command(modal, "close", .31f, .87f, .18f, .10f, "_btn_gold").FeedbackSound = GameUiFeedback.Sound.Cancel;
            Command(modal, "refresh", .51f, .87f, .18f, .10f, "_btn_green");
            panel.gameObject.SetActive(false);
            Bind(root, keys.ToArray());
            return root.gameObject;
        }
    }
}
