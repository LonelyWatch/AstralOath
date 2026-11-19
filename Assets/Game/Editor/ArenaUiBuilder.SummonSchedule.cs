using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildSummonSchedule(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            // gacha_renew_pickup.csb: the timer is 151 design pixels above the bottom,
            // right-aligned to the original clock. Both rows follow the draw buttons.
            var block = Area(root, "Pickup timer and guarantee", 0, 0, 0, 0);
            block.anchorMin = block.anchorMax = new Vector2(1, 0);
            block.pivot = new Vector2(1, 0);
            block.anchoredPosition = new Vector2(-20f, 103f) * SummonCanvasScale;
            block.sizeDelta = new Vector2(390f, 65f) * SummonCanvasScale;
            var timedRow = Area(block, "Timed banner", 0, 0, 1, .52f);
            var green = new Color32(129, 200, 62, 255);
            var time = Text(timedRow, "Time remaining", null, 26f * .72f * SummonCanvasScale,
                0f, 0f, .93f, 1f, green, TextAlignmentOptions.MidlineRight);
            ApplySummonTextOutline(time);
            Picture(timedRow, "Clock", SummonArt("cm_icon_etctime_b"), .932f, .05f, .068f, .9f, true).color = green;
            bindings.Add(("eventCountdown", time));
            bindings.Add(("eventTimerRow", timedRow.gameObject));
            var pity = Text(block, "Guarantee", null, 26f * .72f * SummonCanvasScale,
                0f, .52f, 1f, .48f, alignment: TextAlignmentOptions.MidlineRight);
            ApplySummonTextOutline(pity);
            bindings.Add(("pity", pity));
        }
    }
}
