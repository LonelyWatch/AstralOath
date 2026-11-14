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
            Paint(Area(root, "Modal dim base", 0, 0, 1, 1), null, Black(63f / 255f));
            var dismiss = Button(Area(root, "Backdrop", 0, 0, 1, 1), Art("_dim"), Color.white);
            dismiss.transition = Selectable.Transition.None;
            dismiss.FeedbackSound = GameUiFeedback.Sound.Normal;
            var frame = Area(Area(root, "Create dialog bounds", .03f, .040f, .94f, .944f),
                "Create dialog frame", 0, 0, 1, 1);
            var fit = frame.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            const float cardHeight = .942f;
            fit.aspectRatio = 764f / 645f * cardHeight;
            var card = Area(frame, "Create dialog", 0, 0, 1, cardHeight);
            Paint(card, Art("arena_create_dialog_bg"), Color.white, true, true);
            Text(card, "Create title", "arena.create", 39, .057f, .022f, .88f, .07f).fontStyle = FontStyles.Bold;
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
            var passwordOptions = Area(card, "Password options", .517f, .287f, .43f, .101f);
            var passwordState = passwordOptions.gameObject.AddComponent<CanvasGroup>();
            passwordState.alpha = .30f;
            passwordState.interactable = false;
            passwordState.blocksRaycasts = false;
            VerticalRule(passwordOptions, "Privacy separator", 0, .020f, .042f, .980f, Mist * .7f);
            Text(passwordOptions, "Password hint", "arena.password_hint", 23, .077f, 0, .395f, .861f, Mist, wrap: true);
            var password = Input(passwordOptions, "Password", "arena.enter_text", 4, .507f, .040f, .493f, .772f, true);
            var passwordColors = password.colors;
            passwordColors.disabledColor = passwordColors.normalColor;
            password.colors = passwordColors;
            password.interactable = false;
            foreach (var field in new[] { name, password })
                ((Image)field.targetGraphic).sprite = Art("_box_w4");

            Text(card, "Chat permission", "arena.chat_setting", 28, .05f, .410f, .19f, .095f, Mist, wrap: true);
            var allow = ChatChoice(card, "Allow", "arena.allow", .261f, new Color32(61, 119, 8, 255),
                out var allowCaption, out var allowSelection);
            var deny = ChatChoice(card, "Deny", "arena.deny", .418f, new Color32(162, 27, 27, 255),
                out var denyCaption, out var denySelection);
            denySelection.enabled = false;
            Text(card, "Rules label", "arena.rules", 30, .05f, .638f, .19f, .070f, Mist);
            var rules = Scroll(card, "Rules", .256f, .510f, .719f, .345f, horizontal: true);
            var keys = new System.Collections.Generic.List<(string, Object)>
            {
                ("name", name), ("password", password), ("passwordState", passwordState), ("locked", toggle),
                ("allow", allow), ("deny", deny), ("allowCaption", allowCaption), ("denyCaption", denyCaption),
                ("allowSelection", allowSelection), ("denySelection", denySelection), ("rules", rules)
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
            Paint(Area(card, "Footer separator", .020f, .859f, .960f, .00125f), null, new Color32(81, 117, 145, 180));
            var status = Text(card, "Create status", null, 22, .031f, .956f, .938f, .035f,
                new Color32(255, 191, 158, 255), TextAlignmentOptions.Center);
            var cancel = CommonButton(card, "CancelButton", "arena.cancel", .246f, .871f, .242f, .098f);
            var confirm = CommonButton(card, "ConfirmButton", "arena.confirm", .502f, .871f, .242f, .098f);
            var dismissHint = Area(frame, "Dismiss hint", .432f, .950f, .136f, .048f);
            Picture(dismissHint, "Dismiss icon", Art("cm_icon_etcclose"), 0, .27f, .17f, .49f, true).color
                = new Color32(135, 135, 135, 255);
            Text(dismissHint, "Dismiss caption", "arena.touch_close", 21, .225f, 0, .775f, 1,
                new Color32(176, 176, 176, 255));
            keys.Add(("dismiss", dismiss)); keys.Add(("status", status)); keys.Add(("cancel", cancel)); keys.Add(("confirm", confirm));
            Bind(root, keys.ToArray());
            return root.gameObject;
        }

        private static GameButton ChatChoice(RectTransform card, string name, string key, float x, Color selected,
            out GameText caption, out Image selection)
        {
            var button = Action(card, name, key, 26, x, .418f, .147f, .069f, out caption,
                CommonArt("_btn_small_black"), Color.white);
            PressSmallBlack(button);
            selection = Paint(Area((RectTransform)button.transform, "Selected", 0, 0, 1, 1),
                Art("_btn_small_white"), selected, sliced: true);
            selection.rectTransform.SetAsFirstSibling();
            PressDecorations(button, (selection, CommonArt("_btn_small_black_p")));
            return button;
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
            PressSmallBlack(leave);
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
            var sticker = Picture(chatRow, "Sticker", null, .13f, 0, 0, 0, true);
            sticker.rectTransform.pivot = new Vector2(0, 1);
            sticker.rectTransform.anchoredPosition = new Vector2(0, -35);
            sticker.rectTransform.sizeDelta = new Vector2(142, 114);
            Bind(chatRow, ("avatar", senderAvatar), ("sender", sender), ("age", age), ("body", body), ("sticker", sticker));
            chatRow.gameObject.SetActive(false);
            var composer = Area(chat, "Composer", 0, .853f, 1, .147f);
            var status = Text(composer, "Chat status", null, 22, .05f, 0, .9f, .184f, Mist);
            NativeRule(composer, "Message divider", .03f, .134f, .94f, .143f, Gold, "_bar_2");
            var emojiArea = Area(composer, "Emoji", .032f, .325f, .089f, .538f);
            var emoji = Button(emojiArea, Art("_box_r_op80"), Color.white);
            PressSmallBlack(emoji);
            Picture(emojiArea, "Emoji icon", Art("icon_menu_emoji"), .16f, .16f, .68f, .68f, true);
            var message = Input(composer, "Message", "arena.message", 200, .132f, .325f, .558f, .538f);
            var send = CommonButton(composer, "ConfirmButton", "arena.send", .707f, .325f, .26f, .538f);
            FollowKeyboard(composer, message);
            var emojiPicker = BuildEmojiPicker(root);
            Bind(root, ("leave", leave), ("send", send), ("emoji", emoji), ("emojiPicker", emojiPicker), ("name", name), ("rule", rule), ("count", count),
                ("hostName", hostName), ("guestName", guestName), ("status", status), ("message", message),
                ("host", host), ("guest", guest), ("vacant", vacant), ("emptyInfo", emptyInfo.gameObject), ("members", members), ("memberRow", memberRow.gameObject),
                ("messages", history), ("chatRow", chatRow.gameObject));
            return root.gameObject;
        }

        [System.Serializable] private sealed class StickerRow { public string id; }
        [System.Serializable] private sealed class StickerCatalog { public StickerRow[] stickers; }

        private static GameObject BuildEmojiPicker(RectTransform root)
        {
            var picker = Area(root, "Emoji picker", 0, 0, 1, 1);
            var dismiss = Button(Area(picker, "Dismiss", 0, 0, 1, 1), null, Color.clear);
            dismiss.FeedbackSound = GameUiFeedback.Sound.Silent;
            var panel = Area(picker, "Panel", .338f, .469f, .258f, .513f);
            Paint(panel, Art("emoji_picker_frame"), Color.white, true, true);
            var choices = Scroll(panel, "Emojis", .025f, .037f, .919f, .951f);
            var track = Paint(Area(panel, "Scrollbar", .936f, .035f, .012f, .914f), null, Color.clear, true);
            track.canvasRenderer.cullTransparentMesh = false;
            var trackInner = Area(track.rectTransform, "Sliding area", 0, .015f, 1, .97f);
            var thumb = Paint(Area(trackInner, "Thumb", .12f, 0, .76f, 1), null, new Color32(77, 76, 72, 135));
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = thumb;
            scrollbar.handleRect = thumb.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            choices.verticalScrollbar = scrollbar;
            choices.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            choices.content.gameObject.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(4, 4, 0, 0);
            choices.content.gameObject.GetComponent<VerticalLayoutGroup>().spacing = 4;
            const string assets = Arena + "Res/Textures/ChatStickers/";
            var catalogue = JsonUtility.FromJson<StickerCatalog>(Load<TextAsset>(assets + "source-manifest.json").text);
            RectTransform row = null;
            for (int index = 0; index < catalogue.stickers.Length; index++)
            {
                int column = index % 3;
                if (column == 0)
                {
                    row = Area(choices.content, "Emoji row", 0, 0, 1, 0);
                    row.gameObject.AddComponent<LayoutElement>().preferredHeight = 146;
                }
                string id = catalogue.stickers[index].id;
                var tile = Area(row, id, column / 3f, 0, 1f / 3f, 1);
                Button(tile, null, Color.clear);
                var art = Picture(tile, "Emoji", Load<Sprite>(assets + id + ".png"), .060f, .030f, .88f, .93f, true);
                Bind(tile, ("art", art));
            }
            Bind(picker, ("dismiss", dismiss), ("choices", choices));
            picker.gameObject.SetActive(false);
            return picker.gameObject;
        }

        private static BindingMap ArenaAvatar(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var avatar = Avatar(parent, name, x, y, w, h);
            avatar.Require<Image>("frame").sprite = Art("arena_avatar_frame");
            avatar.Require<Image>("portrait").sprite = Art("c1001_avatar_room");
            return avatar;
        }

        private static Sprite CommonArt(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Common + "Textures/" + name + ".png");
    }
}
