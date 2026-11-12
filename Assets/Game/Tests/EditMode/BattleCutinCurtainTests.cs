using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleCutinCurtainTests
    {
        private GameObject root;
        private Camera camera;
        private object stage;
        private object asset;
        private TextAsset document;
        private Texture2D transparentFrame;
        private MeshRenderer actor;
        private Canvas hud;
        private Canvas pause;

        [SetUp] public void Create()
        {
            root = new GameObject("Cut-in curtain");
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(0, 0, -10);
            actor = new GameObject("Actor", typeof(MeshRenderer)).GetComponent<MeshRenderer>();
            actor.transform.SetParent(root.transform);
            hud = new GameObject("HUD", typeof(Canvas)).GetComponent<Canvas>();
            hud.transform.SetParent(root.transform);
            hud.sortingOrder = 100;
            pause = new GameObject("Pause", typeof(Canvas)).GetComponent<Canvas>();
            pause.transform.SetParent(root.transform);
            pause.sortingOrder = 31000;
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);
            stage = Activator.CreateInstance(type, camera, null, null);
            Call("Begin", actor.transform, new[] { actor.transform }, false);
            var assetType = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, AstralOath.Client.BattleScene", true);
            asset = Activator.CreateInstance(assetType);
            document = new TextAsset("{\"width\":1280,\"height\":723,\"frames\":[{\"durationSeconds\":0.4}]}");
            transparentFrame = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            transparentFrame.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear });
            transparentFrame.Apply();
            assetType.GetField("document").SetValue(asset, document);
            assetType.GetField("pages").SetValue(asset, new[] { transparentFrame });
        }

        [TearDown] public void Cleanup()
        {
            (stage as IDisposable)?.Dispose();
            if (root) Object.DestroyImmediate(root);
            if (document) Object.DestroyImmediate(document);
            if (transparentFrame) Object.DestroyImmediate(transparentFrame);
        }

        [Test] public void DefaultCutinPlacesAnOpaqueBlackCurtainBetweenHudAndFrames()
        {
            Call("PlayCutin", asset);
            var curtain = Curtain();
            Assert.That(curtain, Is.Not.Null, "Original transparent Cut-in regions need the source default curtain.");
            AssertColor(curtain, Color.black);
            var frames = camera.GetComponentsInChildren<MeshRenderer>()
                .Single(item => item.sharedMaterial.mainTexture == transparentFrame);
            Assert.That(curtain.gameObject.layer, Is.EqualTo(5), "The curtain must be excluded from world sprite_blur capture.");
            Assert.That(curtain.sortingOrder, Is.GreaterThan(hud.sortingOrder));
            Assert.That(curtain.sortingOrder, Is.LessThan(frames.sortingOrder));
            Assert.That(frames.sortingOrder, Is.LessThan(pause.sortingOrder));
            Assert.That(curtain.transform.localScale.x, Is.GreaterThanOrEqualTo(2f * camera.orthographicSize * camera.aspect));
            Assert.That(curtain.transform.localScale.y, Is.GreaterThanOrEqualTo(2f * camera.orthographicSize));
            Assert.That(actor.enabled, Is.True, "Curtain composition must not mutate world actor visibility.");
        }

        [TestCase("255,255,255,255", .8f, 1f, 1f, 1f, .8f)]
        [TestCase("0,66,116,229", .8f, 0f, 66f / 255f, 116f / 255f, .8f * 229f / 255f)]
        public void SourceCurtainColorAndOpacityAreApplied(string color, float opacity, float r, float g, float b, float a)
        {
            Call("PlayCutinWithNode", asset, new JObject { ["etty"] = "CUTIN", ["curtain_color"] = color, ["curtain_opacity"] = opacity });
            var curtain = Curtain();
            Assert.That(curtain, Is.Not.Null);
            AssertColor(curtain, new Color(r, g, b, a));
        }

        [Test] public void ExplicitZeroOpacityKeepsTransparentSourceCutinRegionsOpen()
        {
            Call("PlayCutinWithNode", asset, JObject.Parse("{'etty':'CUTIN','curtain_color':'0,0,0,255','curtain_opacity':0}"));
            Assert.That(Curtain(), Is.Null, "Explicit source zero must not be replaced with the opaque default.");
            Assert.That(actor.enabled, Is.True);
            Assert.That(camera.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(1));
        }

        [Test] public void CurtainUsesTheCutinWallClockAndReleasesWithItsLastFrame()
        {
            Call("PlayCutin", asset);
            Assert.That(Curtain(), Is.Not.Null);
            Call("TickCutin", .3f);
            Assert.That(Curtain(), Is.Not.Null, "Source time remains paused while the curtain follows Cut-in time.");
            AssertColor(Curtain(), Color.black);
            Call("TickCutin", .11f);
            Assert.That(camera.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
            Assert.That(actor.enabled, Is.True);
        }

        [TestCase("Reset")]
        [TestCase("Dispose")]
        public void InterruptionReleasesCurtainAndFramesWithoutChangingActors(string method)
        {
            Call("PlayCutin", asset);
            Assert.That(Curtain(), Is.Not.Null);
            Call(method);
            Assert.That(camera.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
            Assert.That(actor.enabled, Is.True);
        }

        private MeshRenderer Curtain() => camera.GetComponentsInChildren<MeshRenderer>()
            .SingleOrDefault(item => item.sharedMaterial && item.sharedMaterial.mainTexture == Texture2D.whiteTexture);
        private static void AssertColor(MeshRenderer renderer, Color expected)
        {
            var color = renderer.GetComponent<MeshFilter>().sharedMesh.colors[0];
            Assert.That(color.r, Is.EqualTo(expected.r).Within(.001f));
            Assert.That(color.g, Is.EqualTo(expected.g).Within(.001f));
            Assert.That(color.b, Is.EqualTo(expected.b).Within(.001f));
            Assert.That(color.a, Is.EqualTo(expected.a).Within(.001f));
        }
        private void Call(string name, params object[] args)
        {
            var method = stage.GetType().GetMethod(name);
            Assert.That(method, Is.Not.Null, "The source CUTIN node must reach the cinematic stage.");
            method.Invoke(stage, args);
        }
    }
}
