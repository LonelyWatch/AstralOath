using System;
using System.Collections.Generic;
using System.Text;
using AstralOath.Client.Core;
using TMPro;
using UnityEngine;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent, AddComponentMenu("UI/Game Text")]
    public sealed class GameText : TextMeshProUGUI
    {
        private readonly HashSet<uint> warnedGlyphs = new HashSet<uint>();
        private BindingScope current;

        public void Bind(ILocaleService locale, BindingScope owner, string key, params object[] arguments)
        {
            Unbind();
            owner.Token.ThrowIfCancellationRequested();
            var label = this;
            if (!label.font)
                label.font = TMP_Settings.defaultFontAsset;
            if (!label.font)
                throw new InvalidOperationException("GameText requires a TMP font.");

            void Refresh() => label.text = Render(label, Format(locale, key, arguments));
            Refresh();
            var binding = new BindingScope(owner.Token);
            current = binding;
            locale.TextChanged += Refresh;
            binding.Add(() => locale.TextChanged -= Refresh);
            binding.Add(owner.Add(() =>
            {
                if (current == binding)
                    Unbind();
            }));
        }

        public static string Format(ILocaleService locale, string key, params object[] arguments) =>
            string.Format(locale.Resolve(key), arguments);

        private string Render(TMP_Text label, string text)
        {
            var display = new StringBuilder(text.Length);
            for (int index = 0; index < text.Length;)
            {
                int count = char.IsSurrogatePair(text, index) ? 2 : 1;
                uint code = count == 2 ? (uint)char.ConvertToUtf32(text, index) : text[index];
                if (char.IsControl(text[index]) || HasGlyph(label, code))
                {
                    display.Append(text, index, count);
                }
                else
                {
                    if (warnedGlyphs.Add(code))
                        Debug.LogWarning($"GameText missing glyph U+{code:X4}.", this);
                    display.Append('?');
                }
                index += count;
            }
            return display.ToString();
        }

        private static bool HasGlyph(TMP_Text label, uint code)
        {
            TMP_FontAsset font = label.font;
            if (TMP_FontAssetUtilities.GetCharacterFromFontAsset(code, font, true,
                    label.fontStyle, label.fontWeight, out _) != null)
                return true;
            if (TMP_Settings.fallbackFontAssets != null &&
                TMP_FontAssetUtilities.GetCharacterFromFontAssets(code, font, TMP_Settings.fallbackFontAssets,
                    true, label.fontStyle, label.fontWeight, out _) != null)
                return true;
            TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
            return fallback && fallback != font &&
                TMP_FontAssetUtilities.GetCharacterFromFontAsset(code, fallback, true,
                    label.fontStyle, label.fontWeight, out _) != null;
        }

        protected override void OnDisable()
        {
            try { Unbind(); }
            finally { base.OnDisable(); }
        }

        protected override void OnDestroy()
        {
            try { Unbind(); }
            finally { base.OnDestroy(); }
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            enableWordWrapping = true;
            overflowMode = TextOverflowModes.Ellipsis;
            richText = false;
            raycastTarget = false;
        }
#endif

        private void Unbind()
        {
            BindingScope binding = current;
            current = null;
            binding?.Dispose();
        }
    }
}