using System;
using System.Collections;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class GameTextTests
    {
        private sealed class Locale : ILocaleService
        {
            public string TextLocale { get; private set; } = "zh-Hans";
            public string VoiceLocale { get; private set; } = "ja";
            public event Action TextChanged;
            public void SelectText(string locale) { TextLocale = locale; TextChanged?.Invoke(); }
            public void SelectVoice(string locale) => VoiceLocale = locale;
            public string Resolve(string key) => key == "missing" ? "A\uFFFF" : TextLocale == "zh-Hans" ? "Alpha" : "Beta";
        }

        [UnityTest]
        public IEnumerator TypographyWrapsEllipsizesAndStopsListeningOnDisable()
        {
            var owner = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(GameText));
            using var scope = new BindingScope(default);
            try
            {
                var locale = new Locale();
                var control = owner.GetComponent<GameText>();
                var text = owner.GetComponent<TextMeshProUGUI>();
                control.Bind(locale, scope, "label");
                Assert.That(text.enableWordWrapping, Is.True);
                Assert.That(text.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis));
                Assert.That(text.font, Is.Not.Null);
                Assert.That(text.text, Is.EqualTo("Alpha"));
                LogAssert.Expect(LogType.Warning, "GameText missing glyph U+FFFF.");
                control.Bind(locale, scope, "missing");
                Assert.That(text.text, Is.EqualTo("A?"));
                control.Bind(locale, scope, "label");
                locale.SelectVoice("zh-Hans");
                Assert.That(text.text, Is.EqualTo("Alpha"));
                locale.SelectText("en");
                Assert.That(text.text, Is.EqualTo("Beta"));
                owner.SetActive(false);
                locale.SelectText("zh-Hans");
                Assert.That(text.text, Is.EqualTo("Beta"));
                yield return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(owner);
            }
        }
    }
}