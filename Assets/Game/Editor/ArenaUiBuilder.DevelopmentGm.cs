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
            keys.Add(("entry", Button(entry, CommonArt("_btn_small_white"), new Color32(25, 39, 55, 250))));
            Picture(entry, "Help glyph", Art("cm_hud_top_btn_help"), .18f, .07f, .64f, .55f, true);
            Text(entry, "GM", "gm.short", 22, .04f, .57f, .92f, .36f, Color.white, TextAlignmentOptions.Center);

            var panel = Area(root, "GM overlay", 0, 0, 1, 1);
            Paint(panel, null, new Color(0, 0, 0, .68f), true);
            keys.Add(("panel", panel.gameObject));
            var modal = Area(panel, "Dialog", 0, .06f, 1, .88f);
            PaintSoftModal(modal);
            Text(modal, "Title", "gm.title", 40, .23f, .014f, .54f, .082f, Color.white, TextAlignmentOptions.Center);
            Rule(modal, "Title line", .21f, .105f, .58f, .0025f);

            GameButton Command(RectTransform parent, string key, float x, float y, float w, float h,
                string sprite = "_btn_small_black")
            {
                var b = Action(parent, key, "gm." + key, 25, x, y, w, h, out var caption, CommonArt(sprite), Color.white);
                keys.Add((key, b));
                if (key == "submit") keys.Add(("submitLabel", caption));
                return b;
            }

            void Stack(RectTransform parent)
            {
                var layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 8;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            Command(modal, "resources", .145f, .122f, .165f, .083f);
            Command(modal, "inventory", .325f, .122f, .165f, .083f);
            Command(modal, "forge", .505f, .122f, .165f, .083f);
            Command(modal, "diagnostics", .685f, .122f, .165f, .083f);
            var editor = Area(modal, "Selected module", .145f, .22f, .71f, .59f);
            keys.Add(("editor", editor.gameObject));
            Stack(editor);
            var kinds = Area(editor, "Inventory kinds", 0, 0, 1, 1);
            kinds.gameObject.AddComponent<LayoutElement>().preferredHeight = 58;
            keys.Add(("kinds", kinds.gameObject));
            Command(kinds, "hero", 0, 0, .30f, 1);
            Command(kinds, "artifact", .35f, 0, .30f, 1);
            Command(kinds, "equipment", .70f, 0, .30f, 1);

            var body = Area(editor, "Selection and action", 0, 0, 1, 1);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var candidates = Area(body, "Candidates", 0, 0, .55f, 1);
            Stack(candidates);
            var filter = Input(candidates, "Filter", "gm.filter", 64, 0, 0, 1, 1);
            filter.gameObject.AddComponent<LayoutElement>().preferredHeight = 58;
            keys.Add(("filter", filter));
            var items = Scroll(candidates, "Items", 0, 0, 1, 1);
            items.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            items.content.GetComponent<VerticalLayoutGroup>().spacing = 8;
            keys.Add(("items", items));
            var row = Action(body, "Item template", null, 24, 0, 0, .45f, .1f, out _, CommonArt("_btn_small_black"), Color.white);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            row.gameObject.SetActive(false);
            keys.Add(("row", row));
            keys.Add(("selection", Text(body, "Selection", null, 29, .59f, .02f, .41f, .30f,
                Color.white, TextAlignmentOptions.Center, true)));
            keys.Add(("amountLabel", Text(body, "Amount label", null, 23, .63f, .36f, .33f, .11f,
                Mist, TextAlignmentOptions.MidlineLeft)));
            var amount = Input(body, "Amount", "gm.amount", 10, .63f, .48f, .33f, .17f);
            amount.contentType = TMP_InputField.ContentType.IntegerNumber;
            keys.Add(("amount", amount));
            Command(body, "submit", .63f, .70f, .33f, .18f, "_btn_green").FeedbackSound = GameUiFeedback.Sound.Confirm;

            var forge = Area(modal, "Equipment forge", .09f, .23f, .82f, .57f);
            keys.Add(("forgePanel", forge.gameObject));
            void Choice(RectTransform parent, string key, float x, float y, float width, float height)
            {
                var button = Action(parent, key, null, 24, x, y, width, height, out _, CommonArt("_btn_small_black"), Color.white);
                keys.Add((key, button));
            }
            void NumberBox(RectTransform parent, string key, float x, float y, float width, float height)
            {
                var field = Input(parent, key, null, 8, x, y, width, height);
                field.contentType = TMP_InputField.ContentType.IntegerNumber;
                keys.Add((key, field));
            }
            void Heading(RectTransform parent, string key, float x, float y, float width, float height) =>
                Text(parent, key, "gm." + key, 23, x, y, width, height, Mist, TextAlignmentOptions.Center);
            Choice(forge, "forgeSlot", 0, 0, .19f, .15f);
            Choice(forge, "forgeTier", .20f, 0, .19f, .15f);
            Choice(forge, "forgeGrade", 0, .18f, .19f, .15f);
            Choice(forge, "forgeSet", .20f, .18f, .19f, .15f);
            Choice(forge, "forgeMain", 0, .36f, .39f, .16f);
            Heading(forge, "forgeEnhance", 0, .56f, .185f, .10f);
            Heading(forge, "forgeCount", .205f, .56f, .185f, .10f);
            NumberBox(forge, "forgeEnhance", 0, .67f, .185f, .15f);
            NumberBox(forge, "forgeCount", .205f, .67f, .185f, .15f);
            var attributes = Area(forge, "Substats", .425f, 0, .575f, 1);
            Heading(attributes, "forgeSub", 0, 0, .31f, .11f);
            Heading(attributes, "forgeRolls", .32f, 0, .15f, .11f);
            Heading(attributes, "forgeValue", .49f, 0, .18f, .11f);
            Heading(attributes, "forgeRange", .68f, 0, .32f, .11f);
            for (int index = 0; index < 4; index++)
            {
                var line = Area(attributes, "Substat " + index, 0, .14f + index * .17f, 1, .14f);
                keys.Add(("forgeRow" + index, line.gameObject.AddComponent<CanvasGroup>()));
                Choice(line, "forgeSub" + index, 0, 0, .31f, 1);
                NumberBox(line, "forgeRolls" + index, .32f, 0, .15f, 1);
                NumberBox(line, "forgeValue" + index, .49f, 0, .18f, 1);
                keys.Add(("forgeRange" + index, Text(line, "Range", null, 23, .68f, 0, .32f, 1, Mist, TextAlignmentOptions.Center)));
            }
            keys.Add(("forgeBudget", Text(forge, "Roll budget", null, 24, 0, .84f, .56f, .15f, Mist, TextAlignmentOptions.MidlineLeft)));
            Command(forge, "forgeMake", .67f, .83f, .33f, .16f, "_btn_green").FeedbackSound = GameUiFeedback.Sound.Confirm;
            forge.gameObject.SetActive(false);
            keys.Add(("report", Text(modal, "Diagnostics", null, 27, .17f, .26f, .66f, .39f,
                Color.white, TextAlignmentOptions.Center, true)));
            Command(modal, "copy", .41f, .66f, .18f, .10f);
            keys.Add(("status", Text(modal, "Status", null, 23, .10f, .815f, .80f, .06f, Mist, TextAlignmentOptions.Center, true)));
            Command(modal, "close", .31f, .883f, .18f, .09f, "_btn_gold").FeedbackSound = GameUiFeedback.Sound.Cancel;
            Command(modal, "refresh", .51f, .883f, .18f, .09f, "_btn_green");
            panel.gameObject.SetActive(false);
            Bind(root, keys.ToArray());
            return root.gameObject;
        }
    }
}
