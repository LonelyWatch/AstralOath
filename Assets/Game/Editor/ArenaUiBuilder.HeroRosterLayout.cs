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

        private static List<(string Key, Object Target)> BuildHeroRosterLayout(RectTransform roster)
        {
            var starField = Picture(roster, "Hero detail star field", RosterArt("base_unit_blue"), 0, 0, 1, 1);
            starField.color = new Color32(234, 214, 199, 255);
            var backdropAspect = starField.gameObject.AddComponent<AspectRatioFitter>();
            backdropAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropAspect.aspectRatio = starField.sprite.rect.width / starField.sprite.rect.height;
            Paint(Area(roster, "Hero header shade", 0, 0, 1, .092f), null, Black(.18f));
            Picture(roster, "Hero help glyph", Art("cm_hud_top_btn_help"), .209f, .013f, .043f, .067f, true);

            var detail = Area(roster, "Selected hero detail", 0, 0, 1, 1);
            var bottomVignette = Picture(detail, "Bottom vignette", Art("_grow_s"), 0, .74f, 1f, .26f);
            bottomVignette.rectTransform.localScale = new Vector3(1, -1, 1);
            bottomVignette.color = Black(.71f);
            Picture(detail, "Summary shadow", Art("_grow"), -.22f, .16f, .61f, 1.12f).color = Black(.65f);

            var detailIllustration = Paint(Area(detail, "Selected hero illustration", .24f, .14f, .54f, .80f),
                null, Color.white);
            detailIllustration.preserveAspect = true;

            var portraitFrame = Area(detail, "Portrait frame", .255f, .141f, .511f, .806f);
            var portraitRoot = Area(portraitFrame, "Native hero portrait", 0, 0, 1, 1);
            var aspect = portraitRoot.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1;
            var detailPortrait = portraitRoot.gameObject.AddComponent<RawImage>();
            detailPortrait.raycastTarget = false;
            detailPortrait.enabled = false;

            BuildHeroDetailRail(detail);
            BuildHeroEquipmentSlots(detail);

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

            var bindings = new List<(string Key, Object Target)>
            {
                ("rosterDetail", detail), ("detailName", heroName), ("detailLevel", heroLevel),
                ("detailStars", heroStars), ("detailIllustration", detailIllustration),
                ("detailPortrait", detailPortrait), ("detailElement", detailElement), ("detailRole", detailRole),
                ("detailElementName", elementName), ("detailRoleName", roleName),
                ("detailZodiac", zodiacIcon), ("detailZodiacName", zodiacName)
            };
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
            BuildHeroRosterList(roster, bindings);
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

        private static void BuildHeroEquipmentSlots(RectTransform detail)
        {
            var equipment = Area(detail, "Hero equipment slots", 0, 0, 1, 1);
            string[] equipmentTypes = { "weapon", "helm", "armor", "neck", "ring", "boots" };
            for (int index = 0; index < equipmentTypes.Length; index++)
            {
                bool left = index < 3;
                string type = equipmentTypes[index];
                var slot = Picture(equipment, "Empty " + type + " slot", RosterArt("cm_item_slot_0"),
                    left ? .300f : .680f, .379f + (index % 3) * .145f, .067f, .1375f, true);
                Picture(slot.rectTransform, type + " symbol", RosterArt("cm_icon_par" + type),
                    left ? .075f : .555f, .556f, .335f, .333f, true).color = new Color32(255, 255, 255, 76);
            }

            var artifact = Picture(equipment, "Empty artifact slot", RosterArt("cm_item_slot_arti_0"),
                .682f, .157f, .086f, .191f, true);
            Picture(artifact.rectTransform, "Artifact symbol", RosterArt("cm_icon_pararti"),
                .386f, .645f, .268f, .241f, true).color = new Color32(255, 255, 255, 76);

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
                string icon = actions[index].Item1;
                Picture(action.rectTransform, "Action icon", index == 0 ? InventoryArt(icon) : RosterArt(icon),
                    .177f, .180f, .645f, .645f, true);
                Text(detail, "Action caption", InventoryPrefix + actions[index].Item2, 22, x - .01f, .928f, .085f,
                    .039f, alignment: TextAlignmentOptions.Center);
            }
            var inspect = Picture(detail, "Inspect equipment", CommonArt("_btn_small_black"), .651f, .831f, .043f, .077f, true);
            Picture(inspect.rectTransform, "Inspect icon", RosterArt("icon_menu_zoom"), .185f, .185f, .63f, .63f, true)
                .color = new Color32(255, 255, 255, 127);
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
            Picture(detail, "No set icon", RosterArt("cm_icon_etcbp"), .072f, .940f, .016f, .026f, true)
                .color = RosterMuted;
            Text(detail, "No equipment set", InventoryPrefix + "roster_no_set", 23, .095f, .934f, .18f, .038f, RosterMuted);
        }

        private static void BuildHeroRosterList(RectTransform roster, List<(string Key, Object Target)> bindings)
        {
            var controls = Area(roster, "Roster controls", 0, .093f, 1, .071f);
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

            var search = BuildRosterSearch(controls, out var searchHint);
            Picture(controls, "Sorting icon", Art("_sorting2"), .833f, .21f, .021f, .56f, true).color = RosterMuted;
            var sortCaption = Text(controls, "Sort caption", InventoryPrefix + "roster_sort_level", 25,
                .855f, .09f, .063f, .78f, RosterMuted);
            Picture(controls, "Capacity plus", RosterArt("_plus"), .919f, .19f, .017f, .58f, true).color = RosterMuted;
            var capacity = Text(controls, "Roster capacity", null, 25, .935f, .09f, .061f, .78f, RosterMuted);

            var expandedHint = Area(controls, "Expanded roster hint", .035f, .21f, .46f, .82f);
            Picture(expandedHint, "Info", Art("cm_icon_etcinfor"), 0, .16f, .035f, .55f, true).color = RosterMuted;
            Text(expandedHint, "Expanded roster caption", InventoryPrefix + "roster_tip", 23,
                .053f, .03f, .90f, .77f, RosterMuted);
            expandedHint.gameObject.SetActive(false);

            var sidebar = Area(roster, "Roster sidebar", .780f, .19f, .22f, .681f);
            var scroll = InventoryScroll(sidebar, "Roster hero rows", 0, 0, 1, 1);
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
                ("rosterSidebar", sidebar), ("search", search), ("searchHint", searchHint),
                ("expand", expand), ("expandLabel", expandLabel), ("rosterExpandIcon", expandPlate.gameObject),
                ("rosterCollapseIcon", collapsePlate.gameObject), ("rosterCollapsedFooter", footer),
                ("rosterExpandedHint", expandedHint.gameObject), ("rosterCapacity", capacity), ("rosterSortCaption", sortCaption),
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
            button.transition = Selectable.Transition.None;
            var selection = Picture(card, "Selected plate", DraftSprite("hero_bg_selected"), 0, 0, 1, 1);
            selection.enabled = false;

            // unit_bar.csb uses the native slanted silhouette to clip the actual wide original portrait.
            var crop = Picture(card, "Hero card clip", DraftSprite("hero_bg_mask"), 0, 0, 1, 1);
            crop.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = Paint(Area(crop.rectTransform, "Wide portrait", .285f, .07f, .715f, .88f),
                null, Color.white);
            portrait.preserveAspect = true;
            var role = Paint(Area(card, "Class", .082f, .169f, .086f, .30f), null, Color.white);
            role.preserveAspect = true;
            var name = Text(card, "Hero name", null, 26, .180f, .141f, .642f, .34f);
            name.fontStyle = FontStyles.Bold;
            name.richText = false;
            var level = Text(card, "Hero level", null, 31, .064f, .557f, .225f, .346f, RosterLevelColor);
            level.fontStyle = FontStyles.Bold;
            var element = Paint(Area(card, "Element", .285f, .559f, .083f, .333f), null, Color.white);
            element.preserveAspect = true;
            var bindings = new List<(string Key, Object Target)>
            {
                ("name", name), ("level", level), ("portrait", portrait), ("selection", selection),
                ("element", element), ("role", role)
            };
            for (int index = 0; index < 6; index++)
            {
                var star = Paint(Area(card, "Star " + (index + 1), .375f + index * .041f, .566f, .081f, .303f), null, Color.white);
                star.preserveAspect = true;
                bindings.Add(("star" + index, star));
            }
            Bind(card, bindings.ToArray());
            card.gameObject.SetActive(false);
            return card.gameObject;
        }
    }
}
