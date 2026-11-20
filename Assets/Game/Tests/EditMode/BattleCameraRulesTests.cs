using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
namespace AstralOath.Tests
{
    public sealed class BattleCameraRulesTests
    {
        private GameObject root;
        private Camera camera;
        private object stage;
        private Sprite sprite;
        private Texture2D texture;
        [TearDown] public void Cleanup()
        {
            (stage as IDisposable)?.Dispose();
            if (root) Object.DestroyImmediate(root);
            if (sprite) Object.DestroyImmediate(sprite);
            if (texture) Object.DestroyImmediate(texture);
        }
        private Transform Create(bool map)
        {
            root = new GameObject("Camera rules");
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 16f/9f;
            var target = new GameObject("Target").transform; target.SetParent(root.transform); target.position = new Vector3(4,-3,0);
            GameObject background = null;
            if (map)
            {
                background = new GameObject("Map", typeof(SpriteRenderer)); background.transform.SetParent(root.transform);
                texture = new Texture2D(2,2); texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0,0,2,2), Vector2.one*.5f, 1f);
                background.GetComponent<SpriteRenderer>().sprite = sprite; background.transform.localScale = new Vector3(15,10,1);
            }
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);
            stage = Activator.CreateInstance(type, camera, (Action<bool>)(value=>{}), background);
            Call("Begin", target, new[]{target}, false); return target;
        }
        [Test] public void FocusingKeepsTheTargetsOriginalGroundFramingWhileZooming()
        {
            var target=Create(false); float y=camera.WorldToViewportPoint(target.position).y;
            Call("Play", JObject.Parse("{'etty':'CAM','location':12,'time':200}"));
            Call("Play", JObject.Parse("{'etty':'ZOOM','scale':1.3,'time':200}")); Call("Tick",.2f);
            Assert.That(camera.WorldToViewportPoint(target.position).y, Is.EqualTo(y).Within(.005f), "A foot anchor must not become the middle of the screen.");
        }

        [TestCase("c1079", "c_chermia_skill_03", 17)]
        [TestCase("c1081", "c_ceris_skill_02", 15)]
        [TestCase("c1086", "c_kawana_skill_03", 18)]
        [TestCase("c1087", "c_furious_skill_03", 31)]
        public void SingleRecipientCameraTwoFollowsTheTargetInBothDirections(string owner, string graph, int guid)
        {
            var target = Create(false);
            var caster = new GameObject("Caster").transform;
            caster.SetParent(root.transform);
            var actorType = Type.GetType("AstralOath.Client.BattleScene.BattlePreviewActor, AstralOath.Client.BattleScene", true);
            var actor = caster.gameObject.AddComponent(actorType);
            var path = System.IO.Path.Combine(Application.dataPath, "Game/Modules/CharacterPresentation/Res/Characters",
                owner, "Configs/stagept", graph + ".graph.json");
            var nodes = (JArray)JObject.Parse(System.IO.File.ReadAllText(path))["source"]["entities"];
            var node = (JObject)nodes.Single(item => (int)item["guid"] == guid);
            Assert.That((int)node["location"], Is.EqualTo(2));
            foreach (int facing in new[] { 1, -1 })
            {
                Call("Reset");
                caster.position = new Vector3(-5 * facing, -2, 0);
                target.position = new Vector3(4 * facing, -3, 0);
                actorType.GetField("faceLeft", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, facing < 0);
                var baseline = camera.transform.position;
                Call("Begin", caster, new[] { target }, false);
                Call("Play", node);
                Call("Tick", 2f);
                Vector3 expected = new Vector3(target.position.x + ((float?)node["x"] ?? 0f) * .01f * facing,
                    baseline.y + ((float?)node["y"] ?? 0f) * .01f, baseline.z);
                Assert.That(Vector3.Distance(camera.transform.position, expected), Is.LessThan(.001f), graph);
                var motion = new Vector3(facing * .75f, -.5f, 0);
                target.position += motion;
                Call("Tick", .1f);
                Assert.That(Vector3.Distance(camera.transform.position, expected + motion), Is.LessThan(.001f), graph);
                Call("Reset");
                Assert.That(camera.transform.position, Is.EqualTo(baseline));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraTwoKeepsAnUnverifiedGroupDistinctFromASingleRecipient(bool removeSecond)
        {
            var first = Create(false);
            var second = new GameObject("Second target").transform;
            second.SetParent(root.transform);
            second.position = new Vector3(-2, 1, 0);
            Call("Reset");
            Call("Begin", root.transform, new[] { first, second }, false);
            if (removeSecond) Object.DestroyImmediate(second.gameObject);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "Unmapped cinematic CAM location 2");
            var anchor = (Vector3)stage.GetType().GetMethod("Anchor", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(stage, new object[] { 2 });
            Assert.That(anchor, Is.EqualTo(camera.transform.position));
        }
        [Test] public void VisibleMapConstrainsRequestedCameraAndHiddenStageDoesNot()
        {
            Create(true);
            Call("Play",JObject.Parse("{'etty':'CAM','x':10000,'y':10000}")); Call("Tick",0f);
            float halfX=camera.orthographicSize*camera.aspect;
            Assert.That(camera.transform.position.x+halfX, Is.LessThanOrEqualTo(15.001f));
            Assert.That(camera.transform.position.y+camera.orthographicSize, Is.LessThanOrEqualTo(10.001f));
            Call("Play",JObject.Parse("{'etty':'BGSHOW','show':false}"));
            Call("Play",JObject.Parse("{'etty':'CAM','x':10000,'y':10000}")); Call("Tick",0f);
            Assert.That(camera.transform.position.x, Is.GreaterThan(90f));
            Call("Reset"); Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0,0,-10)));
        }
        [Test] public void CameraSpineShakeDoesNotAccumulateAndResetsPrecisely()
        {
            Create(false);
            var method=stage.GetType().GetMethod("PlayShake");
            Assert.That(method, Is.Not.Null, "Original camera skeleton curves must execute, rather than warn and skip.");
            var json="{\"skeleton\":{\"spine\":\"2.1.27\",\"hash\":\"\"},\"bones\":[{\"name\":\"root\"},{\"name\":\"camera\",\"parent\":\"root\"}],\"slots\":[],\"skins\":{\"default\":{}},\"animations\":{\"animation\":{\"bones\":{\"camera\":{\"translate\":[{\"time\":0},{\"time\":0.1,\"x\":20,\"y\":10},{\"time\":0.2}]}}}}}";
            var source=new SkeletonPlayerSource(json,"",new Dictionary<string,Texture2D>());
            method.Invoke(stage,new object[]{JObject.Parse("{'etty':'SHAKE','source':'test','timeScale':1}"),source});
            Call("Tick",.1f); Vector3 shaken=camera.transform.position;
            Assert.That(Vector2.Distance(shaken,Vector2.zero), Is.GreaterThan(.05f));
            Call("Tick",0f); Assert.That(camera.transform.position,Is.EqualTo(shaken));
            Call("Tick",.2f); Assert.That(camera.transform.position,Is.EqualTo(new Vector3(0,0,-10)));
            Call("Reset"); Assert.That(camera.transform.position,Is.EqualTo(new Vector3(0,0,-10)));
        }
        [Test] public void HiddenStageRetainsTheRequestedWideZoomAfterAMapConstraint()
        {
            Create(true);
            Call("Play",JObject.Parse("{'etty':'ZOOM','scale':0.25}"));
            Call("Play",JObject.Parse("{'etty':'BGSHOW','show':false}")); Call("Tick",0f);
            Assert.That(camera.orthographicSize,Is.EqualTo(20f).Within(.001f));
        }
        [TestCase("c1008", "boom_cm_power_lf", -1.5f, 0f)]
        [TestCase("c1019", "boom_cm_power_rt", 1.5f, 0f)]
        [TestCase("c1033", "coile_sk3_boom_cm", 0f, -.428f)]
        public void SourceShakeIncludesAuthoredParentTransform(string sourceOwner, string clip, float expectedX, float expectedY)
        {
            Create(false);
            var path = System.IO.Path.Combine(Application.dataPath, "Game/Modules/CharacterPresentation/Res/Characters",
                sourceOwner, "Animations/camera", clip + ".spine.json");
            var source = new SkeletonPlayerSource(System.IO.File.ReadAllText(path), "", new Dictionary<string,Texture2D>());
            Call("PlayShake", new JObject { ["etty"] = "SHAKE", ["source"] = clip }, source);
            Call("Tick", .0333f);
            Assert.That(camera.transform.position.x, Is.EqualTo(expectedX).Within(.001f));
            Assert.That(camera.transform.position.y, Is.EqualTo(expectedY).Within(.001f));
            Call("Tick", 5f);
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0f, 0f, -10f)));
        }
        [Test] public void SezHatchUsesSourceDirectionAndScaleAcrossConsecutiveWideShots()
        {
            Create(false);
            camera.aspect = 21f / 9f;
            string path = System.IO.Path.Combine(Application.dataPath, "Game/Modules/CharacterPresentation/Res/Characters/c1038",
                "Configs/stagept/c_sez_gaien_skill_03.graph.json");
            var nodes = (JArray)JObject.Parse(System.IO.File.ReadAllText(path))["source"]["entities"];
            texture = new Texture2D(1024, 1024);
            Call("SetHatchTextures", (object)new[] { texture, texture, texture });

            Check(17, new Vector2(0f, 1f), 2.4f);
            Check(18, new Vector2(.7071068f, -.7071068f), 2f);
            Call("Reset");
            Assert.That(camera.transform.childCount, Is.Zero);

            void Check(int guid, Vector2 expectedHorizontalUv, float scale)
            {
                Call("Play", nodes.First(entity => (int)entity["guid"] == guid));
                var filter = camera.transform.GetChild(camera.transform.childCount - 1).GetComponent<MeshFilter>();
                var uv = filter.sharedMesh.uv;
                var right = uv[1] - uv[0];
                Assert.That(right.normalized.x, Is.EqualTo(expectedHorizontalUv.x).Within(.001f));
                Assert.That(right.normalized.y, Is.EqualTo(expectedHorizontalUv.y).Within(.001f));
                Assert.That(right.magnitude, Is.EqualTo(720f * camera.aspect / (1024f * scale)).Within(.001f));
                Assert.That((uv[3] - uv[0]).magnitude, Is.EqualTo(720f / (1024f * scale)).Within(.001f));
                Assert.That(Vector2.Distance((uv[0] + uv[2]) / 2f, Vector2.one * .5f), Is.LessThan(.001f));
                var vertices = filter.sharedMesh.vertices;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = camera.WorldToViewportPoint(filter.transform.TransformPoint(vertices[corner]));
                    Assert.That(point.x, Is.EqualTo(corner is 1 or 2 ? 1f : 0f).Within(.001f));
                    Assert.That(point.y, Is.EqualTo(corner >= 2 ? 1f : 0f).Within(.001f));
                }
            }
        }
        [Test] public void SourceFocusFollowsTheMovingVisualActor()
        {
            var target=Create(false); float baseline=camera.WorldToViewportPoint(target.position).y;
            Call("Play",JObject.Parse("{'etty':'CAM','location':11}"));
            target.position+=Vector3.up*10f; Call("Tick",.1f);
            Assert.That(camera.WorldToViewportPoint(target.position).y,Is.EqualTo(baseline).Within(.005f));
        }
        private void Call(string name,params object[] args)=>stage.GetType().GetMethod(name).Invoke(stage,args);
    }
}
