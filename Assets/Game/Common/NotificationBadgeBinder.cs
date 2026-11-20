using System.Globalization;
using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class NotificationBadgeBinder
    {
        private readonly NotificationBadge view;
        private int previousLevel = -1;
        private bool previousDot;

        public NotificationBadgeBinder(BindingMap instance) => view = new NotificationBadge(instance);

        public void Set(bool notify, int enhancement = 0)
        {
            enhancement = Mathf.Max(0, enhancement);
            bool numbered = enhancement > 0;
            bool dot = notify && !numbered;
            if (previousLevel == enhancement && previousDot == dot) return;
            previousLevel = enhancement;
            previousDot = dot;
            view.Label.text = numbered ? "+" + enhancement.ToString(CultureInfo.InvariantCulture) : string.Empty;
            view.Label.enabled = numbered;
            view.Mark.rectTransform.localScale = numbered ? Vector3.one : new Vector3(.48f, .48f, 1f);
            view.Root.SetActive(numbered || dot);
        }
    }
}
