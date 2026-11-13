using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleUnitLevelBadge : IDisposable
    {
        private const float SourceGlyphWidth = 26f;
        private const float SourceScale = .53f;
        private const float SourceDigitSpacing = 8f;
        private readonly Image preview;
        private readonly Sprite[] glyphs;
        private readonly bool originalVisibility;
        private readonly float unit;
        private readonly List<Image> digits = new List<Image>(2);
        private int displayedLevel;

        internal BattleUnitLevelBadge(Image preview, Sprite[] glyphs)
        {
            this.preview = preview;
            this.glyphs = glyphs;
            originalVisibility = preview.enabled;
            preview.enabled = false;
            unit = preview.rectTransform.rect.width / (SourceGlyphWidth * SourceScale);
        }

        internal void Show(int level)
        {
            if (displayedLevel == level || glyphs?.Length != 10) return;
            displayedLevel = level;
            string value = level.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < value.Length; i++)
            {
                if (digits.Count == i) digits.Add(CreateDigit(i));
                var image = digits[i];
                image.sprite = glyphs[value[i] - '0'];
                // hp_bar.csb positions l2 and l1 at x=11 and x=19, relative to lv_color x=14.
                float x = value.Length == 2 ? (i == 0 ? -3f : 5f) :
                    (i - (value.Length - 1) * .5f) * SourceDigitSpacing;
                image.rectTransform.anchoredPosition = new Vector2(x * unit, 0f);
                if (image.sprite) image.rectTransform.sizeDelta = image.sprite.rect.size * (unit * SourceScale);
                image.gameObject.SetActive(image.sprite);
            }
            for (int i = value.Length; i < digits.Count; i++) digits[i].gameObject.SetActive(false);
        }

        private Image CreateDigit(int index)
        {
            string name = index == 0 ? "Level Tens" : index == 1 ? "Level Ones" : "Level Digit " + (index + 1);
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(preview.rectTransform, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * .5f;
            image.raycastTarget = false;
            return image;
        }

        public void Dispose()
        {
            foreach (var image in digits)
            {
                if (!image) continue;
                image.gameObject.SetActive(false);
                image.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(image.gameObject);
            }
            digits.Clear();
            if (preview) preview.enabled = originalVisibility;
        }
    }
}
