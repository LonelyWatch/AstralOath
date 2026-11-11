using System;
using AstralOath.Client.CharacterPresentation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.CharacterLab
{
    // Owns a camera image solely for the lab. Scope of original sprite grouping remains unverified.
    public sealed class CharacterBlurPreview : IDisposable
    {
        private readonly Camera camera;
        private readonly RawImage image;
        private readonly Material material;
        private readonly RenderTexture previous;
        private readonly float previousAspect;
        private readonly Vector3[] corners=new Vector3[4];
        private RenderTexture target;
        private string activeId;
        private double time,end;
        private bool disposed;
        public CharacterBlurPreview(Camera camera,RawImage image,Material source)
        {
            this.camera=camera;this.image=image;
            previous=camera.targetTexture;previousAspect=camera.aspect;
            material=new Material(source){name="CharacterLab Blur Instance"};
            image.material=material;
            material.SetFloat("u_range",0);
            try {Resize();image.gameObject.SetActive(true);} catch {Dispose();throw;}
        }
        public void Resize()
        {
            if(disposed)return;
            // The design-sized preview can be smaller than the screen after CanvasScaler.Expand.
            // Project its edges to pixels so the render target and camera retain that aspect ratio.
            image.rectTransform.GetWorldCorners(corners);
            var canvas=image.canvas;
            var uiCamera=canvas && canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            var origin=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[0]);
            int width=Math.Max(1,Mathf.RoundToInt(Vector2.Distance(origin,RectTransformUtility.WorldToScreenPoint(uiCamera,corners[3]))));
            int height=Math.Max(1,Mathf.RoundToInt(Vector2.Distance(origin,RectTransformUtility.WorldToScreenPoint(uiCamera,corners[1]))));
            if(target && target.width==width && target.height==height)return;
            var old=target;
            var next=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="CharacterLab Camera Preview",useMipMap=false,autoGenerateMips=false};
            if(!next.Create()){Release(next);throw new InvalidOperationException("Could not allocate CharacterLab preview texture.");}
            target=next;
            camera.targetTexture=target;camera.aspect=width/(float)height;image.texture=target;
            material.SetVector("u_resolution",new Vector4(width,height,0,0));
            if(old){old.Release();Release(old);}
        }
        public void Play(JObject node,Action<string> report)
        {
            if((string)node["program"]!="sprite_blur"){report("Unsupported PPEFFECT program: "+node["program"]);return;}
            if(node["u_range"]==null||node["u_sample"]==null||node["duration"]==null)
            {report("PPEFFECT sprite_blur is missing required explicit u_range/u_sample/duration; no defaults invented.");return;}
            float ratio=CharacterEffectStage.Number(node["u_ratio"],0);
            if(node["u_ratio"]==null)report("PPEFFECT u_ratio is absent: lab explicitly previews 0 (fully blurred), original engine default is unverified.");
            var settings=new SpriteBlurSettings(CharacterEffectStage.Number(node["u_range"],0),CharacterEffectStage.Number(node["u_sample"],0),
                ratio,CharacterEffectStage.Number(node["direction_x"],0),CharacterEffectStage.Number(node["direction_y"],0),
                CharacterEffectStage.Number(node["duration"],0)/1000f);
            material.SetVector("u_direction",new Vector4(settings.DirectionX,settings.DirectionY,0,0));
            material.SetFloat("u_range",settings.Range);material.SetFloat("u_sample",settings.Samples);material.SetFloat("u_ratio",settings.Ratio);
            activeId=(string)node["id"]??"pp:"+node["guid"];end=time+settings.Duration;
            report("PPEFFECT sprite_blur uses the source shader's weighted kernel and explicit uniforms. Entire lab camera image is processed; original sprite scope and SINE_VALUE modulation remain unverified.");
        }
        public void Tick(float delta)
        {
            if(disposed)return;
            time+=delta;
            if(activeId!=null&&time>=end)Reset();
        }
        public void Stop(string id){if(activeId==id)Reset();}
        public void Reset()
        {
            activeId=null;
            if(material)material.SetFloat("u_range",0);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(camera && camera.targetTexture==target){camera.targetTexture=previous;camera.aspect=previousAspect;}
            if(image){image.texture=null;image.material=null;image.gameObject.SetActive(false);}
            if(target){target.Release();Release(target);target=null;}
            Release(material);
        }
        private static void Release(UnityEngine.Object value)
        {if(!value)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
