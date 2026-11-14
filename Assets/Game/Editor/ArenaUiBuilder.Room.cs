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
            var card = Area(root, "Create dialog", .244f, .042f, .512f, .888f);
            Paint(card, Art("renew_popup_bg_alpha"), Color.white, true, true);
            Text(card, "Create title", "arena.create", 42, .050f, .022f, .88f, .07f);
            Rule(card, "Header divider", .05f, .107f, .90f, .002f);
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
            Rule(card, "Privacy separator", .526f, .293f, .002f, .085f);
            Text(card, "Password hint", "arena.password_hint", 23, .55f, .287f, .17f, .087f, Mist, wrap: true);
            var password = Input(card, "Password", "arena.enter_text", 4, .735f, .291f, .212f, .078f, true);

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
                size.preferredWidth = 297;
                var button = Button(option, Art("pvplive_mod_" + index.ToString("00")), Color.white);
                var selected = Picture(option, "Selection", Art("_select"), 0, 0, 1, 1);
                if (selected.sprite && selected.sprite.border != Vector4.zero) selected.type = Image.Type.Sliced;
                if (!selected.sprite)
                {
                    selected.enabled = true;
                    selected.color = new Color(1f, .86f, .34f, .12f);
                    var edge = selected.gameObject.AddComponent<Outline>();
                    edge.effectColor = new Color(1f, .9f, .48f, .9f);
                    edge.effectDistance = new Vector2(4, 4);
                }
                Text(option, "Rule label", "arena.rule." + id, 30, .07f, .03f, .88f, .31f, wrap: true);
                keys.Add(("rule_" + id, button));
                keys.Add(("selection_" + id, selected));
            }
            Rule(card, "Footer separator", .03f, .859f, .94f, .002f);
            var status = Text(card, "Create status", null, 22, .05f, .946f, .9f, .05f, new Color32(255, 191, 158, 255), TextAlignmentOptions.Center);
            var cancel = CommonButton(card, "CancelButton", "arena.cancel", .246f, .88f, .24f, .080f);
            var confirm = CommonButton(card, "ConfirmButton", "arena.confirm", .503f, .88f, .24f, .080f);
            Text(root, "Dismiss hint", "arena.touch_close", 24, .39f, .938f, .22f, .044f, Color.white, TextAlignmentOptions.Center);
            keys.Add(("dismiss", dismiss)); keys.Add(("status", status)); keys.Add(("cancel", cancel)); keys.Add(("confirm", confirm));
            Bind(root, keys.ToArray());
            return root.gameObject;
        }

        private static GameObject BuildRoom()
        {
            var root = Root("ArenaRoom");
            Paint(Area(root, "Backdrop", 0, 0, 1, 1), null, Deep);
            Cover(root, "Courtyard", Art("room_courtyard"));
            Paint(Area(root, "Scene tint", 0, 0, 1, 1), null, Black(.39f));
            var left = Area(root, "Room contents", 0, 0, .604f, 1);
            var leave = Action(left, "Leave room", "arena.leave", 25, .025f, .02f, .115f, .063f, out _, Art("_box_r_op80"), Color.white);
            var name = Text(left, "Room title", null, 42, .158f, .013f, .78f, .076f);
            Rule(left, "Top rule", .03f, .093f, .946f, .002f);
            Text(left, "Battle rule caption", "arena.rules", 27, .37f, .175f, .27f, .042f, Gold, TextAlignmentOptions.Center);
            Rule(left, "Rule accent", .39f, .231f, .22f, .002f);
            var rule = Text(left, "Battle rule", null, 30, .37f, .236f, .27f, .045f, Color.white, TextAlignmentOptions.Center);
            var host = Avatar(left, "Host avatar", .143f, .268f, .19f, .205f);
            var guest = Avatar(left, "Guest avatar", .654f, .268f, .19f, .205f);
            var vacant = Picture(left, "Vacant guest slot", Art("game_hud_target_c2"), .632f, .423f, .234f, .133f);
            var hostName = Text(left, "Host name", null, 32, .054f, .463f, .38f, .079f, Green, TextAlignmentOptions.Center);
            var guestName = Text(left, "Guest name", null, 32, .558f, .463f, .38f, .079f, Mist, TextAlignmentOptions.Center);
            Picture(left, "Versus", Art("_battle_pvp_icon_vs"), .434f, .324f, .139f, .165f, true);
            var start = CommonButton(left, "ConfirmButton", "arena.start", .406f, .551f, .19f, .067f);
            start.interactable = false;
            Rule(left, "Members divider", .032f, .640f, .946f, .002f);
            var members = Scroll(left, "Member list", .024f, .655f, .946f, .310f);
            var memberRow = Area(left, "Member template", 0, 0, 1, 0);
            memberRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 82;
            var memberAvatar = Avatar(memberRow, "Member avatar", .014f, .02f, .088f, .94f);
            var memberName = Text(memberRow, "Member name", null, 26, .115f, .05f, .82f, .88f, Green);
            Bind(memberRow, ("avatar", memberAvatar), ("name", memberName));
            memberRow.gameObject.SetActive(false);

            var chat = Area(root, "Arena chat", .605f, 0, .395f, 1);
            Paint(chat, null, Black(.94f));
            Rule(root, "Chat separator", .602f, 0, .003f, 1);
            Text(chat, "Chat title", "arena.title", 39, .042f, .012f, .91f, .066f);
            var channel = Area(chat, "Arena channel", .585f, .083f, .35f, .064f);
            Paint(channel, Art("_tap_select_p"), Color.white, sliced: true);
            Picture(channel, "Arena symbol", Art("icon_menu_arena"), .12f, .18f, .19f, .64f, true);
            Text(channel, "Channel name", "arena.channel", 30, .29f, 0, .62f, 1, Color.white, TextAlignmentOptions.Center);
            Rule(chat, "Channel separator", .03f, .147f, .94f, .002f);
            Picture(chat, "Participants icon", Art("icon_menu_member"), .05f, .162f, .061f, .059f, true);
            Text(chat, "Member count caption", "arena.participants", 25, .119f, .157f, .31f, .037f, Gold);
            var count = Text(chat, "Member count", null, 34, .119f, .187f, .55f, .041f);
            Rule(chat, "Participant separator", .03f, .233f, .94f, .002f);
            var history = Scroll(chat, "Message history", .04f, .255f, .91f, .59f);
            var chatRow = Text(chat, "Chat message template", null, 25, 0, 0, 1, 0, wrap: true);
            chatRow.overflowMode = TextOverflowModes.Overflow;
            chatRow.gameObject.AddComponent<LayoutElement>().minHeight = 32;
            chatRow.gameObject.SetActive(false);
            var status = Text(chat, "Chat status", null, 22, .05f, .848f, .9f, .030f, Mist);
            Rule(chat, "Message divider", .03f, .880f, .94f, .002f);
            var message = Input(chat, "Message", "arena.message", 200, .048f, .899f, .642f, .077f);
            var send = CommonButton(chat, "ConfirmButton", "arena.send", .707f, .899f, .26f, .077f);
            Bind(root, ("leave", leave), ("send", send), ("name", name), ("rule", rule), ("count", count),
                ("hostName", hostName), ("guestName", guestName), ("status", status), ("message", message),
                ("host", host), ("guest", guest), ("vacant", vacant), ("members", members), ("memberRow", memberRow.gameObject),
                ("messages", history), ("chatRow", chatRow));
            return root.gameObject;
        }

        private static Sprite CommonArt(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Common + "Textures/" + name + ".png");
    }
}
