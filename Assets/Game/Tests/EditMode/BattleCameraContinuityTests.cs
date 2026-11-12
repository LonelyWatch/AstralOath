using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleCameraContinuityTests
    {
        private GameObject root;
        private Camera camera;
        private Transform actor;
        private GameObject background;
        private object stage;
        private object successor;
        private Texture2D texture;
        private Sprite sprite;
        private Type StageType => Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);

        [SetUp] public void Create()
        {
            root = new GameObject("Camera continuity");
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            actor = new GameObject("Actor").transform;
            actor.SetParent(root.transform);
            actor.position = new Vector3(4, -3, 0);
            background = new GameObject("Map", typeof(SpriteRenderer));
            background.transform.SetParent(root.transform);
            texture = new Texture2D(2, 2);
            sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 1);
            background.GetComponent<SpriteRenderer>().sprite = sprite;
            background.transform.localScale = new Vector3(15, 10, 1);
            stage = NewStage();
            Call(stage, "Begin", actor, new[] { actor }, false);
        }

        [TearDown] public void Cleanup()
        {
            (stage as IDisposable)?.Dispose();
            (successor as IDisposable)?.Dispose();
            if (root) Object.DestroyImmediate(root);
            if (sprite) Object.DestroyImmediate(sprite);
            if (texture) Object.DestroyImmediate(texture);
        }

        [Test] public void PanStartsFromTheDisplayedMapConstrainedPosition()
        {
            Play(stage, "{'etty':'CAM','x':10000}");
            float displayedX = camera.transform.position.x;
            Play(stage, "{'etty':'CAM','location':0,'time':1000}");
            Assert.That(camera.transform.position.x, Is.EqualTo(displayedX).Within(.001f));
            Call(stage, "Tick", .1f);
            Assert.That(camera.transform.position.x, Is.LessThan(displayedX - .1f),
                "A new pan must move immediately from the displayed pose, not wait behind the map clamp.");
        }

        [Test] public void ZoomStartsFromTheDisplayedMapConstrainedSize()
        {
            Play(stage, "{'etty':'ZOOM','scale':0.25}");
            float displayedSize = camera.orthographicSize;
            Play(stage, "{'etty':'ZOOM','scale':1,'time':1000}");
            Call(stage, "Tick", .1f);
            Assert.That(camera.orthographicSize, Is.LessThan(displayedSize - .1f));
        }

        [Test] public void HandoffKeepsPoseAndOriginalBaselineWhileOldTailCannotMoveCamera()
        {
            Play(stage, "{'etty':'CAM','x':300,'time':0}");
            Play(stage, "{'etty':'ZOOM','scale':1.4,'time':0}");
            Vector3 position = camera.transform.position;
            float size = camera.orthographicSize;
            successor = NewStage();
            Call(stage, "HandoffTo", successor);
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Call(successor, "Begin", actor, new[] { actor }, false);
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Assert.That(camera.orthographicSize, Is.EqualTo(size));
            Play(stage, "{'etty':'CAM','x':-900}");
            Call(stage, "Tick", 1f);
            Call(stage, "Reset");
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Play(successor, "{'etty':'CAM','location':0}");
            Play(successor, "{'etty':'ZOOM','scale':1}");
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0, 0, -10)));
            Assert.That(camera.orthographicSize, Is.EqualTo(5f));
        }

        [Test] public void ReturnToBaselineInterpolatesAndResetRestoresTheSameBaseline()
        {
            Play(stage, "{'etty':'CAM','x':300}");
            Play(stage, "{'etty':'ZOOM','scale':1.4}");
            Vector3 position = camera.transform.position;
            Call(stage, "ReturnToBaseline", .2f);
            Assert.That(camera.transform.position, Is.EqualTo(position));
            Call(stage, "Tick", .1f);
            Assert.That(camera.transform.position.x, Is.InRange(.01f, position.x - .01f));
            Call(stage, "Tick", .1f);
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0, 0, -10)));
            Assert.That(camera.orthographicSize, Is.EqualTo(5f).Within(.001f));
            Call(stage, "Reset");
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0, 0, -10)));
        }

        [Test] public void ExplicitZeroDurationSourceCameraRemainsAnImmediateCut()
        {
            Play(stage, "{'etty':'CAM','x':300,'time':0}");
            Assert.That(camera.transform.position.x, Is.EqualTo(3f).Within(.001f));
        }

        [Test] public void CutinClockAdvancesFramesWithoutAdvancingCameraOrDark()
        {
            var assetType = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, AstralOath.Client.BattleScene", true);
            var asset = Activator.CreateInstance(assetType);
            var document = new TextAsset("{\"frames\":[{\"durationSeconds\":0.2},{\"durationSeconds\":0.2}]}");
            assetType.GetField("document").SetValue(asset, document);
            assetType.GetField("pages").SetValue(asset, new[] { Texture2D.whiteTexture, Texture2D.blackTexture });
            try
            {
                Play(stage, "{'etty':'CAM','x':300,'time':1000}");
                Play(stage, "{'etty':'DARK','duration':200,'opacity':1}");
                Call(stage, "PlayCutin", asset);
                Call(stage, "TickCutin", .3f);
                Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0, 0, -10)));
                var meshes = root.GetComponentsInChildren<MeshRenderer>();
                Assert.That(Array.Exists(meshes, item => item.name.StartsWith("DARK ")), Is.True,
                    "World layers must retain their source-clock lifetime during Cut-in.");
                var overlay = Array.Find(meshes, item => item.name.StartsWith("CUTIN "));
                Assert.That(overlay.sharedMaterial.mainTexture, Is.SameAs(Texture2D.blackTexture));
                var remaining = StageType.GetProperty("CutinRemainingSeconds");
                Assert.That(remaining, Is.Not.Null);
                Assert.That((float)remaining.GetValue(stage), Is.EqualTo(.1f).Within(.001f));
                Call(stage, "TickCutin", .1f);
                Call(stage, "Tick", .1f);
                Assert.That(camera.transform.position.x, Is.EqualTo(.3f).Within(.001f));
            }
            finally { Object.DestroyImmediate(document); }
        }
        private object NewStage() => Activator.CreateInstance(StageType, camera, (Action<bool>)(_ => { }), background);
        private static void Play(object target, string node) => Call(target, "Play", JObject.Parse(node));
        private static void Call(object target, string method, params object[] args)
        {
            var operation = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(operation, Is.Not.Null, method + " must exist for continuous presentation ownership.");
            operation.Invoke(target, args);
        }
    }
}
