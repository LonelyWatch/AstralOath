using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace AstralOath.Client.CharacterLab
{
    // Manual particles share the presentation clock and are never advanced by Unity Update.
    public sealed class CharacterParticlePreview : IDisposable
    {
        private sealed class Particle
        {
            public Vector2 position,velocity;public float age,life,startSize,endSize,rotation;
        }
        private sealed class Emitter
        {
            public JObject data;public GameObject root;public Mesh mesh;public Material material;
            public readonly List<Particle> particles=new List<Particle>();
            public float remainder,age,duration;public int maximum;
        }
        private readonly List<Emitter> emitters=new List<Emitter>();
        private readonly System.Random random=new System.Random(1);
        private readonly float lifetime;
        private float age;
        private bool disposed;
        public CharacterParticlePreview(JObject document,JObject primitive,Vector3 position,IReadOnlyDictionary<string,Texture2D> textures,Transform parent,Action<string> report)
        {
            lifetime=N(primitive,"lifeTime",-1)/1000;
            var shader=Resources.Load<Shader>("CharacterLabUnlit");
            if(!shader){report("Missing particle shader CharacterLabUnlit");return;}
            foreach(JObject data in document["emitters"]??new JArray())
            {
                if(data["disable"]?.Value<bool>()==true)continue;
                if(N(data,"emitterType",0)!=0){report("Unsupported radial particle emitter "+data["name"]);continue;}
                string textureName=(string)data["textureFileName"];
                if(textureName==null||!textures.TryGetValue(textureName,out var texture)){report("Missing particle texture "+textureName);continue;}
                var root=new GameObject("Particle "+data["name"],typeof(MeshFilter),typeof(MeshRenderer));
                root.transform.SetParent(parent,false);
                root.transform.position=position+new Vector3(N(primitive,"x",0)*.01f,N(primitive,"y",0)*.01f,-.1f);
                root.transform.localScale=Vector3.one*N(primitive,"scale",1);
                root.transform.localRotation=Quaternion.Euler(0,0,-N(primitive,"rotate",0));
                var mesh=new Mesh();mesh.MarkDynamic();root.GetComponent<MeshFilter>().sharedMesh=mesh;
                var material=new Material(shader){mainTexture=texture};
                material.SetFloat("_SrcBlend",Blend(N(data,"blendFuncSource",770),5,report));
                material.SetFloat("_DstBlend",Blend(N(data,"blendFuncDestination",771),10,report));
                root.GetComponent<MeshRenderer>().sharedMaterial=material;
                emitters.Add(new Emitter{data=data,root=root,mesh=mesh,material=material,maximum=Mathf.Clamp((int)N(data,"maxParticles",100),1,4096),duration=N(data,"duration",-1)});
                if(data["colorBlends"]!=null||N(data,"burningType",0)!=0||N(data,"positionType",0)!=0||N(data,"repeat",1)!=1)
                    report("Particle preview preserves but does not reproduce colorBlends/burningType/positionType/repeat: "+data["name"]);
            }
        }
        private static float Blend(float value,float fallback,Action<string> report)
        {
            if(value==0)return 0;if(value==1)return 1;if(value==770)return 5;if(value==771)return 10;
            report("Unsupported particle blend "+value);return fallback;
        }
        public void Tick(float delta)
        {
            if(disposed)return;
            age+=delta;
            foreach(var emitter in emitters)
            {
                var d=emitter.data;emitter.age+=delta;
                float start=N(d,"startDelay",0);
                if(emitter.age>=start&&(emitter.duration<0||emitter.age-start<emitter.duration)&&(lifetime<0||age<lifetime))
                {
                    emitter.remainder+=delta*N(d,"emissionRate",0);
                    int count=Math.Min((int)emitter.remainder,emitter.maximum-emitter.particles.Count);emitter.remainder-=(int)emitter.remainder;
                    for(int i=0;i<count;i++)
                    {
                        float angle=Range(N(d,"angle",0),N(d,"angleVariance",0))*Mathf.Deg2Rad;
                        float speed=Range(N(d,"speed",0),N(d,"speedVariance",0))*.01f;
                        emitter.particles.Add(new Particle{
                            position=new Vector2(Range(N(d,"sourcePositionx",0),N(d,"sourcePositionVariancex",0)),Range(N(d,"sourcePositiony",0),N(d,"sourcePositionVariancey",0)))*.01f,
                            velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed,
                            life=Mathf.Max(.01f,Range(N(d,"particleLifespan",1),N(d,"particleLifespanVariance",0))),
                            startSize=Mathf.Max(0,Range(N(d,"startParticleSize",10),N(d,"startParticleSizeVariance",0)))*.01f,
                            endSize=Mathf.Max(0,Range(N(d,"finishParticleSize",10),N(d,"finishParticleSizeVariance",0)))*.01f,
                            rotation=Range(N(d,"rotationStart",0),N(d,"rotationStartVariance",0))*Mathf.Deg2Rad
                        });
                    }
                }
                foreach(var particle in emitter.particles.ToArray())
                {
                    particle.age+=delta;if(particle.age>=particle.life){emitter.particles.Remove(particle);continue;}
                    Vector2 radial=particle.position.normalized;
                    var acceleration=new Vector2(N(d,"gravityx",0),N(d,"gravityy",0))*.01f+
                        radial*N(d,"radialAcceleration",0)*.01f+new Vector2(-radial.y,radial.x)*N(d,"tangentialAcceleration",0)*.01f;
                    particle.velocity+=acceleration*delta;particle.position+=particle.velocity*delta;
                }
                Draw(emitter);
            }
        }
        private static void Draw(Emitter emitter)
        {
            int count=emitter.particles.Count;
            var vertices=new Vector3[count*4];var colors=new Color[count*4];var uv=new Vector2[count*4];var triangles=new int[count*6];
            Color from=C(emitter.data,"start"),to=C(emitter.data,"finish");
            var corners=new[]{new Vector2(-1,-1),new Vector2(1,-1),Vector2.one,new Vector2(-1,1)};
            for(int i=0;i<count;i++)
            {
                var particle=emitter.particles[i];float t=particle.age/particle.life;
                float size=Mathf.Lerp(particle.startSize,particle.endSize,t)*.5f;float sin=Mathf.Sin(particle.rotation),cos=Mathf.Cos(particle.rotation);
                for(int j=0;j<4;j++){Vector2 v=corners[j]*size;vertices[i*4+j]=particle.position+new Vector2(v.x*cos-v.y*sin,v.x*sin+v.y*cos);colors[i*4+j]=Color.Lerp(from,to,t);uv[i*4+j]=(corners[j]+Vector2.one)*.5f;}
                int n=i*4,k=i*6;triangles[k]=n;triangles[k+1]=n+2;triangles[k+2]=n+1;triangles[k+3]=n;triangles[k+4]=n+3;triangles[k+5]=n+2;
            }
            emitter.mesh.Clear();emitter.mesh.vertices=vertices;emitter.mesh.colors=colors;emitter.mesh.uv=uv;emitter.mesh.triangles=triangles;emitter.mesh.RecalculateBounds();
        }
        private float Range(float center,float variance)=>center+((float)random.NextDouble()*2-1)*variance;
        private static float N(JObject data,string key,float fallback)=>CharacterEffectStage.Number(data[key],fallback);
        private static Color C(JObject data,string prefix)=>new Color(N(data,prefix+"ColorRed",1),N(data,prefix+"ColorGreen",1),N(data,prefix+"ColorBlue",1),N(data,prefix+"ColorAlpha",1));
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var emitter in emitters){UnityEngine.Object.Destroy(emitter.root);UnityEngine.Object.Destroy(emitter.mesh);UnityEngine.Object.Destroy(emitter.material);}
            emitters.Clear();
        }
    }
}
