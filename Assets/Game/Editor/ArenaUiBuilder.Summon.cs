using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Client.Summon;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string SummonPath = "Assets/Game/Modules/Summon/";
        private const string FeaturedSkillAssetPath = SummonPath + "Res/Presentation/IseriaSkillLoop/IseriaSkillLoop.asset";
        private const string SummonPrefabPath = SummonPath + "Res/Prefab/SummonWindow.prefab";
        private const string ArtifactArtPath = Home + "Configs/ArtifactArtCatalog.asset";
        private const string ArtifactCatalogSource = Home + "Configs/artifact-catalog.json";

        private static Sprite SummonArt(string name) => Load<Sprite>(SummonPath + "Res/Textures/" + name + ".png");

        [MenuItem("Tools/游戏主页/生成召唤和神器")]
        public static void GenerateSummon()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SummonPrefabPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            font = Load<TMP_FontAsset>(Common + "Fonts/EpicSeven.asset");
            labels = JsonUtility.FromJson<LabelFile>(File.ReadAllText(Assets[4].Path)).rows
                .ToDictionary(row => row.key, row => row.text);
            BuildSummonAssets();
            BuildInventoryAssets();
            SaveInventoryNavigation();
        }

        private static void BuildSummonAssets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SummonPrefabPath));
            SummonPresentationBuilder.Generate();
            BuildFeaturedSkillLoop();
            var source = Load<TextAsset>(ArtifactCatalogSource);
            var data = JsonUtility.FromJson<ArtifactCatalogData>(source.text);
            var catalog = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>(ArtifactArtPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<ArtifactArtCatalog>();
                AssetDatabase.CreateAsset(catalog, ArtifactArtPath);
            }
            catalog.Configure(source, data.templates.Select(row => Load<Sprite>(
                Home + "Res/Textures/Artifacts/" + row.id + ".jpg")).ToArray(),
                data.templates.Select(row => Load<Sprite>(Home + "Res/Textures/Artifacts/Full/" + row.id + ".png")).ToArray(),
                data.templates.Select(row => Load<Sprite>(Home + "Res/Textures/Artifacts/Combat/" + row.id + ".png")).ToArray());
            EditorUtility.SetDirty(catalog);
            Save(BuildSummonWindow(), SummonPrefabPath);
        }

        [Serializable]
        private sealed class FeaturedSkillLayout
        {
            public int frameCount, columns, rows, gutterPixels;
            public float framesPerSecond;
            public string[] pages;
        }

        private static void BuildFeaturedSkillLoop()
        {
            const string directory = SummonPath + "Res/Presentation/IseriaSkillLoop/";
            var layout = JsonUtility.FromJson<FeaturedSkillLayout>(Load<TextAsset>(directory + "loop.json").text);
            var skill = AssetDatabase.LoadAssetAtPath<SummonSkillLoop>(FeaturedSkillAssetPath);
            if (!skill)
            {
                skill = ScriptableObject.CreateInstance<SummonSkillLoop>();
                AssetDatabase.CreateAsset(skill, FeaturedSkillAssetPath);
            }
            skill.Configure(layout.pages.Select(page => Load<Texture2D>(directory + page)).ToArray(),
                layout.frameCount, layout.framesPerSecond, layout.columns, layout.rows, layout.gutterPixels,
                Load<AudioClip>(directory + "native-audio.wav"));
            EditorUtility.SetDirty(skill);
        }

        private static GameObject BuildSummonWindow()
        {
            var root = Root("SummonWindow");
            var binding = new List<(string Key, Object Target)>();
            Paint(root, null, new Color32(4, 17, 31, 255), true);
            var covenantBackdrop = Area(root, "Covenant composition", 0, 0, 1, 1);
            Cover(covenantBackdrop, "Native Covenant backdrop", SummonArt("gacha_wish_bg_3"));
            var upBackdrop = Area(root, "Iseria composition", 0, 0, 1, 1);
            var forest = Area(upBackdrop, "Iseria's original forest", .141f, 0, .859f, 1).gameObject.AddComponent<RawImage>();
            forest.texture = SummonArt("gacha_c1024_01_bg").texture;
            // The old layout baked the artifact on the left. The current layout uses its separate original card at the right.
            forest.uvRect = new Rect(.263f, 0, .737f, 1);
            forest.raycastTarget = false;
            binding.Add(("covenantBackdrop", covenantBackdrop.gameObject));
            binding.Add(("upBackdrop", upBackdrop.gameObject));
            Paint(Area(root, "Left banner shade", 0, .093f, .145f, .907f), null, Black(.78f));
            var top = Area(root, "Summon top bar", 0, 0, 1, 1);
            Paint(Area(top, "Native top bar shade", 0, 0, 1, .093f), null, Black(.52f));
            Paint(Area(top, "Native top bar rule", 0, .093f, 1, .0016f), null, new Color32(255, 255, 255, 37));

            var back = Button(Area(top, "Back", .012f, .014f, .026f, .065f), null, Color.clear);
            Picture((RectTransform)back.transform, "Back icon", Art("cm_icon_etcback"), 0, 0, 1, 1, true);
            back.FeedbackSound = GameUiFeedback.Sound.Back;
            binding.Add(("back", back));
            AddSummonLabel(top, binding, "title", "summon.title", 38, .052f, .013f, .13f, .069f);
            DrawNativeTopBarIcons(top);
            SummonIconButton(top, binding, "help", "cm_btn_helpd", .124f, .018f, .029f, .054f, false);
            Picture(top, "Epic bookmark currency", SummonArt("token_rarecoin"), .593f, .021f, .025f, .046f, true);
            AddSummonLabel(top, binding, "epicBalance", null, 24, .618f, .015f, .042f, .062f);
            Picture(top, "Covenant bookmark currency", SummonArt("token_ticket_rare"), .664f, .021f, .025f, .046f, true);
            AddSummonLabel(top, binding, "balance", null, 24, .689f, .015f, .058f, .062f);

            var roster = Area(root, "Covenant wish roster", .149f, .015f, .815f, .704f);
            binding.Add(("wishRoster", roster.gameObject));
            DrawSummonWishRoster(roster, binding);
            var hero = Area(root, "Featured original hero artwork", .201f, .003f, .533f, .99f).gameObject.AddComponent<RawImage>();
            hero.raycastTarget = false;
            var artifact = Picture(root, "Featured artifact card", null, .799f, .142f, .177f, .60f, true);
            binding.Add(("upPortrait", hero));
            binding.Add(("artifactArt", artifact));
            binding.Add(("artifactFrame", Picture(root, "Original artifact frame", SummonArt("arti_frame_deco_1"),
                .802f, .148f, .047f, .089f, true).gameObject));
            AddSummonLabel(root, binding, "featuredArtifact", null, 25, .799f, .689f, .177f, .053f, true);
            var preview = Area(root, "Iseria native skill preview", .145f, 0, .855f, 1).gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            preview.enabled = false;
            binding.Add(("featuredSkillCanvas", preview));
            StoreSummonSprite(root, binding, "normalCoin", "token_ticket_rare");
            StoreSummonSprite(root, binding, "epicCoin", "token_rarecoin");
            AddSummonLabel(root, binding, "bannerTitle", null, 23, .168f, .501f, .46f, .048f);
            AddSummonLabel(root, binding, "featuredHero", null, 48, .168f, .553f, .43f, .068f);
            var featuredBadges = Area(root, "Original hero badges", .168f, .627f, .155f, .045f);
            binding.Add(("featuredBadges", featuredBadges.gameObject));
            binding.Add(("featuredElement", Picture(featuredBadges, "Element", null, 0, 0, .15f, 1, true)));
            binding.Add(("featuredRole", Picture(featuredBadges, "Role", null, .17f, 0, .15f, 1, true)));
            for (int star = 0; star < 5; star++)
                Picture(featuredBadges, "Star", DraftSprite("TeamBadges/cm_icon_star"), .36f + star * .126f, 0, .13f, 1, true);
            AddSummonLabel(root, binding, "pity", null, 26, .728f, .795f, .25f, .05f, true);
            AddSummonLabel(root, binding, "status", null, 30, .17f, .736f, .59f, .055f);
            SummonBannerButton(root, binding, "iseria", "summon.banner.iseria", .101f, "gacha_c1024_01_banner");
            SummonBannerButton(root, binding, "covenant", "summon.banner.covenant", .246f, "banner_gacha_wish");
            SummonDrawButton(root, binding, "single", .647f, "gray");
            SummonDrawButton(root, binding, "ten", .821f, "gold");
            SummonButton(root, binding, "free", null, .666f, .821f, .119f, .046f, SummonArt("_btn_gacha_gray"),
                SummonArt("_btn_gacha_gray_p"), "freeLabel");
            SummonIconButton(root, binding, "info", "icon_menu_info", .172f, .870f, .043f, .092f);
            SummonIconButton(root, binding, "storage", "icon_quick_tmp_inven", .234f, .870f, .043f, .092f);
            SummonIconButton(root, binding, "recovery", "icon_menu_change", .296f, .870f, .043f, .092f);
            SummonButton(root, binding, "wish", "summon.button.wish", .428f, .734f, .257f, .089f,
                SummonArt("gacha_wish_btn"), SummonArt("gacha_wish_btn_p"), "wishButtonLabel");
            SummonIconButton(root, binding, "epic", "token_rarecoin", .358f, .870f, .043f, .092f);

            top.SetAsLastSibling();
            var sound = Area(root, "Summon audio", 0, 0, 0, 0).gameObject.AddComponent<AudioSource>();
            var music = Area(root, "Summon music", 0, 0, 0, 0).gameObject.AddComponent<AudioSource>();
            var skillAudio = Area(root, "Featured skill audio", 0, 0, 0, 0).gameObject.AddComponent<AudioSource>();
            sound.playOnAwake = music.playOnAwake = skillAudio.playOnAwake = false;
            binding.Add(("featuredSkillAudio", skillAudio));
            binding.Add(("audio", sound)); binding.Add(("music", music));
            var effect = Area(root, "Original summon scene", 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
            effect.raycastTarget = false;
            effect.enabled = false;
            binding.Add(("effectCanvas", effect));

            BuildSummonModalPanels(root, binding);
            BuildSummonReveal(root, binding);
            Bind(root, binding.ToArray());
            return root.gameObject;
        }

        private static void SummonBannerButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, string labelKey, float top, string image)
        {
            var rect = Area(root, key, .009f, top, .127f, .122f);
            var button = Button(rect, null, Color.clear);
            bindings.Add((key, button));
            var highlight = Picture(rect, "Native selected banner outline", SummonArt("banner_cliping_outline"), 0, 0, 1, 1);
            highlight.color = new Color32(121, 213, 236, 230);
            bindings.Add((key + "Selected", highlight.gameObject));
            var mask = Area(rect, "Original clipped banner", .014f, .034f, .972f, .932f);
            Paint(mask, SummonArt("banner_cliping"), Color.white);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Picture(mask, "Original banner texture", SummonArt(image), 0, 0, 1, 1);
            Text(rect, "Banner name", labelKey, 22, .032f, .60f, .936f, .27f, alignment: TextAlignmentOptions.Center);
        }

        private static void SummonDrawButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, float x, string color)
        {
            var rect = Area(root, key, x, .875f, .165f, .092f);
            var normal = SummonArt("_btn_gacha_" + color);
            var button = Button(rect, normal, Color.white);
            PressSprites(button, normal, SummonArt("_btn_gacha_" + color + "_p"));
            bindings.Add((key, button));
            Picture(rect, "Price badge", SummonArt("_btn_small_black_p"), .06f, .24f, .39f, .51f);
            bindings.Add((key + "Coin", Picture(rect, "Currency", null, .021f, .10f, .17f, .80f, true)));
            AddSummonLabel(rect, bindings, key + "Price", null, 27, .17f, .21f, .22f, .54f, true);
            AddSummonLabel(rect, bindings, key + "Label", null, 29, .46f, .12f, .50f, .75f, true);
        }

        private static void SummonIconButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, string icon, float x, float y, float width, float height, bool circle = true)
        {
            var rect = Area(root, key, x, y, width, height);
            var normal = circle ? SummonArt("_btn_circle_black_76") : null;
            var button = Button(rect, normal, circle ? Color.white : Color.clear);
            if (circle) PressSprites(button, normal, SummonArt("_btn_circle_black_76_p"));
            bindings.Add((key, button));
            Picture(rect, "Native " + key + " icon", SummonArt(icon), .16f, .16f, .68f, .68f, true);
        }

        private static void DrawSummonWishRoster(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            const int count = 7;
            var portraits = Area(root, "Clipped originals", 0, 0, 1, 1);
            Picture(root, "Original gradient over portraits", SummonArt("gacha_wish_bg_2"),
                -.183f, -.021f, 1.227f, 1.421f);
            for (int index = 0; index < count; index++)
            {
                string key = "wishHero" + index;
                var card = Area(root, key, index / (float)count, 0, 1f / count, 1);
                bindings.Add((key + "Card", card.gameObject));
                float column = index / (float)count;
                var clip = Area(portraits, "Native slanted portrait mask", column, -.13f, 1f / count, 1.116f);
                Paint(clip, null, Color.white);
                clip.localEulerAngles = new Vector3(0, 0, -9.5f);
                clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var frame = Area(clip, "Original portrait", -.74f, .10f, 2.48f, 1.04f);
                frame.localEulerAngles = new Vector3(0, 0, 9.5f);
                var portrait = frame.gameObject.AddComponent<RawImage>();
                portrait.raycastTarget = false;
                bindings.Add((key, portrait));
                Paint(Area(card, "Ownership label shade", .26f, .683f, .48f, .051f),
                    SummonArt("_grow_white"), new Color32(37, 46, 61, 191));
                AddSummonLabel(card, bindings, key + "Owned", null, 18, .04f, .677f, .92f, .068f, true);
                AddSummonLabel(card, bindings, key + "Name", null, 25, .02f, .779f, .96f, .072f, true);
                if (index > 0)
                    Picture(card, "Original slanted divider", SummonArt("gacha_wish_deco_" + (index + 2)), -.45f, 0, .55f, .82f);
                Picture(card, "Native banner divider", SummonArt("gacha_wish_bar"), 0, .851f, 1, .012f);
                for (int star = 0; star < 5; star++)
                    Picture(card, "Star", DraftSprite("TeamBadges/cm_icon_star"), .14f + star * .145f, .863f, .152f, .06f, true);
                bindings.Add((key + "Role", Picture(card, "Role", null, .34f, .93f, .15f, .055f, true)));
                bindings.Add((key + "Element", Picture(card, "Element", null, .51f, .93f, .15f, .055f, true)));
            }
        }

        private static void BuildSummonModalPanels(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var confirm = SummonPanel(root, bindings, "confirmPanel", 0, .108f, 1, .783f, SummonArt("renew_popup_bg_2_alpha"));
            AddSummonLabel(confirm, bindings, "confirmTitle", null, 41, .28f, .047f, .44f, .095f, true);
            Picture(confirm, "Native confirmation divider", Art("_bar_23"), .251f, .154f, .498f, .022f);
            AddSummonLabel(confirm, bindings, "confirmPrompt", null, 25, .25f, .206f, .50f, .070f, true);
            var heroPreview = Area(confirm, "Featured hero offer", .326f, .321f, .376f, .186f);
            var price = Area(confirm, "Currency offer", .326f, .505f, .376f, .186f);
            bindings.Add(("confirmHeroRow", heroPreview.gameObject));
            bindings.Add(("confirmPriceRow", price));
            Picture(heroPreview, "Character icon frame", CommonArt("_hero_s_bg_ally"), 0, .03f, .175f, .91f, true);
            bindings.Add(("confirmHeroPortrait", Picture(heroPreview, "Featured portrait", null, .02f, .09f, .137f, .787f, true)));
            var heroDivider = Picture(heroPreview, "Divider", SummonArt("z_tile_bar"), .135f, .441f, .20f, .04f);
            heroDivider.rectTransform.localEulerAngles = Vector3.forward * 90;
            AddSummonLabel(heroPreview, bindings, "confirmHeroName", null, 26, .228f, .16f, .67f, .31f);
            AddSummonLabel(heroPreview, bindings, "confirmHeroPity", null, 22, .228f, .49f, .67f, .34f);
            Picture(price, "Original price tile", SummonArt("_itembg_stuff_2"), 0, .029f, .175f, .91f, true);
            bindings.Add(("confirmCoin", Picture(price, "Currency", null, .021f, .09f, .13f, .65f, true)));
            AddSummonLabel(price, bindings, "confirmCost", null, 20, .021f, .674f, .137f, .21f, true);
            var priceDivider = Picture(price, "Divider", SummonArt("z_tile_bar"), .135f, .441f, .20f, .04f);
            priceDivider.rectTransform.localEulerAngles = Vector3.forward * 90;
            AddSummonLabel(price, bindings, "confirmCurrency", null, 24, .228f, .17f, .67f, .42f);
            AddSummonLabel(price, bindings, "confirmBody", null, 21, .228f, .625f, .72f, .24f, wrap: true);
            SummonButton(confirm, bindings, "confirmNo", "summon.button.cancel", .365f, .807f, .125f, .132f,
                SummonArt("_btn_gold_r_2"), SummonArt("_btn_gold_r_p_2"));
            SummonButton(confirm, bindings, "confirmYes", "summon.button.ok", .510f, .807f, .125f, .132f,
                SummonArt("_btn_blue_r_3"), SummonArt("_btn_blue_r_p_3"));

            var info = SummonPanel(root, bindings, "infoPanel", .225f, .086f, .55f, .828f);
            AddSummonLabel(info, bindings, "infoTitle", null, 38, .08f, .03f, .84f, .11f, true);
            SummonScroll(info, bindings, "infoItems", .055f, .161f, .89f, .731f);
            SummonButton(info, bindings, "infoClose", "summon.button.close", .377f, .909f, .245f, .077f);

            BuildSummonWishPanel(root, bindings);

            var storage = SummonPanel(root, bindings, "storagePanel", .235f, .075f, .53f, .85f);
            AddSummonLabel(storage, bindings, "storageTitle", null, 38, .03f, .025f, .94f, .109f, true);
            SummonScroll(storage, bindings, "storageItems", .06f, .165f, .88f, .704f);
            SummonButton(storage, bindings, "storageClose", "summon.button.close", .366f, .899f, .265f, .083f);
        }

        private static void BuildSummonWishPanel(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var wish = SummonPanel(root, bindings, "wishPanel", .115f, .058f, .77f, .882f);
            AddSummonLabel(wish, bindings, "wishTitle", null, 37, .022f, .026f, .47f, .076f);
            Picture(wish, "Original header divider", Art("_bar_23"), .018f, .088f, .96f, .012f);
            var selectedSurface = Picture(wish, "Original selected wish area", SummonArt("gacha_wish_box_1"),
                .022f, .190f, .441f, .67f);
            selectedSurface.color = new Color32(96, 155, 216, 75);
            var candidateSurface = Picture(wish, "Original candidate area", SummonArt("gacha_wish_box_2"),
                .486f, .190f, .493f, .67f);
            candidateSurface.color = new Color32(29, 57, 85, 121);
            AddSummonLabel(wish, bindings, "wishCount", null, 23, .043f, .822f, .386f, .05f, true);
            SummonButton(wish, bindings, "wishHero", "summon.button.wishHeroes", .038f, .112f, .208f, .064f);
            SummonButton(wish, bindings, "wishArtifact", "summon.button.wishArtifacts", .249f, .112f, .208f, .064f);
            SummonButton(wish, bindings, "wishUnowned", "summon.wish.unowned", .534f, .112f, .356f, .064f);
            SummonWishGrid(wish, bindings, "wishSelectedItems", .042f, .211f, .401f, .610f, 4);
            SummonWishGrid(wish, bindings, "wishItems", .508f, .210f, .451f, .620f, 6);
            SummonButton(wish, bindings, "wishClose", "summon.button.close", .332f, .871f, .152f, .096f);
            SummonButton(wish, bindings, "wishSave", "summon.button.ok", .515f, .871f, .152f, .096f, SummonArt("_btn_blue"));
            var card = Area(root, "Original circular wish entry", 0, 0, .08f, .10f);
            var button = Button(card, SummonArt("gacha_wish_slot_cir"), Color.white);
            var icon = Picture(card, "Hero or artifact icon", null, .10f, .03f, .80f, .84f, true);
            var selected = Picture(card, "Native selection", CommonArt("cm_checkicon2"), .64f, .02f, .29f, .32f, true);
            var name = Text(card, "Name", null, 19, .019f, .77f, .962f, .21f, alignment: TextAlignmentOptions.Center);
            Bind(card, ("button", button), ("icon", icon), ("selected", selected), ("name", name));
            card.gameObject.SetActive(false);
            bindings.Add(("wishTemplate", card.gameObject));
            StoreSummonSprite(root, bindings, "heroWishFrame", "gacha_wish_slot_cir");
            StoreSummonSprite(root, bindings, "artifactWishFrame", "gacha_wish_slot_arti");
        }

        private static void StoreSummonSprite(RectTransform root, List<(string Key, Object Target)> bindings, string key, string source)
        {
            var stored = Picture(root, "Source " + key, SummonArt(source), 0, 0, 0, 0);
            stored.gameObject.SetActive(false);
            bindings.Add((key, stored));
        }

        private static void SummonWishGrid(RectTransform panel, List<(string Key, Object Target)> bindings,
            string key, float x, float y, float width, float height, int columns)
        {
            var scroll = InventoryScroll(panel, key, x, y, width, height);
            var grid = scroll.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.spacing = new Vector2(8, 8);
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bindings.Add((key, scroll.content));
        }

        private static void BuildSummonReveal(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var reveal = Area(root, "Summon reveal", 0, 0, 1, 1);
            var shield = Paint(reveal, null, Color.clear, true);
            shield.canvasRenderer.cullTransparentMesh = false;
            bindings.Add(("revealPanel", reveal.gameObject));
            bindings.Add(("heroCaption", Picture(reveal, "Hero reveal caption", SummonArt("gacha_title_bg"),
                .009f, .037f, .435f, .22f).gameObject));
            bindings.Add(("artifactCaption", Picture(reveal, "Artifact reveal caption", SummonArt("gacha_title_arti_bg"),
                .009f, .037f, .435f, .22f).gameObject));
            var revealArt = Picture(reveal, "Original reveal reward art", null, .554f, .063f, .437f, .816f, true);
            bindings.Add(("revealArt", revealArt));
            var rewardHero = Area(revealArt.rectTransform, "Original reward hero", -.22f, -.05f, 1.40f, 1.12f)
                .gameObject.AddComponent<RawImage>();
            rewardHero.raycastTarget = false;
            bindings.Add(("revealHero", rewardHero));
            AddSummonLabel(reveal, bindings, "revealName", null, 51, .025f, .068f, .47f, .10f);
            AddSummonLabel(reveal, bindings, "revealStars", null, 33, .025f, .171f, .48f, .055f);
            AddSummonLabel(reveal, bindings, "revealLabel", null, 25, .025f, .253f, .438f, .16f, false, true);
            SummonButton(reveal, bindings, "skip", null, .912f, .019f, .073f, .052f, null, null, "skipLabel");
            SummonButton(reveal, bindings, "next", null, .810f, .898f, .169f, .071f, CommonArt("_btn_green"), null, "nextLabel");
            reveal.gameObject.SetActive(false);

            BuildSummonResultLayout(root, bindings);

            var row = Area(root, "Native item row", 0, 0, 1, .096f);
            var btn = Button(row, SummonArt("gacha_10bg_ice"), Color.white);
            var icon = Picture(row, "Item artwork", null, .614f, 0, .381f, 1);
            var name = Text(row, "Item name", null, 26, .034f, .073f, .55f, .466f);
            var detail = Text(row, "Item rarity or probability", null, 22, .034f, .539f, .55f, .426f,
                new Color32(255, 216, 122, 255));
            Bind(row, ("button", btn), ("name", name), ("detail", detail), ("icon", icon));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 78;
            row.gameObject.SetActive(false);
            bindings.Add(("rowTemplate", row.gameObject));
        }

        private static RectTransform SummonPanel(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, float x, float y, float width, float height, Sprite background = null)
        {
            var host = Area(root, key, 0, 0, 1, 1);
            Paint(host, null, Black(.61f), true);
            bindings.Add((key, host.gameObject));
            var panel = Area(host, "Native panel", x, y, width, height);
            var surface = background ? background : SummonArt("renew_popup_bg_alpha");
            Paint(panel, surface, Color.white, true);
            host.gameObject.SetActive(false);
            return panel;
        }

        private static void SummonScroll(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, float x, float y, float width, float height)
        {
            var scroll = InventoryScroll(root, key, x, y, width, height);
            var group = scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = 5;
            group.childControlHeight = group.childControlWidth = true;
            group.childForceExpandHeight = false;
            scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bindings.Add((key, scroll.content));
        }

        private static void SummonButton(RectTransform root, List<(string Key, Object Target)> bindings, string key,
            string text, float x, float y, float width, float height, Sprite sprite = null, Sprite pressed = null, string labelBinding = null)
        {
            var rect = Area(root, key, x, y, width, height);
            var button = Button(rect, sprite ?? CommonArt("_btn_small_black"), Color.white);
            if (pressed) PressSprites(button, sprite, pressed);
            bindings.Add((key, button));
            var label = Text(rect, "Text", text, 27, .04f, .045f, .92f, .91f, alignment: TextAlignmentOptions.Center);
            if (labelBinding != null) bindings.Add((labelBinding, label));
        }

        private static void AddSummonLabel(RectTransform root, List<(string Key, Object Target)> bindings, string key, string text,
            float size, float x, float y, float width, float height, bool center = false, bool wrap = false)
        {
            bindings.Add((key, Text(root, key, text, size, x, y, width, height, alignment:
                center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft, wrap: wrap)));
        }
    }
}
