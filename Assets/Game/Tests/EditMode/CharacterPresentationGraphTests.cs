using System;
using System.Collections.Generic;
using AstralOath.Client.CharacterPresentation;
using AstralOath.Client.CharacterLab;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace AstralOath.Tests
{
    public sealed class CharacterPresentationGraphTests
    {
        private GameObject root;
        private FakePlayer player;
        private Camera camera;
        [SetUp] public void Setup(){root=new GameObject("Graph test");camera=root.AddComponent<Camera>();camera.orthographicSize=5;player=new FakePlayer(root);}
        [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(root);
        private static JObject Graph(string entities,string connections,string listeners="[]") =>
            JObject.Parse("{'id':'test','source':{'entrypoint':{'start':1},'entities':"+entities+",'connections':"+connections+",'eventlisteners':"+listeners+"}}");
        [Test] public void PreservesParallelStartDelayAndAnimationCompletion()
        {
            var graph=Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'ANI','ani':'skill1'},{'guid':3,'etty':'ANI','ani':'idle'},{'guid':4,'etty':'EFFECT','source':'test.cfx'}]",
                "[{'from':1,'to':2},{'from':2,'to':3},{'from':2,'to':4,'when':1,'delay':500}]");
            using var session=new StgPlaybackSession(graph,player,camera,true,_=>{});
            int effects=0;session.Effect+=_=>effects++;
            session.Start();Assert.That(player.CurrentAnimation,Is.EqualTo("skill1"));
            session.Advance(.4f);Assert.That(effects,Is.Zero);
            session.Advance(.11f);Assert.That(effects,Is.EqualTo(1));
            player.Emit(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Complete,"skill1"));
            session.Advance(0);Assert.That(player.CurrentAnimation,Is.EqualTo("idle"));
        }
        [Test] public void PpeffectUsesOnlyItsSourceTriggerAndRetainsUnknownSineDriver()
        {
            var graph=Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'ANI','ani':'skill1'},{'guid':3,'etty':'PPEFFECT','program':'sprite_blur','u_range':35,'u_sample':3,'duration':200},{'guid':4,'etty':'SINE_VALUE','name':'u_range'}]",
                "[{'from':1,'to':2},{'from':2,'to':3,'when':1,'delay':200},{'from':3,'to':4,'when':2}]");
            var diagnostics=new List<string>();
            using var session=new StgPlaybackSession(graph,player,camera,true,diagnostics.Add);
            var programs=new List<string>();session.Effect+=node=>programs.Add((string)node["program"]);
            session.Start();Assert.That(programs,Is.Empty);
            session.Advance(.21f);Assert.That(programs,Is.EqualTo(new[]{"sprite_blur"}));
            Assert.That(diagnostics.Exists(d=>d.Contains("when=2")),Is.True);
        }
        [Test] public void StrictModeReportsUnknownWhenWithoutExecutingTheEdge()
        {
            var graph=Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'EFFECT','source':'test.cfx'}]","[{'from':1,'to':2,'when':2}]");
            var diagnostics=new List<string>();
            using var session=new StgPlaybackSession(graph,player,camera,false,diagnostics.Add);
            int effects=0;session.Effect+=_=>effects++;session.Start();session.Advance(1);
            Assert.That(effects,Is.Zero);Assert.That(diagnostics.Exists(d=>d.Contains("when=2")),Is.True);
        }
        [Test] public void OneventRequiresTheSourceAnimationName()
        {
            var graph=Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'ANI','ani':'skill1'},{'guid':3,'etty':'ONEVENT','ani':'skill2','name':'hit'},{'guid':4,'etty':'EFFECT','source':'hit.cfx'}]",
                "[{'from':1,'to':2},{'from':3,'to':4}]", "[3]");
            using var session=new StgPlaybackSession(graph,player,camera,true,_=>{});
            int effects=0;session.Effect+=_=>effects++;session.Start();
            player.Emit(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event,"hit"));session.Advance(0);
            Assert.That(effects,Is.Zero,"Mercedes S2 source mismatch must remain visible, not silently rewritten.");
            player.Play("skill2",false);player.Emit(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event,"hit"));session.Advance(0);
            Assert.That(effects,Is.EqualTo(1));
        }
        [Test] public void SourceTimelineDispatchesMatchingArminOneventAtItsNamedDelay()
        {
            const string assets = "Game/Modules/CharacterPresentation/Res/Characters/c1008/Configs/";
            JObject Read(string path) => JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, assets, path)));
            var graph = Read("stagept/c_aming_skill_02.graph.json");
            using var session = new StgPlaybackSession(graph, player, camera, false, _ => { });
            session.Timeline = Read("model/arming.timeline.json");
            var effects = new List<string>();
            session.Effect += node => { if ((string)node["etty"] == "EFFECT") effects.Add((string)node["source"]); };
            session.Start();
            Assert.That(player.CurrentAnimation, Is.EqualTo("skill2"));
            session.Advance(1.12f);
            Assert.That(effects, Is.Empty);
            session.Advance(.02f);
            Assert.That(effects, Is.EqualTo(new[] { "aming_skill_02_new_pillar.cfx" }));
            player.Emit(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event, "eff_on"));
            session.Advance(1f);
            Assert.That(effects, Has.Count.EqualTo(1));
        }

        [Test] public void SourceDelayAndEmptyShotRetainTheirTiming()
        {
            var graph = Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'DELAY','time':100},{'guid':3,'etty':'SHOT','source':'','time':200},{'guid':4,'etty':'HIT'}]",
                "[{'from':1,'to':2},{'from':2,'to':3,'when':0},{'from':3,'to':4}]");
            using var session = new StgPlaybackSession(graph, player, camera, false, _ => { });
            int hits = 0;
            session.Effect += node => { if ((string)node["etty"] == "HIT") hits++; };
            session.Start();
            session.Advance(.10f);
            session.Advance(.19f);
            Assert.That(hits, Is.Zero);
            session.Advance(.02f);
            Assert.That(hits, Is.EqualTo(1));
        }

        [Test] public void DisposeUnsubscribesAndRestoresCamera()
        {
            var graph=Graph("[{'guid':1,'etty':'ENTRY'},{'guid':2,'etty':'ZOOM','scale':'2','time':100}]","[{'from':1,'to':2}]");
            var session=new StgPlaybackSession(graph,player,camera,true,_=>{});session.Start();session.Advance(.2f);
            Assert.That(camera.orthographicSize,Is.EqualTo(2.5f));Assert.That(player.ListenerCount,Is.EqualTo(1));
            session.Dispose();Assert.That(camera.orthographicSize,Is.EqualTo(5));Assert.That(player.ListenerCount,Is.Zero);
        }
        [Test] public void RejectsBrokenConnectionsBeforePlayback()
        {
            Assert.Throws<FormatException>(()=>new StgPlaybackSession(Graph("[{'guid':1,'etty':'ENTRY'}]","[{'from':1,'to':99}]"),player,camera,false,_=>{}));
        }
        [Test] public void ActualCfxRangesAreDiagnosedBeforeSchedulingAndDoNotRejectValidSibling()
        {
            string path=System.IO.Path.Combine(Application.dataPath,"Game/Modules/CharacterPresentation/Res/Shared/Effects/effect/hit_new_blunt_normal.cfx.json");
            var document=JObject.Parse(System.IO.File.ReadAllText(path));
            var primitives=(JArray)document["primitive"];
            Assert.That(CharacterEffectStage.TryValidatePrimitiveScalars((JObject)primitives[0],out string diagnostic),Is.False);
            StringAssert.Contains("scale=0.45~0.7",diagnostic);
            Assert.That(CharacterEffectStage.TryValidatePrimitiveScalars((JObject)primitives[1],out _),Is.True);
        }
        [Test] public void ShakeUsesScaledSourcePoseWithoutAccumulatingCameraDrift()
        {
            using var lifetime=new BindingScope(default);
            using var services=new ServiceRegistry(default);
            var source=new SkeletonPlayerSource("{}","",new Dictionary<string,Texture2D>());
            player.BonePosition=new Vector2(.4f,-.2f);
            var context=new CharacterLabLaunchContext(services,lifetime,root.transform,root.transform,camera,new FakeFactory(player),new FakeAudio());
            using var screen=new CharacterScreenEffects(context,lifetime,new Dictionary<string,SkeletonPlayerSource>{{"camera/test.scsp",source}},_=>{});
            Vector3 before=camera.transform.position;
            screen.Play(JObject.Parse("{'etty':'SHAKE','guid':1,'source':'test','duration':-1,'timeScale':1}"));
            screen.BeginFrame();screen.Tick(.1f);
            Assert.That(camera.transform.position,Is.EqualTo(before+new Vector3(.4f,-.2f,0)));
            screen.BeginFrame();screen.Tick(.1f);
            Assert.That(camera.transform.position,Is.EqualTo(before+new Vector3(.4f,-.2f,0)));
            screen.Dispose();Assert.That(camera.transform.position,Is.EqualTo(before));
        }
        [Test] public void StopTargetsOnlyTheNamedDarkOverlay()
        {
            using var lifetime=new BindingScope(default);
            using var services=new ServiceRegistry(default);
            var context=new CharacterLabLaunchContext(services,lifetime,root.transform,root.transform,camera,new FakeFactory(player),new FakeAudio());
            using var screen=new CharacterScreenEffects(context,lifetime,new Dictionary<string,SkeletonPlayerSource>(),_=>{});
            screen.Play(JObject.Parse("{'etty':'DARK','id':'d1','color':'0,0,0,255','opacity':0.7,'duration':-1,'fadeIn':0}"));
            screen.Play(JObject.Parse("{'etty':'DARK','id':'d2','color':'0,0,0,255','opacity':0.5,'duration':-1,'fadeIn':0}"));
            Assert.That(root.transform.Find("DARK d1").gameObject.activeSelf,Is.True);
            screen.Play(JObject.Parse("{'etty':'STOP','targetId':'d1'}"));
            Assert.That(root.transform.Find("DARK d1").gameObject.activeSelf,Is.False);
            Assert.That(root.transform.Find("DARK d2").gameObject.activeSelf,Is.True);
        }
        private sealed class FakeFactory : ISkeletonPlayerFactory
        {
            private readonly ISkeletonPlayer player;
            public FakeFactory(ISkeletonPlayer player){this.player=player;}
            public ISkeletonPlayer Create(SkeletonPlayerSource source,Transform parent)=>player;
        }
        private sealed class FakeAudio : ICharacterAudioService
        {
            public bool IsAvailable=>false;public string Diagnostic=>"test";
            public ICharacterAudioPlayback PlayEvent(string path)=>throw new NotSupportedException();
            public ICharacterAudioPlayback PlayVoice(string key)=>throw new NotSupportedException();
        }
        private sealed class FakePlayer : ISkeletonPlayer
        {
            private Action<SkeletonPlaybackEvent> listeners;
            public FakePlayer(GameObject root){Root=root;}
            public Vector2 BonePosition;
            public int ListenerCount=>listeners?.GetInvocationList().Length??0;
            public GameObject Root{get;} public string Version=>"3.8";
            public IReadOnlyList<SkeletonAnimationInfo> Animations{get;}=new[]{new SkeletonAnimationInfo("animation",1),new SkeletonAnimationInfo("skill1",1),new SkeletonAnimationInfo("skill2",1),new SkeletonAnimationInfo("idle",1)};
            public IReadOnlyList<string> Skins{get;}=Array.Empty<string>();
            public string CurrentAnimation{get;private set;} public float Time=>0;public float Duration=>1;public bool IsPaused=>false;
            public event Action<SkeletonPlaybackEvent> Event{add=>listeners+=value;remove=>listeners-=value;}
            public void Emit(SkeletonPlaybackEvent value)=>listeners?.Invoke(value);
            public void Play(string animation,bool loop,float mixSeconds=0)=>CurrentAnimation=animation;
            public bool TryGetBonePose(string bone,out SkeletonBonePose pose){pose=new SkeletonBonePose(BonePosition,0,Vector2.one);return bone=="camera";}
            public bool TryGetBoneSkeletonPosition(string bone,out Vector2 position){position=default;return false;}
            public void SetSkin(string skin){}public void Pause(bool value){}public void Stop()=>CurrentAnimation=null;public void Tick(float delta){}public void Seek(float value){}public void SetFlip(bool x,bool y){}public void Dispose(){}
        }
    }
}
