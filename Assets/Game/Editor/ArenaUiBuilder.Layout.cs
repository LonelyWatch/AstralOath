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
            var portraitArea = Area(root, "Charlotte portrait", .265f, -.035f, .55f, 1.10f);
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
            serialized.FindProperty("center").vector2Value = new Vector2(0, 4.32f);
            serialized.FindProperty("halfHeight").floatValue = 3.35f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var profile = Area(root, "Profile", .015f, .023f, .25f, .112f);
            var avatar = Avatar(profile, "Avatar", 0, 0, .27f, 1);
            avatar.Require<Image>("portrait").sprite = Art("c5009_s", Home);
            avatar.Require<Image>("frame").sprite = Art("icon_border_pvp_season20_rank_3", Home);
            Paint(Area(profile, "Profile shade", .28f, -.04f, .53f, .68f), Art("_box_r_op80"), new Color(1, 1, 1, .85f), sliced: true);
            var profileName = Text(profile, "Profile Name", "home.profile", 32, .30f, .02f, .66f, .45f);
            Paint(Area(profile, "Rank line", .30f, .55f, .46f, .04f), null, new Color32(70, 189, 240, 235));
            var navigation = Area(root, "Arena entry", .785f, .43f, .198f, .112f);
            var arena = Action(navigation, "Arena", "home.arena", 36, 0, 0, 1, 1, out var label,
                Art("_box_r_op80"), new Color32(188, 204, 255, 205));
            Picture(navigation, "Arena icon", Art("main_hud_btn_arena", Home), .065f, .18f, .20f, .64f, true);
            Place((RectTransform)label.transform, .26f, .04f, .72f, .92f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 21;
            label.fontSizeMax = 36;
            Rule(navigation, "Lower accent", .08f, .92f, .84f, .022f);
            Bind(root, ("arena", arena), ("arenaLabel", label), ("profile", profileName));
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
            Picture((RectTransform)back.transform, "Back icon", Art("cm_icon_etcback"), .08f, .01f, .84f, .98f, true);
            Text(top, "Title", "arena.title", 37, .049f, .09f, .35f, .82f);
            Rule(top, "Bottom border", 0, .98f, 1, .018f);

            var left = Area(root, "Season profile", 0, .092f, .193f, .908f);
            Paint(left, null, Black(.45f));
            Picture(left, "Season edge", Art("_bg_black_gold_c"), 0, -.08f, 1.38f, 1.16f);
            Text(left, "Season", "arena.season", 36, .09f, .045f, .78f, .07f, Gold, TextAlignmentOptions.Center);
            Rule(left, "Season separator", .08f, .115f, .80f, .003f);
            Picture(left, "Placement emblem", Art("icon_pvp_sa_league_place"), .22f, .146f, .56f, .235f, true);
            Text(left, "Placement", "arena.placement", 25, .09f, .38f, .78f, .055f, Mist, TextAlignmentOptions.Center);
            Text(left, "Placement count", "arena.placement_count", 33, .11f, .46f, .75f, .055f, Color.white, TextAlignmentOptions.Center);
            Rule(left, "Profile separator", .08f, .545f, .80f, .003f);
            Text(left, "Win rate", "arena.winrate", 27, .07f, .575f, .81f, .06f, Color.white, TextAlignmentOptions.Center);
            Text(left, "Win streak", "arena.streak", 25, .07f, .644f, .81f, .05f, Gold, TextAlignmentOptions.Center);

            var outcomeIcons = new[] { "battle_pvp_icon_win", "battle_pvp_icon_draw", "battle_pvp_icon_lose" };
            for (int i = 0; i < outcomeIcons.Length; i++)
            {
                float x = .19f + .21f * i;
                Picture(left, "Outcome icon " + i, Art(outcomeIcons[i]), x, .719f, .19f, .069f, true);
                Text(left, "Outcome count " + i, "arena.zero", 26, x, .79f, .19f, .045f, Color.white, TextAlignmentOptions.Center);
            }

            var right = Area(root, "Mode navigation", .814f, .092f, .186f, .908f);
            Paint(right, null, Blue);
            var selected = Area(right, "Selected mock battle", 0, 0, 1, .144f);
            Paint(selected, null, new Color32(4, 14, 34, 255));
            Picture(selected, "Native selection", Art("_select2"), -.145f, -.06f, .40f, 1.12f);
            Picture(selected, "Mock battle icon", Art("icon_menu_battle_add"), .075f, .23f, .14f, .54f, true);
            Text(selected, "Mock battle tab", "arena.mock", 34, .245f, .05f, .71f, .90f);

            var body = Area(root, "Directory", .203f, .096f, .597f, .89f);
            Text(body, "Mock battle title", "arena.mock", 39, 0, .010f, .60f, .10f);
            var create = CommonButton(body, "CancelButton", "arena.create", .818f, .022f, .175f, .075f);
            Rule(body, "Top rule", 0, .115f, 1, .002f);
            Rule(body, "Toolbar rule", 0, .202f, 1, .002f);
            var filter = Action(body, "Filter", "arena.filter.all", 26, 0, .13f, .235f, .062f, out var filterLabel, Art("_box_r_op80"), Color.white);
            var search = Action(body, "Search", "arena.search", 26, .246f, .13f, .175f, .062f, out var searchLabel, Art("_box_r_op80"), Color.white);
            var refresh = Action(body, "Refresh", "arena.refresh", 27, .818f, .13f, .175f, .062f, out var refreshLabel, Art("_box_r_op80"), Color.white);
            foreach (var (button, caption, icon) in new[] { (filter, filterLabel, "_sorting2"), (search, searchLabel, "cm_icon_etczoomer"),
                (refresh, refreshLabel, "cm_icon_etcrepeat") })
            {
                Picture((RectTransform)button.transform, "Icon", Art(icon), .05f, .17f, .16f, .66f, true);
                Place((RectTransform)caption.transform, .23f, .04f, .75f, .92f);
            }
            var list = Scroll(body, "Rooms", 0, .210f, 1, .775f);
            var status = Text(body, "Directory status", null, 25, .06f, .46f, .88f, .08f, Mist, TextAlignmentOptions.Center);
            var row = Area(body, "Room row template", 0, 0, 1, 0);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 177;
            var title = Text(row, "Room name", null, 32, 0, .02f, .53f, .30f);
            var lockIcon = Picture(row, "Private", Art("cm_icon_etclock"), .50f, .045f, .032f, .20f, true);
            var avatar = Avatar(row, "Room owner", .012f, .30f, .075f, .54f);
            Picture(row, "Owner rank", Art("icon_pvp_sa_league_place"), .088f, .42f, .05f, .38f, true);
            var hostName = Text(row, "Owner name", null, 29, .163f, .32f, .29f, .43f);
            Rule(row, "Rule separator", .456f, .32f, .002f, .49f);
            Text(row, "Rule caption", "arena.rules", 23, .47f, .34f, .24f, .21f, Gold);
            var rule = Text(row, "Rule name", null, 26, .47f, .55f, .28f, .29f);
            Text(row, "Room state", "arena.waiting", 23, .80f, .05f, .19f, .22f, Green, TextAlignmentOptions.Center);
            var join = CommonButton(row, "ConfirmButton", "arena.join", .801f, .29f, .188f, .56f);
            var joinText = join.GetComponent<BindingMap>().Require<GameText>("label");
            Place((RectTransform)joinText.transform, .06f, .46f, .88f, .47f);
            var count = Text(row, "Players", null, 26, .81f, .35f, .17f, .24f, Green, TextAlignmentOptions.Center);
            Bind(row, ("name", title), ("hostName", hostName), ("avatar", avatar), ("rule", rule), ("count", count),
                ("lock", lockIcon), ("join", join));
            row.gameObject.SetActive(false);

            var searchPanel = Area(root, "Search popup", .203f, .280f, .34f, .16f);
            Paint(searchPanel, Art("_box_r_op80"), new Color32(222, 200, 152, 255), true, true);
            var query = Input(searchPanel, "Query", "arena.search_placeholder", 28, .022f, .10f, .645f, .46f);
            var submit = CommonButton(searchPanel, "ConfirmButton", "arena.search", .68f, .10f, .295f, .46f);
            Text(searchPanel, "Search hint", "arena.search_hint", 24, .027f, .61f, .90f, .31f, Gold);
            searchPanel.gameObject.SetActive(false);
            var choice = Prefab(root, "ChoicePopup", 0, 0, 1, 1);
            var choiceBody = choice.GetComponent<BindingMap>().Require<RectTransform>("body");
            Place(choiceBody, .203f, .279f, .37f, .27f);
            var choiceBackground = choice.GetComponent<BindingMap>().Require<Image>("background");
            choiceBackground.sprite = Art("_box_r_op80");
            var visibility = new[] { "all", "public", "private" };
            var binding = new System.Collections.Generic.List<(string, Object)>();
            for (int i = 0; i < visibility.Length; i++)
            {
                string key = visibility[i];
                var filterChoice = Action(choiceBody, key, "arena.filter." + key, 28, .045f, .09f + i * .277f, .32f, .229f,
                    out var caption, Art("_box_r_op80"), Color.clear);
                var rowRect = (RectTransform)filterChoice.transform;
                Place((RectTransform)caption.transform, .26f, .04f, .68f, .92f);
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                Picture(rowRect, "Box", CommonArt("cm_checkbg"), .04f, .14f, .18f, .72f, true);
                var mark = Picture(rowRect, "Check", CommonArt("cm_checkicon2"), .04f, .14f, .18f, .72f, true);
                binding.Add((key, filterChoice));
                binding.Add((key + "_mark", mark));
            }
            Rule(choiceBody, "Column divider", .393f, .07f, .002f, .85f);
            foreach (var (key, y) in new[] { ("players", .09f), ("recent", .367f) })
            {
                var sortChoice = Action(choiceBody, key, "arena.sort." + key, 28, .417f, y, .535f, .229f, out var caption,
                    Art("_btn_small_white"), Color.clear);
                Place((RectTransform)caption.transform, .07f, .04f, .72f, .92f);
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                binding.Add((key, sortChoice));
                binding.Add((key + "_label", caption));
            }
            Bind(choiceBody, binding.ToArray());
            choice.gameObject.SetActive(false);
            var password = BuildPassword(root);
            Bind(root, ("back", back), ("create", create), ("refresh", refresh), ("filter", filter), ("search", search),
                ("filterLabel", filterLabel), ("searchLabel", searchLabel), ("submitSearch", submit), ("status", status),
                ("searchPanel", searchPanel.gameObject), ("query", query), ("list", list), ("row", row.gameObject),
                ("choice", choice.gameObject), ("password", password));
            return root.gameObject;
        }

        private static GameObject BuildPassword(RectTransform parent)
        {
            var root = Area(parent, "Join password popup", 0, 0, 1, 1);
            Paint(root, null, Black(.63f), true);
            var card = Area(root, "Password card", .32f, .29f, .36f, .42f);
            Paint(card, Art("renew_popup_bg_alpha"), Color.white, true, true);
            Text(card, "Password title", "arena.set_password", 37, .075f, .06f, .85f, .16f);
            var input = Input(card, "Password", "arena.enter_text", 4, .08f, .30f, .84f, .16f, true);
            var status = Text(card, "Password status", null, 24, .08f, .53f, .84f, .14f, Mist);
            var cancel = CommonButton(card, "CancelButton", "arena.cancel", .13f, .73f, .34f, .16f);
            var confirm = CommonButton(card, "ConfirmButton", "arena.confirm", .53f, .73f, .34f, .16f);
            Bind(root, ("input", input), ("status", status), ("cancel", cancel), ("confirm", confirm));
            root.gameObject.SetActive(false);
            return root.gameObject;
        }
    }
}
