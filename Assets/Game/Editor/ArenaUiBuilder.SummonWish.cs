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
        private static void ApplySummonTextOutline(GameText label, bool light = false)
        {
            string preset = light ? "SummonTextLightOutline" : "SummonTextDarkOutline";
            label.fontSharedMaterial = Load<Material>(SummonPath + "Res/Materials/" + preset + ".mat");
        }

        private static void StoreSummonSprite(RectTransform root, List<(string Key, Object Target)> bindings, string key, string source)
        {
            var stored = Picture(root, "Source " + key, SummonArt(source), 0, 0, 0, 0);
            stored.gameObject.SetActive(false);
            bindings.Add((key, stored));
        }

        private static void BuildSummonWishPanel(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var wish = SummonPanel(root, bindings, "wishPanel", .117f, .033f, .765f, .889f, SummonArt("renew_popup_bg_alpha"));
            BakeSummonDesignRect(wish);
            var face = wish.GetComponent<Image>();
            face.type = Image.Type.Sliced;
            face.color = new Color32(255, 255, 255, 229);
            var outside = (RectTransform)wish.parent;
            var shade = outside.GetComponent<Image>();
            float backdrop = 46f / 255f;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) backdrop = Mathf.GammaToLinearSpace(backdrop);
            shade.color = new Color(0f, 0f, 0f, 1f - backdrop);
            var dismiss = outside.gameObject.AddComponent<GameButton>();
            dismiss.targetGraphic = shade;
            dismiss.transition = Selectable.Transition.None;
            bindings.Add(("wishDismiss", dismiss));
            // A GraphicRaycaster bubbles hits to the parent Button. Keep clicks inside the sheet local.
            var inside = wish.gameObject.AddComponent<GameButton>();
            inside.targetGraphic = face;
            inside.transition = Selectable.Transition.None;
            bindings.Add(("wishSurfaceInput", inside));

            Paint(Area(wish, "Candidate surface", .451f, .011f, .543f, .98f),
                SummonArt("_box_w5"), new Color32(0, 7, 31, 229), sliced: true);
            var title = Text(wish, "Wishlist title", null, 40.5f, .037f, .03f, .31f, .08f);
            ApplySummonTextOutline(title, light: true);
            bindings.Add(("wishTitle", title));
            var divider = Picture(wish, "Selected divider", SummonArt("_bar_23"), .027f, .099f, .412f, .018f);
            divider.type = Image.Type.Sliced;
            divider.pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
            divider.color = new Color32(128, 151, 166, 155);
            Paint(Area(wish, "Candidates divider", .476f, .111f, .5f, .002f), null, new Color32(118, 152, 173, 105));
            Picture(wish, "Wish selection surface", SummonArt("gacha_wish_box_2"), .035f, .216f, .396f, .652f)
                .color = new Color32(31, 133, 255, 17);
            Paint(Area(wish, "Tab background", .029f, .136f, .408f, .077f), SummonArt("_btn_small_black"),
                new Color32(255, 255, 255, 153), sliced: true);
            foreach (bool artifact in new[] { false, true })
            {
                string tab = artifact ? "wishArtifact" : "wishHero";
                float x = artifact ? .233f : .034f;
                var lit = Picture(wish, tab + " selection", SummonArt("gacha_wish_box_1"), x, .215f, .197f, .653f);
                bindings.Add((tab + "Highlight", lit.gameObject));
                var rect = Area(wish, tab, x, .143f, .201f, .064f);
                var tabButton = Button(rect, SummonArt("_btn_white"), Color.white);
                bindings.Add((tab, tabButton));
                Text(rect, "Category", artifact ? "summon.button.wishArtifacts" : "summon.button.wishHeroes", 24.84f,
                    .02f, 0f, .96f, 1f, alignment: TextAlignmentOptions.Center);
            }

            var unowned = Area(wish, "Ownership filter", .470f, .019f, .25f, .103f);
            bindings.Add(("wishUnowned", Button(unowned, null, Color.clear)));
            Picture(unowned, "Checkbox", CommonArt("cm_checkbg"), .027f, .22f, .13f, .56f, true);
            bindings.Add(("wishUnownedCheck", Picture(unowned, "Checked", CommonArt("cm_checkicon2"), .035f, .23f, .115f, .51f, true)));
            ApplySummonTextOutline(Text(unowned, "Label", "summon.wish.unowned", 27, .17f, 0, .83f, 1, new Color32(166, 204, 210, 255)));

            var info = Area(wish, "Wishlist info", .378f, .037f, .056f, .073f);
            bindings.Add(("wishInfo", Button(info, CommonArt("_btn_small_black"), Color.white)));
            Picture(info, "Information", Art("cm_icon_etcinfor"), .22f, .17f, .56f, .66f, true);
            var filter = Area(wish, "Class filter", .856f, .035f, .122f, .073f);
            bindings.Add(("wishFilter", Button(filter, CommonArt("_btn_small_black"), Color.white)));
            Picture(filter, "Filter glyph", Art("_sorting2"), .09f, .21f, .18f, .58f, true);
            Text(filter, "Filter", "arena.draft.filter.button", 25, .29f, 0, .64f, 1);
            var chosen = Area(wish, "Current wishes", .047f, .237f, .388f, .62f);
            bindings.Add(("wishSelectedItems", chosen));
            SummonWishGrid(Area(chosen, "Selected heroes", 0, 0, .477f, 1), bindings, "wishSelectedHeroes", 2);
            SummonWishGrid(Area(chosen, "Selected artifacts", .513f, 0, .477f, 1), bindings, "wishSelectedArtifacts", 2);
            var candidates = InventoryScroll(wish, "Candidates", .473f, .117f, .513f, .871f);
            SummonWishGrid(candidates.content, bindings, "wishItems", 5);
            candidates.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var recommend = Area(wish, "Recommended wishlist", .111f, .891f, .244f, .084f);
            bindings.Add(("wishRecommend", Button(recommend, CommonArt("_btn_small_black"), Color.white)));
            Picture(recommend, "Like", SummonArt("icon_menu_good"), .19f, .21f, .16f, .58f, true);
            Text(recommend, "Recommendation", "summon.wish.recommend", 25.2f, .36f, 0, .60f, 1);
            AddSummonLabel(wish, bindings, "wishCount", null, 22, .462f, .895f, .27f, .07f, true);
            SummonButton(wish, bindings, "wishSave", "summon.button.ok", .762f, .887f, .206f, .08f, SummonArt("_btn_blue"));
            // Attached outside the sheet so it stays centered under the frame at every aspect.
            var close = Area(wish, "Close wishlist", .40f, 1.013f, .20f, .071f);
            bindings.Add(("wishClose", Button(close, null, Color.clear)));
            Picture(close, "Close glyph", Art("cm_icon_etcclose"), .276f, .29f, .105f, .43f, true).color = new Color32(255,255,255,150);
            Text(close, "Close", "summon.button.close", 24, .395f, 0, .55f, 1, new Color32(255,255,255,155));

            var hint = Area(wish, "Wish help", .405f, .104f, .42f, .135f);
            Paint(hint, CommonArt("_box_black_white"), Color.white, true, true);
            Text(hint, "Description", "summon.status.wish_complete", 25, .05f, .1f, .9f, .8f,
                alignment: TextAlignmentOptions.Center, wrap: true);
            bindings.Add(("wishHelp", hint.gameObject));
            hint.gameObject.SetActive(false);
            var choices = Area(wish, "Class choices", .786f, .108f, .193f, .570f);
            Paint(choices, CommonArt("_box_black_white"), Color.white, true, true);
            string[] roleKeys = { "all", "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" };
            for (int i = 0; i < roleKeys.Length; i++)
            {
                string roleKey = roleKeys[i];
                var row = Area(choices, roleKey, .025f, i / 7f, .95f, 1f / 7);
                bindings.Add(("wishRole_" + roleKey, Button(row, null, Color.clear)));
                Text(row, "Class", i == 0 ? "summon.wish.all_classes" : "home.hero_inventory.role." + roleKey,
                    24, .05f, 0, .9f, 1, alignment: TextAlignmentOptions.Center);
            }
            bindings.Add(("wishFilters", choices.gameObject));
            choices.gameObject.SetActive(false);

            var card = Area(root, "Wish entry", 0, 0, .08f, .10f);
            var button = Button(card, null, Color.clear);
            var icon = Picture(card, "Portrait", null, .095f, .055f, .81f, .87f, true);
            var heroFrame = Picture(card, "Hero rim", CommonArt("_hero_s_frame_enemy"), .066f, .025f, .873f, .935f, true);
            var role = Picture(card, "Class", null, .055f, .095f, .235f, .235f, true);
            var element = Picture(card, "Element", null, .716f, .085f, .23f, .23f, true);
            for (int i = 0; i < 5; ++i)
                Picture(card, "Star " + i, DraftSprite("TeamBadges/cm_icon_star"), .205f + .118f * i, .763f, .166f, .187f, true);
            var selected = Picture(card, "Selected", SummonArt("_checked"), .35f, .38f, .30f, .22f, true);
            var minus = Picture(card, "Remove wish", SummonArt("btn_minus"), .603f, .416f, .52f, .52f, true);
            var name = Text(card, "Name", null, 19, 0, 0, 1, 1);
            name.gameObject.SetActive(false);
            Bind(card, ("button", button), ("icon", icon), ("heroFrame", heroFrame), ("role", role), ("element", element), ("selected", selected),
                ("remove", minus.gameObject), ("name", name));
            card.gameObject.SetActive(false);
            bindings.Add(("wishTemplate", card.gameObject));
        }
    }
}
