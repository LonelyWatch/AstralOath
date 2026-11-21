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
        private static void BuildArtifactListOptions(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var layer = Area(root, "Artifact sort and filter", 0, 0, 1, 1);
            var keys = new List<(string, Object)>();
            keys.Add(("dismiss", Button(Area(layer, "Dismiss outside", 0, 0, 1, 1), null, Color.clear)));
            var panel = Area(layer, "Artifact filter panel", .322f, .195f, .495f, .43f);
            Paint(panel, BagArt("_box_equip_none"), new Color32(27, 48, 66, 255), true, true);
            Text(panel, "Rarity", "home.hero_inventory.bag_grade", 20, .035f, .025f, .5f, .13f, Mist);
            GameText Choice(string key, string caption, float x, float y, float w, float h, string literal = null)
            {
                var control = Action(panel, key, caption, 22, x, y, w, h, out var label, BagArt("_btn_small_black"), Color.white);
                if (literal != null) label.text = literal;
                keys.Add((key, control));
                keys.Add((key + "Text", label));
                return label;
            }
            for (int i = 0; i < 6; ++i)
                Choice("rarity" + i, i == 0 ? "home.artifacts.allRarities" : null, .033f + i * .092f, .16f, .087f, .15f,
                    i == 0 ? null : "★" + (6 - i));
            Paint(Area(panel, "Rule below rarity", .033f, .354f, .56f, .003f), null, new Color32(145,161,172,90));
            Paint(Area(panel, "Order divider", .611f, .046f, .002f, .90f), null, new Color32(145,161,172,105));
            for (int i = 0; i < 4; ++i)
            {
                var label = Choice("option" + i, "home.artifacts.listOption" + i, .035f, .38f + i * .146f, .56f, .125f);
                var rect = (RectTransform)label.transform.parent;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                Place(label.rectTransform, .085f, 0, .90f, 1);
                rect.GetComponent<Image>().color = Color.clear;
                Picture(rect, "Checkbox", CommonArt("cm_checkbg"), .003f, .18f, .062f, .64f, true);
                var check = Picture(rect, "Checked", CommonArt("cm_checkicon2"), .003f, .18f, .062f, .64f, true);
                keys.Add(("check" + i, check));
            }
            for (int i = 0; i < 5; ++i)
                Choice("order" + i, ArtifactSortKeys[i], .628f, .033f + i * .147f, .355f, .141f);
            Bind(layer, keys.ToArray());
            bindings.Add(("artifactListOptions", layer.GetComponent<BindingMap>()));
            layer.gameObject.SetActive(false);

            var notice = Area(root, "Artifact capacity limit", 0, 0, 1, 1);
            var dismiss = Button(Area(notice, "Dim background", 0, 0, 1, 1), null, Black(.66f));
            var message = Area(notice, "Notice", 0, .245f, 1, .50f);
            Paint(message, BagArt("_box_equip_none"), new Color32(29, 49, 70, 255), true, true);
            Text(message, "Title", "home.artifacts.capacityTitle", 34, .2f, .10f, .6f, .18f, Color.white, TextAlignmentOptions.Center);
            Text(message, "Limit", "home.artifacts.capacityReached", 27, .1f, .37f, .8f, .18f, Mist, TextAlignmentOptions.Center);
            var ok = Action(message, "OK", "home.artifacts.confirm", 25, .433f, .72f, .134f, .18f, out _, SummonArt("_btn_blue"), Color.white);
            bindings.Add(("artifactCapacityNotice", notice.gameObject));
            bindings.Add(("artifactCapacityOk", ok));
            bindings.Add(("artifactCapacityDismiss", dismiss));
            notice.gameObject.SetActive(false);
        }

        private static readonly string[] ArtifactSortKeys = { "home.gear.singleSort", "arena.draft.filter.acquired",
            "home.hero_inventory.bag_grade", "arena.draft.filter.ability", "arena.draft.filter.name" };
    }
}
