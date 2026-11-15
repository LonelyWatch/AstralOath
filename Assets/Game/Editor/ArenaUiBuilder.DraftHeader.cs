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
        private const float DraftStageWidth = 998f;
        private const float DraftStageHeight = 720f;
        private const float DraftUiScale = 1.5f;

        private static void BuildDraftHeader(RectTransform stage, List<(string, Object)> refs)
        {
            // pvplive_ready.csb n_my_info / n_enemy_info. Use the move anchor for
            // a profile without an arena rank. Keeping it frees the unsupported rank position.
            refs.Add(("host", Text(stage, "My player", null, 26 * .82f * DraftUiScale,
                107 / DraftStageWidth, 28 / DraftStageHeight, 299 / DraftStageWidth, 38 / DraftStageHeight)));
            refs.Add(("guest", Text(stage, "Opponent player", null, 26 * .82f * DraftUiScale,
                592 / DraftStageWidth, 28 / DraftStageHeight, 301 / DraftStageWidth, 38 / DraftStageHeight,
                alignment: TextAlignmentOptions.MidlineRight)));
            var lineColor = new Color32(146, 109, 62, 127);
            NativeRule(stage, "Blue header", 55 / DraftStageWidth, 63 / DraftStageHeight,
                360 / DraftStageWidth, 10 / DraftStageHeight, lineColor, "_bar_2");
            NativeRule(stage, "Red header", 583 / DraftStageWidth, 63 / DraftStageHeight,
                360 / DraftStageWidth, 10 / DraftStageHeight, lineColor, "_bar_2");
            refs.Add(("hostAvatar", DraftHeaderAvatar(stage, "Local profile", 59)));
            refs.Add(("guestAvatar", DraftHeaderAvatar(stage, "Remote profile", 939)));
            var shadow = Resources.Load<Material>("AstralOathPlayerNameShadow");
            if (shadow)
                foreach (var (key, value) in refs)
                    if (key is "host" or "guest") ((GameText)value).fontSharedMaterial = shadow;
            DraftHeaderPing(stage, refs, "host", 123, false);
            DraftHeaderPing(stage, refs, "guest", 877, true);
            refs.Add(("title", Text(stage, "Draft phase", null, 52 * .42f * DraftUiScale,
                403 / DraftStageWidth, 16 / DraftStageHeight, 190 / DraftStageWidth, 40 / DraftStageHeight,
                Mist, TextAlignmentOptions.Center)));
            refs.Add(("timer", DraftTimer(stage, "Draft countdown", 449 / DraftStageWidth,
                47 / DraftStageHeight, 96 / DraftStageWidth, 65 / DraftStageHeight, refs, "timer")));
        }

        private static BindingMap DraftHeaderAvatar(RectTransform stage, string name, float x)
        {
            const float frameSize = 126 * .70f * .85f;
            const float faceSize = 112 * .69f * .85f;
            const float inset = (1 - faceSize / frameSize) * .5f;
            const float shift = .85f / frameSize;
            var avatar = Avatar(stage, name, (x - frameSize / 2) / DraftStageWidth,
                (66 - frameSize / 2) / DraftStageHeight, frameSize / DraftStageWidth, frameSize / DraftStageHeight);
            var frame = avatar.Require<Image>("frame").rectTransform;
            Place(frame, shift, shift, 1, 1);
            var clip = (RectTransform)avatar.Require<Image>("portrait").transform.parent;
            Place(clip, inset, inset, 1 - 2 * inset, 1 - 2 * inset);
            var plate = (RectTransform)avatar.transform.Find("Plate");
            Place(plate, inset, inset, 1 - 2 * inset, 1 - 2 * inset);
            return avatar;
        }

        private static void DraftHeaderPing(RectTransform stage, List<(string, Object)> refs,
            string side, float x, bool mirrored)
        {
            var root = Area(stage, side + " connection quality", (x - 17) / DraftStageWidth,
                70 / DraftStageHeight, 34 / DraftStageWidth, 30 / DraftStageHeight);
            Picture(root, "Connection background", DraftSprite("Header/cm_ping_bg"), 0, 0, 1, 1, true);
            if (mirrored) root.localScale = new Vector3(-1, 1, 1);
            refs.Add((side + "Ping", root.gameObject));
            for (int level = 1; level <= 3; level++)
            {
                var levelImage = Picture(root, "Quality " + level, DraftSprite("Header/cm_ping_" + level),
                    2.5f / 34, 2f / 30, 29f / 34, 26f / 30, true);
                levelImage.enabled = false;
                refs.Add((side + "Ping" + level, levelImage));
            }
            root.gameObject.SetActive(false);
        }

        private static void BuildDraftRevealed(RectTransform stage, List<(string, Object)> refs)
        {
            // The CSB footer divider starts at (496,68) in stage space and extends down 56 px.
            var footer = Area(stage, "Revealed preban footer", 0, 0, 1, 1);
            refs.Add(("preHeader", footer.gameObject));
            var caption = Area(footer, "Centered preban title", .5f, 603 / DraftStageHeight, 0, 45 / DraftStageHeight);
            var layout = caption.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fit = caption.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var icon = Picture(caption, "Preban symbol", DraftSprite("cm_icon_st_banned"), 0, 0, 0, 0, true);
            var size = icon.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = 35 * DraftUiScale;
            size.preferredHeight = 30 * DraftUiScale;
            var label = Text(caption, "Caption", "arena.draft.prebanned", 26 * .84f * DraftUiScale,
                0, 0, 0, 0, new Color32(255, 215, 212, 255), TextAlignmentOptions.Center);
            label.overflowMode = TextOverflowModes.Overflow;

            var divider = Picture(footer, "Preban side divider", Art("_bar_2"), 468 / DraftStageWidth,
                674 / DraftStageHeight, 56 / DraftStageWidth, 10 / DraftStageHeight);
            divider.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
            divider.color = new Color32(146, 109, 62, 153);
            divider.type = Image.Type.Sliced;
            divider.pixelsPerUnitMultiplier = 2f / 3f;

            const float chipSize = 126 * .70f * .63f;
            foreach (bool opponent in new[] { false, true })
            {
                float left = opponent ? 534 - chipSize * .5f : 300;
                float right = opponent ? 690 : 459 + chipSize * .5f;
                var row = Area(footer, opponent ? "Opponent prebans" : "Local prebans",
                    left / DraftStageWidth, (679 - chipSize / 2) / DraftStageHeight,
                    (right - left) / DraftStageWidth, chipSize / DraftStageHeight);
                var grid = row.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = Vector2.one * (chipSize * DraftUiScale);
                grid.spacing = new Vector2((55 - chipSize) * DraftUiScale, 0);
                grid.childAlignment = opponent ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                grid.constraintCount = 1;
                refs.Add((opponent ? "revealedEnemy" : "revealedOwn", row.gameObject));
            }
        }
    }
}
