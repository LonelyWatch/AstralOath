using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildManagementFilters(RectTransform pane, List<(string Key, Object Target)> keys)
        {
            var menu = Area(pane, "Attribute menu", 0, 0, 1, 1);
            keys.Add(("manageStatMenu", menu.gameObject));
            var backdrop = Button(Area(menu, "Dismiss menu", 0, 0, 1, 1), null, Color.clear);
            backdrop.FeedbackSound = GameUiFeedback.Sound.Silent;
            keys.Add(("manageStatBackdrop", backdrop));
            var choices = Area(menu, "Attribute choices", .507f, .34f, .452f, .43f);
            PaintSoftModal(choices);
            (string kind, string icon)[] options =
            {
                ("all",null), ("attack","att"), ("defense","def"), ("health","max_hp"), ("attackPct","att"),
                ("defensePct","def"), ("healthPct","max_hp"), ("speed","speed"), ("critChanceBp","cri"),
                ("critDamageBp","cri_dmg"), ("effectivenessBp","acc"), ("resistanceBp","res")
            };
            for (int i = 0; i < options.Length; i++)
            {
                var (kind,icon) = options[i];
                var button = Action(choices, kind, null, 23, .018f + (i / 6) * .489f, .028f + (i % 6) * .157f,
                    .474f, .145f, out var label, CommonArt("_btn_small_black"), Color.white);
                if (icon != null)
                    Picture(button.GetComponent<RectTransform>(), "Stat symbol", InventoryArt("Equipment/cm_icon_stat_" + icon),
                        .047f, .18f, .119f, .64f, true);
                Place(label.rectTransform, .195f, .03f, .785f, .94f);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                keys.Add(("manageStatOption_" + kind, button));
            }
            menu.gameObject.SetActive(false);

            for (int index = 0; index < 5; index++)
            {
                float x = index == 0 ? .507f : index <= 2 ? .662f : .817f;
                float y = index == 0 || index % 2 == 0 ? .272f : .204f;
                var button = Action(pane, "Attribute filter " + index, null, 22, x, y, .142f, .057f, out var caption,
                    CommonArt("_btn_small_black"), Color.white);
                caption.enableAutoSizing = true;
                caption.fontSizeMin = 15;
                caption.fontSizeMax = 22;
                keys.Add(("manageStatFilter" + index, button));
            }
        }
    }
}
