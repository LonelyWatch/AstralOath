using System;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Mail
{
    // Original CFX title animation is sampled without a runtime Spine dependency.
    // Particle parameters and textures come from the same CFX; the random seed is local.
    internal sealed class MailRewardEffects
    {
        private sealed class Particle
        {
            public Image image;public float delay,life,size0,size1,angle,radius0,radius1,speed,rotation;
            public Vector2 origin;public float[] source;
        }
        private readonly RawImage title;
        private readonly RectTransform particles;
        private readonly Particle[] entries;
        private float started=-1;
        internal MailRewardEffects(BindingMap map)
        {
            title=map.Require<RawImage>("effectTitle");particles=map.Require<RectTransform>("effectParticles");
            var images=particles.GetComponentsInChildren<Image>(true);entries=new Particle[images.Length];
            var random=new System.Random(1122);int index=0;
            float V(float value,float variance)=>value+((float)random.NextDouble()*2-1)*variance;
            foreach(var source in MailEffectParticleData.Emitters)
            {
                int count=(int)Math.Ceiling(source[0]*source[1]);
                for(int i=0;i<count&&index<images.Length;i++,index++)
                {
                    entries[index]=new Particle{image=images[index],source=source,delay=i/source[0],life=Mathf.Max(.05f,V(source[3],source[4])),size0=Mathf.Max(1,V(source[19],source[20])),size1=Mathf.Max(1,V(source[21],source[22])),angle=V(source[13],source[14])*Mathf.Deg2Rad,radius0=V(source[9],source[10]),radius1=V(source[11],source[12]),speed=V(source[15],source[16]),rotation=V(source[23],source[24]),origin=new Vector2(V(source[5],source[7]),V(source[6],source[8]))};
                }
            }
            Stop();
        }
        internal void Play(){started=Time.unscaledTime;title.enabled=true;particles.gameObject.SetActive(true);}
        internal void Stop(){started=-1;title.enabled=false;particles.gameObject.SetActive(false);}
        internal void Update()
        {
            if(started<0)return;
            float elapsed=Time.unscaledTime-started;int frame=(int)(elapsed*30);
            title.enabled=frame<24;
            if(frame<24)title.uvRect=new Rect((frame%4)/4f,1-(frame/4+1)/6f,1f/4,1f/6);
            bool alive=false;
            foreach(var entry in entries)
            {
                if(entry==null)continue;
                float age=elapsed-entry.delay,portion=age/entry.life;
                bool visible=age>=0&&portion<1;entry.image.enabled=visible;if(!visible)continue;alive=true;
                var rect=entry.image.rectTransform;float radius=Mathf.Lerp(entry.radius0,entry.radius1,portion);
                Vector2 direction=new Vector2(Mathf.Cos(entry.angle),Mathf.Sin(entry.angle));
                Vector2 displacement=entry.source[2]==1?direction*radius:direction*(entry.speed*age)+new Vector2(entry.source[17],entry.source[18])*(.5f*age*age);
                rect.anchoredPosition=(entry.origin+displacement)*.6f;
                rect.sizeDelta=Vector2.one*Mathf.Lerp(entry.size0,entry.size1,portion)*.12f;
                rect.localRotation=Quaternion.Euler(0,0,entry.rotation);
                float alpha=portion<.04362416f?portion/.04362416f:portion>.9830508f?(1-portion)/(1-.9830508f):1;
                Color color=portion<.6464891f?Color.Lerp(new Color(.5019608f,1,.9764706f),new Color(.34117648f,1,.9764706f),portion/.6464891f):Color.Lerp(new Color(.34117648f,1,.9764706f),new Color(.101960786f,.8039216f,1),(portion-.6464891f)/(1-.6464891f));
                color.a=alpha;entry.image.color=color;
            }
            if(!alive&&elapsed>2.5f)Stop();
        }
    }
}
