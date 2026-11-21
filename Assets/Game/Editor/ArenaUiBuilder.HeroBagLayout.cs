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
        private const float BagDesignScale = 1.5f;
        private const float BagRowSourceWidth = 1120f;
        private const float BagRowSourceHeight = 145f;
        // The source interface blends these translucent layers in gamma space. Their UI tint is
        // calibrated against the recording for the project's linear color space.
        private static readonly Color32 BagInactiveIcon = new Color32(255, 255, 255, 39);
        private static readonly Color32 BagInactiveWhite = new Color32(255, 255, 255, 55);
        private static readonly Color32 BagPlateTint = new Color32(105, 105, 105, 255);
        private static readonly Color32 BagEmptyIcon = new Color32(255, 255, 255, 18);

        private static Sprite BagArt(string stem) => InventoryArt("Bag/" + stem);

        private static List<(string Key, Object Target)> BuildNativeHeroBag(RectTransform bag)
        {
            Paint(bag, null, Deep, true);
            Cover(bag, "Native inventory background", InventoryArt("base_inventory"));
            Paint(BagArea(bag, "Category divider", 109.5f, 67, 1, 653), null, new Color32(255, 255, 255, 1));

            var bindings = new List<(string Key, Object Target)>();
            AddNativeBagTab(bag, bindings, "Heroes", "heroes", "icon_menu_equip", 110, 52, true);
            AddNativeBagTab(bag, bindings, "Equipment", "equipment", "icon_menu_equipment", 191, 49, false);
            AddNativeBagTab(bag, bindings, "Artifacts", "artifacts", "icon_menu_pararti", 272, 50, false);
            BagImage(BagArea(bag, "Exclusive equipment icon", 28, 326, 50, 50), BagArt("icon_menu_private"), BagInactiveIcon);
            BagImage(BagArea(bag, "Materials icon", 29, 409, 48, 48), BagArt("icon_menu_stuff"), BagInactiveIcon);
            BagImage(BagArea(bag, "Reward chest icon", 26.5f, 488.5f, 53, 53), BagArt("icon_menu_reward_box"), BagInactiveIcon);
            BagWindowPlate(bag, "Equipment storage", 9.5f, 640, 93, 66);
            BagImage(BagArea(bag, "Equipment storage icon", 31, 647, 46, 46), BagArt("icon_menu_equipstorage"),
                new Color32(136, 136, 136, 255));

            var filters = Area(bag, "Hero bag toolbar", 0, 0, 1, 1);
            bindings.Add(("bagFilter", filters.gameObject));
            DrawBagToolbarLine(filters, 20);
            string[] keys = { "all", "warrior", "knight", "thief", "ranger", "mage", "soul_weaver" };
            Sprite[] classes =
            {
                BagArt("cm_icon_all"), HudSprite("cm_icon_role_warrior"), HudSprite("cm_icon_role_knight"),
                HudSprite("cm_icon_role_assassin"), HudSprite("cm_icon_role_ranger"), HudSprite("cm_icon_role_mage"),
                HudSprite("cm_icon_role_manauser")
            };
            for (int index = 0; index < keys.Length; ++index)
            {
                float center = 183 + 75 * index;
                var hotZone = BagArea(filters, "Class " + keys[index], center - 37.5f, 83, 75, 58);
                var button = Button(hotZone, null, Color.clear);
                DrawBagFilter(filters, "Class " + keys[index], center, classes[index], index == 0,
                    index == 0 ? 33 : 32.2f, out var active);
                bindings.Add(("filter_" + keys[index], button));
                bindings.Add(("filterActive_" + keys[index], active));
            }
            var sort = BagSortPlate(filters, 26, 91, "sort_power");
            bindings.Add(("bagSortLabel", sort));
            var sortPlate = (RectTransform)sort.transform.parent;
            Place(sort.rectTransform, .27f, .04f, .49f, .88f);
            bindings.Add(("bagSortAttack", Picture(sortPlate, "Attack stat", InventoryArt("Equipment/cm_icon_stat_att"),
                .75f, .24f, .17f, .50f, true)));
            bindings.Add(("bagSort", Button(Area(sortPlate, "Open sort and filters", 0, 0, 1, 1), null, Color.clear)));

            var bagScroll = InventoryScroll(bag, "Bag hero rows", 0, 0, 0, 1);
            var scrollFrame = (RectTransform)bagScroll.transform;
            scrollFrame.offsetMin = new Vector2(134 * BagDesignScale, 0);
            scrollFrame.offsetMax = new Vector2((134 + BagRowSourceWidth) * BagDesignScale, -145 * BagDesignScale);
            bindings.Add(("bagScroll", bagScroll));
            bindings.Add(("bagRow", BuildNativeBagRow(bag)));
            AddEmptyBagCategory(bag, bindings, false, classes);
            AddEmptyBagCategory(bag, bindings, true, classes);
            bindings.Add(("wornFilters", BuildWornBagFilters(bag)));
            return bindings;
        }

        private static void AddNativeBagTab(RectTransform bag, List<(string Key, Object Target)> bindings,
            string bindingSuffix, string key, string iconName, float center, float iconSize, bool active)
        {
            var buttonRect = BagArea(bag, "Bag category " + key, 0, center - 31, 110, 62);
            var button = Button(buttonRect, null, Color.clear);
            Sprite icon = InventoryArt(iconName);
            BagImage(Area(buttonRect, "Category icon", (53 - iconSize / 2) / 110,
                (31 - iconSize / 2) / 62, iconSize / 110,
                iconSize / 62), icon, BagInactiveIcon);
            var activeImage = BagImage(Area(buttonRect, "Active category", -17.5f / 110, 0, 157f / 110, 1),
                BagArt("_tap_select_blue"), Color.white, true);
            BagImage(Area(activeImage.rectTransform, "Active icon", (70.5f - iconSize / 2) / 157,
                (31 - iconSize / 2) / 62, iconSize / 157, iconSize / 62), icon, Color.white);
            activeImage.gameObject.SetActive(active);

            var caption = Text(buttonRect, "Category accessible label", InventoryPrefix + key, 25, 0, 0, 1, 1);
            caption.gameObject.SetActive(false);
            bindings.Add(("tab" + bindingSuffix, button));
            bindings.Add(("tab" + bindingSuffix + "Label", caption));
            bindings.Add(("tabActive_" + key, activeImage));
        }

        private static void DrawBagToolbarLine(RectTransform parent, float right)
        {
            var line = Area(parent, "Native filter rule", 0, 0, 1, 0);
            line.offsetMin = new Vector2(133 * BagDesignScale, -146 * BagDesignScale);
            line.offsetMax = new Vector2(-right * BagDesignScale, -136 * BagDesignScale);
            BagImage(line, BagArt("_bar_2"), new Color32(255, 255, 255, 7), true);
        }

        private static void DrawBagFilter(RectTransform parent, string name, float center, Sprite icon, bool selected,
            float size, out Image active)
        {
            BagImage(BagArea(parent, name + " icon", center - size / 2, 118 - size / 2, size, size),
                icon, BagInactiveIcon);
            active = BagImage(BagArea(parent, name + " selected", center - 37.5f, 87, 75, 54),
                BagArt("_tap_select_blue2"), Color.white, true);
            BagImage(Area(active.rectTransform, "Selected icon", (75 - size) / 150, (62 - size) / 108,
                size / 75, size / 54), icon, Color.white);
            active.gameObject.SetActive(selected);
        }

        private static void AddEmptyBagCategory(RectTransform bag, List<(string Key, Object Target)> bindings,
            bool artifact, Sprite[] classes)
        {
            string suffix = artifact ? "Artifact" : "Equipment";
            var chrome = Area(bag, "Empty " + suffix + " native screen", 0, 0, 1, 1);
            DrawBagToolbarLine(chrome, 444);
            Sprite[] icons = artifact
                ? new[] { classes[0], BagArt("cm_icon_arti_public"), classes[1], classes[2], classes[3], classes[4], classes[5], classes[6] }
                : new[] { classes[0], RosterArt("cm_icon_parweapon"), RosterArt("cm_icon_parhelm"),
                    RosterArt("cm_icon_pararmor"), RosterArt("cm_icon_parneck"), RosterArt("cm_icon_parring"), RosterArt("cm_icon_parboots") };
            for (int index = 0; index < icons.Length; ++index)
            {
                float center = artifact ? 183 + 75 * index : 183 + 79 * index;
                float size = artifact && index == 1 ? 38.5f : 33f;
                DrawBagFilter(chrome, "Category " + index, center, icons[index], index == 0, size, out _);
            }

            var panel = BagRightArea(chrome, "Empty category detail", 29, 142, 399, 578);
            BagImage(panel, BagArt("_box_equip_none"), new Color32(255, 255, 255, 8), true);
            BagImage(Area(panel, "Empty information icon", 187.5f / 399, 259f / 578, 23f / 399, 23f / 578),
                Art("cm_icon_etcinfor"), BagEmptyIcon);
            var message = Text(panel, "Empty inventory information", InventoryPrefix + (artifact ? "empty_artifacts" : "empty_equipment"),
                26, 0, 288f / 578, 1, 45f / 578, new Color32(118, 118, 118, 255), TextAlignmentOptions.Center);
            var sort = BagSortPlate(chrome, 291, 86, artifact ? "sort_upgrade" : "sort_equipment_score");
            var count = BagRightArea(chrome, "Empty capacity", 155, 86, 136, 46);
            BagImage(count, BagArt("_btn_small_black"), BagPlateTint, true);
            Picture(count, "Capacity icon", InventoryArt(artifact ? "icon_menu_pararti" : "icon_menu_equip"),
                .04f, .10f, .27f, .81f, true).color = BagInactiveWhite;
            var organizeRect = BagRightArea(chrome, "Organize", 27, 86, 125, 46);
            var organize = BagImage(organizeRect, BagArt("_btn_small_black"), BagPlateTint, true);
            var organizeLabel = Text(organize.rectTransform, "Organize label", InventoryPrefix + (artifact ? "organize" : "manage"), 25, 0, 0, 1, 1,
                new Color32(255, 255, 255, 127), TextAlignmentOptions.Center);

            bindings.Add((artifact ? "emptyArtifactChrome" : "emptyEquipmentChrome", chrome.gameObject));
            bindings.Add(("bag" + suffix + "SortLabel", sort));
            bindings.Add(("bag" + suffix + "EmptyLabel", message));
            bindings.Add(("bag" + suffix + "OrganizeLabel", organizeLabel));
            chrome.gameObject.SetActive(false);
        }

        private static GameText BagSortPlate(RectTransform parent, float right, float top, string key)
        {
            Image plate = BagImage(BagRightArea(parent, "Sort", right, top, 139, 46), BagArt("_btn_small_black"), BagPlateTint, true);
            Picture(plate.rectTransform, "Native filter icon", BagArt("_sorting2"), .082f, .23f, .16f, .50f, true);
            return Text(plate.rectTransform, "Sort label", InventoryPrefix + key, 26, .27f, .04f, .70f, .88f,
                new Color32(227, 229, 230, 219));
        }

        private static GameObject BuildNativeBagRow(RectTransform parent)
        {
            var row = Area(parent, "Native hero bag row template", 0, 0, 1, 1);
            BagImage(BagRowArea(row, "Native item group outline", 150.5f, 55, 966, 78),
                BagArt("_line_equip_wearing"), new Color32(255, 255, 255, 4), true);
            var hidden = Area(row, "Shared card bindings", 0, 0, 1, 1);
            var name = Text(hidden, "Name", null, 25, 0, 0, 1, 1);
            var level = Text(hidden, "Level", null, 25, 0, 0, 1, 1);
            var selection = Paint(Area(hidden, "Selected", 0, 0, 1, 1), null, Color.clear);
            hidden.gameObject.SetActive(false);

            Sprite equipIcon = InventoryArt("icon_menu_equip");
            var gearControl = BagImage(BagRowArea(row, "Equipment button art", 105, 49, 108, 55),
                BagArt("_btn_small_black"), BagPlateTint, true);
            BagImage(Area(gearControl.rectTransform, "Equipment button icon", 32 / 108f, 3 / 55f, 44 / 108f, 44 / 55f),
                equipIcon, BagInactiveWhite);

            const float faceSize = 89.85f;
            BagImage(BagRowArea(row, "Portrait background", 51 - faceSize / 2, 71 - faceSize / 2, faceSize, faceSize),
                HudSprite("_hero_s_bg_ally"), Color.white);
            var portrait = BagImage(BagRowArea(row, "Hero portrait", 51 - faceSize / 2, 71 - faceSize / 2, faceSize, faceSize),
                null, Color.white);
            BagImage(BagRowArea(row, "Gold portrait frame", 1, 21, 102.55f, 102.55f),
                HudSprite("_hero_s_frame_ally"), Color.white);
            var role = BagImage(BagRowArea(row, "Hero class", -4, 25, 40, 40), null, Color.white);
            var element = BagImage(BagRowArea(row, "Hero element", 72, 24, 31, 31), null, Color.white);
            BagImage(BagRowArea(row, "Lv. sprite", 109, 26.8f, 27.52f, 19.2f),
                DraftSprite("TeamBadges/hero_lv"), Color.white);
            var firstDigit = BagImage(BagRowArea(row, "First level digit", 139, 26.8f, 13.44f, 19.2f), null, Color.white);
            var secondDigit = BagImage(BagRowArea(row, "Second level digit", 151, 26.8f, 13.44f, 19.2f), null, Color.white);
            var max = BagImage(BagRowArea(row, "Max level", 139, 26.8f, 39.04f, 19.2f),
                BagArt("hero_lv_max"), Color.white);
            max.enabled = false;

            var bindings = new List<(string Key, Object Target)>
            {
                ("name", name), ("level", level), ("portrait", portrait), ("selection", selection),
                ("element", element), ("role", role), ("bagLevelFirst", firstDigit), ("bagLevelSecond", secondDigit),
                ("bagLevelMax", max)
            };
            for (int index = 0; index < 10; ++index)
            {
                var digit = BagImage(Area(hidden, "Level sprite " + index, 0, 0, 1, 1),
                    DraftSprite("TeamBadges/hero_lv_" + index), Color.white);
                bindings.Add(("bagLevelDigit" + index, digit));
            }
            for (int index = 0; index < 6; ++index)
            {
                var star = BagImage(BagRowArea(row, "Star " + index, 15 + 12.6f * index, 97, 18, 18), null, Color.white);
                bindings.Add(("star" + index, star));
            }

            float[] gearCenters = { 268, 367, 466, 715, 814, 913 };
            float[] gearIconCenters = { 73, 71, 73, 72, 70, 70 };
            string[] gearIcons =
            {
                "cm_icon_parweapon", "cm_icon_parhelm", "cm_icon_pararmor",
                "cm_icon_parneck", "cm_icon_parring", "cm_icon_parboots"
            };
            for (int index = 0; index < gearCenters.Length; ++index)
            {
                var slot = BuildBagEquipmentSlot(row, "Empty equipment slot " + index, BagArt("cm_item_slot"),
                    RosterArt(gearIcons[index]), new Rect(gearCenters[index] - 45, 12.95f, 90, 116.1f),
                    new Rect((gearIconCenters[index] - 16.5f) / 100, 4.5f / 129, .33f, 33f / 129), sliced: true);
                bindings.Add(("emptyGear" + index, slot.Require<Image>("frame")));
            }
            var artifact = BuildBagEquipmentSlot(row, "Empty artifact slot", BagArt("cm_item_slot_arti"), RosterArt("cm_icon_pararti"),
                new Rect(533.65f, 10.35f, 110.7f, 123.3f), new Rect(69.5f / 123, 23.5f / 137, 33f / 123, 33f / 137));
            var equippedArt = artifact.Require<Image>("item");
            Place(equippedArt.rectTransform, .04f, .04f, .92f, .92f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(equippedArt.rectTransform);
            // The bag's emblem and its badge live a little inside the upper-right edge.
            var badge = (RectTransform)artifact.Require<BindingMap>("badge").transform;
            Place(badge, .637f, .003f, 31f / 123, 30f / 137);
            PrefabUtility.RecordPrefabInstancePropertyModifications(badge);
            bindings.Add(("bagArtifactSlot", artifact));
            BuildWornArtifactMarkers((RectTransform)artifact.transform, "bagArtifact", bindings);
            bindings.Add(("emptyArtifact", artifact.Require<Image>("frame")));
            bindings.Add(("bagEquippedArtifact", equippedArt));
            var exclusive = BuildBagEquipmentSlot(row, "Empty exclusive equipment slot", BagArt("cm_item_slot_private"),
                BagArt("cm_icon_parprivate"), new Rect(984.4f, 12.58f, 101.2f, 116.84f),
                new Rect(36f / 110, 13f / 127, 34f / 110, 34f / 127));
            // The original only activates this reservation for heroes with supported exclusive equipment.
            exclusive.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(exclusive.gameObject);
            bindings.Add(("emptyExclusive", exclusive.Require<Image>("frame")));

            void HotZone(string key, float x, float y, float width, float height)
            {
                var button = Button(BagRowArea(row, key, x, y, width, height), null, Color.clear);
                button.transition = Selectable.Transition.None;
                bindings.Add((key, button));
            }
            HotZone("bagPortrait", 0, 20, 104, 105);
            HotZone("bagManage", 105, 49, 108, 55);
            for (int i = 0; i < gearCenters.Length; i++)
                HotZone("bagInspect" + i, gearCenters[i] - 45, 12.95f, 90, 116.1f);
            HotZone("bagArtifact", 533.65f, 10.35f, 110.7f, 123.3f);
            Bind(row, bindings.ToArray());
            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        // Match-height CanvasScaler preserves the Cocos 720-line coordinate system at both aspect ratios.
        private static RectTransform BagArea(RectTransform parent, string name, float left, float top, float width, float height,
            float horizontalAnchor = 0)
        {
            var rect = Area(parent, name, horizontalAnchor, 0, 0, 0);
            rect.offsetMin = new Vector2(left, -top - height) * BagDesignScale;
            rect.offsetMax = new Vector2(left + width, -top) * BagDesignScale;
            return rect;
        }

        private static RectTransform BagRightArea(RectTransform parent, string name, float right, float top, float width, float height) =>
            BagArea(parent, name, -right - width, top, width, height, 1);

        private static BindingMap BuildBagEquipmentSlot(RectTransform row, string name, Sprite frame, Sprite icon,
            Rect area, Rect iconArea, bool sliced = false)
        {
            var slot = InstantiateEquipmentSlot(row, name, frame, icon);
            var rect = (RectTransform)slot.transform;
            Place(rect, area.x / BagRowSourceWidth, area.y / BagRowSourceHeight,
                area.width / BagRowSourceWidth, area.height / BagRowSourceHeight);
            var plate = slot.Require<Image>("frame");
            plate.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            plate.preserveAspect = !sliced;
            if (sliced) plate.pixelsPerUnitMultiplier = 2f / 3f;
            var symbol = slot.Require<Image>("placeholder");
            Place(symbol.rectTransform, iconArea.x, iconArea.y, iconArea.width, iconArea.height);
            symbol.color = BagEmptyIcon;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
            PrefabUtility.RecordPrefabInstancePropertyModifications(symbol);
            PrefabUtility.RecordPrefabInstancePropertyModifications(symbol.rectTransform);
            return slot;
        }

        private static RectTransform BagRowArea(RectTransform parent, string name, float left, float top, float width, float height) =>
            Area(parent, name, left / BagRowSourceWidth, top / BagRowSourceHeight, width / BagRowSourceWidth, height / BagRowSourceHeight);

        private static Image BagWindowPlate(RectTransform parent, string name, float left, float top, float width, float height,
            string sprite = "_btn_small_black") =>
            BagImage(BagArea(parent, name, left, top, width, height), BagArt(sprite), BagPlateTint, true);

        private static Image BagImage(RectTransform rect, Sprite sprite, Color color, bool sliced = false)
        {
            Image image = Paint(rect, sprite, color, false, sliced);
            image.preserveAspect = !sliced;
            if (image.type == Image.Type.Sliced)
                image.pixelsPerUnitMultiplier = 2f / 3f;
            return image;
        }
    }
}
