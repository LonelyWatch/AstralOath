using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildSummonResultLayout(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var result = Area(root, "Summon result", 0, 0, 1, 1);
            Paint(result, null, new Color32(1, 11, 30, 65), true);
            bindings.Add(("resultPanel", result.gameObject));
            var title = Text(result, "Result title", null, 55.5f, .021f, .025f, .50f, .083f);
            ApplySummonTextOutline(title, light: true);
            bindings.Add(("resultTitle", title));
            var hint = Text(result, "Native summary guide", "summon.result.hint", 28.08f, .021f, .118f, .19f, .063f,
                new Color32(79, 149, 221, 255), TextAlignmentOptions.TopLeft, wrap: true);
            ApplySummonTextOutline(hint);
            bindings.Add(("resultItems", Area(result, "Result cards", 0, 0, 1, 1)));

            var back = SummonFooterButton(result, bindings, "resultClose", "_btn_gold", false, 32, 21, 248, 72);
            ApplySummonTextOutline(Text(back, "Text", "summon.button.back", 32.25f, .04f, .045f, .92f, .91f, alignment: TextAlignmentOptions.Center), light: true);
            var arrow = Picture(back, "Native back arrow", Art("cm_icon_etcback"), .07f, .26f, .13f, .48f, true);
            arrow.color = new Color32(211, 178, 119, 255);
            var again = SummonFooterButton(result, bindings, "resultAgain", "_btn_green", true, -361, 20, 340, 71);
            // gacha_summary/btn_summon_r1. n_counter is one source pixel below its parent.
            var label = Text(again, "Summon again", "summon.button.again", 32.25f,
                (238f - 321f * .43f / 2) / 340, (71f - 39f - 52f * .43f / 2) / 71,
                321f * .43f / 340, 52f * .43f / 71, alignment: TextAlignmentOptions.Center);
            label.overflowMode = TextOverflowModes.Overflow;
            ApplySummonTextOutline(label, light: true);
            SummonPricePlate(again, "Native repeat price plate", "_btn_small_black",
                41f / 340, 12f / 71, 106f / 340, 45f / 71).color = Color.black;
            bindings.Add(("resultAgainCurrency", Picture(again, "Repeat summon currency", SummonArt("token_ticket_rare"),
                6.8f / 340, 8.8f / 71, 50.4f / 340, 50.4f / 71, true)));
            bindings.Add(("resultAgainEpicCurrency", Picture(again, "Repeat summon epic currency", SummonArt("token_rarecoin"),
                6.8f / 340, 8.8f / 71, 50.4f / 340, 50.4f / 71, true)));
            var cost = Text(again, "Repeat summon cost", null, 28.8f,
                (93f - 32f * .8f / 2) / 340, (71f - 39f - 26f * .8f / 2) / 71, 32f * .8f / 340, 26f * .8f / 71,
                new Color32(255, 220, 78, 255), TextAlignmentOptions.Center);
            cost.overflowMode = TextOverflowModes.Overflow;
            ApplySummonTextOutline(cost);
            bindings.Add(("resultAgainPrice", cost));
            result.gameObject.SetActive(false);
            BuildSummonResultCard(root, bindings);
        }

        private static void BuildSummonResultCard(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            string[] elements = { "fire", "ice", "wind", "light", "dark" };
            var row = Area(root, "Native summon summary card", 0, 0, 1, 1);
            var card = new List<(string Key, Object Target)> { ("button", Button(row, null, Color.clear)) };
            foreach (string element in elements)
                card.Add(("background_" + element, Picture(row, "Native background " + element,
                    SummonArt("gacha_10bg_" + element), 0, 0, 1, 1)));

            // The row starts at color_bg (15, 2) in gacha_unit_bar.csb. Panel_1 starts at (14, 0).
            var heroMask = Area(row, "Native hero artwork clipping", 0, 1, 0, 0);
            heroMask.pivot = Vector2.zero;
            heroMask.anchoredPosition = new Vector2(-1, -2) * SummonCanvasScale;
            heroMask.sizeDelta = new Vector2(339, 99) * SummonCanvasScale;
            heroMask.gameObject.AddComponent<RectMask2D>();
            var face = Picture(heroMask, "Original hero face artwork", null, 0, 1, 0, 0, true);
            face.rectTransform.pivot = new Vector2(1, 0);
            face.rectTransform.anchoredPosition = new Vector2(352, 1) * SummonCanvasScale;
            card.Add(("icon", face));
            foreach (string element in elements)
                card.Add(("frame_" + element, Picture(row, "Native frame " + element,
                    SummonArt("gacha_10frame_" + element), -.027f, -.128f, 1.05f, 1.225f)));

            var artifact = Area(row, "Native artifact layout", 0, 0, 1, 1);
            card.Add(("artifact", artifact.gameObject));
            card.Add(("artifactArtwork", Picture(artifact, "Original artifact strip", null, .207f, .007f, .783f, .978f, true)));
            Picture(artifact, "Original artifact rim", SummonArt("arti_frame_small"), .206f, -.092f, .81f, 1.17f);
            card.Add(("artifactBadge", Picture(artifact, "Original artifact hex emblem", null, -.006f, -.165f, .327f, 1.235f, true)));
            for (int i = 0; i < 5; i++)
                Picture(artifact, "Native empty artifact breakthrough marker " + (i + 1),
                    SummonArt("arti_gauge_bg"), .347f + i * .068f, .919f, .073f, .059f);

            card.Add(("role", Picture(row, "Hero class", null, .027f, .081f, .09f, .26f, true)));
            card.Add(("element", Picture(row, "Hero element", null, .120f, .066f, .105f, .29f, true)));
            Sprite star = DraftSprite("TeamBadges/cm_icon_star");
            for (int i = 0; i < 6; i++)
            {
                card.Add(("star" + i, Picture(row, "Native result star " + (i + 1), star,
                    .041f + i * .055f, .382f, .076f, .259f, true)));
                card.Add(("badgeStar" + i, Picture(artifact, "Artifact emblem star " + (i + 1), star,
                    .082f + i * .042f, .879f, .060f, .176f, true)));
            }
            var name = Text(row, "Result name", null, 25, .032f, .594f, .903f, .295f);
            card.Add(("name", name));
            card.Add(("new", Picture(row, "Original NEW seal", SummonArt("gacha_new"),
                .849f, -.221f, .214f, .685f, true)));
            card.Add(("storage", Picture(row, "Temporary storage marker", SummonArt("cm_icon_tmp_inven"),
                -.027f, -.144f, .141f, .403f, true)));
            Bind(row, card.ToArray());
            row.gameObject.SetActive(false);
            bindings.Add(("resultCardTemplate", row.gameObject));
        }
    }
}
