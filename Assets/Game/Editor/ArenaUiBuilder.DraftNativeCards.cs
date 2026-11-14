using System.Collections.Generic;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // pvplive_ready.csb slot owners use one native edge as their local origin.
        private static readonly int[] NativeSlotWidths = { 291, 226, 196, 196, 226 };
        private static readonly int[] NativeRoleXs = { 240, 186, 156, 147, 169 };
        private static readonly int[] NativeBlueLevelXs = { 200, 154, 128, 128, 154 };
        private static readonly int[] NativeRedLevelXs = { -249, -203, -180, -179, -204 };
        private static readonly int[] NativeBlueStarXs = { 140, 102, 78, 90, 90 };
        private static readonly int[] NativeRedStarXs = { -237, -200, -177, -187, -218 };
        private static readonly int[] NativeBlueBanXs = { 210, 169, 150, 150, 172 };
        private static readonly int[] NativeRedBanXs = { -204, -161, -144, -144, -169 };
        private static readonly int[] NativeSlotEffectXs = { 146, 113, 98, 98, 113 };
        private const float NativeSlotHeight = 85f;

        private static BindingMap NativeTeamCard(RectTransform root, int index, bool enemy)
        {
            float width = NativeSlotWidths[index];
            float trayEdge = enemy ? width : 0;
            float direction = enemy ? -1 : 1;
            string slot = "pvplive_slot" + (index + 1);
            Sprite empty = DraftSprite(slot);
            Sprite occupied = DraftSprite(slot + (enemy ? "_red" : "_blue"));
            var button = Button(root, null, Color.clear);
            button.BlockedFeedback = true;
            var visual = TeamCardPicture(root, "Native empty slot", empty, width, new Vector2(width * .5f, 42.5f),
                new Vector2(width, 85), new Vector2(.5f, .5f));
            var filled = TeamCardPicture(root, "Native occupied sprite", occupied, width, new Vector2(width * .5f, 42.5f),
                new Vector2(width, 84), new Vector2(.5f, .5f));
            if (!enemy)
            {
                var mirror = new Vector3(-1, 1, 1);
                visual.rectTransform.localScale = mirror;
                filled.rectTransform.localScale = mirror;
            }
            filled.enabled = false;
            button.targetGraphic = visual;
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;

            var clip = TeamCardPicture(root, "Native card silhouette", occupied, width, new Vector2(width * .5f, 42.5f),
                new Vector2(width, 84), new Vector2(.5f, .5f));
            if (!enemy) clip.rectTransform.localScale = new Vector3(-1, 1, 1);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            // The allies mirror the silhouette and face together. A right-edge pivot in mask
            // space becomes the native left-edge pivot on screen after that mirror.
            var portrait = TeamCardPicture(clip.rectTransform, "Native hero face", null, width,
                new Vector2(width + 1, 42.5f), new Vector2(222 * .95f, 89 * .95f),
                new Vector2(1, .5f), true);
            portrait.enabled = false;

            float roleX = trayEdge + direction * NativeRoleXs[index];
            var role = TeamCardPicture(root, "Native class", null, width, new Vector2(roleX, 42.5f + 23),
                new Vector2(46 * .65f, 46 * .65f), new Vector2(enemy ? 1 : 0, .5f), true);
            var element = TeamCardPicture(root, "Native element", null, width,
                new Vector2(roleX - direction * 30, 42.5f + 38), new Vector2(34 * .88f, 34 * .88f),
                new Vector2(enemy ? 1 : 0, 1), true);

            float levelX = trayEdge + (enemy ? NativeRedLevelXs[index] : NativeBlueLevelXs[index]);
            var level = Area(root, "Level", 0, 0, 1, 1);
            TeamCardPicture(level, "Level plate", DraftSprite("TeamBadges/hero_lv"), width,
                new Vector2(levelX, 30.5f), new Vector2(43 * .65f, 30 * .65f), Vector2.zero);
            var tens = TeamCardPicture(level, "Level tens", DraftSprite("TeamBadges/hero_lv_6"), width,
                new Vector2(levelX + 43 * .65f, 30.5f), new Vector2(21 * .65f, 30 * .65f), Vector2.zero);
            var units = TeamCardPicture(level, "Level units", DraftSprite("TeamBadges/hero_lv_0"), width,
                new Vector2(levelX + 60 * .65f, 30.5f), new Vector2(21 * .65f, 30 * .65f), Vector2.zero);
            level.gameObject.SetActive(false);

            var starRoot = Area(root, "Native stars", 0, 0, 1, 1);
            float starX = trayEdge + (enemy ? NativeRedStarXs[index] : NativeBlueStarXs[index]);
            var starImages = new Image[6];
            for (int star = 0; star < starImages.Length; star++)
            {
                // Current playable catalog is fully awakened; do not copy the CSB placeholder grade.
                string face = "cm_icon_star_j";
                starImages[star] = TeamCardPicture(starRoot, "Star " + (star + 1), DraftSprite("TeamBadges/" + face),
                    width, new Vector2(starX + (3 + star * 19) * .77f, 42.5f - 40 + 4 * .77f),
                    new Vector2(30 * .77f, 29 * .77f), Vector2.zero);
            }
            starRoot.gameObject.SetActive(false);

            var dim = TeamCardPicture(root, "Banned shade", empty, width, new Vector2(width / 2, 42.5f),
                new Vector2(width, 85), new Vector2(.5f, .5f));
            if (!enemy) dim.rectTransform.localScale = new Vector3(-1, 1, 1);
            dim.gameObject.SetActive(false);

            Sprite glow = DraftSprite(slot + (enemy ? "_glow_red" : "_glow_blue"));
            var selection = TeamCardPicture(root, "Native selection glow", glow, width,
                new Vector2(trayEdge + direction * NativeSlotEffectXs[index], 42.5f + (index == 1 ? -1 : 0)),
                glow.rect.size, new Vector2(.5f, .5f));
            var guard = TeamCardPicture(root, "Native safeguard", DraftSprite("_cm_safeguard"), width,
                new Vector2(width / 2, 53.5f), new Vector2(62 * .96f, 66 * .96f), new Vector2(.5f, .5f));
            var mark = TeamCardPicture(root, "Native banned stamp", DraftSprite("_cm_banned_res"), width,
                new Vector2(trayEdge + (enemy ? NativeRedBanXs[index] : NativeBlueBanXs[index]), 39.5f),
                new Vector2(341 * .45f * .78f, 284 * .45f * .78f), new Vector2(.5f, .5f));
            selection.enabled = guard.enabled = mark.enabled = false;
            Image guardPlate = null;
            if (index == 2)
            {
                guardPlate = TeamCardPicture(root, "Native safeguard plate", DraftSprite("pvplive_slot3_safeguard"),
                    width, new Vector2(width / 2, 42.5f), new Vector2(197, 85), new Vector2(.5f, .5f));
                if (!enemy) guardPlate.rectTransform.localScale = new Vector3(-1, 1, 1);
                guardPlate.transform.SetSiblingIndex(clip.transform.GetSiblingIndex());
                guardPlate.enabled = false;
            }

            var name = Text(root, "Legacy name", null, 1, 0, 0, 0, 0, Color.clear);
            var stars = Text(root, "Legacy stars", null, 1, 0, 0, 0, 0, Color.clear);
            var state = Text(root, "Slot status", null, 23, .14f, .32f, .72f, .33f, alignment: TMPro.TextAlignmentOptions.Center);
            var keys = new List<(string, Object)>
            {
                ("button", button), ("filled", filled), ("selection", selection), ("portrait", portrait),
                ("guard", guard), ("mark", mark), ("element", element), ("role", role), ("name", name),
                ("stars", stars), ("state", state), ("dim", dim), ("levelRoot", level.gameObject),
                ("levelTens", tens), ("levelUnits", units), ("starRoot", starRoot.gameObject)
            };
            if (guardPlate) keys.Add(("guardPlate", guardPlate));
            for (int star = 0; star < starImages.Length; star++)
                keys.Add(("star" + star, starImages[star]));
            Bind(root, keys.ToArray());
            return root.GetComponent<BindingMap>();
        }

        // Normalize authored bottom-left coordinates so a 291×85 or other native tray size
        // keeps the exact CSB placement when the enclosing stage is fitted to the design canvas.
        private static Image TeamCardPicture(RectTransform parent, string name, Sprite sprite, float nativeWidth,
            Vector2 position, Vector2 size, Vector2 pivot, bool preserveAspect = false)
        {
            float left = position.x - size.x * pivot.x;
            float bottom = position.y - size.y * pivot.y;
            var result = Picture(parent, name, sprite, left / nativeWidth,
                1 - (bottom + size.y) / NativeSlotHeight, size.x / nativeWidth, size.y / NativeSlotHeight,
                preserveAspect);
            result.rectTransform.pivot = pivot;
            return result;
        }
    }
}
