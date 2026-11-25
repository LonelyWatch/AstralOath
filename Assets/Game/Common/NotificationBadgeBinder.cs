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
            view.Label.enabled = numbered;
            view.Mark.rectTransform.localScale = numbered ? Vector3.one : new Vector3(.48f, .48f, 1f);
            view.Root.SetActive(numbered || dot);
        }
    }
}
