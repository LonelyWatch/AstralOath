using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleDarkLifetimeTests
    {
        private GameObject root;
        private Camera camera;
        private object stage;

        [SetUp] public void Create()
        {
            root = new GameObject("Source DARK lifetime");
            camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0, 0, -10);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);
            stage = Activator.CreateInstance(type, camera, null, null);
            Call("Begin", root.transform, Array.Empty<Transform>(), false);
        }

        [TearDown] public void Cleanup()
        {
            (stage as IDisposable)?.Dispose();
            if (root) Object.DestroyImmediate(root);
        }

        [Test] public void OriginalAngelicaDarkHoldsUntilDurationThenConsumesFullFadeOut()
        {
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1062/Configs/stagept/c_angelica_skill_03.graph.json");
            Assert.That(graph, Is.Not.Null);
            var node = JObject.Parse(graph.text)["source"]["entities"].OfType<JObject>()
                .Single(item => (string)item["etty"] == "DARK");
            Assert.That((float)node["duration"], Is.EqualTo(2500));
            Assert.That((float)node["fadeOut"], Is.EqualTo(700));
            Call("Play", node);
            Call("Tick", 2.4f);
            Assert.That(Alpha(), Is.EqualTo(1f).Within(.001f),
                "The original 2500ms is the fade-out start, not the end of its 700ms fade.");
            Call("Tick", .1f);
            Assert.That(Alpha(), Is.EqualTo(1f).Within(.001f));
            Call("Tick", .35f);
            Assert.That(Alpha(), Is.EqualTo(.5f).Within(.001f));
            Call("Tick", .34f);
            Assert.That(DarkMesh(), Is.Not.Null);
            Assert.That(Alpha(), Is.InRange(0f, .02f));
            Call("Tick", .02f);
            Assert.That(DarkMesh(), Is.Null, "The quad is released only after duration + fadeOut.");
        }

        [Test] public void ShortHoldCanReachFullOpacityBeforeItsLongerFadeOut()
        {
            // Source c_alensia_skill_03 #39 has duration=100, fadeOut=300, opacity=1.
            Call("Play", JObject.Parse("{'etty':'DARK','color':'0,0,0,255','opacity':1,'duration':100,'fadeOut':300}"));
            Assert.That(Alpha(), Is.EqualTo(1f).Within(.001f),
                "A source hold shorter than fadeOut must not start partially transparent.");
            Call("Tick", .1f);
            Assert.That(Alpha(), Is.EqualTo(1f).Within(.001f));
            Call("Tick", .15f);
            Assert.That(Alpha(), Is.EqualTo(.5f).Within(.001f));
            Call("Tick", .16f);
            Assert.That(DarkMesh(), Is.Null);
        }

        [Test] public void IndefiniteDarkUsesStopAsTheFadeOutStart()
        {
            Call("Play", JObject.Parse("{'etty':'DARK','id':'d1','color':'0,0,0,255','opacity':0.7,'duration':-1,'fadeIn':200,'fadeOut':200}"));
            Call("Tick", 5f);
            Assert.That(Alpha(), Is.EqualTo(.7f).Within(.001f));
            Call("Play", JObject.Parse("{'etty':'STOP','targetId':'d1'}"));
            Call("Tick", .1f);
            Assert.That(Alpha(), Is.EqualTo(.35f).Within(.001f));
            Call("Tick", .11f);
            Assert.That(DarkMesh(), Is.Null);
        }

        [TestCase(2500, 700, 3.2f)]
        [TestCase(100, 300, .4f)]
        [TestCase(-1, 200, 0f)]
        public void GraphCompletionIncludesTheFiniteDarkFadeOut(int duration, int fadeOut, float expected)
        {
            var runtime = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation, AstralOath.Client.BattleScene", true);
            var asset = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, AstralOath.Client.BattleScene", true);
            using var presenter = (IDisposable)Activator.CreateInstance(runtime,
                new object[] { root.transform, Array.Empty<TextAsset>(), Array.CreateInstance(asset, 0), null, null, null });
            var node = new JObject { ["etty"] = "DARK", ["duration"] = duration, ["fadeOut"] = fadeOut };
            float completion = (float)runtime.GetMethod("Completion", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(presenter, new object[] { node });
            Assert.That(completion, Is.EqualTo(expected).Within(.0001f),
                "The graph must not terminate a finite DARK before its visible fade completes; an indefinite DARK waits for STOP.");
        }

        private Mesh DarkMesh() => root.GetComponentsInChildren<MeshFilter>()
            .FirstOrDefault(item => item.name.StartsWith("DARK ", StringComparison.Ordinal))?.sharedMesh;
        private float Alpha()
        {
            var mesh = DarkMesh();
            Assert.That(mesh, Is.Not.Null, "The source DARK still owns its visual lifetime.");
            return mesh.colors[0].a;
        }
        private void Call(string name, params object[] args) => stage.GetType().GetMethod(name).Invoke(stage, args);
    }
}
