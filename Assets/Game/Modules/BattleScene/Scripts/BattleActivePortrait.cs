using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleActivePortrait
    {
        private readonly Image image;
        private readonly RectTransform rect;
        private readonly Sprite originalSprite;
        private readonly bool originalEnabled;
        private readonly Vector2 originalSize;
        private readonly Vector2 originalPosition;
        private readonly Vector2 originalBottomLeft;
        private readonly float authoredHeight;

        public BattleActivePortrait(Image image)
        {
            this.image = image;
            rect = image.rectTransform;
            originalSprite = image.sprite;
            originalEnabled = image.enabled;
            originalSize = rect.sizeDelta;
            originalPosition = rect.anchoredPosition;
            originalBottomLeft = originalPosition + rect.rect.min;
            authoredHeight = rect.rect.height;
        }

        public void Show(Sprite sprite)
        {
            image.enabled = sprite != null;
            if (!sprite) return;

            image.sprite = sprite;
            float referenceHeight = originalSprite ? originalSprite.rect.height : sprite.rect.height;
            Vector2 size = sprite.rect.size * (authoredHeight / referenceHeight);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            rect.anchoredPosition = originalBottomLeft - rect.rect.min;
        }

        public void Restore()
        {
            if (!image) return;

            image.sprite = originalSprite;
            rect.sizeDelta = originalSize;
            rect.anchoredPosition = originalPosition;
            image.enabled = originalEnabled;
        }
    }
}
