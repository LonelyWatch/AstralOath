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
    public sealed class NotificationBadgeGeometryTests
    {
        private const string PrefabPath = "Assets/Game/Common/Res/Prefab/NotificationBadge.prefab";

        [Test]
        public void NumberedBadgesUseOriginalWidthsForTheirCurrentEnhancementCap()
        {
            WithBadge((map, host) =>
            {
                var binder = new NotificationBadgeBinder(map);
                var mark = map.Require<Image>("mark");
                var label = map.Require<GameText>("label");
                foreach (var value in new (int level, int cap, float width)[] { (3, 15, 58.5f), (15, 15, 66f),
                    (15, 30, 58.5f), (21, 21, 66f), (21, 30, 58.5f) })
                {
                    binder.Set(false, value.level, value.cap);
                    Assert.That(mark.rectTransform.rect.width, Is.EqualTo(value.width).Within(.01f),
                        $"+{value.level} with cap{value.cap} must retain the source39/44px plate width.");
                    Assert.That(mark.rectTransform.rect.height, Is.EqualTo(45).Within(.01f));
                    Assert.That(mark.rectTransform.anchorMin, Is.EqualTo(new Vector2(1, .5f)));
                    Assert.That(mark.rectTransform.anchorMax, Is.EqualTo(new Vector2(1, .5f)));
                    Assert.That(mark.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(-value.width / 2, 0)));
                    Assert.That(label.rectTransform.rect.size, Is.EqualTo(new Vector2(value.width, 45)));
                    Assert.That(label.rectTransform.anchoredPosition,
                        Is.EqualTo(mark.rectTransform.anchoredPosition + new Vector2(-4.5f, 4.5f)));
                }
                Assert.That(map.Require<RectTransform>("sourceGroup").rect.size, Is.EqualTo(new Vector2(66, 45)));
            });
        }

        [Test]
        public void HostHeightScalesFontAndPlateTogetherWithoutStretchingTheirWidth()
        {
            WithBadge((map, host) =>
            {
                new NotificationBadgeBinder(map).Set(false, 15);
                var mark = map.Require<Image>("mark").rectTransform;
                var glyph = map.GetComponentsInChildren<Image>().First(image => image.sprite && image.sprite.name == "itxt_num1_b").rectTransform;
                host.sizeDelta = new Vector2(100, 45);
                LayoutRebuilder.ForceRebuildLayoutImmediate(host);
                Vector2 plate = WorldSize(mark);
                float glyphHeight = WorldSize(glyph).y;
                Assert.That(glyphHeight, Is.EqualTo(29.07f).Within(.01f));
                foreach (float height in new[] { 22.5f, 67.5f, 45f })
                {
                    host.sizeDelta = new Vector2(170, height);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(host);
                    float scale = height / 45;
                    Assert.That(WorldSize(mark).x, Is.EqualTo(plate.x * scale).Within(.01f),
                        "Only host height controls the complete source composition; host width must not stretch it.");
                    Assert.That(WorldSize(mark).y, Is.EqualTo(plate.y * scale).Within(.01f));
                    Assert.That(WorldSize(glyph).y,
                        Is.EqualTo(glyphHeight * scale).Within(.01f), "The actual Sprite number must shrink with its plate.");
                }
            });
        }

        [Test]
        public void ReusedMaximumBadgeRestoresTheOriginalNotificationDotGeometry()
        {
            WithBadge((map, host) =>
            {
                var binder = new NotificationBadgeBinder(map);
                var mark = map.Require<Image>("mark");
                var label = map.Require<GameText>("label");
                var sprite = mark.sprite;
                Color color = mark.color;
                host.sizeDelta = new Vector2(100, 45);
                binder.Set(false, 15);
                binder.Set(true);
                Assert.That(mark.sprite, Is.SameAs(sprite));
                Assert.That(mark.color, Is.EqualTo(color));
                Assert.That(mark.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(mark.rectTransform.rect.size, Is.EqualTo(new Vector2(46.5f, 45)));
                Assert.That(mark.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(-23.25f, 0)));
                Assert.That(mark.rectTransform.localScale, Is.EqualTo(new Vector3(.48f, .48f, 1)));
                Assert.That(label.enabled, Is.False);
                Assert.That(label.text, Is.Empty);
                binder.Set(false);
                Assert.That(host.gameObject.activeSelf, Is.False);
                binder.Set(true);
                Assert.That(host.gameObject.activeSelf, Is.True);
                binder.Set(false, 3);
                Assert.That(mark.rectTransform.rect.width, Is.EqualTo(58.5f).Within(.01f));
                Assert.That(mark.rectTransform.localScale, Is.EqualTo(Vector3.one));
            });
        }

        private static void WithBadge(Action<BindingMap, RectTransform> check)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                check(instance.GetComponent<BindingMap>(), (RectTransform)instance.transform);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static Vector2 WorldSize(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Vector2(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1]));
        }
    }
}
