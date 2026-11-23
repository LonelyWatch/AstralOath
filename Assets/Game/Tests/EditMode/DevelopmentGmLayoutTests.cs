using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class DevelopmentGmLayoutTests
    {
        private const string Prefab = "Assets/Game/Modules/FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab";

        [Test]
        public void ForgeTabOffersSixSelectorsAndFourEditableSubstatRows()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<BindingMap>();
            Assert.That(map.Require<GameButton>("forge"), Is.Not.Null);
            var body = map.Require<GameObject>("forgePanel").transform;
            foreach (string key in new[] { "forgeSlot", "forgeTier", "forgeGrade", "forgeSet", "forgeMain", "forgeMake" })
                Assert.That(map.Require<GameButton>(key).transform.IsChildOf(body), Is.True, key);
            foreach (string key in new[] { "forgeEnhance", "forgeCount" })
                Assert.That(map.Require<TMP_InputField>(key).contentType, Is.EqualTo(TMP_InputField.ContentType.IntegerNumber));
            for (int index = 0; index < 4; index++)
            {
                var row = map.Require<CanvasGroup>("forgeRow" + index).transform;
                Assert.That(map.Require<GameButton>("forgeSub" + index).transform.IsChildOf(row), Is.True);
                Assert.That(map.Require<TMP_InputField>("forgeRolls" + index).transform.IsChildOf(row), Is.True);
                Assert.That(map.Require<TMP_InputField>("forgeValue" + index).transform.IsChildOf(row), Is.True);
                Assert.That(map.Require<GameText>("forgeRange" + index).transform.IsChildOf(row), Is.True);
            }
        }

        [TestCase(1920, 1080)]
        [TestCase(1440, 810)]
        [TestCase(1280, 720)]
        public void TabsMakeSpaceForSixRowsAndCanScrollTheLastChoiceIntoView(int width, int height)
        {
            var canvas = new GameObject("GM layout test", typeof(RectTransform), typeof(Canvas));
            try
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                // The home CanvasScaler matches its 1080 design height.
                float scale = height / 1080f;
                var screen = (RectTransform)canvas.transform;
                screen.sizeDelta = new Vector2(width / scale, height / scale);
                screen.localScale = Vector3.one * scale;
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), screen, false);
                var map = root.GetComponent<BindingMap>();
                map.Require<GameObject>("panel").SetActive(true);
                var kinds = map.Require<GameObject>("kinds");
                var filter = map.Require<TMP_InputField>("filter");
                var items = map.Require<ScrollRect>("items");
                var template = map.Require<GameButton>("row");
                var rows = new RectTransform[12];
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = Object.Instantiate(template, items.content, false);
                    row.gameObject.SetActive(true);
                    rows[i] = (RectTransform)row.transform;
                }
                float inventorySearchTop = 0;
                foreach (bool inventory in new[] { true, false })
                {
                    kinds.SetActive(inventory);
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
                    Canvas.ForceUpdateCanvases();
                    items.verticalNormalizedPosition = 1;
                    var viewport = BoundsOf(items.viewport);
                    var search = BoundsOf((RectTransform)filter.transform);
                    var status = BoundsOf(map.Require<GameText>("status").rectTransform);
                    var footer = BoundsOf((RectTransform)map.Require<GameButton>("close").transform);
                    Assert.That(search.height, Is.GreaterThanOrEqualTo(50 * scale));
                    Assert.That(BoundsOf(rows[0]).height, Is.GreaterThanOrEqualTo(52 * scale));
                    Assert.That(search.yMin, Is.GreaterThanOrEqualTo(viewport.yMax - .1f));
                    Assert.That(viewport.yMin, Is.GreaterThanOrEqualTo(status.yMax - .1f));
                    Assert.That(status.yMin, Is.GreaterThanOrEqualTo(footer.yMax - .1f));
                    if (inventory)
                    {
                        inventorySearchTop = search.yMax;
                        Assert.That(BoundsOf((RectTransform)kinds.transform).yMin, Is.GreaterThanOrEqualTo(search.yMax - .1f));
                    }
                    else Assert.That(search.yMax - inventorySearchTop, Is.GreaterThanOrEqualTo(50 * scale));
                    AssertFullyVisible(rows[0], viewport);
                    AssertFullyVisible(rows[5], viewport);
                    items.verticalNormalizedPosition = 0;
                    AssertFullyVisible(rows[rows.Length - 1], viewport);
                }
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void EntryHasOneVisibleButtonFaceBehindHelpAndGm()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var entry = prefab.GetComponent<BindingMap>().Require<GameButton>("entry");
            var face = (Image)entry.targetGraphic;
            Assert.That(face.sprite, Is.Not.Null);
            Assert.That(face.color.a, Is.GreaterThan(.85f));
            Assert.That(face.color.grayscale, Is.LessThan(.25f));
            var label = entry.GetComponentInChildren<GameText>(true);
            var glyph = System.Array.Find(entry.GetComponentsInChildren<Image>(true), image => image != face && image.sprite);
            Assert.That(glyph, Is.Not.Null);
            var background = BoundsOf(face.rectTransform);
            foreach (var visual in new[] { label.rectTransform, glyph.rectTransform })
            {
                Assert.That(visual.IsChildOf(face.transform), Is.True);
                var bounds = BoundsOf(visual);
                Assert.That(background.Contains(bounds.min) && background.Contains(bounds.max), Is.True);
            }
        }

        private static Rect BoundsOf(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void AssertFullyVisible(RectTransform row, Rect viewport)
        {
            var bounds = BoundsOf(row);
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - .15f));
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(viewport.yMax + .15f));
        }
    }
}
