using AstralOath.Client.Common;
using AstralOath.Client.Home;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    internal static class SummonResultCard
    {
        private static readonly string[] Elements = { "fire", "ice", "wind", "light", "dark" };

        // Slot order and the staggered three-column layout follow wnd/gacha_summary.csb.
        private static readonly Vector2[] TenSlots =
        {
            new Vector2(.386f, .313f),
            new Vector2(.386f, .485f),
            new Vector2(.135f, .399f),
            new Vector2(.638f, .399f),
            new Vector2(.386f, .143f),
            new Vector2(.386f, .666f),
            new Vector2(.135f, .225f),
            new Vector2(.638f, .225f),
            new Vector2(.135f, .575f),
            new Vector2(.638f, .575f)
        };

        internal static void Place(RectTransform row, int position, int count)
        {
            Vector2 slot = count == 1 ? new Vector2(.386f, .431f) : TenSlots[position - 1];
            Place(row, slot.x, slot.y, .229f, .139f);
        }

        internal static void Present(BindingMap row, SummonReward reward, string name,
            HomeHeroArt.Hero hero, ArtifactArtCatalog.Entry artifact)
        {
            bool isArtifact = reward.kind == "artifact";
            string element = hero?.elementId;
            foreach (string option in Elements)
            {
                bool visible = !isArtifact && option == element;
                row.Require<Image>("background_" + option).enabled = visible;
                row.Require<Image>("frame_" + option).enabled = visible;
            }

            row.Require<GameObject>("artifact").SetActive(isArtifact);
            var heroArtwork = row.Require<Image>("icon");
            var artifactArtwork = row.Require<Image>("artifactArtwork");
            SetImage(heroArtwork, isArtifact ? artifact?.portrait : hero != null && hero.cardPortrait ? hero.cardPortrait : hero?.portrait);
            heroArtwork.enabled &= !isArtifact;
            SetImage(artifactArtwork, artifact?.portrait);
            SetImage(row.Require<Image>("artifactBadge"), artifact?.combatIcon);
            SetImage(row.Require<Image>("element"), hero != null && hero.cardElement ? hero.cardElement : hero?.element);
            SetImage(row.Require<Image>("role"), hero?.role);

            row.Require<Image>("new").enabled = reward.isNew;
            row.Require<Image>("storage").enabled = reward.inStorage;
            for (int i = 0; i < 6; ++i)
            {
                var star = row.Require<Image>("star" + i);
                star.enabled = i < reward.stars;
                Place(star.rectTransform, (isArtifact ? .332f : .041f) + i * .055f,
                    isArtifact ? .292f : .382f, .076f, .259f);
                var badgeStar = row.Require<Image>("badgeStar" + i);
                badgeStar.enabled = isArtifact && i < reward.stars;
                Place(badgeStar.rectTransform, .160f - (reward.stars - 1) * .021f - .03f + i * .042f,
                    .879f, .060f, .176f);
            }
            var label = row.Require<GameText>("name");
            label.text = name;
            Place(label.rectTransform, isArtifact ? .332f : .032f, .594f, isArtifact ? .651f : .951f, .295f);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(x, 1 - y - height);
            rect.anchorMax = new Vector2(x + width, 1 - y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
        }
    }
}
