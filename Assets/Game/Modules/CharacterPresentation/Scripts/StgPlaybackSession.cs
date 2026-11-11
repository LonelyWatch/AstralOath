using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AstralOath.Client.CharacterPresentation
{
    // The source graph is retained. Experimental condition/location mappings are always reported.
    public sealed class StgPlaybackSession : IDisposable
    {
        private readonly JObject graph;
        private readonly ISkeletonPlayer caster;
        private readonly Camera camera;
        private readonly bool experimental;
        private readonly Action<string> report;
        private readonly Dictionary<int,JObject> nodes=new Dictionary<int,JObject>();
        private readonly List<JObject> edges=new List<JObject>();
        private readonly List<(double at,int id)> queue=new List<(double,int)>();
        private readonly HashSet<int> started=new HashSet<int>();
        private readonly HashSet<int> completed=new HashSet<int>();
        private readonly List<(double at,int id)> finishes=new List<(double,int)>();
        private readonly HashSet<string> reports=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<int,string> scopes=new Dictionary<int,string>();
        private double time;
        private int animationNode=-1;
        private float initialZoom;
        private Vector3 initialCamera;
        private bool disposed;
        private ZoomTween zoom;
        private readonly List<PositionTween> positions=new List<PositionTween>();
        private sealed class PositionTween { public Transform target; public Vector3 from,to; public double start,duration; }
        private sealed class ZoomTween { public double start,duration; public float from,to; }
        public Func<int,Vector3?> ResolveLocation {get;set;}
        public event Action<JObject> Effect;
        public event Action<string> Animation;
        public string Id => (string)graph["id"];
        public StgPlaybackSession(JObject graph,ISkeletonPlayer caster,Camera camera,bool experimental,Action<string> report)
        {
            this.graph=graph??throw new ArgumentNullException(nameof(graph));this.caster=caster;this.camera=camera;this.experimental=experimental;this.report=report;
            var source=(JObject)graph["source"]??graph;
            foreach(var token in source["entities"]??new JArray())
            {
                var node=(JObject)token;int id=RequiredId(node["guid"]);
                if(nodes.ContainsKey(id))throw new FormatException("Duplicate STG guid "+id);
                nodes.Add(id,node);
            }
            foreach(var token in source["connections"]??new JArray())
            {
                var edge=(JObject)token;
                if(!nodes.ContainsKey(RequiredId(edge["from"])) || !nodes.ContainsKey(RequiredId(edge["to"])))throw new FormatException("STG connection references missing guid.");
                edges.Add(edge);
            }
            caster.Event+=OnEvent;
        }
        public void Start()
        {
            initialZoom=camera.orthographicSize;initialCamera=camera.transform.position;
            var source=(JObject)graph["source"]??graph;
            if(experimental)Report("EXPERIMENTAL condition mapping: when=1 start, absent when=completion; not original-playback verification.");
            else Report("Strict graph mode: encoded when/location are unverified and are skipped.");
            foreach(var diagnostic in graph["diagnostics"]??new JArray())Report(diagnostic.ToString());
            Enqueue(RequiredId(source["entrypoint"]?["start"]),0);
            Advance(0);
        }
        public void Advance(float delta)
        {
            if(disposed)return;
            if(float.IsNaN(delta)||float.IsInfinity(delta)||delta<0)throw new ArgumentOutOfRangeException(nameof(delta));
            time+=delta;
            foreach(var tween in positions.ToArray())
            {
                float t=tween.duration<=0?1:(float)((time-tween.start)/tween.duration);
                tween.target.position=Vector3.Lerp(tween.from,tween.to,Mathf.Clamp01(t));
                if(t>=1)positions.Remove(tween);
            }
            if(zoom!=null)
            {
                float t=zoom.duration<=0?1:(float)((time-zoom.start)/zoom.duration);
                camera.orthographicSize=Mathf.Lerp(zoom.from,zoom.to,Mathf.Clamp01(t));
                if(t>=1)zoom=null;
            }
            foreach(var item in finishes.Where(f=>f.at<=time).ToArray()) {finishes.Remove(item);Complete(item.id);}
            int budget=2048;
            while(true)
            {
                int index=queue.FindIndex(item=>item.at<=time);
                if(index<0)break;
                if(--budget<0)throw new InvalidOperationException("STG node execution budget exceeded.");
                var item=queue[index];queue.RemoveAt(index);Run(item.id);
            }
        }
        private void Run(int id)
        {
            if(!started.Add(id))return;
            JObject node=nodes[id];string kind=(string)node["etty"];
            StartEdges(id);
            switch(kind)
            {
                case "ENTRY": Complete(id);break;
                case "ANI":
                    string animation=(string)node["ani"];
                    if(!caster.Animations.Any(a=>a.Name==animation)){Report("Missing ANI "+animation);Complete(id);break;}
                    animationNode=id;caster.Play(animation,false);Animation?.Invoke(animation);break;
                case "ONEVENT": Complete(id);break;
                case "ANI_SCOPE":
                    scopes[id]=(string)node["to"];
                    Report("ANI_SCOPE event interval "+node["from"]+".."+node["to"]+" retained; interpolation requires source curve evidence.");
                    break;
                case "IDLE":
                    string idle=caster.Animations.FirstOrDefault(a=>a.Name=="b_idle"||a.Name=="idle")?.Name;
                    if(idle!=null){caster.Play(idle,true);Animation?.Invoke(idle);}
                    Complete(id);break;
                case "ZOOM":
                    if(!experimental){Report("Unverified ZOOM curve "+node.ToString(Newtonsoft.Json.Formatting.None));Complete(id);break;}
                    float scale=Number(node["scale"],1);
                    if(scale<=0){Report("Invalid ZOOM scale");Complete(id);break;}
                    double duration=Number(node["time"],0)/1000;
                    zoom=new ZoomTween{start=time,duration=duration,from=camera.orthographicSize,to=initialZoom/scale};
                    if(node["curve"]!=null)Report("ZOOM uses linear experiment interpolation; original curve is retained: "+node["curve"]);
                    FinishAfter(id,duration);break;
                case "CAM": case "MOVE":
                    if(!experimental){Report("Unverified "+kind+" location "+node.ToString(Newtonsoft.Json.Formatting.None));Complete(id);break;}
                    int location=node["location"]?.Value<int>()??0;
                    Vector3? destination=location==0?(Vector3?)initialCamera:ResolveLocation?.Invoke(location);
                    if(destination==null){Report("Unsupported "+kind+" location "+location);Complete(id);break;}
                    Vector3 target=destination.Value+new Vector3(Number(node["x"],0)*.01f,Number(node["y"],0)*.01f,0);
                    var transform=kind=="CAM"?camera.transform:caster.Root.transform;
                    target.z=transform.position.z;
                    double seconds=Number(node["time"],0)/1000d;
                    if(kind=="MOVE"&&node["time"]==null)
                        Report("MOVE has no verified duration; lab anchor snap only. "+node.ToString(Newtonsoft.Json.Formatting.None));
                    else Report("Experimental "+kind+" location="+location+" uses lab anchor and linear interpolation.");
                    positions.RemoveAll(p=>p.target==transform);
                    if(seconds<=0)transform.position=target;
                    else positions.Add(new PositionTween{target=transform,from=transform.position,to=target,start=time,duration=seconds});
                    FinishAfter(id,seconds);break;
                case "EFFECT": case "HIT": case "CUTIN": case "DARK": case "STOP": case "SHAKE": case "PPEFFECT": Effect?.Invoke(node);Complete(id);break;
                case "SINE_VALUE":
                    Report("Unverified "+kind+" "+node.ToString(Newtonsoft.Json.Formatting.None));Complete(id);break;
                default: Report("Unsupported STG node "+node.ToString(Newtonsoft.Json.Formatting.None));Complete(id);break;
            }
        }
        private void StartEdges(int id)
        {
            foreach(var edge in edges.Where(e=>(int)e["from"]==id && e["when"]!=null))
            {
                int when=(int)edge["when"];
                if(experimental&&when==1)Enqueue((int)edge["to"],Delay(edge));
                else Report("Unverified connection when="+when+" "+edge.ToString(Newtonsoft.Json.Formatting.None));
            }
        }
        private void Complete(int id)
        {
            if(!completed.Add(id))return;
            foreach(var edge in edges.Where(e=>(int)e["from"]==id && e["when"]==null))
                Enqueue((int)edge["to"],Delay(edge));
        }
        private void OnEvent(SkeletonPlaybackEvent value)
        {
            if(disposed)return;
            if(value.Kind==SkeletonPlaybackEventKind.Complete)
            {
                if(animationNode>=0 && (string)nodes[animationNode]["ani"]==value.Name)Complete(animationNode);
                return;
            }
            foreach(var node in nodes.Values.Where(n=>(string)n["etty"]=="ONEVENT" && (string)n["name"]==value.Name))
            {
                string expected=(string)node["ani"];
                if(!string.IsNullOrEmpty(expected)&&expected!=caster.CurrentAnimation)continue;
                Enqueue((int)node["guid"],0);
            }
            foreach(var scope in scopes.Where(pair=>pair.Value==value.Name).ToArray()){scopes.Remove(scope.Key);Complete(scope.Key);}
        }
        private void Enqueue(int id,double delay)
        {
            if(!nodes.ContainsKey(id))throw new FormatException("Missing STG node "+id);
            if(delay<0 || double.IsNaN(delay)||double.IsInfinity(delay))throw new FormatException("Invalid STG delay.");
            queue.Add((time+delay,id));
        }
        private void FinishAfter(int id,double duration)
        {if(duration<=0)Complete(id);else finishes.Add((time+duration,id));}
        private static double Delay(JObject edge) => Number(edge["delay"],0)/1000d;
        private static int RequiredId(JToken token) => token?.Value<int>()??throw new FormatException("Missing STG guid.");
        private static float Number(JToken token,float fallback) => token==null?fallback:float.Parse(token.ToString(),CultureInfo.InvariantCulture);
        private void Report(string message){if(reports.Add(message))report?.Invoke(message);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;caster.Event-=OnEvent;queue.Clear();finishes.Clear();scopes.Clear();positions.Clear();
            if(camera){camera.orthographicSize=initialZoom;camera.transform.position=initialCamera;}
            Effect=null;Animation=null;
        }
    }
}
