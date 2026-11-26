using System;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class NotificationBadgeDigitSpriteTests
    {
        [Test]
        public void PlusThreeDrawsTwoOriginalSpriteImagesAndKeepsTheCompatibilityLabelHidden()
        {
            WithBadge((map, binder) =>
            {
                binder.Set(false, 3);
                Assert.That(map.Require<GameText>("label").enabled, Is.False,
                    "The original Sprite numerals must draw directly instead of using the TMP rendering path.");
                Assert.That(map.Require<GameText>("label").text, Is.EqualTo("+3"));
                Assert.That(LiveGlyphs(map).Select(image => image.sprite.name),
                    Is.EqualTo(new[] { "itxt_numplus_b", "itxt_num3_b" }));
            });
        }

        [Test]
        public void PlusFifteenUsesNativeSpriteQuadsDefaultUiMaterialAndSourceAdvances()
        {
            WithBadge((map, binder) =>
            {
                binder.Set(false, 15);
                var images = LiveGlyphs(map);
                Assert.That(images.Length, Is.EqualTo(3), "Only plus, tens and ones should draw.");
                Assert.That(images.Select(image => image.sprite.name),
                    Is.EqualTo(new[] { "itxt_numplus_b", "itxt_num1_b", "itxt_num5_b" }));
                foreach (var image in images)
                {
                    Assert.That(AssetDatabase.GetAssetPath(image.sprite),
                        Does.StartWith("Assets/Game/Common/Res/Textures/EnhancementDigits/"));
                    Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(26, 34)));
                    Assert.That(image.rectTransform.rect.size, Is.EqualTo(new Vector2(26, 34)));
                    Assert.That(image.rectTransform.localScale.x, Is.EqualTo(.855f).Within(.0001f));
                    Assert.That(image.rectTransform.localScale.y, Is.EqualTo(.855f).Within(.0001f));
                    Assert.That(image.rectTransform.localScale.z, Is.EqualTo(1));
                    Assert.That(image.rectTransform.anchorMin, Is.EqualTo(new Vector2(0, .5f)));
                    Assert.That(image.rectTransform.anchorMax, Is.EqualTo(new Vector2(0, .5f)));
                    Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(0, .5f)));
                    Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
                    Assert.That(image.material, Is.SameAs(image.defaultMaterial));
                    Assert.That(image.material.shader.name, Is.EqualTo("UI/Default"));
                    Assert.That(image.color, Is.EqualTo(Color.white));
                }
                Assert.That(images[0].rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(images[1].rectTransform.anchoredPosition.x - images[0].rectTransform.anchoredPosition.x,
                    Is.EqualTo(15 * .855f).Within(.001f));
                Assert.That(images[2].rectTransform.anchoredPosition.x - images[1].rectTransform.anchoredPosition.x,
                    Is.EqualTo(16 * .855f).Within(.001f));
                Assert.That(images[2].transform.GetSiblingIndex(), Is.LessThan(images[1].transform.GetSiblingIndex()),
                    "The source draws the ones image before its nested tens image.");
                Assert.That(images[1].transform.GetSiblingIndex(), Is.LessThan(images[0].transform.GetSiblingIndex()),
                    "The plus image draws last over the deliberately overlapping dark edges.");
            });
        }

        [Test]
        public void ReusingFifteenAsThreeAndThenADotHidesOldDigitsWithoutDuplicatingThePool()
        {
            WithBadge((map, binder) =>
            {
                binder.Set(false, 15);
                var original = LiveGlyphs(map);
                Assert.That(original.Length, Is.EqualTo(3));
                binder.Set(false, 3);
                var single = LiveGlyphs(map);
                Assert.That(single.Select(image => image.sprite.name),
                    Is.EqualTo(new[] { "itxt_numplus_b", "itxt_num3_b" }));
                Assert.That(single[0], Is.SameAs(original[0]));
                Assert.That(single[1], Is.SameAs(original[1]));
                binder.Set(true);
                Assert.That(LiveGlyphs(map), Is.Empty, "No previous tens or ones image may remain over a notification dot.");
                Assert.That(map.Require<Component>("number").gameObject.activeSelf, Is.False);
                Assert.That(map.Require<GameText>("label").enabled, Is.False);
                binder.Set(false, 15);
                Assert.That(LiveGlyphs(map), Is.EqualTo(original));
                Assert.That(map.GetComponentsInChildren<Image>(true).Count(IsSourceGlyph), Is.LessThanOrEqualTo(4),
                    "Pool only the needed two digits, prefix and inactive template, rather than an eleven-image bank.");
            });
        }

        private static Image[] LiveGlyphs(BindingMap map) => map.GetComponentsInChildren<Image>()
            .Where(image => image.enabled && IsSourceGlyph(image))
            .OrderBy(image => image.rectTransform.anchoredPosition.x).ToArray();

        private static bool IsSourceGlyph(Image image) => image.sprite && image.sprite.name.StartsWith("itxt_num", StringComparison.Ordinal);

        private static void WithBadge(Action<BindingMap, NotificationBadgeBinder> check)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/NotificationBadge.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                check(map, new NotificationBadgeBinder(map));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
