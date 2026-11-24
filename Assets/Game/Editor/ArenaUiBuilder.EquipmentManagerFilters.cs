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
        private static void BuildManagementFilters(RectTransform pane, List<(string Key, Object Target)> keys)
        {
            var menu = Area(pane, "Attribute menu", 0, 0, 1, 1);
            keys.Add(("manageStatMenu", menu.gameObject));
            var backdrop = Button(Area(menu, "Dismiss menu", 0, 0, 1, 1), null, Color.clear);
            backdrop.FeedbackSound = GameUiFeedback.Sound.Silent;
            keys.Add(("manageStatBackdrop", backdrop));
            var choices = Area(menu, "Attribute choices", .488f, .332f, .419f, .415f);
            // The source frame includes 32 px of transparent shadow outside its visible border.
            Paint(Area(choices,"Source popup frame",-.052f,-.107f,1.104f,1.214f),ManagementArt("_box_black_white"),
                Color.white,true,true).pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            Paint(Area(choices,"Source column rule",.498f,.035f,.002f,.93f),ManagementArt("z_tile_bar"),ManagementSourceTint(255,255,255,127));
            (string kind, string icon)[] options =
            {
                ("all",null), ("attack","att"), ("defense","def"), ("health","max_hp"), ("attackPct","att"),
                ("defensePct","def"), ("healthPct","max_hp"), ("speed","speed"), ("critChanceBp","cri"),
                ("critDamageBp","cri_dmg"), ("effectivenessBp","acc"), ("resistanceBp","res")
            };
            for (int i = 0; i < options.Length; i++)
            {
                var (kind,icon) = options[i];
                var rect = Area(choices,kind,.018f + (i / 6) * .496f,.025f + (i % 6) * .160f,.474f,.143f);
                var button = Button(rect,null,Color.clear);
                var selected = Picture(rect,"Selected source row",CommonArt("_btn_small_white"),0,0,1,1);
                selected.type = Image.Type.Sliced;
                selected.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
                keys.Add(("manageStatSelected_" + kind,selected));
                keys.Add(("manageStatOptionSymbol_" + kind,Picture(rect,"Stat symbol",icon == null ? Art("icon_menu_all") : InventoryArt("Equipment/cm_icon_stat_" + icon),
                    .026f,.12f,.128f,.76f,true)));
                var label = Text(rect,"Caption",null,28,.175f,.03f,.80f,.94f,Color.white,TextAlignmentOptions.MidlineLeft);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                keys.Add(("manageStatOption_" + kind, button));
            }
            menu.gameObject.SetActive(false);

            for (int index = 0; index < 5; index++)
            {
                float x = index == 0 ? .565f : index <= 2 ? .688f : .799f;
                float y = index == 0 || index % 2 == 0 ? .268f : .203f;
                var button = Action(pane, "Attribute filter " + index, null, 24, x, y, .110f, .054f, out var caption,
                    CommonArt("_btn_small_black"), Color.white);
                Place(caption.rectTransform,.22f,.04f,.75f,.92f);
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                keys.Add(("manageStatSymbol" + index,Picture((RectTransform)button.transform,"Filter source symbol",Art("icon_menu_all"),.025f,.10f,.18f,.80f,true)));
                caption.enableAutoSizing = true;
                caption.fontSizeMin = 15;
                caption.fontSizeMax = 24;
                keys.Add(("manageStatFilter" + index, button));
            }
            var art = Area(pane,"Filter source states",0,0,.01f,.01f);
            keys.Add(("manageFilterBlackArt",Picture(art,"Normal",CommonArt("_btn_small_black"),0,0,1,1)));
            keys.Add(("manageFilterWhiteArt",Picture(art,"Selected",CommonArt("_btn_small_white"),0,0,1,1)));
            keys.Add(("manageFilterAllArt",Picture(art,"All",Art("icon_menu_all"),0,0,1,1)));
            art.gameObject.SetActive(false);
            menu.SetAsLastSibling();
        }
    }
}
