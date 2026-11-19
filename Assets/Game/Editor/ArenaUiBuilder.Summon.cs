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
            Paint(root, null, Color.black, true);
            var covenantBackdrop = Area(root, "Covenant composition", 0, 0, 1, 1);
            Cover(covenantBackdrop, "Native Covenant backdrop", SummonArt("gacha_wish_bg_3"));
            var upBackdrop = Area(root, "Iseria composition", 0, 0, 1, 1);
            var scenery = upBackdrop.gameObject.AddComponent<CanvasGroup>();
            binding.Add(("featuredScenery", scenery));
            var forest = Area(upBackdrop, "Iseria's original forest", 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
            forest.texture = SummonArt("gacha_c1024_01_bg").texture;
            // The old layout baked the artifact on the left. The current layout uses its separate original card at the right.
            forest.uvRect = new Rect(.263f, 0, .737f, 1);
            forest.raycastTarget = false;
            binding.Add(("covenantBackdrop", covenantBackdrop.gameObject));
            binding.Add(("upBackdrop", upBackdrop.gameObject));
            var preview = Area(root, "Iseria native skill preview", 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            preview.enabled = false;
            binding.Add(("featuredSkillCanvas", preview));
            var actorLayer = Area(root, "Featured hero poster", 0, 0, 1, 1);
            var offerLayer = Area(root, "Featured artifact poster", 0, 0, 1, 1);
            binding.Add(("featuredActorLayer", actorLayer.gameObject.AddComponent<CanvasGroup>()));
            binding.Add(("featuredOfferLayer", offerLayer.gameObject.AddComponent<CanvasGroup>()));
            DrawSummonSidebarShade(root);
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

            var roster = Area(root, "Covenant wish roster", 0, 0, 1, 1);
            binding.Add(("wishRoster", roster.gameObject));
            DrawSummonWishRoster(roster, binding);
            BuildPickupPoster(actorLayer, offerLayer, binding);
            StoreSummonSprite(root, binding, "normalCoin", "token_ticket_rare");
            StoreSummonSprite(root, binding, "epicCoin", "token_rarecoin");
            AddSummonLabel(root, binding, "pity", null, 26, .728f, .795f, .25f, .05f, true);
            AddSummonLabel(root, binding, "status", null, 30, .17f, .736f, .59f, .055f);
            SummonBannerButton(root, binding, "iseria", 134, "gacha_c1024_01_banner");
            SummonBannerButton(root, binding, "covenant", 241, "banner_gacha_wish");
            SummonDrawButton(root, binding, "single", -528, "gray", 94);
            SummonDrawButton(root, binding, "ten", -271, "gold", 84);
            DrawSummonFreeBubble(root, binding);
            SummonFooterIcon(root, binding, "info", "icon_menu_info", 250, 39, 39, 57, 57);
            SummonFooterIcon(root, binding, "storage", "icon_quick_tmp_inven", 342, 39, 29, 96, 91.2f);
            SummonFooterIcon(root, binding, "recovery", "icon_menu_change", 437, 38, 40, 57.6f, 57.6f);
            var wishButton = Area(root, "wish", .5f, .5f, 0, 0);
            wishButton.pivot = new Vector2(.5f, .5f);
            wishButton.anchoredPosition = new Vector2(90, -200) * SummonCanvasScale;
            wishButton.sizeDelta = new Vector2(406, 75) * SummonCanvasScale;
            var wishControl = Button(wishButton, SummonArt("gacha_wish_btn"), Color.white);
            PressSprites(wishControl, SummonArt("gacha_wish_btn"), SummonArt("gacha_wish_btn_p"));
            wishControl.image.pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
            binding.Add(("wish", wishControl));
            var wishLabel = Text(wishButton, "Wish count", null, 31.32f, .08f, 0, .84f, .91f,
                alignment: TextAlignmentOptions.Center);
            ApplySummonTextOutline(wishLabel);
            wishLabel.richText = true;
            binding.Add(("wishButtonLabel", wishLabel));
            SummonFooterIcon(root, binding, "epic", "token_rarecoin", 533, 38, 38, 58.8f, 58.8f);

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

        private static void BuildPickupPoster(RectTransform actorLayer, RectTransform offerLayer, List<(string Key, Object Target)> bindings)
        {
            // gacha_renew_pickup.csb places n_pos1 at 1.5x. Match the upright half-body crop
            // against the 720-high recording while keeping the native portrait camera square.
            var portrait = Area(actorLayer, "Featured original hero artwork", .158f, 0f, .516f, 1.575f)
                .gameObject.AddComponent<RawImage>();
            portrait.raycastTarget = false;
            var portraitAspect = portrait.gameObject.AddComponent<AspectRatioFitter>();
            portraitAspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            portraitAspect.aspectRatio = 1f;
            bindings.Add(("upPortrait", portrait));

            var caption = Area(actorLayer, "Pickup hero caption", .168f, .478f, 0, 0);
            caption.pivot = new Vector2(0f, 1f);
            caption.sizeDelta = new Vector2(306f, 140f) * SummonCanvasScale;
            var softShade = Picture(caption, "Original title glow", SummonArt("_grow_white"), -.67f, -.40f, 2.63f, 2.50f);
            softShade.color = Black(.4f);
            var tag = Picture(caption, "Normal pickup category", SummonArt("renew_main_hud_btn_deco_pickup"),
                -.028f, .012f, .352f, .314f);
            AddSummonLabel(tag.rectTransform, bindings, "bannerTitle", null, 21, .065f, .09f, .87f, .82f, true);
            var shade = Text(caption, "Featured name shadow", null, 56, .006f, .322f, .99f, .366f, Black(.87f));
            var name = Text(caption, "Featured hero name", null, 56, 0, .303f, .99f, .366f);
            shade.fontStyle = name.fontStyle = FontStyles.Bold;
            bindings.Add(("featuredHeroShadow", shade));
            bindings.Add(("featuredHero", name));

            var badges = Area(caption, "Original hero badges", 0f, .681f, .780f, .288f);
            bindings.Add(("featuredBadges", badges.gameObject));
            bindings.Add(("featuredElement", Picture(badges, "Element", null, 0f, .04f, .147f, .90f, true)));
            bindings.Add(("featuredRole", Picture(badges, "Role", null, .169f, .04f, .147f, .90f, true)));
            for (int star = 0; star < 5; star++)
                Picture(badges, "Star " + (star + 1), SummonArt("cm_icon_star_big"), .340f + star * .111f, 0f, .175f, 1f, true);

            // The source card is 378 x 598 at 0.72x. Children share the card's fixed aspect,
            // including its corner frame, artifact emblem and descending row of grade stars.
            var card = Area(offerLayer, "Featured artifact card", .982f, .142f, 0f, .598f);
            card.pivot = new Vector2(1f, .5f);
            var cardAspect = card.gameObject.AddComponent<AspectRatioFitter>();
            cardAspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            cardAspect.aspectRatio = 378f / 598f;
            bindings.Add(("artifactFrame", card.gameObject));
            var blur = Picture(card, "Original card shadow", SummonArt("_grow_white"), -.095f, -.065f, 1.185f, 1.130f);
            blur.color = Black(.5f);
            bindings.Add(("artifactArt", Picture(card, "Featured artifact illustration", null, 0, 0, 1, 1, true)));
            Picture(card, "Original artifact frame", SummonArt("arti_frame_deco_1"), 0, 0, 170f / 378f, 242f / 598f);
            bindings.Add(("artifactEmblem", Picture(card, "Artifact emblem", null, .005f, .001f, .215f, .150f, true)));
            for (int star = 0; star < 6; star++)
            {
                var grade = Picture(card, "Artifact star " + (star + 1), DraftSprite("TeamBadges/cm_icon_star"),
                    339f / 378f, (13f + star * 30f) / 598f, 30f / 378f, 29f / 598f, true);
                bindings.Add(("artifactStar" + star, grade.gameObject));
            }
            var nameShade = Picture(card, "Original artifact name glow", SummonArt("_grow_white"), -.07f, .897f, 1.14f, .092f);
            nameShade.color = Black(.5f);
            var nameRow = Area(card, "Artifact class and name", .015f, .893f, .97f, .094f);
            var horizontal = nameRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childControlWidth = horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = false;
            horizontal.spacing = 4f;
            var role = Picture(nameRow, "Artifact role", null, 0, 0, 0, 0, true);
            var roleSize = role.gameObject.AddComponent<LayoutElement>();
            roleSize.preferredWidth = roleSize.preferredHeight = 34f;
            bindings.Add(("artifactRole", role));
            AddSummonLabel(nameRow, bindings, "featuredArtifact", null, 25, 0, 0, 0, 0, true);
        }

        private static void DrawSummonSidebarShade(RectTransform root)
        {
            var shade = Picture(root, "Native sidebar gradient", Art("_grow"), 0, 0, 0, 0);
            var rect = shade.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(-250, 1) * SummonCanvasScale;
            rect.sizeDelta = new Vector2(344f * 3.6f, 167f * 12.42f) * SummonCanvasScale;
            shade.color = new Color32(255, 255, 255, 216);
        }

        private static void SummonBannerButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, float centerFromTop, string image)
        {
            var rect = Area(root, key, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(16f, -centerFromTop) * SummonCanvasScale;
            rect.sizeDelta = new Vector2(320.5f, 131f) * (.65f / 1.1f * SummonCanvasScale);
            bindings.Add((key, Button(rect, null, Color.clear)));

            var edge = Picture(rect, "Original banner outline", SummonArt("banner_cliping_outline"), 0, 0, 1, 1);
            edge.color = new Color32(255, 255, 255, 127);
            bindings.Add((key + "Selected", edge.gameObject));
            var mask = Area(rect, "Original clipped banner", .5f / 320.5f, 0, 319.5f / 320.5f, 1);
            Paint(mask, SummonArt("banner_cliping"), Color.white);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var poster = Picture(mask, "Original banner texture", SummonArt(image),
                -17f / 319.5f, -20f / 131f, 356f / 319.5f, 174f / 131f);
            bindings.Add((key + "Poster", poster));
            if (key == "covenant")
            {
                var crest = Text(mask, "Covenant title", "summon.banner.covenant", 34, .04f, .22f, .92f, .56f,
                    new Color32(30, 52, 91, 255), TextAlignmentOptions.Center);
                crest.fontStyle = FontStyles.Bold;
                ApplySummonTextOutline(crest, true);
            }
            else
            {
                var shade = Picture(mask, "Ownership caption shade", SummonArt("glow_banner_right"),
                    .58f, .57f, .42f, .43f);
                var owned = Text(shade.rectTransform, "Ownership", "summon.wish.unowned_badge", 22,
                    0, .02f, .92f, .91f, new Color32(210, 210, 210, 255), TextAlignmentOptions.MidlineRight);
                ApplySummonTextOutline(owned);
                bindings.Add(("pickupUnowned", shade.gameObject));
            }
        }

        // Summon layouts use a 720-high design surface. The UI canvas uses a reference height of 1080.
        private const float SummonCanvasScale = 1080f / 720f;

        private static RectTransform SummonFooterButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, string artwork, bool right, float x, float bottom, float width, float height)
        {
            var rect = Area(root, key, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = new Vector2(right ? 1 : 0, 0);
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, bottom) * SummonCanvasScale;
            rect.sizeDelta = new Vector2(width, height) * SummonCanvasScale;
            var normal = SummonArt(artwork);
            var button = Button(rect, normal, Color.white);
            PressSprites(button, normal, SummonArt(artwork + "_p"));
            ((Image)button.targetGraphic).pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
            bindings.Add((key, button));
            return rect;
        }

        private static void SummonPricePlate(RectTransform rect, string name, string sprite,
            float left, float top, float width, float height)
        {
            var plate = Picture(rect, name, SummonArt(sprite + "_linear"), left, top, width, height);
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
        }

        private static void SummonDrawButton(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, float x, string color, float badgeWidth)
        {
            var rect = SummonFooterButton(root, bindings, key, "_btn_gacha_" + color, true, x, 21, 256, 76);
            SummonPricePlate(rect, "Price badge", "_btn_small_black_p", 24f / 256, 16f / 76, badgeWidth / 256, 40f / 76);
            bindings.Add((key + "Coin", Picture(rect, "Currency", null, 3f / 256, 8f / 76, 55f / 256, 55f / 76, true)));
            var price = Text(rect, "Price", null, 28.8f, badgeWidth * .56f / 256f,
                14.8f / 76, 48f / 256, 41.6f / 76, new Color32(255, 220, 78, 255), TextAlignmentOptions.Center);
            ApplySummonTextOutline(price);
            bindings.Add((key + "Price", price));
            var label = Text(rect, key + "Label", null, 31.5f, 118f / 256, 13f / 76, 124f / 256, 50f / 76,
                Color.white, TextAlignmentOptions.Center);
            label.fontSharedMaterial = Load<Material>(SummonPath + "Res/Materials/SummonDrawLabelOutline.mat");
            bindings.Add((key + "Label", label));
        }

        private static void DrawSummonFreeBubble(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var bubble = SummonFooterButton(root, bindings, "free", "cm_ballonsm", true, -516, 88, 239, 45);
            Picture(bubble, "Bubble pointer", SummonArt("cm_ballonsm_arrow"), 108f / 239, 36f / 45, 24f / 239, 14f / 45);
            var label = Text(bubble, "Free draw", null, 25.56f, .035f, 0f, .93f, 1f,
                new Color32(25, 107, 0, 255), TextAlignmentOptions.Center);
            ApplySummonTextOutline(label, true);
            bindings.Add(("freeLabel", label));
        }

        private static void SummonFooterIcon(RectTransform root, List<(string Key, Object Target)> bindings,
            string key, string artwork, float left, float centerX, float centerY, float width, float height)
        {
            var rect = SummonFooterButton(root, bindings, key, "_btn_circle_black_76", false, left, 19, 76, 76);
            var icon = Picture(rect, "Native " + key + " icon", SummonArt(artwork),
                (centerX - width * .5f) / 76f, (76f - centerY - height * .5f) / 76f, width / 76f, height / 76f, true);
            if (key != "epic") icon.color = new Color32(255, 255, 255, 127);
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

        // Coordinates from gacha_wish.csb are relative to a centered 1280 × 720 layer.
        // Cocos locates each child from its parent's lower left, even when its pivot is elsewhere.
        private static RectTransform WishRect(RectTransform parent, string name, float x, float y,
            float width, float height, float pivotX = 0, float pivotY = 0)
        {
            var rect = Area(parent, name, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(pivotX, pivotY);
            rect.anchoredPosition = new Vector2(x, y) * SummonCanvasScale;
            rect.sizeDelta = new Vector2(width, height) * SummonCanvasScale;
            return rect;
        }

        private static void DrawSummonWishRoster(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            const float layerWidth = 1280f / (2464f * 720f / 1200f);
            const float left = (1f - layerWidth) * .5f;
            var material = Load<Material>(SummonPath + "Res/Materials/SummonWishPortrait.mat");
            var nameOutline = Load<Material>(SummonPath + "Res/Materials/SummonWishNameOutline.mat");
            var portraits = Area(root, "Clipped originals", left, 0, layerWidth, 1);
            Cover(root, "Original gradient over portraits", SummonArt("gacha_wish_bg_2"));
            var captions = Area(root, "Roster captions", left, 0, layerWidth, 1);
            bindings.Add(("wishPortraitStrip", portraits));
            bindings.Add(("wishCaptionStrip", captions));

            // Native mask origin, width and the portrait's authored face attachment within it.
            var poses = new (float X, float Y, float Width, float FaceX, float FaceY)[]
            {
                (303, -226, 500, 414, 739), (307, -203, 134, 65, 723),
                (443, -203, 135, 68, 742), (580, -203, 135, 68, 771),
                (717, -203, 136, 65, 747), (855, -203, 134, 66, 765),
                (991, -203, 500, 77, 716)
            };
            float[] captionOrigins = { 238, 376, 512, 649, 786, 923, 1067 };
            for (int index = 0; index < poses.Length; index++)
            {
                string key = "wishHero" + index;
                var pose = poses[index];
                var clip = WishRect(portraits, "Native slanted portrait mask", pose.X, pose.Y,
                    pose.Width, 1200, index == 0 ? 1 : 0);
                clip.localEulerAngles = new Vector3(0, 0, -9.5f);
                Paint(clip, null, Color.white);
                clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

                // The camera places ui_face 30% from the top of its square. Place that point
                // at n_pos rather than using the geometric center of the variable-width mask.
                const float portraitSize = 580;
                var frame = WishRect(clip, "Original portrait", pose.FaceX, pose.FaceY,
                    portraitSize, portraitSize, .5f, .70f);
                frame.localEulerAngles = new Vector3(0, 0, 10f);
                var portrait = frame.gameObject.AddComponent<RawImage>();
                portrait.material = material;
                portrait.raycastTarget = false;
                bindings.Add((key, portrait));

                var card = WishRect(captions, key, captionOrigins[index], 0, 138, 720);
                bindings.Add((key + "Card", card.gameObject));
                var ownership = WishRect(card, "Ownership", 94, 343, 164, 48, .5f, .5f);
                Paint(Area(ownership, "Ownership label shade", 0, 0, 1, 1),
                    SummonArt("_grow_white"), new Color32(37, 46, 61, 191));
                var owned = Text(ownership, key + "Owned", null, 24 * .65f * SummonCanvasScale, 0, 0, 1, 1,
                    new Color32(210, 210, 210, 255), TextAlignmentOptions.Center);
                owned.fontStyle = FontStyles.Italic;
                bindings.Add((key + "Owned", owned));

                var label = Text(card, key + "Name", null, 24 * .89f * SummonCanvasScale,
                    (139 - 141 * .89f) / 138, (720 - 274 - 56 * .89f) / 720, 141 * .89f / 138,
                    56 * .89f / 720, alignment: TextAlignmentOptions.BottomRight, wrap: true);
                label.fontStyle = FontStyles.Italic;
                label.fontSharedMaterial = nameOutline;
                bindings.Add((key + "Name", label));

                bool last = index == poses.Length - 1;
                var bar = last
                    ? WishRect(card, "Native banner divider", 11, 266, 151, 25, 0, .5f)
                    : WishRect(card, "Native banner divider", 82, 266, 131, 7, .5f, .5f);
                Paint(bar, SummonArt(last ? "gacha_wish_deco_2" : "gacha_wish_deco_1"), Color.white);
                for (int star = 0; star < 5; star++)
                    Paint(WishRect(card, "Star", 45 + star * 19 * .85f, 236, 30 * .85f, 29 * .85f),
                        DraftSprite("TeamBadges/cm_icon_star"), Color.white);
                var role = Paint(WishRect(card, "Role", 95, 217, 46 * .6f, 46 * .6f, 1, .5f), null, Color.white);
                var element = Paint(WishRect(card, "Element", 112, 218, 34 * .85f, 34 * .85f, .5f, .5f), null, Color.white);
                role.preserveAspect = element.preserveAspect = true;
                role.enabled = element.enabled = false;
                bindings.Add((key + "Role", role));
                bindings.Add((key + "Element", element));
            }

            // The six narrow separator textures each have their own dimensions and center.
            var seams = new (float X, float Y, float Width, float Height)[]
            {
                (166, 373, 35, 553), (303, 383, 28, 653), (442, 399, 42, 604),
                (580, 394, 5, 613), (718, 402, 26, 610), (850, 377, 52, 575)
            };
            for (int index = 0; index < seams.Length; index++)
            {
                var seam = seams[index];
                var rect = WishRect(captions, "Original slanted divider", 238 + seam.X, seam.Y,
                    seam.Width, seam.Height, .5f, .5f);
                rect.localEulerAngles = new Vector3(0, 0, -9.5f);
                Paint(rect, SummonArt("gacha_wish_deco_" + (index + 3)), Color.white);
            }
        }

        private static void BuildSummonModalPanels(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var confirm = SummonPanel(root, bindings, "confirmPanel", 0f, .104f, 1f, .792f, SummonArt("renew_popup_bg_2_alpha"));
            confirm.anchorMin = new Vector2(0f, .5f);
            confirm.anchorMax = new Vector2(1f, .5f);
            confirm.pivot = new Vector2(.5f, .5f);
            confirm.sizeDelta = new Vector2(0f, 571f * SummonCanvasScale);
            confirm.anchoredPosition = Vector2.zero;
            // On held confirmation frames, the unoccluded white HUD behind the modal drops to 46/255.
            float screenTransmission = 46f / 255f;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                screenTransmission = Mathf.GammaToLinearSpace(screenTransmission);
            confirm.parent.GetComponent<Image>().color = Black(1f - screenTransmission);
            confirm.GetComponent<Image>().type = Image.Type.Sliced;
            confirm.GetComponent<Image>().pixelsPerUnitMultiplier = 1f / SummonCanvasScale;

            // Coordinates use the native 720-high dialog and remain centred on ultrawide displays.
            RectTransform Box(RectTransform container, string name, float x, float y, float width, float height, bool bottom = false)
            {
                var rect = Area(container, name, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, bottom ? 0f : 1f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(width, height) * SummonCanvasScale;
                rect.anchoredPosition = new Vector2(x, bottom ? y : -y) * SummonCanvasScale;
                return rect;
            }
            GameText Label(RectTransform box, string key, float nativeSize, Color color, bool centered = false, bool light = false,
                string initial = null)
            {
                var text = Text(box, key, initial, nativeSize * SummonCanvasScale, 0, 0, 1, 1, color,
                    centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft);
                ApplySummonTextOutline(text, light);
                bindings.Add((key, text));
                return text;
            }
            void Divider(RectTransform box, float x)
            {
                var line = Picture(box, "Original divider", SummonArt("z_tile_bar"), 0, 0, 1, 1);
                line.rectTransform.anchorMin = line.rectTransform.anchorMax = new Vector2(0, .5f);
                line.rectTransform.sizeDelta = new Vector2(100f, 4f) * SummonCanvasScale;
                line.rectTransform.anchoredPosition = new Vector2(x * SummonCanvasScale, 0f);
                line.rectTransform.localEulerAngles = Vector3.forward * 90f;
                line.type = Image.Type.Sliced;
                line.pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
                line.color = new Color32(255, 255, 255, 102);
            }

            Color subdued = new Color32(180, 180, 180, 255);
            Color secondary = new Color32(166, 204, 210, 255);
            Label(Box(confirm, "Confirmation title", 0, 56, 600, 48), "confirmTitle", 31.5f, Color.white, true, true);
            var bar = Picture(Box(confirm, "Confirmation title divider", 0, 96, 640, 12), "Original divider",
                SummonArt("_bar_23"), 0, 0, 1, 1);
            bar.type = Image.Type.Sliced;
            bar.pixelsPerUnitMultiplier = 1f / SummonCanvasScale;
            bar.color = new Color32(255, 255, 255, 127);
            Label(Box(confirm, "Confirmation prompt", 0, 147, 725, 52), "confirmPrompt", 19.92f, subdued, true);
            var heroPreview = Box(confirm, "Featured hero offer", 0, 238, 500, 100);
            bindings.Add(("confirmHeroRow", heroPreview.gameObject));
            Picture(heroPreview, "Character background", CommonArt("_hero_s_bg_ally"), 0f, .04f, .184f, .92f, true);
            bindings.Add(("confirmHeroPortrait", Picture(heroPreview, "Featured portrait", null, 0f, .04f, .184f, .92f, true)));
            Picture(heroPreview, "Character frame", CommonArt("_hero_s_frame_enemy"), -.009f, -.025f, .205f, 1.05f, true);
            Divider(heroPreview, 114f);
            Label(Area(heroPreview, "Hero name", .254f, .095f, .735f, .33f), "confirmHeroName", 21.84f, Color.white);
            Label(Area(heroPreview, "Hero guarantee", .254f, .403f, .735f, .36f), "confirmHeroPity", 19.92f, subdued);

            var price = Box(confirm, "Currency offer", 0, 356, 500, 100);
            bindings.Add(("confirmPriceRow", price));
            var item = Area(price, "Currency item", -.004f, .02f, .192f, .96f);
            Picture(item, "Original price tile", SummonArt("_itembg_stuff_3"), 0, 0, 1, 1);
            bindings.Add(("confirmCoin", Picture(item, "Currency", null, .0625f, .0625f, .875f, .875f, true)));
            Label(Area(item, "Price", -.08f, .77f, 1.16f, .32f), "confirmCost", 21.6f, Color.white, true);
            var divide = Area(price, "Currency divider", 0, 0, 1, 1);
            Divider(divide, 114f);
            bindings.Add(("confirmPriceDivider", divide.gameObject));
            var caption = Area(price, "Currency type and name", .254f, 0, .735f, 1);
            bindings.Add(("confirmCurrencyCaption", caption));
            Label(Area(caption, "Currency type", 0f, .089f, 1f, .27f), "confirmType", 18f, secondary, initial: "summon.title");
            Label(Area(caption, "Currency name", 0f, .307f, 1f, .35f), "confirmCurrency", 21.84f, Color.white);
            var owned = Box(price, "Owned currency", 125, 74f, 492, 38);
            Label(owned, "confirmBody", 19.92f, secondary);
            foreach (bool yes in new[] { false, true })
            {
                string key = yes ? "confirmYes" : "confirmNo";
                string normal = yes ? "_btn_blue_r_3" : "_btn_gold_r_2";
                string down = yes ? "_btn_blue_r_p_3" : "_btn_gold_r_p_2";
                var buttonRect = Box(confirm, key, yes ? 107f : -107f, 57f, 388f * .53f, 128f * .53f, bottom: true);
                var button = Button(buttonRect, SummonArt(normal), Color.white);
                PressSprites(button, SummonArt(normal), SummonArt(down));
                ((Image)button.targetGraphic).pixelsPerUnitMultiplier = 1f / (.53f * SummonCanvasScale);
                bindings.Add((key, button));
                // Original label is 50 x .75 within a .53-scale button and sits 6 px above its centre.
                var label = Text(buttonRect, "Text", yes ? "summon.button.ok" : "summon.button.cancel",
                    50f * .75f * .53f * SummonCanvasScale, 0f, -.047f, 1f, 1f, alignment: TextAlignmentOptions.Center);
                ApplySummonTextOutline(label, light: true);
            }

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

        private static void SummonWishGrid(RectTransform content, List<(string Key, Object Target)> bindings, string key, int columns)
        {
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.spacing = new Vector2(8, 8);
            bindings.Add((key, content));
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
