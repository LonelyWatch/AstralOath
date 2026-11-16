using System;
using System.Linq;
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
        public void ActiveHealthUsesSlantedGradientGeometryAndPreservesBitmapInk()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleHud.prefab");
            var copy = Object.Instantiate(prefab);
            try { CheckHealthPresentation(copy.transform.Find("Active Unit")); }
            finally { Object.DestroyImmediate(copy); }
        }

        private static void CheckHealthPresentation(Transform group)
        {
            var bar = group.Find("Active HP Fill").GetComponent<Image>();
            Assert.That(bar.GetType().Name, Is.EqualTo("BattleActiveHealthBar"));
            var health = group.Find("Active HP").GetComponent<TMPro.TextMeshProUGUI>();
            Assert.That(health.font.name, Is.EqualTo("BattleHpDigits"));
            Assert.That(health.fontSharedMaterial.shader.name, Is.EqualTo("TextMeshPro/Sprite"),
                "Use the source bitmap's RGB so the dark glyph outlines remain dark.");
            Assert.That(health.fontSharedMaterial.mainTexture, Is.SameAs(health.font.atlasTexture));

            var populate = bar.GetType().GetMethod("OnPopulateMesh",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(VertexHelper) }, null);
            using var mesh = new VertexHelper();
            float fullTrackRight = 0f;
            foreach (float fill in new[] { 1f, .5f, .05f, 0f })
            {
                bar.fillAmount = fill;
                populate.Invoke(bar, new object[] { mesh });
                Assert.That(mesh.currentVertCount, Is.GreaterThan(6), "The empty bar still has its full dark track.");
                var vertex = UIVertex.simpleVert;
                float leftAtTop = float.PositiveInfinity, leftAtBottom = float.PositiveInfinity;
                float rightAtTop = float.NegativeInfinity, rightAtBottom = float.NegativeInfinity;
                byte minGreen = byte.MaxValue, maxGreen = byte.MinValue;
                float trackRight = float.NegativeInfinity;
                int coloredVertices = 0;
                for (int i = 0; i < mesh.currentVertCount; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, i);
                    if (vertex.color.g < vertex.color.r + 20)
                    {
                        trackRight = Mathf.Max(trackRight, vertex.position.x);
                        continue;
                    }
                    coloredVertices++;
                    if (vertex.position.y > bar.rectTransform.rect.yMax - .01f)
                    {
                        leftAtTop = Mathf.Min(leftAtTop, vertex.position.x);
                        rightAtTop = Mathf.Max(rightAtTop, vertex.position.x);
                    }
                    if (vertex.position.y < bar.rectTransform.rect.yMin + .01f)
                    {
                        leftAtBottom = Mathf.Min(leftAtBottom, vertex.position.x);
                        rightAtBottom = Mathf.Max(rightAtBottom, vertex.position.x);
                    }
                    minGreen = Math.Min(minGreen, vertex.color.g);
                    maxGreen = Math.Max(maxGreen, vertex.color.g);
                }
                if (fill == 1f) fullTrackRight = trackRight;
                Assert.That(trackRight, Is.EqualTo(fullTrackRight).Within(.01f), "Every HP value uses the same full-width track.");
                if (fill == 0f)
                {
                    Assert.That(coloredVertices, Is.Zero);
                    continue;
                }
                Assert.That(leftAtTop, Is.GreaterThan(leftAtBottom + .25f));
                Assert.That(rightAtTop, Is.GreaterThan(rightAtBottom + .25f));
                Assert.That(maxGreen - minGreen, Is.GreaterThan(20));
            }
        }

        [Test]
        public void ActivePortraitHandoffDefersDetailsAndRepeatedRefreshDoesNotRestart()
        {
            var root = new GameObject("Active Unit", typeof(RectTransform));
            try
            {
                var image = new GameObject("Portrait", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(root.transform, false);
                image.rectTransform.sizeDelta = new Vector2(654f, 309f);
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1001_su.png");
                var details = new GameObject("Element", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                details.transform.SetParent(root.transform, false);
                var silk = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1004_su.png");
                var vildred = AssetDatabase.LoadAssetAtPath<Sprite>(Faces + "c1007_su.png");
                var type = Type.GetType("AstralOath.Client.BattleScene.BattleActivePortrait, AstralOath.Client.BattleScene");
                var presenter = Activator.CreateInstance(type, new object[] { image });
                var next = type.GetMethod("ShowTurn");
                var tick = type.GetMethod("Tick");
                Assert.That(next, Is.Not.Null);
                Assert.That(tick, Is.Not.Null);
                string name = "";
                void Show(Sprite sprite, string instance, string detail) =>
                    next.Invoke(presenter, new object[] { sprite, instance, (Action)(() => name = detail) });
                void Advance(float seconds) => tick.Invoke(presenter, new object[] { seconds });

                var original = image.sprite;
                Show(original, "ras", "Ras");
                Assert.That(name, Is.EqualTo("Ras"));
                Show(silk, "silk", "Silk");
                Advance(.05f);
                var outgoing = root.GetComponentsInChildren<Image>().Single(item => item.name == "Outgoing Portrait");
                var outgoingDetails = root.GetComponentsInChildren<Image>().Single(item => item.name == "Outgoing Element");
                Assert.That(outgoing.sprite, Is.SameAs(original));
                Assert.That(outgoing.canvasRenderer.GetAlpha(), Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(outgoingDetails.rectTransform.anchoredPosition.x, Is.GreaterThan(0f));
                Assert.That(image.sprite, Is.SameAs(silk));
                Assert.That(image.canvasRenderer.GetAlpha(), Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(details.rectTransform.anchoredPosition.x, Is.LessThan(0f));
                Assert.That(name, Is.EqualTo("Silk"));
                Show(silk, "silk", "Updated Silk");
                Assert.That(name, Is.EqualTo("Updated Silk"));
                Assert.That(root.GetComponentsInChildren<Image>().Single(item => item.name == "Outgoing Portrait"), Is.SameAs(outgoing));
                Advance(.05f);
                Assert.That(outgoing == null, Is.True, "Dispose the outgoing portrait as its fade finishes.");

                Show(vildred, "vildred", "Vildred");
                Advance(.30f);
                Assert.That(image.sprite, Is.SameAs(vildred));
                Assert.That(name, Is.EqualTo("Vildred"));
                Assert.That(details.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
                Assert.That(details.canvasRenderer.GetAlpha(), Is.EqualTo(1f).Within(.01f));
            }
            finally { Object.DestroyImmediate(root); }
        }

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
