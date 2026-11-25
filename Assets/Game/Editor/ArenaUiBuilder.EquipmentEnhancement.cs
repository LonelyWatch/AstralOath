using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string EquipmentEnhancementPath = Home + "Res/Prefab/EquipmentEnhancement.prefab";
        private const string EquipmentEnhancementPrefix = "home.gear.enhance.";

        // unit_item_upgrade.csb, ordinary equipment branch; 2026-11-25 recording 00:10–01:33.
        private static void BuildEquipmentEnhancement()
        {
            var root = Root("EquipmentEnhancement");
            var keys = new List<(string Key, Object Target)>();
            var gold = new Color32(170, 132, 79, 255);
            var orange = new Color32(255, 126, 0, 255);
            Sprite ArtFor(string name) => InventoryArt("EquipmentEnhancement/" + name);
            void Link(string key, Object target) => keys.Add((key, target));
            GameText Label(string key, RectTransform p, float x, float y, float w, float h, float size,
                Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
            {
                var text = Text(p, key, null, size, x, y, w, h, color, align, wrap);
                text.fontSharedMaterial = Load<Material>(Home + "Res/Materials/EquipmentDetailText.mat");
                Link(key, text); return text;
            }
            GameButton Control(string key, RectTransform p, float x, float y, float w, float h, string sprite)
            {
                var normal = sprite == "_btn_blue" ? ArtFor(sprite) : CommonArt(sprite);
                var button = Button(Area(p, key, x, y, w, h), normal, Color.white);
                var pressed = sprite == "_btn_blue" ? ArtFor(sprite + "_p") : CommonArt(sprite + "_p");
                if (pressed) PressSprites(button, normal, pressed);
                Link(key, button);
                Label(key + "Label", (RectTransform)button.transform, 0, 0, 1, 1, 30, Color.white, TextAlignmentOptions.Center);
                return button;
            }
            Link("background", Paint(root, InventoryArt("base_unit_hero"), Color.white, true));
            // The source page dims its body beneath the independent top bar. Keep the same full-page
            // texture coordinates through the mask so the header retains the original background brightness.
            var backgroundBody = Area(root, "Body background viewport", 0, .092f, 1, .908f);
            backgroundBody.gameObject.AddComponent<RectMask2D>();
            Paint(Area(backgroundBody, "Body source background", 0, -.092f/.908f, 1, 1/.908f),
                InventoryArt("base_unit_hero"), new Color32(127,127,127,255));
            var header = Area(root, "Enhancement top bar", 0, 0, 1, .092f);
            Link("back", SharedHeaderBack(header));
            Label("title", header, .047f, 0, .30f, 1, 36, Color.white).fontStyle = FontStyles.Bold;
            Picture(header, "Help", Art("cm_hud_top_btn_help"), .155f, .12f, .042f, .76f, true);
            DrawNativeTopBarIcons(root, dimmed: true);
            Picture(header, "Gold", InventoryArt("ArtifactEnhancement/token_gold"), .615f, .12f, .032f, .76f, true);
            Label("balance", header, .65f, 0, .125f, 1, 28, Color.white);
            Paint(Area(root, "Material shade", .648f, .092f, .352f, .908f), null, Black(.64f));

            var left = Area(root, "Equipment information", .094f, .123f, .224f, .84f);
            Link("thumbnail", SingleSlotThumbnail(left, -.014f, .006f, .28f, .148f));
            Label("grade", left, .30f, .025f, .68f, .05f, 28, gold);
            Label("name", left, .30f, .073f, .70f, .06f, 35, Color.white);
            Paint(Area(left, "Title rule", 0, .176f, 1, .013f), BagArt("_bar_2"), gold, sliced: true);
            Link("mainIcon", Picture(left, "Main property", null, 0, .206f, .074f, .055f, true));
            Label("mainName", left, .084f, .205f, .45f, .06f, 34, Color.white);
            Label("mainValue", left, .53f, .205f, .28f, .06f, 38, Color.white, TextAlignmentOptions.MidlineRight);
            Label("mainGain", left, .815f, .205f, .185f, .06f, 30, orange, TextAlignmentOptions.MidlineRight);
            Paint(Area(left, "Main property rule", 0, .277f, 1, .01f), BagArt("_bar_2"), gold, sliced: true);
            for (int i = 0; i < 4; i++)
            {
                float y = .31f + i * .045f;
                Label("subName" + i, left, 0, y, .58f, .043f, 28, Mist);
                Label("subValue" + i, left, .59f, y, .41f, .043f, 30, Mist, TextAlignmentOptions.MidlineRight).richText = true;
            }
            Paint(Area(left, "Sub property rule", 0, .49f, 1, .01f), BagArt("_bar_2"), gold, sliced: true);
            Label("scoreLabel", left, 0, .514f, .7f, .055f, 31, new Color32(255, 221, 90, 255));
            Label("score", left, .67f, .514f, .33f, .055f, 34, new Color32(255, 221, 90, 255), TextAlignmentOptions.MidlineRight);
            Paint(Area(left, "Set backing", 0, .584f, 1, .064f), null, new Color32(255, 255, 255, 5));
            Label("set", left, .012f, .584f, .97f, .064f, 27, Color.white);
            Label("status", left, 0, .69f, 1, .21f, 25, Mist, TextAlignmentOptions.TopLeft, true);
            foreach (float x in new[] { .337f, .648f })
            {
                var rail = Area(root, "Native divider", x, .115f, 0, .85f);
                for (int i = 0; i < 2; i++)
                {
                    var half = Area(rail, "Ornament " + i, .5f, i == 0 ? .25f : .75f, 0, 0);
                    half.sizeDelta = new Vector2(306, 17) * BagDesignScale;
                    half.localEulerAngles = new Vector3(0, 0, i == 0 ? -90 : 90);
                    Paint(half, ArtifactRestoreArt("_bar_4"), gold, sliced: true).pixelsPerUnitMultiplier = 1f / BagDesignScale;
                }
            }
            var dial = Area(root, "Native v2 rune dial", .49f, .349f, 0, 0);
            Image Rune(string name, float w, float h, float x = 0, float y = 0, byte opacity = 255)
            {
                var pivot = Area(dial, name + " pivot", .5f, .5f, 0, 0); Link(name, pivot);
                var rect = Area(pivot, name, .5f, .5f, 0, 0);
                rect.sizeDelta = new Vector2(w, h) * 1.1f * BagDesignScale;
                rect.anchoredPosition = new Vector2(x, y) * 1.1f * BagDesignScale;
                return Paint(rect, ArtFor(name), ManagementSourceTint(255, 255, 255, opacity));
            }
            Rune("upgrade_bg", 527, 477, 0, 1, 149);
            Rune("panta_bg", 355, 354, 0, 0, 154);
            Rune("star_01", 254, 63, 10.7f, 151.7f, 138);
            Rune("star_02", 129, 180, -81.3f, 51.8f, 138);
            Rune("star_03", 88, 139, 96.8f, 47.2f, 138);
            Rune("stone_b", 146, 146);
            Rune("panta_in", 156, 143, 0, 0, 111);
            Link("stoneImage", Rune("stone", 210, 209));
            Link("glow", Rune("stone_glow", 220, 212, .84f, 3.2f, 0));
            Link("card", SingleSlotThumbnail(root, .459f, .287f, .063f, .123f));
            Link("success", Rune("flash", 268, 268, 0, 0, 0));
            Label("gain", root, .385f, .508f, .215f, .047f, 31, orange, TextAlignmentOptions.Center);
            Paint(Area(root, "Experience rail", .378f, .556f, .23f, .014f), ArtifactRestoreArt("_progress_bg"), Color.white, sliced: true);
            var fill = Area(root, "Experience fill", .378f, .556f, .23f, .014f); fill.pivot = new Vector2(0, .5f);
            Paint(fill, ArtifactRestoreArt("_progress_bar"), new Color32(174, 139, 74, 255)); Link("fill", fill);
            Label("experience", root, .379f, .546f, .228f, .032f, 23, Color.white, TextAlignmentOptions.Center);
            Label("nextLevel", root, .605f, .541f, .035f, .04f, 24, orange, TextAlignmentOptions.Center);
            for (int i = 0; i < 5; i++)
            {
                var tile = Area(root, "Selected material " + i, .361f + i * .053f, .608f, .046f, .125f);
                Link("remove" + i, Button(tile, CommonArt("cm_item_slot"), Color.white));
                Link("ingredientQuality" + i, Picture(tile, "Quality", null, .01f, .015f, .98f, .75f, true));
                Link("ingredient" + i, Picture(tile, "Icon", null, .06f, .05f, .88f, .68f, true));
                Label("ingredientCount" + i, tile, 0, .72f, 1, .26f, 25, Color.white, TextAlignmentOptions.Center);
                Link("ingredientFlash" + i, Picture(tile, "Consume flash", ArtFor("flash"), -.15f, -.04f, 1.3f, 1.1f, true));
            }
            var arcane = Picture(root, "Arcane enhancement", ArtFor("upgrade_arcane_btn"), .349f, .775f, .253f, .08f);
            arcane.type = Image.Type.Sliced;
            Label("arcaneLabel", root, .349f, .775f, .253f, .08f, 29, new Color32(192, 160, 198, 255), TextAlignmentOptions.Center);
            Control("clear", root, .349f, .881f, .097f, .086f, "_btn_gold");
            var submit = Control("submit", root, .452f, .881f, .178f, .086f, "_btn_green");
            Place(keys.Find(k => k.Key == "submitLabel").Target is GameText caption ? caption.rectTransform : null, .64f, 0, .34f, 1);
            Picture((RectTransform)submit.transform, "Price backing", CommonArt("_btn_small_black_p"), .03f, .18f, .48f, .64f).color = new Color32(255,255,255,127);
            Picture((RectTransform)submit.transform, "Gold price", InventoryArt("ArtifactEnhancement/token_gold"), .02f, .12f, .18f, .76f, true);
            Label("cost", (RectTransform)submit.transform, .18f, .12f, .34f, .76f, 27, new Color32(255, 218, 75, 255), TextAlignmentOptions.Center);
            Label("materialsTitle", root, .665f, .116f, .28f, .055f, 31, gold);
            Paint(Area(root, "Material heading rule", .665f, .161f, .25f, .008f), BagArt("_bar_2"), gold, sliced: true);
            for (int i = 0; i < 4; i++)
            {
                var row = Area(root, "Material " + i, .665f, .183f + i * .132f, .253f, .12f);
                var select = Button(row, null, Color.clear);
                Picture(row, "Material quality", ArtFor("_itembg_stuff_" + new[] { 1, 2, 4, 5 }[i]), 0, .02f, .23f, .96f, true);
                var icon = Picture(row, "Material icon", ArtFor("icon_eq_stone_equip_" + i), .015f, .08f, .20f, .84f, true);
                var name = Text(row, "Name", null, 28, .268f, .02f, .49f, .40f, gold);
                var exp = Text(row, "Experience", null, 34, .268f, .43f, .49f, .44f);
                var quantity = Text(row, "Quantity", null, 28, .76f, .37f, .29f, .5f, Color.white, TextAlignmentOptions.MidlineLeft);
                quantity.overflowMode = TextOverflowModes.Overflow;
                quantity.enableAutoSizing = true; quantity.fontSizeMin = 20; quantity.fontSizeMax = 28;
                var countLabel = Text(row, "Quantity label", null, 24, .76f, .05f, .24f, .30f, Mist);
                Bind(row, ("select", select), ("icon", icon), ("name", name), ("experience", exp), ("quantity", quantity), ("quantityLabel", countLabel));
                Link("row" + i, row.GetComponent<BindingMap>());
            }
            var info = Area(root, "Overflow information", .665f, .717f, .253f, .136f);
            Paint(info, BagArt("_btn_small_black"), Color.white, sliced: true);
            Picture(info, "Info", ArtFor("icon_menu_info_2"), .03f, .30f, .075f, .36f, true);
            Label("overflow", info, .14f, .12f, .82f, .76f, 25, Mist, TextAlignmentOptions.MidlineLeft, true);
            Control("auto", root, .705f, .881f, .178f, .086f, "_btn_blue");

            var auto = Area(root, "Automatic registration", .705f, .338f, .178f, .525f);
            Paint(auto, InventoryArt("Roster/SourceSearch/_box_black_gold"), Color.white, true, true); Link("autoPopup", auto.gameObject);
            Control("powderOnly", auto, .05f, .025f, .90f, .115f, "_btn_small_black");
            for (int i = 0; i < 5; i++) Control("auto" + (15 - i * 3), auto, .04f, .22f + i * .15f, .92f, .14f, "_btn_small_black");
            auto.gameObject.SetActive(false);
            var quantityPopup = Area(root, "Material quantity popup", 0, 0, 1, 1); Link("quantityPopup", quantityPopup.gameObject);
            Link("quantityCancel", Button(Area(quantityPopup, "Dismiss quantity", 0, 0, 1, 1), null, Black(.64f)));
            var box = Area(quantityPopup, "Register material", .5f, .49f, 0, 0);
            box.sizeDelta = new Vector2(757, 410) * BagDesignScale;
            Paint(box, ArtFor("renew_popup_bg"), ManagementSourceTint(255,255,255,229), true, true).pixelsPerUnitMultiplier = 1f/BagDesignScale;
            Label("quantityTitle", box, .05f, .025f, .9f, .13f, 38, Color.white, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            Paint(Area(box, "Title rule", .05f, .183f, .9f, .013f), BagArt("_bar_2"), new Color32(175, 179, 183, 150), sliced: true);
            Label("quantityInstruction", box, .05f, .24f, .90f, .11f, 28, Mist, TextAlignmentOptions.Center);
            Label("quantityCount", box, .1f, .425f, .80f, .10f, 47, Color.white, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            Control("quantityMin", box, .075f, .535f, .10f, .115f, "_btn_small_black");
            Control("quantityMinus", box, .185f, .535f, .076f, .115f, "_btn_small_black");
            Control("quantityPlus", box, .738f, .535f, .076f, .115f, "_btn_small_black");
            Control("quantityMax", box, .825f, .535f, .10f, .115f, "_btn_small_black");
            var sliderArea = Area(box, "Quantity slider", .28f, .548f, .44f, .08f);
            Paint(sliderArea, null, Color.clear, true);
            Paint(Area(sliderArea, "Track", 0, .36f, 1, .28f), ArtFor("_slider_bg"), Color.white, sliced:true);
            var slider = sliderArea.gameObject.AddComponent<Slider>();
            var handle = Picture(sliderArea, "Handle", ArtFor("option_bar_circle"), .5f, .5f, 0, 0, true);
            handle.rectTransform.sizeDelta = new Vector2(22,22) * BagDesignScale;
            handle.enabled = true; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight; slider.wholeNumbers = true; Link("quantitySlider", slider);
            Control("quantityConfirm", box, .325f, .79f, .35f, .14f, "_btn_green");
            quantityPopup.gameObject.SetActive(false);
            var artCatalog = Area(root, "Material art catalog", 0, 0, 0, 0);
            foreach (var group in new[] { "equip", "acce" })
                for (int i = 0; i < 4; i++)
                    Link("materialArt_" + group + "_" + i, Picture(artCatalog, group + i, ArtFor("icon_eq_stone_" + group + "_" + i), 0, 0, 1, 1));
            for (int i = 0; i < 4; i++)
                Link("materialQuality" + i, Picture(artCatalog, "Quality " + i, ArtFor("_itembg_stuff_" + new[] { 1, 2, 4, 5 }[i]), 0, 0, 1, 1));
            artCatalog.gameObject.SetActive(false);
            var audio = root.gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0; Link("audio", audio);
            foreach (var pair in new[] { ("slotInSound", "SlotIn"), ("slotOutSound", "SlotOut"), ("startSound", "Start"), ("finishSound", "Finish"), ("goodSound", "Good"), ("greatSound", "Great") })
            {
                var cue = Area(root, pair.Item1, 0, 0, 0, 0).gameObject.AddComponent<AudioSource>();
                cue.playOnAwake = false; cue.spatialBlend = 0;
                cue.clip = Load<AudioClip>(Home + "Res/Sounds/EquipmentEnhancement/" + pair.Item2 + ".wav");
                Link(pair.Item1, cue);
            }
            Bind(root, keys.ToArray());
            Save(root.gameObject, EquipmentEnhancementPath);
            UIViewRegistry.ValidatePrefab<EquipmentEnhancement>(Load<GameObject>(EquipmentEnhancementPath));
        }
    }
}
