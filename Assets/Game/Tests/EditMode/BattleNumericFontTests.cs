using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleNumericFontTests
    {
        private const string Module = "Assets/Game/Modules/BattleScene";

        [TestCase("BattleHpDigits")]
        [TestCase("BattleDamageDigits")]
        public void NumericFontContainsDrawableUnderlineInPrimaryAtlas(string name)
        {
            var font = Load(name);
            Assert.That(font.characterLookupTable.TryGetValue('_', out var underline), Is.True,
                "TMP looks up U+005F only in the primary font, even when the label does not use underlining.");
            var glyph = underline.glyph;
            Assert.That(glyph.metrics.width, Is.GreaterThan(0));
            Assert.That(glyph.metrics.height, Is.GreaterThan(0));
            Assert.That(glyph.glyphRect.width, Is.GreaterThan(0));
            Assert.That(glyph.glyphRect.height, Is.GreaterThan(0));
            Assert.That(font.faceInfo.underlineThickness, Is.GreaterThan(0), "An empty compatibility glyph must not hide the warning.");
            var atlas = font.atlasTextures[glyph.atlasIndex];
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(readable.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(atlas))), Is.True);
                var rect = glyph.glyphRect;
                Assert.That(readable.GetPixels(rect.x, rect.y, rect.width, rect.height).Any(pixel => pixel.a > 0.5f), Is.True,
                    "The compatibility underline must point to visible atlas pixels.");
            }
            finally { Object.DestroyImmediate(readable); }
        }

        [TestCase("BattleHpDigits")]
        [TestCase("BattleDamageDigits")]
        public void AssigningAndRenderingGameTextDoesNotProduceFontWarnings(string name)
        {
            var font = Load(name);
            var warnings = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Warning && message.Contains(name)) warnings.Add(message);
            }
            var root = new GameObject("Numeric Font Regression", typeof(Canvas));
            Application.logMessageReceived += Capture;
            try
            {
                var child = new GameObject("Digits", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                var label = child.AddComponent<GameText>();
                label.font = font;
                label.fontSharedMaterial = font.material;
                label.fontSize = font.faceInfo.pointSize;
                label.rectTransform.sizeDelta = new Vector2(600, 200);
                label.fontStyle = FontStyles.Underline;
                label.text = "123";
                label.ForceMeshUpdate();
                Assert.That(warnings, Is.Empty, "Font assignment and underline drawing must both be warning-free.");
                // TMP 3.0.7 deliberately does not add decoration vertices to meshInfo.vertexCount
                // (TMPro_UGUI_Private.cs, last_vert_index). Inspect the uploaded mesh instead.
                int underlineStart = label.textInfo.characterInfo[0].underlineVertexIndex;
                Assert.That(underlineStart, Is.GreaterThanOrEqualTo(12));
                var mesh = label.mesh;
                Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(underlineStart + 12));
                CollectionAssert.IsSubsetOf(Enumerable.Range(underlineStart, 12).ToArray(), mesh.triangles,
                    "The uploaded mesh must actually reference all three underline quads.");
                var vertices = mesh.vertices;
                var colors = mesh.colors32;
                for (int index = underlineStart; index < underlineStart + 12; index += 4)
                {
                    Assert.That(Mathf.Abs(vertices[index].y - vertices[index + 1].y), Is.GreaterThan(0.01f),
                        "Every underline segment must have nonzero height.");
                    Assert.That(Mathf.Abs(vertices[index + 2].x - vertices[index + 1].x), Is.GreaterThan(0.01f));
                    Assert.That(colors[index].a, Is.GreaterThan(0), "Underline geometry must be visible.");
                }
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                Object.DestroyImmediate(root);
            }
        }

        [TestCase("BattleHpDigits", "epic7num")]
        [TestCase("BattleDamageDigits", "damage")]
        public void OriginalBitmapCharactersKeepTheirSourceRectanglesAndAdvance(string name, string source)
        {
            var font = Load(name);
            var description = AssetDatabase.LoadAssetAtPath<TextAsset>(Module + "/Res/Textures/Hud/Fonts/" + source + ".fnt.txt");
            foreach (string line in description.text.Split('\n').Where(line => line.StartsWith("char ", StringComparison.Ordinal)))
            {
                uint unicode = (uint)Metric(line, "id");
                var glyph = font.characterLookupTable[unicode].glyph;
                var rect = glyph.glyphRect;
                Assert.That(rect.x, Is.EqualTo(Metric(line, "x")), unicode.ToString());
                Assert.That(rect.y, Is.EqualTo(font.atlasTexture.height - Metric(line, "y") - Metric(line, "height")));
                Assert.That(rect.width, Is.EqualTo(Metric(line, "width")));
                Assert.That(rect.height, Is.EqualTo(Metric(line, "height")));
                Assert.That(glyph.metrics.horizontalAdvance, Is.EqualTo(Metric(line, "xadvance")));
                Assert.That(glyph.metrics.horizontalBearingX, Is.EqualTo(Metric(line, "xoffset")));
            }
        }

        private static TMP_FontAsset Load(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Module + "/Res/Fonts/" + name + ".asset");
            Assert.That(asset, Is.Not.Null);
            asset.ReadFontAssetDefinition();
            return asset;
        }

        private static int Metric(string line, string key) =>
            int.Parse(Regex.Match(line, @"(?:^|\s)" + key + @"=(-?\d+)(?:\s|$)").Groups[1].Value);
    }
}