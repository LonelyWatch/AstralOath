using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.CharacterPresentation;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AstralOath.Client.CharacterLab
{
    // A diagnostic preview, with raw documents retained and explicit opt-in for location mappings.
    public sealed class CharacterEffectStage : IDisposable
    {
        private readonly CharacterLabLaunchContext context;
        private readonly IAssetService assets;
        private readonly BindingScope scope;
        private readonly ISkeletonPlayer caster;
        private readonly Action<string> report;
        private readonly bool experimental;
        private readonly string skillId;
        private readonly Dictionary<string,JObject> documents=new Dictionary<string,JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string,SkeletonPlayerSource> skeletons=new Dictionary<string,SkeletonPlayerSource>(StringComparer.Ordinal);
        private readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
        private readonly List<(double at,Action action)> pending=new List<(double,Action)>();
        private readonly List<ISkeletonPlayer> active=new List<ISkeletonPlayer>();
        private readonly List<ISkeletonPlayer> allies=new List<ISkeletonPlayer>();
        private readonly List<CharacterParticlePreview> particles=new List<CharacterParticlePreview>();
        private readonly Dictionary<string,List<ISkeletonPlayer>> named=new Dictionary<string,List<ISkeletonPlayer>>(StringComparer.Ordinal);
        private readonly List<(GameObject root,JObject descriptor,double start)> cutins=new List<(GameObject,JObject,double)>();
        private readonly Vector3 casterPosition,casterScale,cameraPosition;
        private readonly float cameraSize;
        public JObject Timeline { get; private set; }
        private double time;
        private int timelineGeneration;
        private bool disposed;
        private readonly CharacterScreenEffects screen;
        private bool assistTriggered;
        public CharacterEffectStage(CharacterLabLaunchContext context,BindingScope scope,ISkeletonPlayer caster,string skillId,bool experimental,Action<string> report,CharacterBlurPreview blur=null)
        {
            this.context=context;this.scope=scope;this.caster=caster;this.skillId=skillId;this.experimental=experimental;this.report=report;
            assets=context.Services.Get<IAssetService>();casterPosition=caster.Root.transform.localPosition;casterScale=caster.Root.transform.localScale;
            cameraPosition=context.PreviewCamera.transform.position;cameraSize=context.PreviewCamera.orthographicSize;
            screen=new CharacterScreenEffects(context,scope,skeletons,report,blur);scope.Add(screen.Dispose);
        }
        public async Task PrepareAsync(JObject graph,CharacterRecord character,CharacterLabCatalog catalog)
        {
            foreach(var item in graph["assets"]??new JArray())
            {
                string source=(string)item["source"],address=(string)item["address"],type=(string)item["type"];
                if(string.IsNullOrEmpty(source)||string.IsNullOrEmpty(address))continue;
                try
                {
                    if(type=="skeleton")
                        skeletons[source]=await CharacterSkeletonLoader.LoadSourceAsync(address,assets,scope);
                    else if(type=="effect"||type=="particle"||type=="timeline"||type=="cutin")
                        documents[source]=JObject.Parse((await assets.LoadAsync<TextAsset>(address,scope)).text);
                    else if(type=="texture")textures[source]=await assets.LoadAsync<Texture2D>(address,scope);
                }
                catch(OperationCanceledException){throw;}
                catch(Exception error){report("Missing dependency "+source+": "+error.Message);}
            }
            foreach(var doc in documents.Where(pair=>pair.Key.EndsWith(".webp",StringComparison.Ordinal)).ToArray())
                foreach(var frame in doc.Value["frames"]??new JArray())
                {
                    string address=(string)frame["address"];
                    if(!string.IsNullOrEmpty(address)&&!textures.ContainsKey(address))
                        textures[address]=await assets.LoadAsync<Texture2D>(address,scope);
                }
            if(!string.IsNullOrEmpty(character.timelineAddress))
                Timeline=JObject.Parse((await assets.LoadAsync<TextAsset>(character.timelineAddress,scope)).text);
            if(experimental)
            {
                report("EXPERIMENTAL locations: 1/11 caster, 5 test opponent, 6 selected allies; positions are lab anchors, not verified source coordinates.");
                context.PreviewCamera.orthographicSize=Mathf.Max(5,cameraSize);context.PreviewCamera.transform.position=new Vector3(0,1,-10);
                var records=new[]{catalog.characters.First(c=>c.id=="c1001"),catalog.characters.First(c=>c.id=="c1005"),catalog.characters.First(c=>c.id=="c1062")};
                caster.Root.transform.localPosition=new Vector3(-2.7f,-2,0);caster.Root.transform.localScale=casterScale*.65f;
                allies.Add(caster);
                for(int i=0;i<3;i++)
                {
                    var record=records[(i+(character.id=="c1001"?1:0))%records.Length];
                    var ally=await CharacterSkeletonLoader.LoadAsync(record.battleSkeletonAddress,assets,context.Skeletons,context.WorldRoot,scope);
                    ally.Root.transform.localPosition=new Vector3(-.9f+i*1.8f,-2,0);ally.Root.transform.localScale*=.65f;
                    string idle=ally.Animations.FirstOrDefault(a=>a.Name=="b_idle"||a.Name=="idle")?.Name;
                    if(idle!=null)ally.Play(idle,true);
                    allies.Add(ally);active.Add(ally);
                }
                if(skillId=="c_ras_skill_02")report("Ras S2: explicit test partner is Mercedes. Assist is presentation only.");
                if(skillId=="c_angelica_skill_02")report("Angelica S2: two selected allied test actors.");
                if(skillId=="c_angelica_skill_03")report("Angelica S3: all four allied test actors.");
            }
        }
        public Vector3? ResolveLocation(int location)
        {
            if(!experimental)return null;
            if(location==1||location==11)return new Vector3(-2.7f,-2,0);
            if((location==5||location==12||location==16)&&allies.Count>1)return allies[1].Root.transform.position;
            if(location==31&&allies.Count>=2)return (allies[0].Root.transform.position+allies[1].Root.transform.position)*.5f;
            if((location==32||location==41||location==42)&&allies.Count>0)
                return allies.Select(a=>a.Root.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/allies.Count;
            return null;
        }
        public void Assist(string animationEvent)
        {
            if(!experimental||assistTriggered||skillId!="c_ras_skill_02"||animationEvent!="hit"||allies.Count<2)return;
            assistTriggered=true; report("Test assist starts once on the first caster hit event; original assist timing is not verified.");
            var partner=allies[1];
            if(partner.Animations.Any(a=>a.Name=="skill1"))partner.Play("skill1",false);
        }
        public void Animation(string name)
        {
            int generation=++timelineGeneration;
            var frames=Timeline?["timeline"]?["default"]?[name]?["frames"] as JArray;
            if(frames==null)return;
            foreach(JObject frame in frames)
            {
                if ((string)frame["func"] == "event") continue; // Delivered by the STG session to ONEVENT.
                double delay=Number(frame["time"],0)/1000d;
                pending.Add((time+delay,()=>{
                    if(generation!=timelineGeneration)return;
                    string function=(string)frame["func"];var param=(JObject)frame["param"]??new JObject();
                    if(function=="effect")Effect(param);
                    else if(function=="remove")Remove((string)param["targetId"]);
                    else if (function=="shake")
                    {
                        var shake=(JObject)param.DeepClone();shake["etty"]="SHAKE";Effect(shake);
                    }
                    else report("Unsupported timeline function "+frame.ToString(Newtonsoft.Json.Formatting.None));
                }));
            }
        }
        public void Effect(JObject node)
        {
            if(disposed)return;
            string kind=(string)node["etty"];
            if(kind=="DARK"||kind=="STOP"||kind=="SHAKE"||kind=="PPEFFECT")
            {
                if(experimental)screen.Play(node);
                else report("Strict mode retains unsupported "+kind+" fields: "+node.ToString(Newtonsoft.Json.Formatting.None));
                return;
            }
            if(kind=="CUTIN"){Cutin((string)node["args"]);return;}
            string source=(string)node["source"]??(string)node["effect"];
            if(string.IsNullOrEmpty(source))return;
            int location=node["location"]?.Value<int>()??5;
            if(!experimental){report("Unverified location="+location+" for "+source);return;}
            string path=source.StartsWith("effect/",StringComparison.Ordinal)?source:"effect/"+source;
            if(!path.EndsWith(".cfx",StringComparison.Ordinal))path+=".cfx";
            if(!documents.TryGetValue(path,out var cfx)){report("Missing CFX "+path);return;}
            IEnumerable<Transform> targets;
            if(location==1||location==11)targets=new[]{caster.Root.transform};
            else if(location==5)targets=new[]{allies.Count>1?allies[1].Root.transform:caster.Root.transform};
            else if(location==6)targets=allies.Take(skillId=="c_angelica_skill_02"?2:allies.Count).Select(a=>a.Root.transform);
            else if(location==41||location==42)
            {
                // Group effect plays once, at the center of the experimental target group.
                var group=new GameObject("Lab effect group anchor").transform;
                group.SetParent(context.WorldRoot,false);
                var recipients=allies.Skip(1).ToArray();
                group.position=recipients.Length==0?caster.Root.transform.position:
                    recipients.Select(a=>a.Root.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/recipients.Length;
                group.localScale=caster.Root.transform.localScale;
                scope.Add(()=>UnityEngine.Object.Destroy(group.gameObject));
                targets=new[]{group};
            }
            else {report("Unsupported location "+location+" for "+path);return;}
            foreach(JObject primitive in cfx["primitive"]??new JArray())
            {
                if(!TryValidatePrimitiveScalars(primitive,out string diagnostic)){report(diagnostic);continue;}
                foreach(var target in targets)
                {
                    var captured=target;var part=primitive;
                    pending.Add((time+Number(part["delay"],0)/1000d,()=>Spawn(part,captured,(string)node["id"],Number(node["scale"],1))));
                }
            }
        }
        public static bool TryValidatePrimitiveScalars(JObject primitive,out string diagnostic)
        {
            foreach(string key in new[]{"x","y","z","globalZ","scale","rotate","delay","lifeTime"})
            {
                var value=primitive[key];
                if(value!=null&&!PresentationScalar.TryRead(value.ToString(),out _))
                {
                    diagnostic="Unsupported CFX scalar/range "+primitive["source"]+"."+key+"="+value+"; primitive skipped, range semantics remain unverified.";
                    return false;
                }
            }
            foreach(JObject action in primitive["actions"]??new JArray())
                if(action["delay"]!=null&&!PresentationScalar.TryRead(action["delay"].ToString(),out _))
                {
                    diagnostic="Unsupported CFX action delay "+primitive["source"]+"="+action["delay"]+"; primitive skipped.";
                    return false;
                }
            diagnostic=null;return true;
        }
        private void Spawn(JObject part,Transform anchor,string id,float scale)
        {
            string source=(string)part["source"],format=(string)part["format"];
            if(format=="spine")
            {
                if((part["actions"] as JArray)?.OfType<JObject>().Any(a=>(string)a["action"]=="Fire")==true)
                {
                    report("Unverified CFX Fire action; primitive withheld rather than playing its default animation at a guessed time: "+part.ToString(Newtonsoft.Json.Formatting.None));
                    return;
                }
                string path="effect/"+source+".scsp";
                if(!skeletons.TryGetValue(path,out var data)){report("Missing spineFX "+path);return;}
                var effect=context.Skeletons.Create(data,context.WorldRoot);
                effect.Root.transform.position=anchor.position+new Vector3(Number(part["x"],0)*.01f,Number(part["y"],0)*.01f,-Number(part["z"]??part["globalZ"],0)*.001f);
                effect.Root.transform.localScale=Vector3.one*Number(part["scale"],1)*scale*anchor.localScale.x;
                effect.Root.transform.localRotation=Quaternion.Euler(0,0,-Number(part["rotate"],0));
                scope.Add(effect.Dispose);active.Add(effect);
                if(!string.IsNullOrEmpty(id)){if(!named.TryGetValue(id,out var list))named.Add(id,list=new List<ISkeletonPlayer>());list.Add(effect);}
                string animation=effect.Animations.FirstOrDefault(a=>a.Name=="animation"||a.Name=="idle")?.Name??effect.Animations.FirstOrDefault()?.Name;
                if(animation!=null)effect.Play(animation,false);
                foreach(JObject action in part["actions"]??new JArray())
                {
                    string name=(string)action["action"];
                    if(effect.Animations.Any(a=>a.Name==name))pending.Add((time+Number(action["delay"],0)/1000d,()=>effect.Play(name,false)));
                    else report("Unverified CFX action "+action.ToString(Newtonsoft.Json.Formatting.None));
                }
                double lifetime=Number(part["lifeTime"],0)/1000d;
                if(lifetime<=0)lifetime=effect.Duration;
                if(lifetime>0)pending.Add((time+lifetime,()=>{active.Remove(effect);effect.Root.SetActive(false);}));
            }
            else if(format=="particle")
            {
                string path="effect/"+source+".particle";
                if(!documents.TryGetValue(path,out var doc)){report("Missing particle "+path);return;}
                var particle=new CharacterParticlePreview(doc,part,anchor.position,textures,context.WorldRoot,report);
                particles.Add(particle);scope.Add(particle.Dispose);
            }
            else report("Unsupported CFX primitive "+part.ToString(Newtonsoft.Json.Formatting.None));
        }
        private void Remove(string id)
        {
            if(id==null||!named.TryGetValue(id,out var values))return;
            foreach(var value in values){active.Remove(value);value.Root.SetActive(false);}named.Remove(id);
        }
        private void Cutin(string name)
        {
            string path="cut/fhd/"+name+".webp";
            if(!documents.TryGetValue(path,out var descriptor)){report("Missing CUTIN "+path);return;}
            var root=new GameObject("CUTIN "+name,typeof(MeshFilter),typeof(MeshRenderer));
            var shader=Resources.Load<Shader>("CharacterLabUnlit");
            if(!shader){UnityEngine.Object.Destroy(root);report("Missing CharacterLabUnlit shader.");return;}
            var material=new Material(shader);
            root.GetComponent<MeshRenderer>().sharedMaterial=material;
            var mesh=Quad();root.GetComponent<MeshFilter>().sharedMesh=mesh;
            root.transform.SetParent(context.PreviewCamera.transform,false);root.transform.localPosition=new Vector3(0,0,2);
            float height=context.PreviewCamera.orthographicSize*2;
            root.transform.localScale=new Vector3(height*Number(descriptor["width"],1920)/Number(descriptor["height"],1080),height,1);
            scope.Add(()=>{UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);});
            cutins.Add((root,descriptor,time));UpdateCutins();
        }
        public void BeginFrame() => screen.BeginFrame();
        public void Tick(float delta)
        {
            if(disposed)return;time+=delta;
            foreach(var item in pending.Where(p=>p.at<=time).ToArray()){pending.Remove(item);item.action();}
            if(delta>0) {
                foreach(var item in active.ToArray())if(item.Root.activeSelf)item.Tick(delta);
                foreach(var item in particles)item.Tick(delta);
            }
            UpdateCutins();screen.Tick(delta);
        }
        private void UpdateCutins()
        {
            foreach(var item in cutins.ToArray())
            {
                double elapsed=time-item.start;JObject current=null;
                foreach(JObject frame in item.descriptor["frames"]??new JArray())
                {double duration=Number(frame["durationSeconds"],0);if(elapsed<duration){current=frame;break;}elapsed-=duration;}
                if(current==null){item.root.SetActive(false);cutins.Remove(item);continue;}
                string address=(string)current["address"];
                if(textures.TryGetValue(address,out var texture))item.root.GetComponent<MeshRenderer>().sharedMaterial.mainTexture=texture;
            }
        }
        internal static Mesh Quad()
        {
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(.5f,.5f),new Vector3(-.5f,.5f)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
            mesh.triangles=new[]{0,2,1,0,3,2};return mesh;
        }
        internal static float Number(JToken value,float fallback)=>value==null?fallback:float.Parse(value.ToString(),CultureInfo.InvariantCulture);
        public void Dispose()
        {
            if(disposed)return;disposed=true;screen.Dispose();pending.Clear();active.Clear();cutins.Clear();
            foreach(var item in particles)item.Dispose();particles.Clear();
            if(caster.Root){caster.Root.transform.localPosition=casterPosition;caster.Root.transform.localScale=casterScale;}
            if(context.PreviewCamera){context.PreviewCamera.orthographicSize=cameraSize;context.PreviewCamera.transform.position=cameraPosition;}
        }
    }
}
