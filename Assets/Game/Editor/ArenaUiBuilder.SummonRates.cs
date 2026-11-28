using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildSummonRatePanel(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var panel = SummonPanel(root, bindings, "infoPanel", .5f, .5f, 0f, 0f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(818, 618) * SummonCanvasScale;
            panel.anchoredPosition = new Vector2(0f, -10f) * SummonCanvasScale;
            bindings.Add(("infoSheet", panel));
            var face = panel.GetComponent<Image>();
            face.type = Image.Type.Sliced;
            var shade = panel.parent.GetComponent<Image>();
            float opacity = 46f / 255f;
            shade.color = new Color(0, 0, 0, 1f - (QualitySettings.activeColorSpace == ColorSpace.Linear ?
                Mathf.GammaToLinearSpace(opacity) : opacity));
            var close = shade.gameObject.AddComponent<GameButton>();
            close.targetGraphic = shade;
            close.transition = Selectable.Transition.None;
            bindings.Add(("infoClose", close));
            // Consume pointer hits on blank panel space, leaving the surrounding scrim as the dismiss target.
            var blocker = panel.gameObject.AddComponent<GameButton>();
            blocker.transition = Selectable.Transition.None;
            blocker.targetGraphic = face;
            bindings.Add(("infoSurfaceInput", blocker));
            var title = Text(panel, "Summon information", null, 38, .08f, .031f, .84f, .079f,
                alignment: TextAlignmentOptions.Center);
            bindings.Add(("infoTitle", title));
            foreach (bool rates in new[] { false, true })
            {
                string key = rates ? "infoRatesTab" : "infoNotesTab";
                var tab = Area(panel, key, rates ? .223f : .040f, .112f, .184f, .105f);
                bindings.Add((key, Button(tab, null, Color.clear)));
                bindings.Add((key + "Selected", Picture(tab, "Selected", DraftSprite("_tap_select_blue2"), 0, 0, 1, 1).gameObject));
                Text(tab, "Tab label", rates ? "summon.info.rates" : "summon.info.notes", 26, .01f, .18f, .98f, .84f,
                    alignment: TextAlignmentOptions.Center);
            }
            var bar = Picture(panel, "Header divider", SummonArt("_bar_23"), .022f, .204f, .956f, .022f);
            bar.type = Image.Type.Sliced;
            bar.color = new Color32(255, 255, 255, 127);
            var footer = Area(panel, "Dismiss hint", .40f, 1.001f, .20f, .051f);
            Picture(footer, "Cross", Art("cm_icon_etcclose"), .225f, .21f, .14f, .57f, true).color = new Color32(255, 255, 255, 155);
            Text(footer, "Dismiss", "summon.button.close", 22.5f, .395f, 0, .6f, 1f, new Color32(255, 255, 255, 155));
            var notes = Area(panel, "Notices", .04f, .238f, .92f, .72f);
            bindings.Add(("infoNotes", notes.gameObject));
            bindings.Add(("infoNoteTitle", Text(notes, "Notice title", null, 27, 0, .004f, 1, .08f)));
            for (int i = 0; i < 4; ++i)
            {
                var line = Area(notes, "Notice " + i, 0, .078f + i * .081f, 1, .08f);
                var ink = new Color32(166, 204, 210, 145);
                Picture(line, "Information", Art("cm_icon_etcinfor"), 0f, .24f, .024f, .52f, true).color = ink;
                bindings.Add(("infoNote" + i, Text(line, "Text", null, 25f, .03f, 0, .97f, 1, ink)));
            }

            var scroll = InventoryScroll(panel, "Chance table", .055f, .235f, .89f, .749f);
            bindings.Add(("infoRates", scroll.gameObject));
            bindings.Add(("infoItems", scroll.content));
            foreach (bool artifact in new[] { false, true })
            {
                string kind = artifact ? "Artifact" : "Hero";
                var column = Area(scroll.content, kind, artifact ? .525f : 0f, 0f, .475f, 0f);
                column.pivot = new Vector2(.5f, 1);
                column.sizeDelta = new Vector2(0f, 98f * SummonCanvasScale);
                var heading = Text(column, kind + " total", null, 36f, 0f, .005f, 1f, .37f,
                    alignment: TextAlignmentOptions.Center);
                bindings.Add(("info" + kind + "Total", heading));
                var band = Area(column, "Table headings", 0f, .50f, 1f, .42f);
                Paint(band, CommonArt("_btn_small_white"), new Color32(102, 120, 144, 76), sliced: true);
                var ink = new Color32(166, 204, 210, 155);
                Text(band, "Grade", "summon.info.grade", 25, .042f, 0, .52f, 1, ink);
                Text(band, "Chance", "summon.info.chance", 25, .49f, 0, .47f, 1, ink, TextAlignmentOptions.MidlineRight);
                var rows = Area(column, "Rows", 0f, 1f, 1f, 0f);
                bindings.Add(("info" + kind + "Column", rows));
            }
            var template = Area(root, "Probability row template", 0f, 0f, 1f, 0f);
            template.pivot = new Vector2(.5f, 1f);
            var label = Text(template, "Name", null, 25, .04f, 0, .64f, 1);
            var value = Text(template, "Rate", null, 25, .67f, 0, .29f, 1, alignment: TextAlignmentOptions.MidlineRight);
            var separator = Picture(template, "Group divider", CommonArt("_bar_2"), .005f, 0, .99f, 1);
            separator.type = Image.Type.Sliced;
            separator.color = new Color32(255, 255, 255, 76);
            Bind(template, ("name", label), ("detail", value), ("divider", separator));
            template.gameObject.SetActive(false);
            bindings.Add(("infoRowTemplate", template.gameObject));
        }
    }
}
