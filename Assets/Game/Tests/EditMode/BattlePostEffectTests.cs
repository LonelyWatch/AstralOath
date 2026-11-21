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
        public void DingosDarkPlateReproducesTheGraphsDisplayColor()
        {
            const string path = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1021/Configs/stagept/c_dinggo_skill_03.graph.json";
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            var plate = graph["source"]["entities"].OfType<JObject>().Single(node => (int)node["guid"] == 20);
            Assert.That((string)plate["color"], Is.EqualTo("255,53,0,255"));
            var root = new GameObject("Dingo plate camera", typeof(Camera));
            try
            {
                var camera = root.GetComponent<Camera>();
                camera.orthographic = true;
                var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);
                using var stage = (IDisposable)Activator.CreateInstance(type, camera, null, null);
                type.GetMethod("Begin").Invoke(stage, new object[] { null, Array.Empty<Transform>(), false });
                type.GetMethod("Play").Invoke(stage, new object[] { plate });
                type.GetMethod("Tick").Invoke(stage, new object[] { .1f });
                var color = root.GetComponentsInChildren<MeshFilter>().Single(item => item.name.StartsWith("DARK ")).sharedMesh.colors[0];
                var display = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.gamma : color;
                Assert.That((Color32)display, Is.EqualTo(new Color32(255, 53, 0, 255)));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void DingosForegroundSmokeUsesTheSkeletalSlotsAuthoredColorSpace()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = Type.GetType("AstralOath.Client.BattleScene.Editor.SkillTimelinePreviewSession, AstralOath.Client.BattleScene.Editor", true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("LoadScenario", flags).Invoke(session, new object[]
                { "c1021", "c_dinggo_skill_03", null, null, false, true, true, false, false });
            type.GetMethod("Seek", flags).Invoke(session, new object[] { 3.5f });
            type.GetMethod("Render", flags).Invoke(session, null);
            var scene = (UnityEngine.SceneManagement.Scene)type.GetField("scene", flags).GetValue(session);
            var smoke = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>())
                .Single(renderer => renderer.name == "dingo_eff_sk3_p2");
            var slots = smoke.GetComponent<MeshFilter>().sharedMesh.colors32;
            Assert.That(slots, Does.Contain(new Color32(255, 165, 88, 255)), "The orange fire slot is active.");
            Assert.That(slots, Does.Contain(new Color32(255, 255, 255, 255)), "The neutral slot is active.");
            var properties = new MaterialPropertyBlock();
            smoke.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_SrgbVertexTint"), Is.EqualTo(QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f));
        }

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

        [TestCase("02", 31, 25f, .175f, 17.67767f)]
        [TestCase("03", 36, 7.725425f, .2222222f, 25f)]
        public void SerilasBlurTakesItsRangeFromTheConnectedSine(string skill, int id,
            float initial, float sampleTime, float expected)
        {
            string path = $"Assets/Game/Modules/CharacterPresentation/Res/Characters/c1040/Configs/stagept/c_serilla_skill_{skill}.graph.json";
            var graph = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text)["source"];
            var nodes = graph["entities"].OfType<JObject>().ToDictionary(node => (int)node["guid"]);
            var edge = graph["connections"].Single(item => (int)item["from"] == id && (int?)item["when"] == 2);
            var blur = nodes[id];
            Assert.That(blur["u_range"], Is.Null);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleSourcePostEffect, AstralOath.Client.BattleScene");
            effect = (IDisposable)Activator.CreateInstance(type, new object[] { null });
            Call("Play", blur, new[] { nodes[(int)edge["to"]] });
            Assert.That(ActiveRange(), Is.EqualTo(initial).Within(.001f));
            Call("Tick", sampleTime);
            Assert.That(ActiveRange(), Is.EqualTo(expected).Within(.001f));
            Call("Tick", .3f);
            Assert.That(ActiveRange(), Is.Null);
            Call("Play", blur, Array.Empty<JObject>());
            Assert.That(ActiveRange(), Is.Null, "A missing base range needs a valid range provider.");
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
