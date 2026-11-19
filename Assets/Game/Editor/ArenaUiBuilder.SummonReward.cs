using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildSummonRewardStage(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var reveal = Area(root, "Summon reveal", 0, 0, 1, 1);
            Paint(reveal, null, Color.clear, true).canvasRenderer.cullTransparentMesh = false;
            bindings.Add(("revealPanel", reveal.gameObject));
            var art = Picture(reveal, "Reward art track", null, .553f, .065f, .435f, .82f, true);
            bindings.Add(("revealArt", art));
            // Portrait and battle miniature use separate native skeletons. The close-up extends below the viewport.
            var portrait = Area(art.rectTransform, "Animated hero close-up", -.22f, -.27f, 1.57f, 1.89f);
            var fitter = portrait.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = 1;
            var actor = portrait.gameObject.AddComponent<RawImage>();
            actor.raycastTarget = false;
            bindings.Add(("revealHero", actor));

            // Dimensions in gacha_result_unit.csb, on the 720-unit original display surface.
            var info = Area(reveal, "Reward caption", 0, 0, 0, 0);
            info.anchorMin = info.anchorMax = info.pivot = Vector2.up;
            info.sizeDelta = new Vector2(620, 340) * SummonCanvasScale;
            bindings.Add(("revealCaption", info.gameObject));
            bindings.Add(("revealCaptionFade", info.gameObject.AddComponent<CanvasGroup>()));
            var glow = Picture(info, "Caption shadow", Art("_grow"), -.55f, -.48f, 1.42f, 1.58f);
            glow.color = new Color32(255, 255, 255, 102);

            var heroInfo = Area(info, "Hero attributes", 30f / 620, 14f / 340, 410f / 620, 47f / 340);
            bindings.Add(("heroCaption", heroInfo.gameObject));
            var row = heroInfo.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.spacing = 2;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            foreach (string field in new[] { "Element", "Role", "Zodiac" })
            {
                var icon = Picture(heroInfo, field, null, 0, 0, 1, 1, true);
                var size = icon.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = size.preferredHeight = 26 * SummonCanvasScale;
                bindings.Add(("reveal" + field, icon));
                var text = Text(heroInfo, field + " name", null, 16 * SummonCanvasScale, 0, 0, 1, 1, new Color32(198, 198, 198, 255));
                bindings.Add(("reveal" + field + "Text", text));
            }
            var name = Text(info, "Reward name", null, 46f * SummonCanvasScale, 31f / 620, 59f / 340, .94f, 56f / 340);
            name.fontStyle = FontStyles.Bold;
            ApplySummonTextOutline(name);
            bindings.Add(("revealName", name));
            var stars = Area(info, "Native grade stars", 0, 78f / 340, 0, 29f / 340);
            stars.pivot = new Vector2(0, .5f);
            stars.sizeDelta = new Vector2(126 * SummonCanvasScale, 0);
            bindings.Add(("revealGrade", stars));
            for (int i = 0; i < 6; i++)
                bindings.Add(("revealStar" + i, Picture(stars, "Star " + (i + 1), DraftSprite("TeamBadges/cm_icon_star"),
                    19f * i/126, 0, 30f / 126, 1, true).gameObject));
            // This is the artifact type line in the original artifact layout; hero metadata occupies its own icon row.
            var artifact = Text(info, "Artifact type", null, 25, 31f / 620, .045f, .64f, .13f, new Color32(205, 190, 155, 255));
            bindings.Add(("artifactCaption", artifact.gameObject));
            bindings.Add(("revealStars", artifact));
            var intro = Text(info, "Native two-line introduction", null, 18 * SummonCanvasScale, 31f / 620, 121f / 340,
                266f / 620, 58f / 340, new Color32(79, 149, 221, 255), TextAlignmentOptions.TopLeft, wrap: true);
            bindings.Add(("revealLabel", intro));
            bindings.Add(("revealNew", Picture(info, "New hero or artifact", SummonArt("gacha_new"),
                273f / 620, 118f / 340, 68f / 620, 68f / 340, true).gameObject));

            var details = Area(info, "Reward details", 24f / 620, 176f / 340, 221f / 620, 49f / 340);
            var detailButton = Button(details, SummonArt("_btn_small_black"), Color.white);
            PressSprites(detailButton, SummonArt("_btn_small_black"), SummonArt("_btn_small_black_p"));
            bindings.Add(("revealDetails", detailButton));
            Picture(details, "Magnifier", Art("cm_icon_etczoomer"), .049f, .2f, .13f, .6f, true).color = new Color32(255, 255, 255, 150);
            Text(details, "Details", "summon.reveal.details", 25, .20f, 0, .70f, 1, new Color32(225, 225, 225, 255), TextAlignmentOptions.Center);
            var cv = Area(info, "Acquisition voice", 31f / 620, 234f / 340, 340f / 620, 49f / 340);
            bindings.Add(("revealVoice", Button(cv, null, Color.clear)));
            Picture(cv, "Microphone", SummonArt("icon_menu_mic"), 0, 0, 40f / 340, 1, true);
            var credit = Text(cv, "Voice actor", null, 25, 45f / 340, 0, .86f, 1, new Color32(205, 205, 205, 255));
            bindings.Add(("revealCV", credit));

            var skip = Area(reveal, "Skip sequence", .907f, .012f, .083f, .078f);
            bindings.Add(("skip", Button(skip, null, Color.clear)));
            bindings.Add(("skipLabel", Text(skip, "Caption", null, 28, 0, 0, .65f, 1, new Color32(195, 195, 195, 255), TextAlignmentOptions.Right)));
            Picture(skip, "Arrow", SummonArt("cm_icon_etcarrow2"), .68f, .26f, .20f, .48f, true);
            var next = SummonFooterButton(reveal, bindings, "next", "_btn_green", true, -265, 22, 241, 63);
            bindings.Add(("nextLabel", Text(next, "Caption", null, 29, .13f, 0, .72f, 1, alignment: TextAlignmentOptions.Center)));
            Picture(next, "Arrow", SummonArt("cm_icon_etcarrow2"), .855f, .32f, .065f, .36f, true);

            var speech = Area(reveal, "Acquisition dialogue", 0, .694f, 1, .306f);
            bindings.Add(("revealSpeech", speech.gameObject));
            var plate = Area(speech, "Dialogue backdrop", 0, .28f, 1, .72f);
            // The recording has an open-ended full-width subtitle strip. Crop the panel's side and bottom strokes.
            plate.offsetMin = new Vector2(-60f, -65f);
            plate.offsetMax = new Vector2(60f, 0f);
            Paint(plate, SummonArt("renew_popup_bg_alpha"), new Color32(175, 184, 201, 220), sliced: true);
            var speaker = Text(speech, "Speaker", null, 34, .15f, 0, .70f, .32f, new Color32(206, 226, 225, 255), TextAlignmentOptions.Center);
            bindings.Add(("revealSpeaker", speaker));
            var line = Text(speech, "Acquisition line", null, 35, .13f, .32f, .74f, .49f, alignment: TextAlignmentOptions.Center, wrap: true);
            line.enableAutoSizing = true;
            line.fontSizeMin = 24f;
            line.fontSizeMax = 35f;
            bindings.Add(("revealQuote", line));
            Picture(speech, "Continue dialogue", SummonArt("cm_talk_next1"), .49f, .87f, .02f, .08f, true);
            bindings.Add(("revealDismiss", Button(Area(speech, "Continue", 0, 0, 1, 1), null, Color.clear)));
            speech.gameObject.SetActive(false);
            reveal.gameObject.SetActive(false);
        }
    }
}
