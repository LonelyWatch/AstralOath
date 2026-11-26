using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    public sealed class NotificationBadgeBinder
    {
        private readonly NotificationBadge view;
        private int previousLevel = -1;
        private bool previousDot;
        private bool previousMaximum;
        private readonly Sprite dotSprite;
        private readonly Color dotColor;

        public NotificationBadgeBinder(BindingMap instance)
        {
            view = new NotificationBadge(instance);
            dotSprite = view.Mark.sprite;
            dotColor = view.Mark.color;
        }

        public void Set(bool notify, int enhancement = 0, int maxEnhancement = 15)
        {
            enhancement = Mathf.Max(0, enhancement);
            bool numbered = enhancement > 0;
            bool dot = notify && !numbered;
            bool maximum = numbered && maxEnhancement > 0 && enhancement >= maxEnhancement;
            if (previousLevel == enhancement && previousDot == dot && previousMaximum == maximum) return;
            previousLevel = enhancement;
            previousDot = dot;
            previousMaximum = maximum;
            view.Mark.sprite = numbered ? (maximum ? view.MaximumPlate.sprite : view.NormalPlate.sprite) : dotSprite;
            view.Mark.color = numbered ? Color.white : dotColor;
            view.Mark.type = numbered ? Image.Type.Sliced : Image.Type.Simple;
            view.Label.text = numbered ? "+" + enhancement.ToString(CultureInfo.InvariantCulture) : string.Empty;
            view.Label.enabled = false;
            float height = view.ScaleLayout.AuthoredHeight;
            float sourceWidth = numbered ? (maximum ? 44f : 39f) : 31f;
            float width = sourceWidth * height / 30f;
            Vector2 position = new Vector2(-width / 2, 0);
            Place(view.Mark.rectTransform, position);
            Place(view.Label.rectTransform, position + new Vector2(-height / 10, height / 10));
            view.Number.gameObject.SetActive(numbered);
            if (numbered)
            {
                var numberRect = (RectTransform)view.Number.transform;
                numberRect.anchorMin = numberRect.anchorMax = new Vector2(1, .5f);
                numberRect.pivot = new Vector2(.5f, .5f);
                numberRect.anchoredPosition = position + new Vector2(-height / 10, height / 10);
                view.Number.Set(enhancement, .57f * height / 30f);
            }
            view.Mark.rectTransform.localScale = numbered ? Vector3.one : new Vector3(.48f, .48f, 1f);
            view.Root.SetActive(numbered || dot);
            view.ScaleLayout.SetLayoutVertical();

            void Place(RectTransform rect, Vector2 anchoredPosition)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = anchoredPosition;
            }
        }
    }
}
