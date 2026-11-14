using System;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDraft : UIBaseView
    {
        public const string Address = "arena/draft";
        public override string PrefabPath => Address;

        internal GameText Label(string key) => Bindings.Require<GameText>(key);
        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameObject Node(string key) => Bindings.Require<GameObject>(key);
        internal Image Picture(string key) => Bindings.Require<Image>(key);
        internal RawImage Surface(string key) => Bindings.Require<RawImage>(key);
        internal ScrollRect Scroll(string key) => Bindings.Require<ScrollRect>(key);
        internal TMP_InputField Search(string key) => Bindings.Require<TMP_InputField>(key);
        internal BindingMap Card(string side, int index) => Bindings.Require<BindingMap>(side + index);
        internal BindingMap Avatar(string side) => Bindings.Require<BindingMap>(side + "Avatar");

        internal void FitLayout()
        {
            var layout = Bindings.Require<RectTransform>("layout");
            var parent = (RectTransform)layout.parent;
            float scale = Mathf.Min(parent.rect.width / 1920f, parent.rect.height / 1080f);
            layout.localScale = Vector3.one * Mathf.Max(.01f, scale);
        }

        public override void Validate()
        {
            foreach (string key in new[] { "title", "timer", "preTimer", "host", "guest", "hint", "preCount", "actionLabel", "preActionLabel", "status" })
                _ = Label(key);
            foreach (string key in new[] { "action", "preAction", "exit" }) _ = Button(key);
            foreach (string key in new[] { "preban", "formation", "preSelected", "revealed", "row", "gridCell", "chip", "recent", "preHeader", "reveal", "revealHeroes" })
                _ = Node(key);
            foreach (string key in new[] { "roster", "grid" }) _ = Scroll(key);
            foreach (string key in new[] { "search", "preSearch" }) _ = Search(key);
            foreach (string key in new[] { "blue", "red", "featuredFallback", "banMark" }) _ = Picture(key);
            foreach (string clock in new[] { "timer", "preTimer" })
                foreach (string digit in new[] { "Tens", "Units", "Single" })
                    _ = Picture(clock + digit);
            _ = Surface("featured");
            _ = Bindings.Require<RectTransform>("layout");
            _ = Picture("banGlow");
            _ = Bindings.Require<BindingMap>("hostAvatar"); _ = Bindings.Require<BindingMap>("guestAvatar");
            for (int index = 0; index < 5; index++)
            {
                ValidateCard(Card("blue", index));
                ValidateCard(Card("red", index));
            }
            for (int index = 0; index < 4; index++)
            {
                ValidateCard(Card("formation", index));
                _ = Card("formation", index).Require<RawImage>("model");
            }
            foreach (string key in new[] { "row", "gridCell", "chip" })
                ValidateCard(Node(key).GetComponent<BindingMap>());
        }

        private static void ValidateCard(BindingMap card)
        {
            _ = card.Require<GameButton>("button");
            _ = card.Require<Image>("portrait");
            _ = card.Require<Image>("selection");
            _ = card.Require<GameText>("name");
        }
    }
}
