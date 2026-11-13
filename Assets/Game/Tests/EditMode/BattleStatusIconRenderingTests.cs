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
        [TestCase("itxt_num3")]
        public void NativeHudGlyphsKeepLosslessSamplingAfterImport(string name)
        {
            AssertLosslessGlyph(Root + name + ".png");
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

        private static void AssertLosslessGlyph(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path);
            Assert.That(importer.userData, Is.EqualTo("AstralOath.ImportOverride"), path);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
            Assert.That(importer.mipmapEnabled, Is.False, path);
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "Android" })
                Assert.That(importer.GetPlatformTextureSettings(platform).textureCompression,
                    Is.EqualTo(TextureImporterCompression.Uncompressed), path + " (" + platform + ")");
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
