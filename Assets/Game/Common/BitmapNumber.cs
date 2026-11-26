using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BitmapNumber : UIBehaviour
    {
        [SerializeField] private Sprite[] digitSprites = new Sprite[10];
        [SerializeField] private Sprite prefixSprite;
        [SerializeField] private Image prefixImage;
        [SerializeField] private Image digitTemplate;
        [SerializeField] private float prefixAdvance = 15;
        [SerializeField] private float digitAdvance = 16;

        private readonly List<Image> digits = new List<Image>();

        public void Set(int value, float glyphScale)
        {
            if (!prefixSprite || !prefixImage || !digitTemplate || digitSprites == null || digitSprites.Length != 10)
                throw new InvalidOperationException("BitmapNumber requires its original prefix, ten digits and image template.");
            string text = Mathf.Max(0, value).ToString(CultureInfo.InvariantCulture);
            while (digits.Count < text.Length)
            {
                var image = Instantiate(digitTemplate, transform, false);
                image.name = "Digit " + digits.Count;
                digits.Add(image);
            }
            digitTemplate.gameObject.SetActive(false);
            Place(prefixImage, prefixSprite, 0);
            float height = prefixSprite.rect.height;
            for (int index = 0; index < digits.Count; index++)
            {
                bool active = index < text.Length;
                digits[index].gameObject.SetActive(active);
                if (!active) continue;
                var sprite = digitSprites[text[index] - '0'];
                if (!sprite) throw new InvalidOperationException("BitmapNumber is missing digit " + text[index] + ".");
                Place(digits[index], sprite, prefixAdvance + index * digitAdvance);
                // The source nests tens beneath ones, then the prefix beneath tens.
                // Draw the rightmost glyph first so their overlapping dark edges retain that order.
                digits[index].transform.SetAsFirstSibling();
                height = Mathf.Max(height, sprite.rect.height);
            }
            prefixImage.transform.SetAsLastSibling();
            float width = prefixAdvance + (text.Length - 1) * digitAdvance + digits[text.Length - 1].sprite.rect.width;
            ((RectTransform)transform).sizeDelta = new Vector2(width, height) * glyphScale;

            void Place(Image image, Sprite sprite, float x)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.useSpriteMesh = false;
                image.preserveAspect = false;
                image.material = null;
                image.color = Color.white;
                image.raycastTarget = false;
                image.enabled = true;
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                rect.pivot = new Vector2(0, .5f);
                rect.sizeDelta = sprite.rect.size;
                rect.anchoredPosition = new Vector2(x * glyphScale, 0);
                rect.localScale = new Vector3(glyphScale, glyphScale, 1);
                image.gameObject.SetActive(true);
            }
        }
    }
}
