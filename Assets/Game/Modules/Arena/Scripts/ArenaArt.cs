using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    internal static class ArenaArt
    {
        internal static void SetAvatar(BindingMap view, ArenaPlayer player, bool owner)
        {
            var portrait = view.Require<Image>("portrait");
            portrait.enabled = player != null && portrait.sprite;
            view.Require<Image>("owner").gameObject.SetActive(owner);
        }

        internal static void SetText(GameText text, string value)
        {
            text.richText = false;
            text.text = value ?? string.Empty;
        }
    }
}
