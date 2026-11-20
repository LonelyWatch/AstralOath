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
        private static RectTransform BuildDraftRosterRow(RectTransform parent)
        {
            var root = Area(parent, "Roster hero template", 0, 0, 1, 1);
            var button = Button(root, null, Color.clear);
            button.BlockedFeedback = true;
            var plate = RosterPicture(root, "Normal plate", DraftSprite("hero_bg_normal"), 0, 0, 361, 112);
            var selection = RosterPicture(root, "Selected plate", DraftSprite("hero_bg_selected"), 0, 0, 361, 112);
            selection.enabled = false;
            button.targetGraphic = plate;
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;

            // unit_bar.csb lays the portrait on the right edge of the slanted 361 x 112 card.
            // The image's left edge moves with the source width, and the imported face stays 89 px tall.
            var crop = RosterPicture(root, "Native hero clip", DraftSprite("hero_bg_mask"), 0, 0, 361, 112);
            crop.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = RosterPicture(crop.rectTransform, "Hero", null, 125, 10, 222, 89, true);
            var role = RosterPicture(root, "Class", null, 42, 23, 32, 32, true);
            var title = RosterName(root);
            var element = RosterPicture(root, "Element", null, 70, 67, 34, 34, true);
            var shade = RosterPicture(root, "State shade", DraftSprite("hero_bg_normal"), 0, 0, 361, 112);
            var picked = RosterPicture(root, "Picked icon", DraftSprite("Roster/cm_icon_check_b"), 45, 24, 30, 22, true);
            picked.color = new Color32(140, 205, 94, 255);
            var banned = RosterPicture(root, "Prebanned icon", DraftSprite("cm_icon_st_banned"), 41, 20, 35, 30, true);
            var state = Text(root, "Hero status", null, 24, 82f / 361, 20f / 112, 166f / 361, 36f / 112);
            state.fontStyle = FontStyles.Bold;
            var keys = new List<(string, Object)>
            {
                ("button", button), ("portrait", portrait), ("name", title), ("selection", selection),
                ("role", role), ("element", element), ("dim", shade), ("state", state),
                ("pickedIcon", picked), ("bannedIcon", banned)
            };
            for (int digit = 0; digit < 3; digit++)
                keys.Add(("speed" + digit, RosterPicture(root, "Speed digit " + digit, null, 22 + 16 * digit, 70, 18, 26, true)));
            for (int star = 0; star < 6; star++)
                keys.Add(("star" + star, RosterPicture(root, "Awakened star " + star, DraftSprite("TeamBadges/cm_icon_star_j"),
                    103 + 10 * star, 69, 26.1f, 25.23f, true)));
            // These labels and markers are normally hidden. State graphics render over the profile.
            shade.transform.SetAsLastSibling();
            picked.transform.SetAsLastSibling();
            banned.transform.SetAsLastSibling();
            state.transform.SetAsLastSibling();
            foreach (var node in new Component[] { shade, picked, banned, state }) node.gameObject.SetActive(false);
            // Choice also backs circular preban and formation cards, which use these optional layers.
            keys.Add(("filled", RosterPicture(root, "Unused filled plate", null, 0, 0, 0, 0)));
            keys.Add(("guard", RosterPicture(root, "Unused guard", null, 0, 0, 0, 0)));
            keys.Add(("mark", RosterPicture(root, "Unused banned stamp", null, 0, 0, 0, 0)));
            keys.Add(("stars", Text(root, "Unused grade caption", null, 1, 0, 0, 0, 0, Color.clear)));
            Bind(root, keys.ToArray());
            root.gameObject.SetActive(false);
            return root;
        }

        private static GameText RosterName(RectTransform parent)
        {
            // unit_bar.csb places its regular 24 pt name at 0.85 scale, centred at (78, 37).
            var label = Text(parent, "Hero name", null, 24f * .85f,
                78f / 361f, 22f / 112f, 218f / 361f, 30f / 112f);
            label.fontSharedMaterial = Resources.Load<Material>("AstralOathPlayerNameShadow");
            label.richText = false;
            return label;
        }

        private static Image RosterPicture(RectTransform parent, string name, Sprite sprite,
            float x, float y, float width, float height, bool preserveAspect = false) =>
            Picture(parent, name, sprite, x / 361, y / 112, width / 361, height / 112, preserveAspect);
    }
}
