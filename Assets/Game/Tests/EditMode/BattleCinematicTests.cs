using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattleCinematicTests
    {
        private const string RuntimeAssembly = "AstralOath.Client.BattleScene";
        private GameObject root;
        private object stage;
        [TearDown] public void Cleanup() { (stage as IDisposable)?.Dispose(); if(root) UnityEngine.Object.DestroyImmediate(root); }

        [Test] public void CollectionIncludesOriginalCutinDescriptorAndEveryFrame()
        {
            root = new GameObject("Cutin collection test"); root.SetActive(false);
            var host = root.AddComponent(Type.GetType("AstralOath.Client.BattleScene.BattleHudPreview, " + RuntimeAssembly, true));
            using var serialized = new SerializedObject(host);
            var property = serialized.FindProperty("skillAssets");
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/Configs/stagept/c_ras_skill_03.graph.json");
            var collector = Type.GetType("AstralOath.Client.BattleScene.Editor.BattleSkillPresentationAssets, AstralOath.Client.BattleScene.Editor", true);
            collector.GetMethod("Collect", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { property, new[] { graph } });
            var entries = Enumerable.Range(0, property.arraySize).Select(property.GetArrayElementAtIndex).ToArray();
            var cutin = entries.SingleOrDefault(row => row.FindPropertyRelative("source").stringValue == "cut/fhd/sk_c1001_3.webp");
            Assert.That(cutin, Is.Not.Null, "Battle collection currently drops the original CUTIN descriptor.");
            var descriptor = cutin.FindPropertyRelative("document").objectReferenceValue as TextAsset;
            var frames = (JArray)JObject.Parse(descriptor.text)["frames"];
            Assert.That(frames.Count, Is.EqualTo(16));
            Assert.That(cutin.FindPropertyRelative("pages").arraySize, Is.EqualTo(frames.Count));
            for (int i=0; i<frames.Count; i++) Assert.That(cutin.FindPropertyRelative("pages").GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
        }

        [Test] public void SourceZoomAndCameraRestoreOnInterruptedCinematic()
        {
            root = new GameObject("Cinematic lifecycle test");
            var camera = root.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5;
            camera.transform.position = new Vector3(1,2,-10);
            var background = new GameObject("Background"); background.transform.SetParent(root.transform);
            bool hidden = false;
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, " + RuntimeAssembly);
            Assert.That(type, Is.Not.Null, "Battle playback needs a source-driven cinematic stage.");
            stage = Activator.CreateInstance(type, new object[] { camera, (Action<bool>)(value => hidden=value), background });
            Call("Begin", root.transform, new Transform[0], true);
            Call("Play", JObject.Parse("{'etty':'ZOOM','scale':'2','time':100,'curve':'0,1,1,1'}"));
            Call("Play", JObject.Parse("{'etty':'CAM','location':11,'x':100,'time':100}"));
            Call("Play", JObject.Parse("{'etty':'BGSHOW'}")); Call("Tick", .11f);
            Assert.That(hidden, Is.True); Assert.That(camera.orthographicSize, Is.EqualTo(2.5f).Within(.001f));
            Assert.That(camera.transform.position.x, Is.EqualTo(2f).Within(.001f)); Assert.That(background.activeSelf, Is.False);
            Call("Reset"); Assert.That(hidden, Is.False); Assert.That(camera.orthographicSize, Is.EqualTo(5f));
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(1,2,-10))); Assert.That(background.activeSelf, Is.True);
        }

        [Test] public void CutinUsesAllSourceFrameDurationsRatherThanPointFourFiveSeconds()
        {
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, " + RuntimeAssembly); Assert.That(type, Is.Not.Null);
            var descriptor = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/Effects/cut/fhd/sk_c1001_3.cutin.json").text);
            float duration = (float)type.GetMethod("CutinDuration").Invoke(null, new object[]{ descriptor });
            Assert.That(duration, Is.EqualTo(descriptor["frames"].Sum(frame => (float)frame["durationSeconds"])).Within(.0001f));
            Assert.That(duration, Is.GreaterThan(1f));
        }
        [Test] public void SequentialCutinsReachTheirExactWallClockBoundaries()
        {
            root = new GameObject("Sequential Cut-in clock test");
            var camera = root.AddComponent<Camera>(); camera.orthographic = true;
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, " + RuntimeAssembly, true);
            var assetType = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, " + RuntimeAssembly, true);
            var asset = Activator.CreateInstance(assetType);
            var descriptor = new TextAsset("{\"frames\":[{\"durationSeconds\":0.36}]}");
            assetType.GetField("document").SetValue(asset, descriptor);
            assetType.GetField("pages").SetValue(asset, new[] { Texture2D.whiteTexture });
            stage = Activator.CreateInstance(type, new object[] { camera, null, null });
            try
            {
                Call("Begin", root.transform, Array.Empty<Transform>(), true);
                Call("Tick", 2.8266f);
                var remaining = type.GetProperty("CutinRemainingSeconds");
                for (int i = 0; i < 2; i++)
                {
                    Call("PlayCutin", asset);
                    Call("TickCutin", (float)remaining.GetValue(stage) - .0000005f);
                    Assert.That((float)remaining.GetValue(stage), Is.Zero);
                    Assert.That(root.transform.childCount, Is.Zero);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(descriptor); }
        }

        [Test] public void CutinIsExcludedFromTheWorldBlurCapture()
        {
            root = new GameObject("Overlay composition test");
            var camera = root.AddComponent<Camera>(); camera.orthographic = true;
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, " + RuntimeAssembly, true);
            var assetType = Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, " + RuntimeAssembly, true);
            var asset = Activator.CreateInstance(assetType);
            var descriptor = new TextAsset("{\"frames\":[{\"durationSeconds\":0.2}]}");
            assetType.GetField("document").SetValue(asset, descriptor);
            assetType.GetField("pages").SetValue(asset, new[] { Texture2D.whiteTexture });
            stage = Activator.CreateInstance(type, new object[] { camera, null, null });
            try
            {
                Call("Begin", root.transform, Array.Empty<Transform>(), true);
                Call("PlayCutin", asset);
                var overlay = root.GetComponentsInChildren<MeshRenderer>().Single(item => item.name.StartsWith("CUTIN "));
                Assert.That(overlay.gameObject.layer, Is.EqualTo(5), "CUTIN must be composited once above world blur.");
            }
            finally { UnityEngine.Object.DestroyImmediate(descriptor); }
        }

        [Test] public void FarAwaySourceLayoutRestoresRendererVisibilityWithoutMovingSlots()
        {
            root = new GameObject("Source layout test");
            var camera=root.AddComponent<Camera>(); camera.orthographic=true; camera.orthographicSize=5;
            var actor=new GameObject("Caster"); actor.transform.SetParent(root.transform);
            var target=new GameObject("Target",typeof(MeshRenderer)); target.transform.SetParent(root.transform);
            target.transform.localPosition=new Vector3(3,1,0);
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, "+RuntimeAssembly,true);
            stage=Activator.CreateInstance(type,new object[]{camera,(Action<bool>)(_=>{}),null});
            Call("Begin",actor.transform,new[]{target.transform},true);
            Call("Play",JObject.Parse("{'etty':'LAYOUT','away_x':5000,'time':200,'fade':200}"));
            Call("Tick",.21f);
            Assert.That(target.GetComponent<MeshRenderer>().enabled,Is.False,"Source away_x=5000 removes non-caster visuals from the authored frame.");
            Assert.That(target.transform.localPosition,Is.EqualTo(new Vector3(3,1,0)));
            Call("Play",JObject.Parse("{'etty':'LAYOUT','mode':1,'time':200}"));
            Call("Tick",.21f);
            Assert.That(target.GetComponent<MeshRenderer>().enabled,Is.True);
            Call("Play",JObject.Parse("{'etty':'LAYOUT','away_x':5000}"));
            Call("Reset");
            Assert.That(target.GetComponent<MeshRenderer>().enabled,Is.True,"Interrupted actions restore their layout mask.");
        }
        [Test] public void ImmediateSourceCameraIsAppliedBeforeItsTimedSuccessor()
        {
            root=new GameObject("Source camera chain test");
            var camera=new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform); camera.orthographic=true; camera.orthographicSize=5;
            camera.transform.position=new Vector3(0,0,-10);
            var target=new GameObject("Target"); target.transform.SetParent(root.transform); target.transform.position=new Vector3(4,1,0);
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, "+RuntimeAssembly,true);
            stage=Activator.CreateInstance(type,new object[]{camera,(Action<bool>)(_=>{}),null});
            Call("Begin",target.transform,new[]{target.transform},true);
            Call("Play",JObject.Parse("{'etty':'CAM','location':42,'x':-300,'y':200}"));
            Assert.That(camera.transform.position,Is.EqualTo(new Vector3(1,2,-10)),"Zero-duration CAM preserves ground framing before its outgoing connection.");
            Call("Play",JObject.Parse("{'etty':'CAM','location':42,'y':-50,'time':200}"));
            Call("Tick",.1f);
            Assert.That(camera.transform.position,Is.EqualTo(new Vector3(2.5f,.75f,-10)));
            Call("Tick",.11f);
            Assert.That(camera.transform.position,Is.EqualTo(new Vector3(4,-.5f,-10)));
        }
        [Test] public void LargeFrameAdvancesNewCueFromItsSourceTime()
        {
            root=new GameObject("Frame scheduling test");
            var camera=root.AddComponent<Camera>(); camera.orthographic=true; camera.transform.position=new Vector3(0,0,-10);
            var runtime=Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation, "+RuntimeAssembly,true);
            var asset=Type.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset, "+RuntimeAssembly,true);
            using var presenter=(IDisposable)Activator.CreateInstance(runtime,new object[]{root.transform,new TextAsset[0],Array.CreateInstance(asset,0),camera,(Action<bool>)(_=>{}),null});
            stage=runtime.GetField("cinematic",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presenter);
            Call("Begin",root.transform,new Transform[0],true);
            runtime.GetMethod("Schedule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{.6f,(Action)(()=>Call("Play",JObject.Parse("{'etty':'CAM','x':100,'time':200}")))});
            runtime.GetMethod("Tick").Invoke(presenter,new object[]{1f});
            Assert.That(camera.transform.position.x,Is.EqualTo(1f).Within(.0001f),"A cue at .6s must have completed its .2s move when the frame ends at 1s.");
        }
        [TestCase("restore")]
        [TestCase("reset")]
        [TestCase("next_action")]
        [TestCase("dispose")]
        public void FocusLayoutKeepsCasterAndTargetsAndRestoresSpectators(string finish)
        {
            root = new GameObject("Focus layout test");
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.orthographic = true;
            camera.orthographicSize = 5f;

            var slots = new Transform[4];
            var actors = new Transform[4];
            var renderers = new MeshRenderer[4];
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index] = new GameObject("Slot " + index).transform;
                slots[index].SetParent(root.transform);
                slots[index].localPosition = new Vector3(index, 1f, 0f);
                actors[index] = new GameObject("Actor " + index, typeof(MeshRenderer)).transform;
                actors[index].SetParent(slots[index], false);
                renderers[index] = actors[index].GetComponent<MeshRenderer>();
            }
            renderers[3].enabled = false;
            var spectatorShadow = new GameObject("Spectator shadow", typeof(SpriteRenderer));
            spectatorShadow.transform.SetParent(slots[2], false);

            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, " + RuntimeAssembly, true);
            stage = Activator.CreateInstance(type, new object[] { camera, (Action<bool>)(_ => { }), null });
            Call("SetParticipants", (object)slots);
            Call("Begin", actors[0], new[] { actors[1] }, true);
            var focus = JObject.Parse("{'etty':'LAYOUT','self_x':0.1,'away_x':150,'fade':200,'time':200}");
            Call("Play", focus);
            Call("Play", focus);
            Call("Tick", .21f);

            Assert.That(renderers[0].enabled, Is.True, "The caster remains visible.");
            Assert.That(renderers[1].enabled, Is.True, "Accepted action targets remain visible.");
            Assert.That(renderers[2].enabled, Is.False, "Uninvolved actors must not stand inside the skill close-up.");
            Assert.That(spectatorShadow.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(renderers[3].enabled, Is.False, "An originally hidden actor stays hidden.");
            for (int index = 0; index < slots.Length; index++)
            {
                Assert.That(slots[index].localPosition, Is.EqualTo(new Vector3(index, 1f, 0f)));
                Assert.That(slots[index].gameObject.activeSelf, Is.True);
            }

            switch (finish)
            {
                case "restore":
                    Call("Play", JObject.Parse("{'etty':'LAYOUT','mode':1,'time':200}"));
                    break;
                case "next_action":
                    Call("Begin", actors[1], new[] { actors[0] }, false);
                    break;
                case "dispose":
                    ((IDisposable)stage).Dispose();
                    break;
                default:
                    Call("Reset");
                    break;
            }
            Assert.That(renderers[0].enabled, Is.True);
            Assert.That(renderers[1].enabled, Is.True);
            Assert.That(renderers[2].enabled, Is.True, "A completed or interrupted action restores the spectator.");
            Assert.That(spectatorShadow.GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(renderers[3].enabled, Is.False, "Restore preserves the original disabled state.");
        }
        private void Call(string name, params object[] args) => stage.GetType().GetMethod(name).Invoke(stage,args);
    }
}