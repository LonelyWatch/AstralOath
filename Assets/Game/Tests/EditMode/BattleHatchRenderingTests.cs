using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleHatchRenderingTests
    {
        private const string TextureRoot = "Assets/Game/Modules/BattleScene/Res/Effects/Cinematic/";

        [Test]
        public void TransparentWhiteSourceTexelsDoNotBecomeOpaqueSpeedLines()
        {
            var texture = Solid(new Color(1f, 1f, 1f, 0f));
            try
            {
                var actual = Render(texture, Color.white, 1f, Color.black, Vector2.one * .5f);
                AssertRgb(actual, Color.black, "The source PNG stores streak intensity in alpha, not RGB.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(1f)]
        [TestCase(.4f)]
        public void SourceTextureTintAndLayerOpacityAreMultipliedOnlyOnce(float opacity)
        {
            var source = new Color(1f, 1f, 1f, 64f / 255f);
            var tint = new Color(1f, 0f, 0f, .5f);
            var background = new Color(.1f, .2f, .3f, 1f);
            var texture = Solid(source);
            try
            {
                var actual = Render(texture, tint, opacity, background, Vector2.one * .5f);
                float alpha = tint.a * opacity;
                Color expected = tint * (source.a * alpha) + background * (1f - alpha);
                AssertRgb(actual, expected, "Cocos PNG premultiplication and premultiplied sprite blending must both be retained.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(.4f)]
        [TestCase(.2f)]
        public void ThreeLayerAlphaSaturatesCoverageWithoutMultiplyingTheColorSumAgain(float opacity)
        {
            var textureAlphas = new[] { 64f / 255f, 128f / 255f, 192f / 255f };
            var sources = new[] { Solid(new Color(1f, 1f, 1f, textureAlphas[0])),
                Solid(new Color(1f, 1f, 1f, textureAlphas[1])),
                Solid(new Color(1f, 1f, 1f, textureAlphas[2])) };
            var tints = new[] { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, .75f),
                new Color(0f, 0f, 1f, 1f) };
            var background = new Color(.1f, .2f, .3f, .25f);
            try
            {
                var actual = Render(sources, tints, opacity, background, Vector2.one * .5f);
                const float colorAlphaSum = 2.75f;
                float coverage = Mathf.Clamp01(colorAlphaSum * opacity);
                var expected = new Color(textureAlphas[0] * tints[0].a,
                    textureAlphas[1] * tints[1].a, textureAlphas[2] * tints[2].a) * opacity;
                expected += background * (1f - coverage);
                AssertRgb(actual, expected,
                    "All three original hatch layers must sum in premultiplied space before coverage is saturated.");
                Assert.That(actual.a, Is.EqualTo(coverage + background.a * (1f - coverage)).Within(.008f),
                    "At fade .4 the accumulated alpha is 1.1: coverage must be opaque without boosting RGB. " +
                    "At fade .2 the accumulated alpha is .55: the background must remain partially visible.");
            }
            finally { foreach (var source in sources) Object.DestroyImmediate(source); }
        }
        [TestCase(1, 512, 512)]
        [TestCase(2, 512, 512)]
        [TestCase(3, 512, 512)]
        public void ImportedSourceHatchKeepsItsOriginalLowIntensity(int layer, int x, int y)
        {
            string path = TextureRoot + "bg_hatch_" + layer + ".png";
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(imported, Is.Not.Null);
            var original = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(original.LoadImage(File.ReadAllBytes(path)), Is.True);
                var sample = original.GetPixel(x, y);
                var tint = Color.white;
                var uv = new Vector2((x + .5f) / original.width, (y + .5f) / original.height);
                var actual = Render(imported, tint, 1f, Color.black, uv);
                var linearSample = sample.linear;
                var expected = new Color(linearSample.r * tint.r * sample.a,
                    linearSample.g * tint.g * sample.a, linearSample.b * tint.b * sample.a, 1f);
                AssertRgb(actual, expected, "Raw hatch PNG alpha must survive the shader port.");
            }
            finally { Object.DestroyImmediate(original); }
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return texture;
        }

        private static Color Render(Texture2D source, Color tint, float opacity, Color clear, Vector2 uv) =>
            Render(new[] { source, Texture2D.blackTexture, Texture2D.blackTexture },
                new[] { tint, Color.clear, Color.clear }, opacity, clear, uv);

        private static Color Render(Texture2D[] sources, Color[] tints, float opacity, Color clear, Vector2 uv)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Pixel regression requires a graphics-enabled Unity test run.");
            var shader = Resources.Load<Shader>("BattleCinematicHatch");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var material = new Material(shader);
            var mesh = new Mesh();
            var target = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            var commands = new CommandBuffer { name = "Source BGHATCH pixel regression" };
            try
            {
                material.SetTexture("_MainTex", sources[0]);
                material.SetTexture("_Texture1", sources[1]);
                material.SetTexture("_Texture2", sources[2]);
                // Primary/white endpoints keep the fixture independent of Color-property sRGB conversion.
                material.SetVector("_FragColor0", tints[0]);
                material.SetVector("_FragColor1", tints[1]);
                material.SetVector("_FragColor2", tints[2]);
                mesh.vertices = new[] { new Vector3(-1f, -1f), new Vector3(1f, -1f),
                    new Vector3(1f, 1f), new Vector3(-1f, 1f) };
                mesh.uv = new[] { uv, uv, uv, uv };
                var vertex = new Color(1f, 1f, 1f, opacity);
                mesh.colors = new[] { vertex, vertex, vertex, vertex };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                commands.DrawMesh(mesh, Matrix4x4.identity, material);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
                readback.Apply();
                return readback.GetPixel(8, 8);
            }
            finally
            {
                RenderTexture.active = previous;
                commands.Release();
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        private static void AssertRgb(Color actual, Color expected, string reason)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.008f), reason + " Red.");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.008f), reason + " Green.");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.008f), reason + " Blue.");
        }
    }
}