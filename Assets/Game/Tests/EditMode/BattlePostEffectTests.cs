using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattlePostEffectTests
    {
        private const string RasThirdSkill = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/Configs/stagept/c_ras_skill_03.graph.json";
        private IDisposable effect;

        [TearDown]
        public void Cleanup() => effect?.Dispose();

        [Test]
        public void SourceSineModulationSharesTheBlurWindow()
        {
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(RasThirdSkill).text);
            var nodes = graph["source"]["entities"].OfType<JObject>().ToDictionary(node => (int)node["guid"]);
            var edge = graph["source"]["connections"].Single(item => (int)item["from"] == 33 && (int?)item["when"] == 2);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            Assert.That(type, Is.Not.Null, "Battle playback needs a source-driven post-effect lifetime.");

            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });
            Call("Play", nodes[33], new[] { nodes[(int)edge["to"]] });
            Assert.That(ActiveRange(), Is.EqualTo(150f).Within(.001f),
                "The hit blur starts with its source amplitude at phase .5.");
            Call("Tick", .15f);
            Assert.That(ActiveRange(), Is.EqualTo(135.355f).Within(.001f));
            Call("Tick", .15f);
            Assert.That(ActiveRange(), Is.Null, "The shader is released when the 300 ms source interval ends.");
        }

        [Test]
        public void KensInversionUsesItsOwnWindowWhenABlurIsAlreadyActive()
        {
            const string path = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1047/Configs/stagept/c_ken_skill_03.graph.json";
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            var invert = graph["source"]["entities"].OfType<JObject>().Single(node => (string)node["program"] == "sprite_invert");
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });
            var blur = Blur("underlying", 40f);
            blur["duration"] = 1500;
            Call("Play", blur, Array.Empty<JObject>());
            Assert.That(type.GetMethod("Play").Invoke(effect, new object[] { invert, Array.Empty<JObject>() }), Is.True);
            var inverted = type.GetProperty("IsInverted", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(inverted.GetValue(effect), Is.True);
            Assert.That(ActiveRange(), Is.EqualTo(40f));
            Call("Tick", .99f);
            Assert.That(inverted.GetValue(effect), Is.True);
            Call("Tick", .02f);
            Assert.That(inverted.GetValue(effect), Is.False);
            Assert.That(ActiveRange(), Is.EqualTo(40f));
            Call("Play", invert, Array.Empty<JObject>());
            Call("Stop", "pp:" + invert["guid"]);
            Assert.That(inverted.GetValue(effect), Is.False);
        }

        [Test]
        public void InversionComplementsDisplayRgbAndPreservesAlpha()
        {
            var shader = Resources.Load<Shader>("BattleSourcePostEffect");
            var material = new Material(shader);
            var sample = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var result = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                float green = linear ? Mathf.GammaToLinearSpace(.25f) : .25f;
                sample.SetPixel(0, 0, new Color(0f, green, 1f, .42f));
                sample.Apply();
                material.SetFloat("_Invert", 1f);
                Graphics.Blit(sample, target, material);
                RenderTexture.active = target;
                result.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                var pixel = result.GetPixel(0, 0);
                Assert.That(pixel.r, Is.EqualTo(1f).Within(.01f));
                Assert.That(pixel.g, Is.EqualTo(linear ? Mathf.GammaToLinearSpace(.75f) : .75f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(0f).Within(.01f));
                Assert.That(pixel.a, Is.EqualTo(.42f).Within(.01f));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(sample);
                UnityEngine.Object.DestroyImmediate(result);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void StoppingOneBlurKeepsTheOtherSourceWindow()
        {
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            Assert.That(type, Is.Not.Null);
            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });

            Call("Play", Blur("first", 40f), Array.Empty<JObject>());
            Call("Play", Blur("second", 70f), Array.Empty<JObject>());
            Assert.That(ActiveRange(), Is.EqualTo(70f));

            Call("Stop", "second");
            Assert.That(ActiveRange(), Is.EqualTo(40f));
            Call("Reset");
            Assert.That(ActiveRange(), Is.Null);
        }

        private float? ActiveRange() => (float?)effect.GetType()
            .GetProperty("ActiveRange", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effect);

        private void Call(string method, params object[] args) => effect.GetType().GetMethod(method).Invoke(effect, args);

        private static JObject Blur(string id, float range) => new JObject
        {
            ["id"] = id,
            ["program"] = "sprite_blur",
            ["u_range"] = range,
            ["u_sample"] = 3,
            ["direction_y"] = 1,
            ["duration"] = 500
        };
    }
}
