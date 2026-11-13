using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattleScopedCameraTests
    {
        const string Module = "AstralOath.Client.BattleScene";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static Type TypeOf(string name) => Type.GetType(Module + "." + name + ", " + Module, true);
        GameObject root;
        Component actor;
        IDisposable playback;
        Texture2D page;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        JObject graph;
        (float At, JObject Node, float Duration, JObject[] Modulators)[] cues;
        Camera camera;

        void Load(string id, string model, Action<JObject> mutate = null)
        {
            root = new GameObject("Real S1 scoped camera"); root.SetActive(false);
            var actorObject = new GameObject("Actor"); actorObject.transform.SetParent(root.transform, false);
            actor = actorObject.AddComponent(TypeOf("BattlePreviewActor"));
            string folder = "Assets/Game/Modules/CharacterPresentation/Res/Characters/" + id + "/";
            graph = JObject.Parse(File.ReadAllText(folder + "Configs/stagept/c_" + model + "_skill_01.graph.json"));
            mutate?.Invoke(graph);
            var skeleton = new TextAsset(File.ReadAllText(folder + "Animations/model/" + model + ".spine.json"));
            var atlas = new TextAsset(File.ReadAllText(folder + "Atlas/model/" + model + ".atlas.txt"));
            owned.Add(skeleton); owned.Add(atlas); page = new Texture2D(2,2);
            void Set(string name, object value) => actor.GetType().GetField(name, Private).SetValue(actor, value);
            Set("skeleton", skeleton); Set("atlas", atlas); Set("page", page);
            Set("pageName", atlas.text.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries)[0]);
            actor.GetType().GetMethod("OnEnable", Private).Invoke(actor, null);
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0,0,-10); camera.orthographic = true; camera.orthographicSize = 5;
            playback = (IDisposable)Activator.CreateInstance(TypeOf("BattleSkillPresentation"), new object[]{root.transform,
                Array.Empty<TextAsset>(), Array.CreateInstance(TypeOf("BattleSkillPresentationAsset"),0), camera, null, null});
            playback.GetType().GetField("caster",Private).SetValue(playback,actor);
            cues = ((IEnumerable<(float, JObject, float, JObject[])>)playback.GetType().GetMethod("ReadCues", Private)
                .Invoke(playback, new object[] { graph })).ToArray();
        }
        [TearDown] public void Cleanup()
        {
            playback?.Dispose(); playback=null;
            if(actor) actor.GetType().GetMethod("OnDisable",Private).Invoke(actor,null);
            if(root) UnityEngine.Object.DestroyImmediate(root);
            foreach(var item in owned) UnityEngine.Object.DestroyImmediate(item); owned.Clear();
            if(page) UnityEngine.Object.DestroyImmediate(page);
        }
        float Event(string animation, string name) => (float)actor.GetType().GetMethod("GetBattleAnimationEventTime",Private).Invoke(actor,new object[]{animation,name});
        [TestCase("c1001","ras","jump_forward")][TestCase("c1001","ras","jump_back")]
        [TestCase("c1062","angelica","jump_forward")][TestCase("c1062","angelica","jump_back")]
        public void SourceScopeCameraAndZoomUseTheSameJumpWindowAsTravel(string id,string model,string animation)
        {
            Load(id,model);
            var ani=cues.Single(c=>(string)c.Node["etty"]=="ANI"&&(string)c.Node["ani"]==animation);
            int scopeId=(int)graph["source"]["connections"].Single(e=>(int)e["from"]==(int)ani.Node["guid"]&&(int?)e["when"]==1&&graph["source"]["entities"].Any(n=>(int)n["guid"]==(int)e["to"]&&(string)n["etty"]=="ANI_SCOPE"))["to"];
            var edges=graph["source"]["connections"].Where(e=>(int)e["from"]==scopeId).Select(e=>(int)e["to"]).ToArray();
            float start=ani.At+Event(animation,"jump"), duration=Event(animation,"landing")-Event(animation,"jump");
            foreach(var cue in cues.Where(c=>edges.Contains((int)c.Node["guid"])&&((string)c.Node["etty"]=="CAM"||(string)c.Node["etty"]=="ZOOM")))
            {
                Assert.That(cue.At,Is.EqualTo(start).Within(.0001f), model+" "+animation+" "+cue.Node["etty"]+" must start at jump, not hard-cut at landing.");
                Assert.That(cue.Duration,Is.EqualTo(duration).Within(.0001f));
                Assert.That((float?)cue.Node["time"],Is.EqualTo(duration*1000f).Within(.01f),"The effective time must reach the real cinematic player.");
                Assert.That(graph["source"]["entities"].Single(n=>(int)n["guid"]==(int)cue.Node["guid"])["time"],Is.Null,"Do not mutate the immutable source document.");
            }
        }
        [TestCase(0f)][TestCase(150f)]
        public void ExplicitSourceTimesRemainAtTheDeclaredCompletionEdge(float milliseconds)
        {
            Load("c1001","ras", g=>((JObject)g["source"]["entities"].Single(n=>(int)n["guid"]==12))["time"]=milliseconds);
            var cue=cues.Single(c=>(int)c.Node["guid"]==12);
            Assert.That(cue.At,Is.EqualTo(Event("jump_forward","landing")).Within(.0001f));
            Assert.That(cue.Duration,Is.EqualTo(milliseconds/1000f).Within(.0001f));
        }
        [Test] public void ScopeStartEdgesAndExplicitDelaysRemainDistinct()
        {
            Load("c1001","ras",g=>
            {
                ((JObject)g["source"]["connections"].Single(e=>(int)e["from"]==10&&(int)e["to"]==12))["delay"]=70;
                ((JObject)g["source"]["connections"].Single(e=>(int)e["from"]==10&&(int)e["to"]==8))["when"]=1;
            });
            Assert.That(cues.Single(c=>(int)c.Node["guid"]==12).At,Is.EqualTo(Event("jump_forward","jump")+.07f).Within(.0001f));
            var zoom=cues.Single(c=>(int)c.Node["guid"]==8);
            Assert.That(zoom.At,Is.EqualTo(0f)); Assert.That(zoom.Duration,Is.EqualTo(0f));
        }
        [TestCase("c1001","ras")][TestCase("c1062","angelica")]
        public void EffectiveSourceCameraMovesBeforeLandingWithoutASingleFrameCut(string id,string model)
        {
            Load(id,model);
            var stage=playback.GetType().GetField("cinematic",Private).GetValue(playback);
            var target=new GameObject("Target").transform; target.SetParent(root.transform); target.position=new Vector3(4,-3,0);
            stage.GetType().GetMethod("Begin").Invoke(stage,new object[]{actor.transform,new[]{target},false});
            var forward=cues.Where(c=>c.At<1f&&((string)c.Node["etty"]=="CAM"||(string)c.Node["etty"]=="ZOOM")).OrderBy(c=>c.At).ToArray();
            float jump=Event("jump_forward","jump"),landing=Event("jump_forward","landing"),previous=0;
            int next=0; float maxDelta=0; Vector3 old=camera.transform.position;
            for(int frame=1;frame<=120;frame++)
            {
                float time=frame/120f;
                while(next<forward.Length&&forward[next].At<=time)
                {
                    stage.GetType().GetMethod("Tick").Invoke(stage,new object[]{forward[next].At-previous});previous=forward[next].At;
                    stage.GetType().GetMethod("Play").Invoke(stage,new object[]{forward[next++].Node});
                }
                stage.GetType().GetMethod("Tick").Invoke(stage,new object[]{time-previous});previous=time;
                maxDelta=Mathf.Max(maxDelta,Vector3.Distance(camera.transform.position,old));old=camera.transform.position;
                if(time>jump+.1f&&time<landing-.1f) Assert.That(camera.transform.position.x,Is.GreaterThan(.01f),"Camera must already travel during the airborne scope.");
            }
            Assert.That(maxDelta,Is.LessThan(.2f),"Missing-duration scope transitions cannot be a single frame cut.");
        }
    }
}
