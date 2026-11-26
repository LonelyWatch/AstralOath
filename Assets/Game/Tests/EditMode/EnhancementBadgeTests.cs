using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class EnhancementBadgeTests
    {
        [Test]
        public void EnhancementBadgeSlicesTheSourceCircleAndUsesItsBitmapNumberStyle()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/NotificationBadge.prefab");
            var instance = Object.Instantiate(source);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                new NotificationBadgeBinder(map).Set(false, 15);
                var plate = map.Require<Image>("mark");
                Assert.That(plate.sprite.rect.size, Is.EqualTo(new Vector2(30, 30)), "The source is a circle, not the final 44px-wide plate.");
                Assert.That(plate.sprite.border, Is.EqualTo(new Vector4(12, 11, 15, 11)),
                    "Insets must be converted from the source texture size; using the displayed width duplicates the round ends.");
                Assert.That(instance.GetComponentsInChildren<Image>().Length, Is.EqualTo(4), "One live plate and the three original numeral sprites should draw.");
                var label = map.Require<GameText>("label");
                Assert.That(label.enabled, Is.False, "The compatibility text must not draw a second set of numerals.");
                Assert.That(label.text, Is.EqualTo("+15"));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void OnlyMaximumEnhancementUsesTheOriginalRedPlateAndReusedCardsReset()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/NotificationBadge.prefab");
            var instance = Object.Instantiate(source);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                var binder = new NotificationBadgeBinder(map);
                var mark = map.Require<Image>("mark");
                foreach (int level in new[] { 1, 3, 6, 14 })
                {
                    binder.Set(false, level);
                    Assert.That(instance.activeSelf, Is.True);
                    Assert.That(mark.sprite.name, Is.EqualTo("_upgrade_num_orange"));
                    Assert.That(map.Require<GameText>("label").text, Is.EqualTo("+" + level));
                }
                binder.Set(false, 15);
                Assert.That(mark.sprite.name, Is.EqualTo("_upgrade_num_red"));
                binder.Set(false, 3);
                Assert.That(mark.sprite.name, Is.EqualTo("_upgrade_num_orange"));
                binder.Set(false, 15, 15);
                Assert.That(mark.sprite.name, Is.EqualTo("_upgrade_num_red"));
                binder.Set(false, 15, 18);
                Assert.That(mark.sprite.name, Is.EqualTo("_upgrade_num_orange"), "A limit break changes the cap without changing the level.");
                binder.Set(false, 0);
                Assert.That(instance.activeSelf, Is.False);
                binder.Set(true);
                Assert.That(instance.activeSelf, Is.True);
                Assert.That(map.Require<GameText>("label").enabled, Is.False);
                Assert.That(mark.sprite.name, Is.EqualTo("_notification_num"));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
