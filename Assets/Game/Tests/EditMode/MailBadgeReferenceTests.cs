using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class MailBadgeReferenceTests
    {
        private const string Prefab = "Assets/Game/Modules/Mail/Res/Prefab/MailEntry.prefab";

        [Test]
        public void HeaderCounterKeepsOriginalAnchorSlicesAndDinmedRegularDigits()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var map = root.GetComponent<BindingMap>();
            var counter = (RectTransform)map.Require<GameObject>("counter").transform;
            Assert.That(counter.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(counter.anchorMax, Is.EqualTo(Vector2.zero));
            Assert.That(counter.anchoredPosition, Is.EqualTo(new Vector2(19, 48) * 1.5f));
            Assert.That(counter.sizeDelta.y, Is.EqualTo(45));
            var image = counter.GetComponent<Image>();
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.sprite.border, Is.EqualTo(new Vector4(13, 9, 15, 9)));
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(1f / 1.5f).Within(.001f));
            var label = map.Require<GameText>("count");
            Assert.That(label.font.name, Is.EqualTo("MailCountDigits"));
            Assert.That(label.fontStyle, Is.EqualTo(FontStyles.Normal));
            Assert.That(label.enableAutoSizing, Is.False);
            Assert.That(label.fontSize, Is.EqualTo(21 * .82f * 1.5f).Within(.001f));
            Assert.That(label.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(-1, 2.5f) * 1.5f));
            Assert.That(label.font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            foreach (char digit in "0123456789") Assert.That(label.font.HasCharacter(digit), Is.True);
            Assert.That(label.font.HasCharacter('_'),Is.True,"TMP初始化需要下划线特殊字形，即使角标不画下划线");
        }

        [TestCase(0, false, 31)]
        [TestCase(1, true, 31)]
        [TestCase(15, true, 38)]
        [TestCase(100, true, 48)]
        public void CountsResizeOnlyTheSlicedCentreAndZeroHidesTheCounter(int count, bool visible, float width)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            using var scope = new BindingScope(CancellationToken.None);
            using var session = new MailSession(new EmptyApi());
            try
            {
                var map = root.GetComponent<BindingMap>();
                MailEntryBinder.Bind(map, scope, session, _ => Task.CompletedTask);
                session.ApplySummary(new MailSummary { version = 1, effectiveCount = count });
                var counter = map.Require<GameObject>("counter");
                Assert.That(counter.activeSelf, Is.EqualTo(visible));
                Assert.That(((RectTransform)counter.transform).sizeDelta, Is.EqualTo(new Vector2(width, 30) * 1.5f));
                Assert.That(map.Require<GameText>("count").text, Is.EqualTo(count == 0 ? "" : count.ToString()));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private sealed class EmptyApi : IMailApi
        {
            public void Dispose() { }
            public Task<MailSummary> SummaryAsync(CancellationToken token) => Task.FromResult(new MailSummary());
            public Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token) => throw new NotSupportedException();
            public Task<MailLetter> ReadAsync(string id, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest request, CancellationToken token) => throw new NotSupportedException();
            public Task WatchAsync(Action<long> version, CancellationToken token) => Task.CompletedTask;
        }
    }
}
