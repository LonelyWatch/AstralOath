using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleActivePortraitTests
    {
        private const string Faces = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Faces/";

        [Test]
        public void SwitchingToSilkKeepsNativeAspectAndAuthoredBottomLeft()
        {
            var root = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            try
            {
                var image = root.GetComponent<Image>();
                var rect = image.rectTransform;
                var authoredAnchor = new Vector2(0.1f, 0.15f);
                var authoredSize = new Vector2(654f, 309f);
                var authoredPosition = new Vector2(327f, 154.5f);
                rect.anchorMin = rect.anchorMax = authoredAnchor;
                rect.anchoredPosition = authoredPosition;
                rect.sizeDelta = authoredSize;
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1001_su.png");
                var silk = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1004_su.png");
                var vildred = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1007_su.png");
                Assert.That(image.sprite.rect.size, Is.EqualTo(new Vector2(436f, 206f)));
                Assert.That(silk.rect.size, Is.EqualTo(new Vector2(242f, 206f)));
                Vector2 originalBottomLeft = rect.anchoredPosition + rect.rect.min;

                var presenterType = Type.GetType("AstralOath.Client.BattleScene.BattleActivePortrait, AstralOath.Client.BattleScene");
                Assert.That(presenterType, Is.Not.Null, "Active portraits need a native-aspect layout owner.");
                var presenter = Activator.CreateInstance(presenterType, new object[] { image });
                var show = presenterType.GetMethod("Show");
                var restore = presenterType.GetMethod("Restore");

                show.Invoke(presenter, new object[] { silk });
                Assert.That(image.sprite, Is.SameAs(silk));
                Assert.That(rect.rect.size, Is.EqualTo(new Vector2(363f, 309f)));
                Assert.That(rect.anchoredPosition + rect.rect.min, Is.EqualTo(originalBottomLeft));
                Assert.That(rect.anchorMin, Is.EqualTo(authoredAnchor));
                Assert.That(rect.anchorMax, Is.EqualTo(authoredAnchor));

                show.Invoke(presenter, new object[] { vildred });
                Assert.That(rect.rect.size, Is.EqualTo(authoredSize));
                Assert.That(rect.anchoredPosition, Is.EqualTo(authoredPosition));

                show.Invoke(presenter, new object[] { null });
                Assert.That(image.enabled, Is.False);
                restore.Invoke(presenter, null);
                Assert.That(image.enabled, Is.True);
                Assert.That(image.sprite.name, Is.EqualTo("c1001_su"));
                Assert.That(rect.sizeDelta, Is.EqualTo(authoredSize));
                Assert.That(rect.anchoredPosition, Is.EqualTo(authoredPosition));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
