using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleStatusIconRenderingTests
    {
        private const string Hud = "Assets/Game/Modules/BattleScene/Res/Textures/Hud/";
        private const string Root = Hud + "Statuses/";

        [TestCase("stic_att_up")]
        [TestCase("stic_protect")]
        [TestCase("stic_speed_up")]
        [TestCase("itxt_num3")]
        public void OverheadStatusGlyphsKeepUncompressedBilinearSamplingAfterImport(string name)
        {
            AssertLosslessGlyph(Root + name + ".png", FilterMode.Bilinear);
        }

        [Test]
        public void ReadinessDetailsKeepStatusGlyphsSharp()
        {
            AssertLosslessGlyph(Hud + "Readiness/stic_protect.png");
        }

        [Test]
        public void LevelUsesTheTenLosslessBoldSourceDigits()
        {
            for (int digit = 0; digit < 10; digit++)
                AssertLosslessGlyph(Hud + "itxt_num" + digit + "_b.png");
        }

        private static void AssertLosslessGlyph(string path, FilterMode sampling = FilterMode.Point)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.userData, Is.EqualTo("AstralOath.ImportOverride"), path);
            Assert.That(importer.filterMode, Is.EqualTo(sampling), path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "Android" })
                Assert.That(importer.GetPlatformTextureSettings(platform).textureCompression,
                    Is.EqualTo(TextureImporterCompression.Uncompressed), path + " (" + platform + ")");
        }

        [TestCase(37)]
        [TestCase(53)]
        public void FractionalScaleInterpolatesAcrossGlyphAndCornerWithoutChangingDarkBacking(int width)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This test samples graphics output.");

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "stic_protect.png");
            var target = RenderTexture.GetTemporary(width, width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(width, width, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                GL.Clear(false, true, Color.white);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0f, width, width, 0f);
                    Graphics.DrawTexture(new Rect(0f, 0f, width, width), sprite.texture, new Rect(0f, 0f, 1f, 1f),
                        0, 0, 0, 0, Color.white, Graphic.defaultGraphicMaterial);
                }
                finally { GL.PopMatrix(); }
                image.ReadPixels(new Rect(0, 0, width, width), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                Color32 Pixel(int x, int y) => pixels[(width - 1 - y) * width + x];
                // In the source, the left edge of the shield glyph at y=14 crosses texels
                // x=4 and x=5. Sample between their centers on a non-integer scale.
                int sampleY = Mathf.FloorToInt(14.5f * width / 28f);
                int edgeX = Mathf.FloorToInt(5f * width / 28f);
                Color32 edge = Pixel(edgeX, sampleY);
                Assert.That((int)edge.b - edge.r, Is.InRange(100, 175),
                    "A fractional sample at a glyph edge must blend blue and black.");
                Assert.That(Pixel(width / 2, width / 6).r, Is.EqualTo(64).Within(6));
                Assert.That(Pixel(0, 0).r, Is.GreaterThan(245));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }

        [TestCase("stic_protect", 15, 5, 64)]
        [TestCase("stic_speed_dn", 15, 5, 51)]
        public void IconBackingLooksDarkOverWhiteAndRetainsSoftCorners(string name, int x, int y, int expected)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This test samples graphics output.");
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));
            string path = Root + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(sprite, Is.Not.Null);
            var target = RenderTexture.GetTemporary(28, 28, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var image = new Texture2D(28, 28, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                GL.Clear(false, true, Color.white);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0f, 28f, 28f, 0f);
                    Graphics.DrawTexture(new Rect(0f, 0f, 28f, 28f), sprite.texture, new Rect(0f, 0f, 1f, 1f),
                        0, 0, 0, 0, Color.white, Graphic.defaultGraphicMaterial);
                }
                finally { GL.PopMatrix(); }
                image.ReadPixels(new Rect(0, 0, 28, 28), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                Assert.That(pixels[(27 - y) * 28 + x].r, Is.EqualTo(expected).Within(4),
                    "On white, the nominal backing should keep the source's display-space transmittance.");
                Assert.That(pixels[27 * 28].r, Is.GreaterThan(245), "Rounded corners must stay transparent.");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
