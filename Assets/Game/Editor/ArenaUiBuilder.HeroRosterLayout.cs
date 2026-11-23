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
        private static readonly Color32 RosterMuted = new Color32(171, 179, 191, 255);
        private static readonly Color32 RosterLevelColor = new Color32(255, 150, 40, 255);

        private static Sprite RosterArt(string stem) => Load<Sprite>(InventoryTextures + "Roster/" + stem + ".png");

        private static Sprite HeroCardElementArt(string element) =>
            element == "fire" || element == "ice" || element == "wind"
                ? RosterArt("cm_icon_pro" + element)
                : HudSprite("cm_icon_prom" + element);

        private static Sprite HeroZodiacArt(string zodiac)
        {
            string stem = zodiac switch
            {
                "aries" => "ram",
                "taurus" => "bull",
                "gemini" => "twins",
                "cancer" => "crab",
                "leo" => "lion",
                "virgo" => "maiden",
                "libra" => "scales",
                "scorpio" => "scorpion",
                "sagittarius" => "archer",
                "capricorn" => "goat",
                "aquarius" => "waterbearer",
                "pisces" => "fish",
                _ => null
            };
            return stem == null ? null : RosterArt("cm_icon_zodiac_" + stem);
        }

        private static List<(string Key, Object Target)> BuildHeroRosterLayout(RectTransform roster, out RectTransform composition)
        {
            var starField = Picture(roster, "Hero detail star field", RosterArt("base_unit_blue"), 0, 0, 1, 1);
            starField.color = new Color32(234, 214, 199, 255);
            var backdropAspect = starField.gameObject.AddComponent<AspectRatioFitter>();
            backdropAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropAspect.aspectRatio = starField.sprite.rect.width / starField.sprite.rect.height;
            var shading = Area(roster, "Hero detail shading", 0, 0, 1, 1);
            var bottomVignette = Picture(shading, "Bottom vignette", Art("_grow_s"), 0, .74f, 1f, .26f);
            bottomVignette.rectTransform.localScale = new Vector3(1, -1, 1);
            bottomVignette.color = Black(.71f);
            Picture(shading, "Summary shadow", Art("_grow"), -.22f, .16f, .61f, 1.12f).color = Black(.65f);
            composition = Area(roster, "Hero composition", 0, 0, 1, 1);
            var header = Area(roster, "Hero header composition", 0, 0, 1, 1);
            Paint(Area(header, "Hero header shade", 0, 0, 1, .092f), null, Black(.18f));
            Picture(header, "Hero help glyph", Art("cm_hud_top_btn_help"), .209f, .013f, .043f, .067f, true);
            DrawNativeTopBarIcons(Area(header, "Roster top bar icons", 0, 0, 1, 1));

            var detail = Area(composition, "Selected hero detail", 0, 0, 1, 1);
            var detailIllustration = Paint(Area(detail, "Selected hero illustration", .24f, .14f, .54f, .80f),
                null, Color.white);
            detailIllustration.preserveAspect = true;

            var portraitFrame = Area(detail, "Portrait frame", 0, 0, 1, 1);
            var portraitRoot = Area(portraitFrame, "Native hero portrait", .5f, 0, 0, 1);
            var aspect = portraitRoot.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspect.aspectRatio = 1;
            var detailPortrait = portraitRoot.gameObject.AddComponent<RawImage>();
            detailPortrait.raycastTarget = false;
            detailPortrait.enabled = false;

            BuildHeroDetailRail(detail);
            var bindings = BuildHeroEquipmentSlots(detail);

            var detailElement = Paint(Area(detail, "Element", .069f, .112f, .020f, .042f), null, Color.white);
            var detailRole = Paint(Area(detail, "Class", .148f, .116f, .020f, .036f), null, Color.white);
            detailElement.preserveAspect = detailRole.preserveAspect = true;
            var zodiacIcon = Paint(Area(detail, "Zodiac", .215f, .115f, .020f, .036f), null, Color.white);
            zodiacIcon.preserveAspect = true;
            var elementName = Text(detail, "Element name", null, 23, .092f, .113f, .054f, .042f, RosterMuted);
            var roleName = Text(detail, "Class name", null, 23, .170f, .113f, .043f, .042f, RosterMuted);
            var zodiacName = Text(detail, "Zodiac name", null, 23, .238f, .113f, .063f, .042f, RosterMuted);

            var nameLine = Area(detail, "Name and rarity", .072f, .155f, .338f, .062f);
            var nameLayout = nameLine.gameObject.AddComponent<HorizontalLayoutGroup>();
            nameLayout.childControlWidth = true;
            nameLayout.childControlHeight = true;
            nameLayout.childForceExpandWidth = false;
            nameLayout.childForceExpandHeight = true;
            nameLayout.childAlignment = TextAnchor.MiddleLeft;
            nameLayout.spacing = 10f;
            var heroName = Text(nameLine, "Hero name", null, 49, 0, 0, 1, 1);
            heroName.fontStyle = FontStyles.Bold;
            heroName.richText = false;
            var stars = Area(nameLine, "Rarity icons", 0, 0, 0, 1);
            stars.gameObject.AddComponent<LayoutElement>().preferredWidth = 165;
            var heroStars = Text(detail, "Rarity text", null, 1, 0, 0, 0, 0, Color.clear);
            heroStars.gameObject.SetActive(false);

            Paint(Area(detail, "Name divider", .068f, .231f, .180f, .013f), Art("_bar_23"),
                new Color32(255, 255, 255, 65), sliced: true);
            var heroLevel = Text(detail, "Hero level", null, 43, .073f, .261f, .18f, .056f, RosterLevelColor);
            heroLevel.fontStyle = FontStyles.Bold;

            bindings.AddRange(new (string Key, Object Target)[]
            {
                ("rosterComposition", composition), ("rosterHeaderComposition", header),
                ("rosterShading", shading),
                ("rosterDetail", detail), ("detailName", heroName), ("detailLevel", heroLevel),
                ("detailStars", heroStars), ("detailIllustration", detailIllustration),
                ("detailPortrait", detailPortrait), ("detailElement", detailElement), ("detailRole", detailRole),
                ("detailElementName", elementName), ("detailRoleName", roleName),
                ("detailZodiac", zodiacIcon), ("detailZodiacName", zodiacName)
            });
            for (int index = 0; index < 6; index++)
            {
                var star = Paint(Area(stars, "Star " + (index + 1), .139f * index, .11f, .226f, .74f),
                    null, Color.white);
                star.preserveAspect = true;
                bindings.Add(("detailStar" + index, star));
            }
            BuildHeroDetailStats(detail, bindings);

            var rightShadow = Picture(detail, "Roster list vignette", Art("_grow"), .741f, -.178f, .48f, 1.38f);
            rightShadow.color = Black(.93f);
            BuildHeroRosterList(composition, header, bindings);
            return bindings;
        }

        private static void BuildHeroDetailRail(RectTransform detail)
        {
            var rail = Area(detail, "Hero detail sections", 0, .092f, .058f, .908f);
            Paint(rail, RosterArt("_box_gray"), new Color32(132, 142, 196, 255));
            var selected = Picture(detail, "Equipment tab highlight", InventoryArt("_tap_select_blue"),
                0, .197f, .078f, .087f);
            selected.color = Color.white;

            string[] tabs = { "icon_menu_hero", "icon_menu_equip", "icon_menu_profile", "icon_menu_story" };
            for (int index = 0; index < tabs.Length; index++)
            {
                var art = index == 1 ? InventoryArt(tabs[index]) : RosterArt(tabs[index]);
                Picture(detail, tabs[index], art, .010f, .102f + index * .104f, .038f, .066f, true);
            }
        }

        private static List<(string Key, Object Target)> BuildHeroEquipmentSlots(RectTransform detail)
        {
            var bindings = new List<(string Key, Object Target)>();
            var equipment = Area(detail, "Hero equipment slots", 0, 0, 1, 1);
            string[] equipmentTypes = { "weapon", "helm", "armor", "neck", "ring", "boots" };
            for (int index = 0; index < equipmentTypes.Length; index++)
            {
                bool left = index < 3;
                string type = equipmentTypes[index];
                var slot = InstantiateEquipmentSlot(equipment, "Empty " + type + " slot",
                    RosterArt("cm_item_slot_0"), RosterArt("cm_icon_par" + type), !left);
                float x = left ? (index == 0 ? 449f : 450f) : 909f;
                float y = index % 3 == 0 ? 397f : index % 3 == 1 ? 292f : 188f;
                PlaceHeroControl(slot, x / 1280f, y / 720f, 98, 99);
                if (!left) AnchorEquipmentToRoster(slot, x);
                bindings.Add(("equipmentSlot_" + type, slot.GetComponent<BindingMap>()));
            }

            var artifact = InstantiateEquipmentSlot(equipment, "Empty artifact slot",
                RosterArt("cm_item_slot_arti_0"), RosterArt("cm_icon_pararti"), true, artifact: true);
            PlaceHeroControl(artifact, 929f / 1280f, 539f / 720f, 123, 137);
            AnchorEquipmentToRoster(artifact, 929f);

            var actions = new[]
            {
                ("icon_menu_equip", "roster_manage_equipment"),
                ("icon_menu_equip_auto", "roster_auto_equip"),
                ("icon_menu_unequip_auto", "roster_remove_all")
            };
            for (int index = 0; index < actions.Length; index++)
            {
                float x = .424f + index * .071f;
                var action = Picture(detail, actions[index].Item2, RosterArt("_btn_blue_r"), x, .811f, .065f, .134f, true);
                PlaceHeroControl(action.rectTransform, x + .0325f, 89f / 720f, 95, 97);
                string icon = actions[index].Item1;
                Picture(action.rectTransform, "Action icon", index == 0 ? InventoryArt(icon) : RosterArt(icon),
                    14.5f / 95f, 13.5f / 97f, 65f / 95f, 65f / 97f, true);
                Text(action.rectTransform, "Action caption", InventoryPrefix + actions[index].Item2, 22, -.16f, .99f,
                    1.32f, .32f, alignment: TextAlignmentOptions.Center);
            }
            var inspect = Picture(detail, "Inspect equipment", CommonArt("_btn_small_black"), .651f, .831f, .043f, .077f, true);
            PlaceHeroControl(inspect.rectTransform, .6725f, 91f / 720f, 60, 60);
            Picture(inspect.rectTransform, "Inspect icon", RosterArt("icon_menu_zoom"), .185f, .185f, .63f, .63f, true)
                .color = new Color32(255, 255, 255, 127);
            return bindings;
        }

        private static void PlaceHeroControl(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height) * HomeDesignScale;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(rect))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        }

        private static void AnchorEquipmentToRoster(RectTransform control, float nativeX)
        {
            // The 361 px focus card is anchored to the right. Keep the space between
            // it and the equipment column when the landscape composition narrows.
            control.anchorMin = control.anchorMax = new Vector2(1f, control.anchorMin.y);
            control.anchoredPosition = new Vector2((nativeX - 1280f) * HomeDesignScale, 0f);
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(control);
        }

        private static void BuildHeroDetailStats(RectTransform detail, List<(string Key, Object Target)> bindings)
        {
            string[] stats = { "attack", "defense", "health", "speed", "critChance", "critDamage", "effectiveness", "resistance" };
            for (int index = 0; index < stats.Length; index++)
            {
                string key = stats[index];
                float y = .568f + index * .0315f;
                var label = Text(detail, "Stat " + key, InventoryPrefix + "stat." + key, 23, .072f, y, .10f, .036f, RosterMuted);
                var value = Text(detail, "Stat value " + key, null, 23, .170f, y, .047f, .036f, RosterMuted,
                    TextAlignmentOptions.MidlineRight);
                bindings.Add(("detailStat_" + key, value));
                bindings.Add(("detailStatLabel_" + key, label));
            }
        }

        private static void BuildHeroRosterList(RectTransform roster, RectTransform header, List<(string Key, Object Target)> bindings)
        {
            var controls = Area(header, "Roster controls", 0, .093f, 1, .071f);
            var expand = Button(Area(controls, "Expand roster", .706f, .102f, .029f, .76f), null, Color.clear);
            var expandPlate = Picture((RectTransform)expand.transform, "Folded button", CommonArt("_btn_small_black"), 0, 0, 1, 1);
            var collapsePlate = Picture((RectTransform)expand.transform, "Unfolded button", Art("_btn_small_white"), 0, 0, 1, 1);
            Picture(expandPlate.rectTransform, "Folded arrow", RosterArt("icon_menu_arrow2"), .15f, .14f, .70f, .72f, true)
                .rectTransform.localScale = new Vector3(-1, 1, 1);
            var collapseArrow = Picture(collapsePlate.rectTransform, "Unfolded arrow", RosterArt("icon_menu_arrow2"),
                .15f, .14f, .70f, .72f, true);
            collapseArrow.color = new Color32(12, 18, 25, 255);
            collapsePlate.gameObject.SetActive(false);
            var expandLabel = Text(controls, "Expand roster label", null, 1, 0, 0, 0, 0, Color.clear);
            expandLabel.gameObject.SetActive(false);

            Picture(controls, "Capacity plus", RosterArt("_plus"), .919f, .19f, .017f, .58f, true).color = RosterMuted;
            var capacity = Text(controls, "Roster capacity", null, 25, .935f, .09f, .061f, .78f, RosterMuted);

            var expandedHint = Area(controls, "Expanded roster hint", .035f, .21f, .46f, .82f);
            Picture(expandedHint, "Info", Art("cm_icon_etcinfor"), 0, .16f, .035f, .55f, true).color = RosterMuted;
            Text(expandedHint, "Expanded roster caption", InventoryPrefix + "roster_tip", 23,
                .053f, .03f, .90f, .77f, RosterMuted);
            expandedHint.gameObject.SetActive(false);

            var sidebar = Area(roster, "Roster sidebar", .780f, .16f, .22f, .711f);
            var scroll = InventoryScroll(sidebar, "Roster hero rows", 0, 0, 1, 1);
            scroll.gameObject.AddComponent<FocusedRosterScroll>().enabled = false;
            var footer = Area(roster, "Roster footer", .805f, .861f, .19f, .119f);
            Paint(Area(footer, "Footer divider", 0, .015f, 1, .095f), Art("_bar_23"),
                new Color32(255, 255, 255, 72), sliced: true);
            var reset = Paint(Area(footer, "Reset equipment", 0, .22f, .20f, .63f),
                CommonArt("_btn_small_black"), Color.white, sliced: true);
            Picture(reset.rectTransform, "Reset icon", RosterArt("icon_menu_equip_undo"), .08f, .08f, .84f, .84f, true)
                .color = RosterMuted;
            var transfer = Paint(Area(footer, "Transfer hero", .234f, .22f, .71f, .63f),
                CommonArt("_btn_small_black"), Color.white, sliced: true);
            Picture(transfer.rectTransform, "Transfer icon", RosterArt("icon_quick_transfer"), .05f, .16f, .18f, .68f, true)
                .color = RosterMuted;
            Text(transfer.rectTransform, "Transfer caption", InventoryPrefix + "roster_transfer", 23,
                .25f, .07f, .69f, .86f, RosterMuted, TextAlignmentOptions.Center);

            bindings.AddRange(new (string Key, Object Target)[]
            {
                ("rosterSidebar", sidebar),
                ("expand", expand), ("expandLabel", expandLabel), ("rosterExpandIcon", expandPlate.gameObject),
                ("rosterCollapseIcon", collapsePlate.gameObject), ("rosterCollapsedFooter", footer),
                ("rosterExpandedHint", expandedHint.gameObject), ("rosterCapacity", capacity),
                ("rosterScroll", scroll), ("rosterRow", BuildHeroRosterCard(roster))
            });
        }

        private static TMP_InputField BuildRosterSearch(RectTransform parent, out GameText hint)
        {
            var rect = Area(parent, "Search heroes", .738f, .102f, .084f, .76f);
            var plate = Paint(rect, CommonArt("_btn_small_black"), Color.white, true, true);
            Picture(rect, "Search icon", Art("cm_icon_etczoomer"), .06f, .135f, .235f, .73f, true).color = RosterMuted;
            var field = rect.gameObject.AddComponent<TMP_InputField>();
            var viewport = Area(rect, "Text viewport", .28f, .02f, .68f, .96f);
            viewport.gameObject.AddComponent<RectMask2D>();
            field.targetGraphic = plate;
            field.textViewport = viewport;
            field.textComponent = Text(viewport, "Search text", null, 24, 0, 0, 1, 1);
            hint = Text(viewport, "Placeholder", InventoryPrefix + "roster_search", 24, 0, 0, 1, 1,
                RosterMuted, TextAlignmentOptions.Center);
            field.placeholder = hint;
            field.characterLimit = 24;
            field.richText = false;
            return field;
        }

        private static GameObject BuildHeroRosterCard(RectTransform parent)
        {
            var card = Area(parent, "Hero card template", 0, 0, 1, 1);
            var button = Button(card, DraftSprite("hero_bg_normal"), Color.white);
            AddRosterPlateShadow(button.targetGraphic);
            button.transition = Selectable.Transition.None;
            var selection = Picture(card, "Selected plate", DraftSprite("hero_bg_selected"), 0, 0, 1, 1);
            selection.enabled = false;

            // unit_bar.csb uses the native slanted silhouette to clip the actual wide original portrait.
            var crop = Picture(card, "Hero card clip", DraftSprite("hero_bg_mask"), 0, 0, 1, 1);
            crop.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = RosterPicture(crop.rectTransform, "Wide portrait", null, 125, 10, 222, 89, true);
            var role = RosterPicture(card, "Class", null, 42, 23, 32, 32, true);
            var name = RosterName(card);
            // n_lv uses right-aligned native digits at 0.85 scale. The rightmost digit
            // ends at x=71. Max replaces both digits in the same space.
            var first = RosterPicture(card, "Level units", null, 53.15f, 62.7f, 17.85f, 25.5f, true);
            var second = RosterPicture(card, "Level tens", null, 39.42f, 62.7f, 17.85f, 25.5f, true);
            var maximum = RosterPicture(card, "Max level", BagArt("hero_lv_max"), 20.85f, 62.7f, 51.85f, 25.5f, true);
            maximum.enabled = false;
            var element = RosterPicture(card, "Element", null, 70, 67, 34, 34, true);
            var hidden = Area(card, "Level sprite references", 0, 0, 0, 0);
            hidden.gameObject.SetActive(false);
            var bindings = new List<(string Key, Object Target)>
            {
                ("name", name), ("portrait", portrait), ("selection", selection), ("element", element), ("role", role),
                ("rosterLevelFirst", first), ("rosterLevelSecond", second), ("rosterLevelMax", maximum)
            };
            for (int index = 0; index < 10; ++index)
                bindings.Add(("rosterLevelDigit" + index, RosterPicture(hidden, "Digit " + index,
                    DraftSprite("TeamBadges/hero_lv_" + index), 0, 0, 0, 0)));
            for (int index = 0; index < 6; index++)
            {
                var star = RosterPicture(card, "Star " + (index + 1), null, 103 + index * 10, 69, 26.1f, 25.23f, true);
                bindings.Add(("star" + index, star));
            }
            Bind(card, bindings.ToArray());
            card.gameObject.SetActive(false);
            return card.gameObject;
        }
    }
}
