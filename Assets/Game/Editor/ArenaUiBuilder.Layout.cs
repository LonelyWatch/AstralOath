using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static Color Black(float alpha) => new Color(0, 0, 0, alpha);
        private static readonly Color Blue = new Color32(12, 32, 58, 245);
        private static readonly Color Deep = new Color32(7, 13, 24, 240);

        private static GameObject BuildHome()
        {
            var root = Root("HomeWindow");
            Paint(Area(root, "Stage base", 0, 0, 1, 1), null, Deep);
            Cover(root, "Native lobby stage", Art("lobby_bg3", Home));
            var portraitArea = Area(root, "Charlotte portrait", .233f, -.035f, .55f, 1.10f);
            var aspect = portraitArea.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            aspect.aspectRatio = 1;
            var raw = portraitArea.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var portrait = portraitArea.gameObject.AddComponent<UISkeletonPortrait>();
            var serialized = new SerializedObject(portrait);
            serialized.FindProperty("output").objectReferenceValue = raw;
            serialized.FindProperty("skeleton").objectReferenceValue = Load<TextAsset>(Home + "Res/Animations/c5009.spine.json");
            serialized.FindProperty("atlas").objectReferenceValue = Load<TextAsset>(Home + "Res/Atlas/c5009.atlas.txt");
            serialized.FindProperty("page").objectReferenceValue = Load<Texture2D>(Home + "Res/Textures/c5009.png");
            serialized.FindProperty("pageName").stringValue = "c5009.sct";
            serialized.FindProperty("center").vector2Value = new Vector2(0, 4.85f);
            serialized.FindProperty("halfHeight").floatValue = 3.10f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Picture(root, "Lobby comic accents", Art("ma_sticker_emo2", Home), .126f, .599f, .047f, .128f, true);
            var cloud = Picture(root, "Lobby cloud sticker", Art("ma_sticker_emo3", Home), .222f, .552f, .113f, .232f, true);
            cloud.rectTransform.localScale = new Vector3(-1, 1, 1);
            Picture(root, "Lobby heart sticker", Art("ma_sticker_emo1", Home), .761f, .472f, .176f, .411f, true).color
                = new Color32(226, 226, 226, 255);

            var profile = Area(root, "Profile", .015f, .023f, .25f, .112f);
            var avatar = Avatar(profile, "Avatar", 0, 0, .27f, 1);
            avatar.Require<Image>("portrait").sprite = Art("c5009_s", Home);
            avatar.Require<Image>("frame").sprite = Art("icon_border_pvp_season20_rank_3", Home);
            Paint(Area(profile, "Profile shade", .24f, -.04f, .57f, .83f), Art("_box_r_op80"),
                new Color32(105, 120, 147, 195), sliced: true);
            var profileName = Text(profile, "Profile Name", "home.profile", 32, .30f, .02f, .66f, .45f);
            Picture(profile, "Profile gauge backing", Art("renew_gauge_profile_bg", Home), .30f, .71f, .45f, .09f);
            Picture(profile, "Profile gauge", Art("renew_gauge_profile_bar", Home), .30f, .71f, .45f, .09f);
            var navigation = Area(root, "Arena entry", .852f, .522f, .132f, .073f);
            var arena = Action(navigation, "Arena", "home.arena", 36, 0, 0, 1, 1, out var label,
                Art("_box_r_op80"), new Color32(184, 197, 222, 164));
            Picture(navigation, "Arena icon", Art("main_hud_btn_arena", Home), .065f, .18f, .20f, .64f, true);
            Place((RectTransform)label.transform, .26f, .04f, .72f, .92f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 21;
            label.fontSizeMax = 36;

            var displayMode = Action(root, "Display mode", "home.window.fullscreen", 24, .85f, .026f, .13f, .055f,
                out var windowLabel, Art("_box_r_op80"), Color.white);
            Bind(root, ("arena", arena), ("arenaLabel", label), ("profile", profileName),
                ("displayMode", displayMode), ("displayModeLabel", windowLabel));
            return root.gameObject;
        }

        private static GameObject BuildDirectory()
        {
            var root = Root("ArenaDirectory");
            Paint(Area(root, "Background", 0, 0, 1, 1), null, new Color32(31, 34, 40, 255));
            Cover(root, "Native arena season", Art("pvp_rta_ss21_d"));
            Paint(Area(root, "Directory shading", 0, 0, 1, 1), null, Black(.20f));
            var top = Area(root, "Header", 0, 0, 1, .092f);
            Paint(top, null, Black(.65f));
            var back = Button(Area(top, "Back", .013f, .12f, .035f, .76f), null, Color.clear);
            back.FeedbackSound = GameUiFeedback.Sound.Back;
            Picture((RectTransform)back.transform, "Back icon", Art("cm_icon_etcback"), .08f, .01f, .84f, .98f, true);
            Text(top, "Title", "arena.title", 37, .049f, .09f, .35f, .82f);
            Paint(Area(top, "Bottom border", 0, .98f, 1, .015f), null, new Color32(126, 119, 111, 115));

            var left = Area(root, "Season profile", 0, .092f, .193f, .908f);
            Paint(Area(left, "Season shadow", 0, 0, .93f, 1), null, Black(.72f));
            // pvplive_base uses the straight edge for left_bg. The curved left2_bg belongs behind the page.
            Picture(left, "Season edge", Art("_bg_black_gold_s"), 0, .007f, 1, 1.009f);
            var seasonGold = new Color32(181, 150, 108, 255);
            var sideGray = new Color32(152, 152, 152, 255);
            Text(left, "Season", "arena.season", 33, .075f, .035f, .80f, .066f, seasonGold, TextAlignmentOptions.Center);
            SeasonLine(left, "Season separator", "_bar_1", .068f, .127f, .40f, .013f, new Color32(144, 122, 91, 200));
            Picture(left, "Placement emblem", Art("icon_pvp_sa_league_place"), .207f, .156f, .535f, .215f, true);
            Text(left, "Placement", "arena.placement", 24, .078f, .382f, .79f, .055f, sideGray, TextAlignmentOptions.Center);
            Text(left, "Placement count", "arena.placement_count", 33, .09f, .46f, .77f, .055f, Color.white, TextAlignmentOptions.Center);
            SeasonLine(left, "Profile separator", "_bar_4", .080f, .532f, .395f, .019f, new Color32(98, 80, 56, 170));
            Text(left, "Win rate", "arena.winrate", 27, .07f, .564f, .81f, .06f, Color.white, TextAlignmentOptions.Center);
            Text(left, "Win streak", "arena.streak", 25, .07f, .622f, .81f, .05f, seasonGold, TextAlignmentOptions.Center);

            var outcomeIcons = new[] { "battle_pvp_icon_win", "battle_pvp_icon_draw", "battle_pvp_icon_lose" };
            for (int i = 0; i < outcomeIcons.Length; i++)
            {
                float x = .16f + .213f * i;
                Picture(left, "Outcome icon " + i, Art(outcomeIcons[i]), x, .706f, .19f, .086f, true);
                Text(left, "Outcome count " + i, "arena.zero", 26, x, .788f, .19f, .043f, Color.white, TextAlignmentOptions.Center);
            }

            Picture(left, "Global restrictions icon", Art("cm_icon_etcinfor"), .413f, .840f, .12f, .041f, true).color = sideGray;
            var restrictions = Text(left, "Global restrictions", "arena.no_global_bans", 21, .027f, .886f, .89f, .054f,
                sideGray, TextAlignmentOptions.Center);
            restrictions.enableAutoSizing = true;
            restrictions.fontSizeMin = 15;
            restrictions.fontSizeMax = 21;

            var right = Area(root, "Mode navigation", .814f, .092f, .186f, .908f);
            Paint(right, null, Blue);
            var selected = Area(right, "Selected mock battle", 0, 0, 1, .144f);
            Paint(selected, null, new Color32(4, 14, 34, 255));
            var selectedArt = Area(selected, "Native selection", 0, 0, 1, 1);
            selectedArt.offsetMin = new Vector2(-33f, 0);
            Paint(selectedArt, Art("directory_tab"), Color.white, sliced: true).pixelsPerUnitMultiplier = 2f / 3f;
            Picture(selected, "Mock battle icon", Art("icon_menu_battle_add"), .075f, .23f, .14f, .54f, true);
            Text(selected, "Mock battle tab", "arena.mock", 34, .245f, .05f, .71f, .90f);

            var body = Area(root, "Directory", .203f, .096f, .597f, .89f);
            Text(body, "Mock battle title", "arena.mock", 39, 0, .010f, .60f, .10f);
            const float buttonLeft = .818f;
            const float buttonWidth = .175f;
            const float toolbarTop = .116f;
            const float toolbarHeight = .061f;
            var create = CommonButton(body, "CancelButton", "arena.create", buttonLeft, .022f, buttonWidth, .083f);
            PressSprites(create, Art("directory_create"), Art("directory_create_pressed"));
            ((Image)create.targetGraphic).pixelsPerUnitMultiplier = 2f / 3f;
            var createLabel = create.GetComponent<BindingMap>().Require<GameText>("label");
            createLabel.fontSize = 28f;
            createLabel.characterSpacing = -1.5f;
            Place(createLabel.rectTransform, .04f, 0, .90f, .92f);

            DirectoryRule(body, "Top rule", .098f, 178);
            DirectoryRule(body, "Toolbar rule", .181f, 127);
            var filter = Action(body, "Filter", "arena.filter.all", 26, 0, toolbarTop, .235f, toolbarHeight, out var filterLabel, Art("_box_r_op80"), Color.white);
            var search = Action(body, "Search", "arena.search", 26, .246f, toolbarTop, .175f, toolbarHeight, out var searchLabel, Art("_box_r_op80"), Color.white);
            var refresh = Action(body, "Refresh", "arena.refresh", 26, buttonLeft, toolbarTop, buttonWidth, toolbarHeight, out var refreshLabel,
                Art("directory_refresh"), Color.white);
            PressSmallBlack(filter);
            PressSmallBlack(search);
            PressSprites(refresh, Art("directory_refresh"), Art("directory_refresh_pressed"));
            ((Image)refresh.targetGraphic).pixelsPerUnitMultiplier = 2f / 3f;
            refreshLabel.color = new Color32(193, 193, 193, 255);
            Place(refreshLabel.rectTransform, .23f, 0, .756f, .85f);
            Picture((RectTransform)refresh.transform, "Icon", Art("cm_icon_etcrepeat"), .048f, .11f, .245f, .66f, true).color
                = new Color32(144, 144, 144, 255);
            var searchButtonRect = (RectTransform)search.transform;
            var searchSelected = Picture(searchButtonRect, "Search active", Art("_btn_white_flat"), 0, 0, 1, 1);
            searchSelected.type = Image.Type.Sliced;
            searchSelected.enabled = false;
            searchSelected.transform.SetAsFirstSibling();
            var searchPressed = Picture(searchButtonRect, "Search active pressed art", Art("_btn_white_flat_p"), 0, 0, 1, 1);
            searchPressed.enabled = false;
            var searchIcon = ToolbarIcon(search, searchLabel, "cm_icon_etczoomer");
            ToolbarIcon(filter, filterLabel, "_sorting2");
            var list = Scroll(body, "Rooms", 0, .210f, 1, .775f);
            var status = Text(body, "Directory status", null, 25, .06f, .46f, .88f, .08f, Mist, TextAlignmentOptions.Center);
            var row = Area(body, "Room row template", 0, 0, 1, 0);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 181;
            var title = Text(row, "Room name", null, 32, 0, .02f, .53f, .30f);
            var lockIcon = Picture(row, "Private", Art("cm_icon_etclock"), .50f, .045f, .032f, .20f, true);
            var avatar = Avatar(row, "Room owner", .012f, .30f, .075f, .54f);
            Picture(row, "Owner rank", Art("icon_pvp_sa_league_place"), .088f, .42f, .05f, .38f, true);
            var hostName = Text(row, "Owner name", null, 29, .163f, .32f, .29f, .43f);
            VerticalRule(row, "Rule separator", .450f, .32f, .018f, .49f, Gold);
            Text(row, "Rule caption", "arena.rules", 23, .47f, .34f, .24f, .21f, Gold);
            var rule = Text(row, "Rule name", null, 26, .47f, .55f, .28f, .29f);
            Text(row, "Room state", "arena.waiting", 23, .80f, .05f, .19f, .22f, Green, TextAlignmentOptions.Center);
            var join = CommonButton(row, "ConfirmButton", "arena.join", .801f, .29f, .188f, .56f);
            var joinText = join.GetComponent<BindingMap>().Require<GameText>("label");
            Place((RectTransform)joinText.transform, .06f, .46f, .88f, .47f);
            Picture(row, "Player count icon", Art("icon_menu_member"), .842f, .353f, .035f, .21f, true).color = Green;
            var count = Text(row, "Players", null, 30, .883f, .352f, .084f, .24f, Green);
            Bind(row, ("name", title), ("hostName", hostName), ("avatar", avatar), ("rule", rule), ("count", count),
                ("lock", lockIcon), ("join", join));
            row.gameObject.SetActive(false);

            var searchPanel = Area(root, "Search popup", .203f, .280f, .34f, .16f);
            Paint(searchPanel, Art("_box_r_op80"), new Color32(222, 200, 152, 255), true, true);
            Paint(Area(searchPanel, "Popup dark fill", .008f, .033f, .984f, .934f), Art("_box_r_op80"), Color.white, sliced: true);
            var query = Input(searchPanel, "Query", "arena.search_placeholder", 28, .022f, .10f, .645f, .46f);
            var submit = CommonButton(searchPanel, "ConfirmButton", "arena.search", .68f, .10f, .295f, .46f);
            Text(searchPanel, "Search hint", "arena.search_hint", 24, .027f, .61f, .90f, .31f, Gold);
            searchPanel.gameObject.SetActive(false);
            var choice = Prefab(root, "ChoicePopup", 0, 0, 1, 1);
            var choiceBody = choice.GetComponent<BindingMap>().Require<RectTransform>("body");
            Place(choiceBody, .203f, .279f, .37f, .27f);
            var choiceBackground = choice.GetComponent<BindingMap>().Require<Image>("background");
            choiceBackground.sprite = Art("_box_r_op80");
            var choiceFill = Area(choiceBody, "Popup dark fill", .007f, .025f, .986f, .95f);
            Paint(choiceFill, null, Black(.94f));
            choiceFill.SetAsFirstSibling();
            var visibility = new[] { "all", "public", "private" };
            var binding = new System.Collections.Generic.List<(string, Object)>();
            for (int i = 0; i < visibility.Length; i++)
            {
                string key = visibility[i];
                var filterChoice = Action(choiceBody, key, "arena.filter." + key, 28, .045f, .09f + i * .277f, .32f, .229f,
                    out var caption, Art("_btn_small_white"), Color.clear);
                PressSprites(filterChoice, Art("_btn_small_white"), Art("_btn_small_white_p"));
                var rowRect = (RectTransform)filterChoice.transform;
                Place((RectTransform)caption.transform, .26f, .04f, .68f, .92f);
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                Picture(rowRect, "Box", CommonArt("cm_checkbg"), .04f, .14f, .18f, .72f, true);
                var mark = Picture(rowRect, "Check", CommonArt("cm_checkicon2"), .04f, .14f, .18f, .72f, true);
                binding.Add((key, filterChoice));
                binding.Add((key + "_mark", mark));
            }
            NativeRule(choiceBody, "Visibility divider", .023f, .357f, .354f, .025f, Mist * .68f);
            VerticalRule(choiceBody, "Column divider", .38f, .07f, .027f, .85f, Mist * .71f);
            foreach (var (key, y) in new[] { ("players", .09f), ("recent", .367f) })
            {
                var sortChoice = Action(choiceBody, key, "arena.sort." + key, 28, .417f, y, .535f, .229f, out var caption,
                    Art("_btn_small_white"), Color.clear);
                Place((RectTransform)caption.transform, .07f, .04f, .72f, .92f);
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                PressSprites(sortChoice, Art("_btn_small_white"), Art("_btn_small_white_p"));
                var sortRect = (RectTransform)sortChoice.transform;
                var up = Picture(sortRect, "Ascending", Art("cm_btn_sorting_up"), .706f, -.08f, .285f, 1.16f, true);
                var down = Picture(sortRect, "Descending", Art("cm_btn_sorting_down"), .706f, -.08f, .285f, 1.16f, true);
                PressDecorations(sortChoice, (up, Art("cm_btn_sorting_up_p")), (down, Art("cm_btn_sorting_down_p")));
                up.enabled = false;
                down.enabled = false;
                binding.Add((key, sortChoice));
                binding.Add((key + "_label", caption));
                binding.Add((key + "_up", up));
                binding.Add((key + "_down", down));
            }
            Bind(choiceBody, binding.ToArray());
            choice.gameObject.SetActive(false);
            var password = CreatePasswordEntry(root);
            var toast = CreateDirectoryToast(root);
            Bind(root, ("back", back), ("create", create), ("refresh", refresh), ("filter", filter), ("search", search),
                ("filterLabel", filterLabel), ("searchLabel", searchLabel), ("searchSelected", searchSelected),
                ("searchIcon", searchIcon), ("searchPressed", searchPressed), ("submitSearch", submit), ("status", status),
                ("searchPanel", searchPanel.gameObject), ("query", query), ("list", list), ("row", row.gameObject),
                ("choice", choice.gameObject), ("password", password), ("toast", toast));
            return root.gameObject;
        }

        private static void SeasonLine(RectTransform parent, string name, string art, float x, float y,
            float halfWidth, float height, Color color)
        {
            var sprite = Art(art);
            Paint(Area(parent, name + " left", x, y, halfWidth, height), sprite, color, sliced: true);
            var right = Area(parent, name + " right", x + halfWidth, y, halfWidth, height);
            right.localScale = new Vector3(-1, 1, 1);
            Paint(right, sprite, color, sliced: true);
        }

        private static Image ToolbarIcon(GameButton button, GameText caption, string icon)
        {
            Place(caption.rectTransform, .06f, .04f, .88f, .92f);
            return Picture((RectTransform)button.transform, "Icon", Art(icon), .05f, .17f, .16f, .66f, true);
        }

        private static void DirectoryRule(RectTransform parent, string name, float y, byte alpha) =>
            Paint(Area(parent, name, 0, y, 1, .012f), Art("directory_rule"),
                new Color32(146, 109, 62, alpha), sliced: true).pixelsPerUnitMultiplier = 2f / 3f;
        private static void NativeRule(RectTransform parent, string name, float x, float y, float w, float h, Color color,
            string art = "_bar_23") =>
            Paint(Area(parent, name, x, y, w, h), Art(art), color, sliced: true);

        private static void VerticalRule(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var area = Area(parent, name, x, y, w, h);
            var rotated = Area(area, "Decoration", .50f, 0, 0, 1);
            rotated.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            var fitter = rotated.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = 1;
            var line = Area(rotated, "Vertical rule sprite", 0, .487f, 1, .026f);
            line.localRotation = Quaternion.Euler(0, 0, 90);
            Paint(line, Art("_bar_5"), color);
        }

    }
}
