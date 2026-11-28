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
        private const string HeroInformationPath = Home + "Res/Prefab/HeroInformation.prefab";
        private const string ArtifactInformationPath = Home + "Res/Prefab/ArtifactInformation.prefab";

        private static void BuildInformationPrefabs()
        {
            BuildHomeHeroArt();
            BuildInformationPrefab(true);
            BuildInformationPrefab(false);
        }

        private static void BuildInformationPrefab(bool hero)
        {
            var root = Root(hero ? "HeroInformation" : "ArtifactInformation");
            var keys = new List<(string Key, Object Target)>();
            var close = Button(Area(root, "Dismiss", 0, 0, 1, 1), ChatArt("ChatDim"), Color.white);
            keys.Add(("close", close));
            var card = Area(root, "Information", 0, 0, 0, 0);
            card.anchorMin = card.anchorMax = card.pivot = Vector2.one * .5f;
            card.sizeDelta = hero ? new Vector2(1224, 915) : new Vector2(570, 860);
            card.anchoredPosition = hero ? Vector2.zero : new Vector2(0, 65);
            if (hero) BuildCatalogHero(card, root, keys);
            else BuildCatalogArtifact(card, root, keys);
            Bind(root, keys.ToArray());
            Save(root.gameObject, hero ? HeroInformationPath : ArtifactInformationPath);
        }

        private static void BuildCatalogHero(RectTransform card, RectTransform root, List<(string Key, Object Target)> keys)
        {
            // The CSB uses an 816x610 clipped content plane inside a larger, shadow-padded 890x683 frame.
            var surround = Area(card, "Original frame shadow", -.044f, -.055f, 1.09f, 1.12f);
            Paint(surround, CommonArt("_box_black_white"), Color.white, true, true).pixelsPerUnitMultiplier = 1f / 1.5f;
            var art = Area(card, "Illustration surface", 0, 0, 1, 1);
            art.gameObject.AddComponent<RectMask2D>();
            Picture(art, "Backdrop", InventoryArt("cm_preview_bg"), 0, 0, 1, 1);
            keys.Add(("illustration", Picture(art, "Portrait fallback", null, .28f, 0, .72f, 1, true)));
            var animated = Area(art, "Animated portrait", .27f, -.015f, .75f, 1.05f).gameObject.AddComponent<RawImage>();
            animated.raycastTarget = false;
            keys.Add(("animated", animated));
            Paint(Area(card, "Original silver rim", -.044f, -.055f, 1.09f, 1.12f),
                  InventoryArt("Catalog/_box_black_white_1"), Color.white, false, true).pixelsPerUnitMultiplier = 1f / 1.5f;

            for (int i = 0; i < 3; i++)
            {
                var key = new[] { "element", "role", "zodiac" }[i];
                keys.Add((key, Picture(card, key, null, .036f, .038f, .036f, .049f, true)));
                keys.Add((key + "Name", Text(card, key + " label", null, 26, .075f, .036f, .19f, .051f, Mist)));
            }
            var name = Text(card, "Hero name", null, 54, .035f, .094f, .55f, .087f, Color.white);
            name.fontStyle = FontStyles.Bold;
            keys.Add(("name", name));
            var stars = Area(card, "Stars", .20f, .115f, .14f, .055f);
            keys.Add(("stars", stars));
            for (int i = 0; i < 5; i++)
                Picture(stars, "Star " + i, DraftSprite("TeamBadges/cm_icon_star"), i * .17f, 0, .255f, 1, true);
            Picture(card, "Rule", CommonArt("_bar_2"), .03f, .187f, .287f, .016f);
            BuildCatalogLevel(Area(card, "Hero level", .029f, .225f, .153f, .052f), true, keys);
            var intro = Text(card, "Introduction", null, 25, .036f, .305f, .286f, .138f, Color.white, TextAlignmentOptions.TopLeft, true);
            keys.Add(("intro", intro));

            keys.Add(("imprintFormation", Picture(card, "Imprint formation", null, .031f, .445f, .076f, .084f, true)));
            Picture(card, "Locked imprint", Art("cm_icon_etclock"), .089f, .485f, .026f, .045f, true);
            keys.Add(("imprintLock", Text(card, "Imprint state", null, 25, .124f, .433f, .13f, .057f, Mist)));
            keys.Add(("imprintLabel", Text(card, "Imprint effect", null, 24, .124f, .485f, .151f, .093f, Color.white, TextAlignmentOptions.TopLeft, true)));
            var inspect = Button(Area(card, "Inspect imprint", .264f, .444f, .061f, .084f), BagArt("_btn_small_black"), Color.white);
            Picture((RectTransform)inspect.transform, "Magnify", Art("cm_icon_etczoomer"), .16f, .15f, .68f, .66f, true).color = new Color32(142,142,142,255);
            keys.Add(("imprintButton", inspect));
            for (int i = 0; i < 3; i++)
            {
                var button = Button(Area(card, "Skill " + i, .035f + i * .0933f, .596f, .09f, .122f), null, Color.clear);
                keys.Add(("skill" + i, button));
                keys.Add(("skillIcon" + i, Picture((RectTransform)button.transform, "Skill icon", null, 0, 0, 1, 1, true)));
                keys.Add(("skillAnchor" + i, Area(card, "Skill tooltip anchor " + i, .035f + i * .0933f, .96f, .09f, .042f)));
            }
            var skillTemplate = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(CommonUiPrefabs.PanelPath), root);
            skillTemplate.SetActive(false);
            keys.Add(("skillTemplate", skillTemplate));
        }

        private static void BuildCatalogLevel(RectTransform host, bool hero, List<(string Key, Object Target)> keys)
        {
            Sprite Digit(int n) => DraftSprite("TeamBadges/hero_lv_" + n);
            Picture(host, "Level prefix", hero ? DraftSprite("TeamBadges/hero_lv") : InventoryArt("Catalog/hero_lv_artiskill"), 0, 0, .30f, 1, true);
            Picture(host, "Current level", Digit(1), .31f, 0, .16f, 1, true);
            Picture(host, "Level separator", InventoryArt("Catalog/hero_lv_s"), .43f, 0, .16f, 1, true);
            if (!hero) { Picture(host, "Skill level limit", Digit(6), .58f, 0, .16f, 1, true); return; }
            for (int stars = 1; stars <= 6; stars++)
                keys.Add(("limit" + stars, Picture(host, "Native grade " + stars, Digit(stars), .58f, 0, .16f, 1, true)));
            Picture(host, "Level units", Digit(0), .71f, 0, .16f, 1, true);
        }

        private static void BuildCatalogArtifact(RectTransform card, RectTransform root, List<(string Key, Object Target)> keys)
        {
            // The original vertical panel has a transparent perimeter in its nine-sliced sprite.
            var surface = Area(card, "Original blue gradient panel", 0, 0, 1, 1);
            surface.offsetMin = new Vector2(-46, -46);
            surface.offsetMax = new Vector2(46, 46);
            Paint(surface, ArtifactRestoreArt("_box_equip_0_linear"), Color.white, true, true).pixelsPerUnitMultiplier = 1f / 1.5f;
            var face = Area(card, "Card illustration", .052f, .037f, .548f, .574f);
            keys.Add(("card", Picture(face, "Painting", null, 0, 0, 1, 1)));
            Paint(Area(face, "Original surround", -.008f, -.003f, 1.029f, 1.018f),
                ArtifactRestoreArt("arti_frame_box"), Color.white, false, true).pixelsPerUnitMultiplier = 1f / 1.5f;
            Picture(face, "Gold filigree", SummonArt("arti_frame_deco_1"), 0, 0, .44f, .40f);
            keys.Add(("icon", Picture(face, "Artifact emblem", null, -.036f, -.039f, .261f, .213f, true)));
            for (int i = 0; i < 5; i++)
                keys.Add(("star" + i, Picture(face, "Star " + i, DraftSprite("TeamBadges/cm_icon_star"), .865f, .021f + i * .05f, .081f, .052f, true)));
            var zoom = Button(Area(face, "Zoom", .054f, .86f, .194f, .111f), BagArt("_btn_small_black"), Color.white);
            Picture((RectTransform)zoom.transform, "Magnify", RosterArt("icon_menu_zoom"), .16f, .09f, .69f, .78f, true);
            keys.Add(("zoom", zoom));
            foreach (var role in new[] {"warrior", "knight", "ranger", "mage", "thief", "soul_weaver"})
            {
                string file = role == "thief" ? "assassin" : role == "soul_weaver" ? "manauser" : role;
                keys.Add(("class." + role, Picture(face, role, HudSprite("cm_icon_role_" + file), .795f, .879f, .17f, .11f, true)));
            }

            keys.Add(("grade", Text(card, "Artifact grade", null, 23, .619f, .107f, .367f, .052f, new Color32(248,117,66,255))));
            keys.Add(("name", Text(card, "Artifact name", null, 26, .619f, .154f, .356f, .095f, Color.white, TextAlignmentOptions.TopLeft, true)));
            BuildCatalogLevel(Area(card, "Artifact skill level", .618f, .253f, .209f, .041f), false, keys);
            for (int i = 0; i < 5; i++)
                Picture(card, "Limit break slot " + i, SummonArt("arti_gauge_bg"), .622f + .061f * i, .292f, .064f, .018f, true);
            keys.Add(("attackLabel", Text(card, "Attack heading", null, 25, .624f, .315f, .343f, .049f, Mist)));
            keys.Add(("attack", Text(card, "Attack", null, 30, .624f, .354f, .343f, .055f, Color.white)));
            keys.Add(("healthLabel", Text(card, "Health heading", null, 25, .624f, .402f, .343f, .049f, Mist)));
            keys.Add(("health", Text(card, "Health", null, 30, .624f, .444f, .343f, .055f, Color.white)));
            Paint(Area(card, "Tabs base", .053f, .632f, .902f, .067f), SummonArt("_btn_white"), new Color32(12,26,39,226), false, true);
            for (int i = 0; i < 2; i++)
            {
                string key = i == 0 ? "skill" : "story";
                var tab = Button(Area(card, key, .053f + i * .45f, .632f, .451f, .067f), SummonArt("_btn_white"), Color.white);
                keys.Add((key, tab));
                keys.Add((key + "Label", Text((RectTransform)tab.transform, "Label", null, 24, i == 0 ? .17f : 0, 0,
                    i == 0 ? .77f : 1, 1, Color.black, TextAlignmentOptions.Center)));
                if (i == 0) keys.Add(("skillGlyph", Picture((RectTransform)tab.transform, "Artifact skill", InventoryArt("icon_menu_pararti"), .17f, .17f, .16f, .64f, true)));
            }
            var content = InventoryScroll(card, "Skill and story description", .060f, .712f, .89f, .267f);
            var layout = content.content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var desc = Text(content.content, "Description", null, 25, 0, 0, 1, 1, Color.white, TextAlignmentOptions.TopLeft, true);
            desc.overflowMode = TextOverflowModes.Overflow;
            keys.Add(("description", desc));
            keys.Add(("descriptionScroll", content));

            var painting = Area(root, "Painting", 0, 0, 1, 1);
            keys.Add(("painting", painting.gameObject));
            Paint(Area(painting, "Original gallery backdrop", -.114f, -.072f, 1.15f, 1.32f), InventoryArt("base_unit_hero"), Color.white, true);
            var page = Area(painting, "Oriented painting page", 0, 0, 1, 1);
            keys.Add(("paintingPage", page));
            keys.Add(("paintingArt", Picture(page, "Full painting", null, .02f, .022f, .96f, .956f, true)));
            var glow = Picture(page, "Gallery controls shadow", RosterArt("SourceShadows/_grow"), .24f, .53f, .52f, .59f);
            glow.color = new Color32(255,255,255,169);
            keys.Add(("galleryShadow",glow.rectTransform));
            keys.Add(("paintBack", SharedHeaderBack(Area(page, "Gallery header", 0, 0, 1, .09f))));
            var title=Text(page, "Title", null, 34, 0, 0, .5f, .09f, Color.white);
            title.rectTransform.anchorMin=title.rectTransform.anchorMax=title.rectTransform.pivot=Vector2.up;
            title.rectTransform.anchoredPosition=new Vector2(100,0);
            title.rectTransform.sizeDelta=new Vector2(600,99);
            keys.Add(("paintingTitle", title));
            var rotate = Button(Area(page, "Rotate", .593f, .705f, .11f, .067f), BagArt("_btn_small_black"), Color.white);
            keys.Add(("rotate", rotate));
            Picture((RectTransform)rotate.transform, "Rotate icon", InventoryArt("Catalog/icon_menu_auto"), .08f, .18f, .22f, .64f, true);
            keys.Add(("rotateLabel", Text((RectTransform)rotate.transform, "Label", null, 25, .30f, 0, .67f, 1, Mist, TextAlignmentOptions.Center)));
            var hint = Area(page, "Gesture hint", .315f, .79f, .38f, .181f);
            Paint(hint, InventoryArt("Catalog/_box_whiteline"), new Color32(255,255,255,96), false, true).pixelsPerUnitMultiplier=1f/1.5f;
            keys.Add(("gesturePanel",hint));
            Picture(hint, "Information", SummonArt("icon_menu_info"), .47f, .075f, .06f, .225f, true);
            keys.Add(("gestureHelp", Text(hint, "Instructions", null, 24, .04f, .30f, .92f, .67f, Mist, TextAlignmentOptions.Center, true)));
            painting.gameObject.SetActive(false);
        }
    }
}
