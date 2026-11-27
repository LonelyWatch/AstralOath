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
        // Source: item_tooltip_simple.csb/Layer/frame and the 11/22 mail recording, 25/96/99s.
        // Coordinates are fixed Canvas units (original 720-design units x 1.5), not viewport fractions.
        private static (string Key, Object Target)[] BuildMailTooltip(RectTransform parent)
        {
            var refs = new List<(string Key, Object Target)>();
            var dim = parent.GetComponent<Image>();
            if (dim) { dim.color = Color.clear; dim.raycastTarget = false; }
            refs.Add(("tooltipClose", Button(Area(parent, "Dismiss description", 0, 0, 1, 1), null, Color.clear)));

            var card = Area(parent, "Attachment description", 0, 0, 0, 0);
            card.anchorMin = card.anchorMax = new Vector2(.78f, .84f);
            card.pivot = Vector2.one;
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(480, 375);
            refs.Add(("tooltipPanel", card));
            // The source includes its soft shadow and a 31-35px inset before the visible frame.
            // Keep that padding outside the authored content bounds, otherwise the icon sits over the rim.
            var frameBounds=Area(card,"Original frame and shadow",0,0,1,1);
            frameBounds.offsetMin=new Vector2(-46.5f,-54);
            frameBounds.offsetMax=new Vector2(52.5f,46.5f);
            var frame = Paint(frameBounds, MailArt("MailItemTooltipFrame"), Color.white, true, true);
            frame.pixelsPerUnitMultiplier = 1f / 1.5f;

            var item = Fixed("Item", 27, 27, 126, 126);
            var itemBackground = Paint(item, MailArt("_itembg_stuff_1"), Color.white);
            refs.Add(("tooltipItemBackground", itemBackground));
            var icon = Picture(item, "Icon", MailArt("to_stamina"), 0, 0, 1, 1, true);
            refs.Add(("tooltipIcon", icon));
            Caption("tooltipKind", 174, 29, 276, 35, 27, Mist);
            Caption("tooltipName", 174, 65, 276, 85, 30, Color.white);
            var body = Caption("tooltipBody", 27, 166, 426, 104, 26, new Color32(150, 165, 184, 255));
            body.overflowMode = TextOverflowModes.Truncate;

            var owned = Fixed("Owned amount", 27, 282, 426, 61);
            refs.Add(("tooltipOwnedFrame", owned.gameObject));
            var rule = Picture(owned, "Original divider", MailArt("_bar_2"), 0, 0, 1, .10f);
            rule.type = Image.Type.Sliced;
            rule.pixelsPerUnitMultiplier = 1f / 1.5f;
            rule.color = new Color(1, 1, 1, .20f);
            var amount = Text(owned, "Owned", null, 27, 0, .25f, 1, .65f,
                new Color32(119, 203, 0, 255), TextAlignmentOptions.Center);
            amount.richText = false;
            refs.Add(("tooltipOwned", amount));
            owned.gameObject.SetActive(false);
            return refs.ToArray();

            RectTransform Fixed(string name, float x, float y, float width, float height)
            {
                var rect = Area(card, name, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(x, -y);
                rect.sizeDelta = new Vector2(width, height);
                return rect;
            }
            GameText Caption(string key, float x, float y, float width, float height, float size, Color color)
            {
                var rect = Fixed(key, x, y, width, height);
                var label = Text(rect, "Text", null, size, 0, 0, 1, 1, color, TextAlignmentOptions.TopLeft, true);
                label.richText = false;
                label.fontStyle = FontStyles.Normal;
                refs.Add((key, label));
                return label;
            }
        }
    }
}
