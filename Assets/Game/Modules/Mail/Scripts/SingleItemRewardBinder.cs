using System;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Client.Mail
{
    // Source gravity-mode burst is stepped in source units, then mapped to Canvas.
    // This class never claims inventory, sends requests, plays another reward or owns the caller's clock.
    public sealed class SingleItemRewardBinder:IDisposable
    {
        private sealed class Particle
        {
            internal Image image;internal Vector2 origin,position,velocity,gravity;
            internal float life,startSize,endSize,rotation,spin,radial,tangential;internal int tick;
        }
        private struct Key { internal float time;internal Color color; }
        private readonly SingleItemReward view;private readonly BindingScope owner;private readonly Image effect;
        private readonly Sprite originalAtlas;private readonly Sprite[] frames;private readonly Particle[] particles;
        private readonly JArray emitters;private readonly Key[] ramp;private readonly float scale,delay,fps;private readonly RectTransform particleRoot;
        private float elapsed;private bool spawned,disposed;
        private const float Step=1f/120;
        public bool IsPlaying{get;private set;}
        public int ActiveParticleCount{get;private set;}
        public float Duration=>frames.Length/fps;
        public SingleItemRewardBinder(SingleItemReward view,BindingScope owner)
        {
            this.view=view??throw new ArgumentNullException(nameof(view));this.owner=owner??throw new ArgumentNullException(nameof(owner));owner.Token.ThrowIfCancellationRequested();
            var source=JObject.Parse(SingleItemRewardSource.Json);emitters=(JArray)source["emitters"];
            scale=(float)source["sourceToDesignScale"];delay=(int)source["sourcePrimitiveDelayMilliseconds"]/1000f;fps=(float)source["framesPerSecond"];
            effect=view.Map.Require<Image>("effect");originalAtlas=effect.sprite;
            int columns=(int)source["columns"],rows=(int)source["rows"],w=(int)source["cellWidth"],h=(int)source["cellHeight"];
            frames=new Sprite[(int)source["frameCount"]];
            try
            {
                for(int i=0;i<frames.Length;i++)
                {frames[i]=Sprite.Create(originalAtlas.texture,new Rect(i%columns*w,(rows-1-i/columns)*h,w,h),Vector2.one*.5f,100,0,SpriteMeshType.FullRect);frames[i].name="SingleRewardFrame"+i.ToString("000",CultureInfo.InvariantCulture);}
                particleRoot=view.Map.Require<RectTransform>("particles");var images=particleRoot.GetComponentsInChildren<Image>(true);
                int capacity=emitters.Sum(e=>(int)e["maxParticles"]);if(images.Length!=capacity)throw new InvalidOperationException("Single reward particle pool differs from its original emitters.");
                particles=images.Select(i=>new Particle{image=i}).ToArray();
                ramp=ParseRamp((string)emitters[0]["colorBlends"][0]);
                foreach(var e in emitters)
                    if((int)e["burningType"]!=1||(int)e["emitterType"]!=0||(int)e["blendFuncSource"]!=770||(int)e["blendFuncDestination"]!=1||(string)e["colorBlends"][0]!=(string)emitters[0]["colorBlends"][0]||N(e,"startDelay")!=0||N(e,"startDelayVar")!=0||N(e,"interval")!=0||(int)e["repeat"]!=1||N(e,"scale")!=1)
                        throw new FormatException("Single reward source emitter schema changed; review before rendering.");
                owner.Add(Dispose);Stop();
            }
            catch{foreach(var frame in frames)Destroy(frame);throw;}
        }
        public void Play(Sprite icon,string quantity,string kind,string text)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SingleItemRewardBinder));owner.Token.ThrowIfCancellationRequested();Stop();
            var image=view.Map.Require<Image>("icon");image.sprite=icon;image.enabled=icon;
            Set("quantity",quantity);Set("kind",kind);Set("text",text);
            elapsed=0;IsPlaying=true;spawned=false;view.Root.GetComponent<CanvasGroup>().alpha=1;view.Show();Update(0);
        }
        private void Set(string key,string value){var text=view.Map.Require<GameText>(key);text.richText=false;text.text=value??"";}
        // elapsed is cumulative unscaled seconds since Play, not deltaTime. Seeking backward resets the source burst.
        public void Update(float elapsed)
        {
            if(disposed||!IsPlaying)return;if(float.IsNaN(elapsed)||float.IsInfinity(elapsed)||elapsed<0)throw new ArgumentOutOfRangeException(nameof(elapsed));
            if(elapsed>=Duration){Stop();return;}
            if(elapsed<this.elapsed){spawned=false;foreach(var p in particles){p.tick=0;p.image.enabled=false;}}
            this.elapsed=elapsed;effect.sprite=frames[Mathf.Clamp((int)(elapsed*fps),0,frames.Length-1)];effect.enabled=true;
            view.Root.GetComponent<CanvasGroup>().alpha=Mathf.Clamp01((Duration-elapsed)/.20f);
            if(elapsed<delay){ActiveParticleCount=0;return;}
            if(!spawned){Spawn();spawned=true;}
            float age=elapsed-delay;ActiveParticleCount=0;
            foreach(var p in particles)
            {
                bool alive=age<p.life;p.image.enabled=alive;if(!alive)continue;ActiveParticleCount++;
                int tick=Mathf.FloorToInt(age/Step);
                while(p.tick<tick){Advance(ref p.position,ref p.velocity,p.gravity,p.radial,p.tangential,Step);p.tick++;}
                var position=p.position;var velocity=p.velocity;Advance(ref position,ref velocity,p.gravity,p.radial,p.tangential,age-p.tick*Step);
                float t=age/p.life;var rect=p.image.rectTransform;rect.anchoredPosition=position*scale;
                rect.sizeDelta=Vector2.one*Mathf.Lerp(p.startSize,p.endSize,t)*scale;rect.localRotation=Quaternion.Euler(0,0,-(p.rotation+p.spin*age));p.image.color=ColorAt(ramp,t);
            }
        }
        private void Spawn()
        {
            // Source variances are stochastic; keep one local seed for reproducible captures and no shared game RNG.
            var random=new System.Random(1122);int index=0;
            float Sample(JToken e,string value,string variance)=>N(e,value)+(float)(random.NextDouble()*2-1)*N(e,variance);
            foreach(var e in emitters)
            {
                for(int i=0;i<(int)e["maxParticles"];i++)
                {
                    var p=particles[index++];p.origin=new Vector2(Sample(e,"sourcePositionx","sourcePositionVariancex"),Sample(e,"sourcePositiony","sourcePositionVariancey"));p.position=p.origin;p.tick=0;
                    float angle=Sample(e,"angle","angleVariance")*Mathf.Deg2Rad;float speed=Sample(e,"speed","speedVariance");p.velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed;p.gravity=new Vector2(N(e,"gravityx"),N(e,"gravityy"));
                    p.life=Mathf.Max(.01f,Sample(e,"particleLifespan","particleLifespanVariance"));p.startSize=Mathf.Max(0,Sample(e,"startParticleSize","startParticleSizeVariance"));p.endSize=Mathf.Max(0,Sample(e,"finishParticleSize","finishParticleSizeVariance"));
                    p.radial=Sample(e,"radialAcceleration","radialAccelVariance");p.tangential=Sample(e,"tangentialAcceleration","tangentialAccelVariance");p.rotation=Sample(e,"rotationStart","rotationStartVariance");p.spin=Sample(e,"rotationDir","rotationDirVariance");
                }
            }
        }
        private static float N(JToken token,string key)=>(float?)token[key]??0;
        private static void Advance(ref Vector2 position,ref Vector2 velocity,Vector2 gravity,float radial,float tangential,float dt)
        {
            var axis=position.normalized;var acceleration=gravity+axis*radial+new Vector2(-axis.y,axis.x)*tangential;
            position+=(velocity+acceleration*(.5f*dt))*dt;velocity+=acceleration*dt;
        }
        private static Key[] ParseRamp(string value)=>value.Split(';').Select(row=>
        {var n=row.Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray();if(n.Length!=5)throw new FormatException("Invalid source particle color key.");return new Key{time=n[0],color=new Color(n[2],n[3],n[4],n[1])};}).ToArray();
        private static Color ColorAt(Key[] keys,float time)
        {
            if(time<=keys[0].time)return keys[0].color;
            for(int i=1;i<keys.Length;i++)if(time<=keys[i].time)return Color.LerpUnclamped(keys[i-1].color,keys[i].color,(time-keys[i-1].time)/(keys[i].time-keys[i-1].time));
            return keys[keys.Length-1].color;
        }
        public void Stop()
        {
            IsPlaying=false;ActiveParticleCount=0;spawned=false;elapsed=0;
            if(particles!=null)foreach(var p in particles)if(p.image)p.image.enabled=false;
            if(effect){effect.sprite=originalAtlas;effect.enabled=false;}
            if(view.Root)view.Hide();
        }
        public void Dispose(){if(disposed)return;Stop();disposed=true;foreach(var sprite in frames)Destroy(sprite);}
        private static void Destroy(Object target){if(!target)return;if(Application.isPlaying)Object.Destroy(target);else Object.DestroyImmediate(target);}
    }
}
