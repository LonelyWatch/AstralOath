using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.CharacterPresentation;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AstralOath.Client.CharacterLab
{
    public sealed class CharacterScreenEffects : IDisposable
    {
        private sealed class Dark
        {
            public string id;public GameObject root;public Mesh mesh;public Material material;public Color color;
            public PresentationFade fade;public double start,duration;
        }
        private sealed class Shake { public string id;public ISkeletonPlayer player;public float speed;public double end; }
        private readonly CharacterLabLaunchContext context;
        private readonly BindingScope scope;
        private readonly IReadOnlyDictionary<string,SkeletonPlayerSource> sources;
        private readonly Action<string> report;
        private readonly CharacterBlurPreview blur;
        private readonly List<Dark> darks=new List<Dark>();
        private readonly List<Shake> shakes=new List<Shake>();
        private Vector3 appliedOffset;
        private double time;
        private bool disposed;
        public CharacterScreenEffects(CharacterLabLaunchContext context,BindingScope scope,IReadOnlyDictionary<string,SkeletonPlayerSource> sources,Action<string> report,CharacterBlurPreview blur=null)
        {this.context=context;this.scope=scope;this.sources=sources;this.report=report;this.blur=blur;}
        public void Play(JObject node)
        {
            string kind=(string)node["etty"];
            if(kind=="PPEFFECT"){if(blur==null)report("Missing lab camera blur preview.");else blur.Play(node,report);return;}
            if(kind=="STOP"){Stop((string)node["targetId"]);return;}
            if(kind=="DARK"){StartDark(node);return;}
            if(kind=="SHAKE")StartShake(node);
        }
        private void StartDark(JObject node)
        {
            if(N(node,"accel",1)!=1){report("Unsupported DARK accel: "+node["accel"]);return;}
            string[] components=((string)node["color"]??"0,0,0,255").Split(',');
            if(components.Length!=4){report("Invalid DARK color");return;}
            var color=new Color(Parse(components[0])/255,Parse(components[1])/255,Parse(components[2])/255,Parse(components[3])/255);
            string id=(string)node["id"]??"dark:"+node["guid"];
            Stop(id);
            var shader=Resources.Load<Shader>("CharacterLabUnlit");
            if(!shader){report("Missing DARK shader");return;}
            var root=new GameObject("DARK "+id,typeof(MeshFilter),typeof(MeshRenderer));
            root.transform.SetParent(context.PreviewCamera.transform,false);
            // Source layer codes are retained; only the explicit lab preview selects a depth.
            root.transform.localPosition=new Vector3(0,0,N(node,"globalZ",-1)<0?12:2);
            var mesh=CharacterEffectStage.Quad();root.GetComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(shader){mainTexture=Texture2D.whiteTexture};root.GetComponent<MeshRenderer>().sharedMaterial=material;
            double sourceDuration=N(node,"duration",-1),duration=sourceDuration<0?-1:sourceDuration/1000d;
            var dark=new Dark{id=id,root=root,mesh=mesh,material=material,color=color,start=time,duration=duration,
                fade=new PresentationFade(Mathf.Clamp01(N(node,"opacity",1)),duration,N(node,"fadeIn",0)/1000d,N(node,"fadeOut",0)/1000d)};
            darks.Add(dark);
            scope.Add(()=>{Release(root);Release(mesh);Release(material);});
            report("DARK source color/opacity/fade fields active. Lab preview treats duration as total interval and uses experimental globalZ depth; original overlap/layer semantics are unverified.");
            UpdateDark(dark);
        }
        private void StartShake(JObject node)
        {
            string source=(string)node["source"];
            if(string.IsNullOrWhiteSpace(source)){report("Unsupported procedural SHAKE without source camera skeleton.");return;}
            string path=source.StartsWith("camera/",StringComparison.Ordinal)?source:"camera/"+source;
            if(!path.EndsWith(".scsp",StringComparison.Ordinal))path+=".scsp";
            if(!sources.TryGetValue(path,out var data)){report("Missing camera skeleton "+path);return;}
            float speed=N(node,"timeScale",1);
            if(speed<=0){report("Invalid SHAKE timeScale");return;}
            var player=context.Skeletons.Create(data,context.WorldRoot);scope.Add(player.Dispose);
            string animation=player.Animations.FirstOrDefault(a=>a.Name=="animation")?.Name??player.Animations.FirstOrDefault()?.Name;
            if(animation==null){report("Camera skeleton has no animation: "+path);player.Dispose();return;}
            player.Play(animation,false);
            if(!player.TryGetBonePose("camera",out _)){report("Camera skeleton has no camera bone: "+path);player.Dispose();return;}
            double duration=N(node,"duration",-1);
            duration=duration<0?player.Duration/speed:duration/1000;
            shakes.Add(new Shake{id=(string)node["id"]??"shake:"+node["guid"],player=player,speed=speed,end=time+duration});
            report("SHAKE uses original camera bone curves through Spine, with source.Scale and timeScale. powerX/powerY/type amplitude semantics remain unapplied: "+path);
        }
        public void BeginFrame()
        {
            if(context.PreviewCamera)context.PreviewCamera.transform.position-=appliedOffset;
            appliedOffset=Vector3.zero;
        }
        public void Tick(float delta)
        {
            if(disposed)return;
            time+=delta;blur?.Tick(delta);
            foreach(var dark in darks.ToArray())
            {
                if(dark.duration>=0&&time-dark.start>=dark.duration){dark.root.SetActive(false);darks.Remove(dark);}
                else UpdateDark(dark);
            }
            Vector3 offset=Vector3.zero;
            foreach(var shake in shakes.ToArray())
            {
                if(time>=shake.end){shake.player.Dispose();shakes.Remove(shake);continue;}
                if(delta>0)shake.player.Tick(delta*shake.speed);
                if(shake.player.TryGetBonePose("camera",out var pose))
                    offset+=new Vector3(pose.LocalPosition.x,pose.LocalPosition.y,0);
            }
            if(context.PreviewCamera){context.PreviewCamera.transform.position+=offset;appliedOffset+=offset;}
        }
        private void UpdateDark(Dark dark)
        {
            float height=context.PreviewCamera.orthographicSize*2;
            dark.root.transform.localScale=new Vector3(height*context.PreviewCamera.aspect,height,1);
            Color color=dark.color;color.a*= (float)dark.fade.Evaluate(time-dark.start);
            dark.mesh.colors=new[]{color,color,color,color};
        }
        private void Stop(string id)
        {
            if(string.IsNullOrEmpty(id))return;blur?.Stop(id);
            foreach(var dark in darks.Where(d=>d.id==id).ToArray()){dark.root.SetActive(false);darks.Remove(dark);}
            foreach(var shake in shakes.Where(s=>s.id==id).ToArray()){shake.player.Dispose();shakes.Remove(shake);}
        }
        private static void Release(UnityEngine.Object value)
        {
            if(!value)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
        private static float N(JObject node,string key,float fallback)=>CharacterEffectStage.Number(node[key],fallback);
        private static float Parse(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
        public void Dispose()
        {
            if(disposed)return;disposed=true;BeginFrame();blur?.Reset();
            foreach(var dark in darks)if(dark.root)dark.root.SetActive(false);
            foreach(var shake in shakes)shake.player.Dispose();
            darks.Clear();shakes.Clear();
        }
    }
}
