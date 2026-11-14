using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static GameObject BuildCreate()
        {
            var root = Root("ArenaCreateRoom");
            var dismiss = Button(Area(root, "Backdrop", 0, 0, 1, 1), null, Black(.73f));
            dismiss.FeedbackSound = GameUiFeedback.Sound.Cancel;
            var card = Area(root, "Create dialog", .244f, .042f, .512f, .888f);
            Paint(card, Art("renew_popup_bg_alpha"), Color.white, true, true);
            Text(card, "Create title", "arena.create", 42, .050f, .022f, .88f, .07f);
            NativeRule(card, "Header divider", .046f, .096f, .908f, .023f, new Color32(170, 184, 187, 225));
            Text(card, "Name label", "arena.room_name", 30, .05f, .143f, .19f, .079f, Mist);
            var name = Input(card, "Room name", "arena.enter_text", 28, .262f, .132f, .685f, .084f);
            Text(card, "Room name hint", "arena.room_name_hint", 24, .262f, .221f, .68f, .054f, new Color32(149, 158, 167, 255));

            Text(card, "Public label", "arena.privacy", 30, .05f, .298f, .19f, .079f, Mist);
            var toggleArea = Area(card, "Set password toggle", .260f, .301f, .246f, .072f);
            var toggle = toggleArea.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = Paint(toggleArea, null, Color.clear, true);
            toggle.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            Picture(toggleArea, "Box", CommonArt("cm_checkbg"), .008f, .16f, .174f, .68f, true);
            var box = Picture(toggleArea, "Check", CommonArt("cm_checkicon2"), .008f, .16f, .174f, .68f, true);
            toggle.graphic = box;
            toggle.isOn = false;
            Text(toggleArea, "Set password", "arena.set_password", 28, .216f, .01f, .78f, .98f);
            VerticalRule(card, "Privacy separator", .517f, .289f, .018f, .099f, Mist * .7f);
            Text(card, "Password hint", "arena.password_hint", 23, .55f, .287f, .17f, .087f, Mist, wrap: true);
            var password = Input(card, "Password", "arena.enter_text", 4, .735f, .291f, .212f, .078f, true);
            foreach (var field in new[] { name, password })
                ((Image)field.targetGraphic).sprite = Art("_box_w4");

            Text(card, "Chat permission", "arena.chat_setting", 28, .05f, .410f, .19f, .095f, Mist, wrap: true);
            var allow = Action(card, "Allow", "arena.allow", 27, .261f, .418f, .147f, .069f, out _, Art("_btn_small_white"), Color.white);
            var deny = Action(card, "Deny", "arena.deny", 27, .418f, .418f, .147f, .069f, out _, Art("_btn_small_white"), Color.white);
            Text(card, "Rules label", "arena.rules", 30, .05f, .638f, .19f, .070f, Mist);
            var rules = Scroll(card, "Rules", .256f, .510f, .719f, .345f, horizontal: true);
            var keys = new System.Collections.Generic.List<(string, Object)>
            {
                ("name", name), ("password", password), ("locked", toggle), ("allow", allow), ("deny", deny), ("rules", rules)
            };
            foreach (var (id, index) in new[] { ("wild",5), ("draft",4), ("round",3), ("no_duplicate_class",2), ("basic",1) })
            {
                var option = Area(rules.content, id, 0, 0, 0, 1);
                var size = option.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = 278;
                var tile = Area(option, "Square card", 0, 0, 1, 1);
                var tileAspect = tile.gameObject.AddComponent<AspectRatioFitter>();
                tileAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                tileAspect.aspectRatio = 1;
                var button = Button(tile, Art("pvplive_mod_" + index.ToString("00")), Color.white);
                var selected = Picture(tile, "Selection", Art("_select"), -.018f, -.018f, 1.036f, 1.036f);
                if (selected.sprite && selected.sprite.border != Vector4.zero) selected.type = Image.Type.Sliced;
                if (!selected.sprite)
                {
                    selected.enabled = true;
                    selected.color = new Color(1f, .86f, .34f, .12f);
                    var edge = selected.gameObject.AddComponent<Outline>();
                    edge.effectColor = new Color(1f, .9f, .48f, .9f);
                    edge.effectDistance = new Vector2(4, 4);
                }
                var caption = Text(tile, "Rule label", "arena.rule." + id, 31, .073f, .053f, .81f, .28f,
                    alignment: TextAlignmentOptions.TopLeft, wrap: true);
                caption.fontStyle = FontStyles.Bold;
                keys.Add(("rule_" + id, button));
                keys.Add(("selection_" + id, selected));
            }
            Rule(card, "Footer separator", .03f, .859f, .94f, .002f);
            var status = Text(card, "Create status", null, 22, .031f, .956f, .938f, .035f,
                new Color32(255, 191, 158, 255), TextAlignmentOptions.Center);
            var cancel = CommonButton(card, "CancelButton", "arena.cancel", .246f, .88f, .24f, .080f);
            var confirm = CommonButton(card, "ConfirmButton", "arena.confirm", .503f, .88f, .24f, .080f);
            var dismissHint = Area(root, "Dismiss hint", .419f, .938f, .162f, .044f);
            Picture(dismissHint, "Dismiss icon", Art("cm_icon_etcclose"), .03f, .16f, .12f, .68f, true).color = Mist;
            Text(dismissHint, "Dismiss caption", "arena.touch_close", 24, .12f, 0, .82f, 1,
                Color.white, TextAlignmentOptions.Center);
            keys.Add(("dismiss", dismiss)); keys.Add(("status", status)); keys.Add(("cancel", cancel)); keys.Add(("confirm", confirm));
            Bind(root, keys.ToArray());
            return root.gameObject;
        }

        private static GameObject BuildRoom()
        {
            var root = Root("ArenaRoom");
            Paint(Area(root, "Backdrop", 0, 0, 1, 1), null, Deep);
            Cover(root, "Courtyard", Art("room_courtyard"));
            Paint(Area(root, "Scene tint", 0, 0, 1, 1), null, Black(.48f));
            Picture(root, "Native room vignette", Art("vignetting4"), 0, 0, 1, 1);
            var left = Area(root, "Room contents", 0, 0, .604f, 1);
            Paint(Area(left, "Room header shading", 0, 0, 1, .095f), null, Black(.55f));
            var leave = Button(Area(left, "Leave room", .024f, .012f, .077f, .065f), Art("_box_r_op80"), Color.white);
            leave.FeedbackSound = GameUiFeedback.Sound.Back;
            Picture((RectTransform)leave.transform, "Leave icon", Art("cm_hud_top_btn_esc"), 0, -.17f, 1, 1.34f, true);
            var name = Text(left, "Room title", null, 42, .119f, .01f, .78f, .066f);
            NativeRule(left, "Top rule", .024f, .079f, .954f, .023f, Gold, "_bar_2");
            Picture(left, "Battle rule icon", Art("icon_menu_battle"), .446f, .184f, .034f, .035f, true).color = Gold;
            Text(left, "Battle rule caption", "arena.rules", 27, .481f, .183f, .21f, .043f, Gold);
            NativeRule(left, "Rule accent", .39f, .221f, .22f, .021f, Gold, "_bar_2");
            var rule = Text(left, "Battle rule", null, 30, .37f, .236f, .27f, .045f, Color.white, TextAlignmentOptions.Center);
            var host = ArenaAvatar(left, "Host avatar", .148f, .251f, .155f, .197f);
            var guest = ArenaAvatar(left, "Guest avatar", .651f, .251f, .155f, .197f);
            Picture(host.transform as RectTransform, "Host rank", Art("icon_pvp_sa_league_place"), .943f, .338f, .43f, .43f, true);
            Picture(guest.transform as RectTransform, "Guest rank", Art("icon_pvp_sa_league_place"), .943f, .338f, .43f, .43f, true);
            var vacant = Picture(left, "Vacant guest slot", Art("game_hud_target_c2"), .621f, .431f, .242f, .136f);
            var emptyInfo = Area(left, "Vacant details", .552f, .254f, .38f, .115f);
            Picture(emptyInfo, "Info", Art("cm_icon_etcinfor"), .42f, .02f, .16f, .37f, true).color = Mist * .68f;
            Text(emptyInfo, "Empty label", "arena.no_player", 28, 0, .42f, 1, .47f, Mist, TextAlignmentOptions.Center);
            var hostName = Text(left, "Host name", null, 32, .052f, .441f, .37f, .065f, Green, TextAlignmentOptions.Center);
            var guestName = Text(left, "Guest name", null, 32, .553f, .441f, .38f, .065f, Mist, TextAlignmentOptions.Center);
            Picture(left, "Versus", Art("_battle_pvp_icon_vs"), .424f, .311f, .17f, .192f, true);
            var start = CommonButton(left, "ConfirmButton", "arena.start", .405f, .518f, .19f, .078f);
            start.interactable = false;
            start.gameObject.AddComponent<CanvasGroup>().alpha = .55f;
            NativeRule(left, "Members divider", .024f, .628f, .954f, .026f, Gold);
            var members = Scroll(left, "Member list", .024f, .655f, .946f, .310f);
            var memberRow = Area(left, "Member template", 0, 0, 1, 0);
            memberRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 82;
            var memberAvatar = ArenaAvatar(memberRow, "Member avatar", .014f, .02f, .088f, .94f);
            var memberName = Text(memberRow, "Member name", null, 26, .115f, .05f, .82f, .88f, Green);
            Bind(memberRow, ("avatar", memberAvatar), ("name", memberName));
            memberRow.gameObject.SetActive(false);

            var chat = Area(root, "Arena chat", .605f, 0, .395f, 1);
            Paint(chat, null, Black(.94f));
            Paint(Area(root, "Chat separator", .602f, 0, .0022f, 1), null, Gold * .8f);
            Text(chat, "Chat title", "arena.title", 39, .042f, .012f, .91f, .066f);
            var channel = Area(chat, "Arena channel", .585f, .083f, .35f, .064f);
            Paint(channel, Art("_tap_select_p"), Color.white, sliced: true);
            Picture(channel, "Arena symbol", Art("icon_menu_arena"), .12f, .18f, .19f, .64f, true);
            Text(channel, "Channel name", "arena.channel", 30, .29f, 0, .62f, 1, Color.white, TextAlignmentOptions.Center);
            NativeRule(chat, "Channel separator", .03f, .136f, .94f, .022f, Gold, "_bar_2");
            Picture(chat, "Participants icon", Art("icon_menu_member"), .05f, .162f, .061f, .059f, true);
            Text(chat, "Member count caption", "arena.participants", 25, .119f, .157f, .31f, .037f, Gold);
            var count = Text(chat, "Member count", null, 34, .119f, .187f, .55f, .041f);
            NativeRule(chat, "Participant separator", .03f, .220f, .94f, .022f, Gold * .60f, "_bar_2");
            var history = Scroll(chat, "Message history", .035f, .245f, .93f, .613f);
            history.content.anchorMin = Vector2.zero;
            history.content.anchorMax = Vector2.right;
            history.content.pivot = new Vector2(.5f, 0);
            history.content.anchoredPosition = Vector2.zero;
            history.content.GetComponent<VerticalLayoutGroup>().spacing = 8;
            var chatRow = Area(chat, "Chat message template", 0, 0, 1, 0);
            var messageLayout = chatRow.gameObject.AddComponent<LayoutElement>();
            messageLayout.minHeight = 91;
            messageLayout.preferredHeight = 91;
            var senderAvatar = ArenaAvatar(chatRow, "Sender avatar", 0, 0, .111f, 1);
            var sender = Text(chatRow, "Sender", null, 26, .131f, 0, .60f, 0, Green);
            sender.rectTransform.offsetMin = new Vector2(0, -40);
            var age = Text(chatRow, "Age", null, 21, .735f, 0, .23f, 0, Gold * .80f);
            age.rectTransform.offsetMin = new Vector2(0, -40);
            var body = Text(chatRow, "Content", null, 27, .131f, 0, .846f, 1, Mist,
                TextAlignmentOptions.TopLeft, wrap: true);
            body.rectTransform.offsetMin = new Vector2(0, 6);
            body.rectTransform.offsetMax = new Vector2(0, -42);
            body.overflowMode = TextOverflowModes.Overflow;
            Bind(chatRow, ("avatar", senderAvatar), ("sender", sender), ("age", age), ("body", body));
            chatRow.gameObject.SetActive(false);
            var composer = Area(chat, "Composer", 0, .853f, 1, .147f);
            var status = Text(composer, "Chat status", null, 22, .05f, 0, .9f, .184f, Mist);
            NativeRule(composer, "Message divider", .03f, .134f, .94f, .143f, Gold, "_bar_2");
            var message = Input(composer, "Message", "arena.message", 200, .036f, .325f, .654f, .538f);
            var send = CommonButton(composer, "ConfirmButton", "arena.send", .707f, .325f, .26f, .538f);
            FollowKeyboard(composer, message);
            Bind(root, ("leave", leave), ("send", send), ("name", name), ("rule", rule), ("count", count),
                ("hostName", hostName), ("guestName", guestName), ("status", status), ("message", message),
                ("host", host), ("guest", guest), ("vacant", vacant), ("emptyInfo", emptyInfo.gameObject), ("members", members), ("memberRow", memberRow.gameObject),
                ("messages", history), ("chatRow", chatRow.gameObject));
            return root.gameObject;
        }

        private static BindingMap ArenaAvatar(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var avatar = Avatar(parent, name, x, y, w, h);
            avatar.Require<Image>("frame").sprite = Art("icon_border_pvp_season20_rank_3", Home);
            return avatar;
        }

        private static Sprite CommonArt(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Common + "Textures/" + name + ".png");
    }
}
