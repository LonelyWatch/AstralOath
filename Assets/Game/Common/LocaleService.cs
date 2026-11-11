using System;
using System.Collections.Generic;
using AstralOath.Client.Core;

namespace AstralOath.Client.Common
{
    public sealed class LocaleService : ILocaleService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> messages;
        private readonly HashSet<string> voices;

        public LocaleService(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> messages,
            string text, string voice, params string[] voices)
        {
            this.messages = messages;
            this.voices = new HashSet<string>(voices, StringComparer.Ordinal);
            SelectText(text);
            SelectVoice(voice);
        }

        public string TextLocale { get; private set; }
        public string VoiceLocale { get; private set; }
        public event Action TextChanged;

        public void SelectText(string locale)
        {
            if (!messages.ContainsKey(locale))
                throw new ArgumentException($"Unknown text locale {locale}.", nameof(locale));
            if (TextLocale == locale)
                return;
            TextLocale = locale;
            TextChanged?.Invoke();
        }

        public void SelectVoice(string locale)
        {
            if (!voices.Contains(locale))
                throw new ArgumentException($"Unknown voice locale {locale}.", nameof(locale));
            VoiceLocale = locale;
        }

        public string Resolve(string key)
        {
            if (!messages[TextLocale].TryGetValue(key, out string value))
                throw new KeyNotFoundException($"Missing locale key {TextLocale}/{key}.");
            return value;
        }
    }
}
