using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleDeathPresentationTests
    {
        [TestCase(0f, 1f, 0f)]
        [TestCase(.5f, 1f, .5f)]
        [TestCase(0f, .5f, .1f)]
        [TestCase(0f, 0f, .2f)]
        public void SkeletonMaterialCanDarkenThenFadeWithoutReplacingSourceTextures(float brightness, float opacity, float expected)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A real GPU is required.");
            var material = new Material(Resources.Load<Shader>("AstralOathSkeleton"));
            var mesh = new Mesh();
            var target = RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16,16,TextureFormat.RGBA32,false,true);
            var previous = RenderTexture.active;
            var commands = new CommandBuffer();
            try
            {
                material.mainTexture = Texture2D.whiteTexture;
                material.SetFloat("_PresentationBrightness",brightness);
                material.SetFloat("_PresentationOpacity",opacity);
                mesh.vertices = new[] { new Vector3(-1,-1),new Vector3(1,-1),new Vector3(1,1),new Vector3(-1,1) };
                mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
                mesh.SetUVs(1,new List<Vector4>{Vector4.zero,Vector4.zero,Vector4.zero,Vector4.zero});
                mesh.colors = new[] { Color.white,Color.white,Color.white,Color.white };
                mesh.triangles = new[] {0,2,1,0,3,2};
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false,true,new Color(.2f,.2f,.2f,1));
                commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                commands.DrawMesh(mesh,Matrix4x4.identity,material);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active=target;
                readback.ReadPixels(new Rect(0,0,16,16),0,0);readback.Apply();
                var actual=readback.GetPixel(8,8);
                Assert.That(actual.r,Is.EqualTo(expected).Within(.008f),"Death must change rendered pixels, not only bookkeeping.");
                Assert.That(actual.g,Is.EqualTo(expected).Within(.008f));
                Assert.That(actual.b,Is.EqualTo(expected).Within(.008f));
            }
            finally
            {
                RenderTexture.active=previous;commands.Release();RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TimingProfileMustBeFinitePositiveAndCannotChangeDuringDeath()
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDeathPresentation, AstralOath.Client.BattleScene",true);
            var method=type.GetMethod("ConfigureTimings");
            Assert.That(method,Is.Not.Null,"Timing must be configurable from the shared presentation profile.");
            var root=new GameObject("Death timing regression");
            var presentation=(IDisposable)Activator.CreateInstance(type,root.transform,null,new Dictionary<string,Texture2D>());
            try
            {
                foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
                {
                    var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(presentation,new object[]{invalid,.4f}));
                    Assert.That(error.InnerException,Is.TypeOf<ArgumentOutOfRangeException>());
                    error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(presentation,new object[]{.2f,invalid}));
                    Assert.That(error.InnerException,Is.TypeOf<ArgumentOutOfRangeException>());
                }
                method.Invoke(presentation,new object[]{.35f,.6f});
                Assert.That(type.GetProperty("DarkenSeconds").GetValue(presentation),Is.EqualTo(.35f));
                Assert.That(type.GetProperty("FadeSeconds").GetValue(presentation),Is.EqualTo(.6f));
                type.GetMethod("Begin").Invoke(presentation,null);
                var running=Assert.Throws<TargetInvocationException>(()=>method.Invoke(presentation,new object[]{.1f,.1f}));
                Assert.That(running.InnerException,Is.TypeOf<InvalidOperationException>());
                type.GetMethod("Tick").Invoke(presentation,new object[]{.94f});
                Assert.That(type.GetProperty("IsComplete").GetValue(presentation),Is.False);
                type.GetMethod("Tick").Invoke(presentation,new object[]{.02f});
                Assert.That(type.GetProperty("IsComplete").GetValue(presentation),Is.True);
                type.GetMethod("Reset").Invoke(presentation,null);
                method.Invoke(presentation,new object[]{.2f,.4f});
            }
            finally { presentation.Dispose();Object.DestroyImmediate(root); }
        }

        [TestCase(BlendMode.One,BlendMode.OneMinusSrcAlpha,.4f)]
        [TestCase(BlendMode.One,BlendMode.One,.4f)]
        [TestCase(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha,.4f)]
        [TestCase(BlendMode.One,BlendMode.OneMinusSrcColor,.4f)]
        [TestCase(BlendMode.One,BlendMode.OneMinusSrcAlpha,0f)]
        [TestCase(BlendMode.One,BlendMode.One,0f)]
        [TestCase(BlendMode.DstColor,BlendMode.OneMinusSrcAlpha,0f)]
        [TestCase(BlendMode.One,BlendMode.OneMinusSrcColor,0f)]
        public void DeathOpacityPreservesEverySkeletonBlendEquation(BlendMode src,BlendMode dst,float opacity)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A real GPU is required.");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            var color=new Color(.6f,.4f,.2f,.75f);texture.SetPixels(new[]{color,color,color,color});texture.Apply();
            var material=new Material(Resources.Load<Shader>("AstralOathSkeleton"));
            var mesh=new Mesh();var commands=new CommandBuffer();
            var target=RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var image=new Texture2D(16,16,TextureFormat.RGBA32,false,true);var previous=RenderTexture.active;
            var clear=new Color(.2f,.3f,.4f,1f);const float brightness=.35f;
            try
            {
                material.mainTexture=texture;material.SetInt("_SrcBlend",(int)src);material.SetInt("_DstBlend",(int)dst);
                material.SetFloat("_PresentationBrightness",brightness);material.SetFloat("_PresentationOpacity",opacity);
                mesh.vertices=new[]{new Vector3(-1,-1),new Vector3(1,-1),new Vector3(1,1),new Vector3(-1,1)};
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
                // The real skeleton mesh always supplies the Spine two-color dark-tint stream.
                // Omitting TEXCOORD1 allows backend fallback data to tint the synthetic quad.
                mesh.SetUVs(1,new List<Vector4>{Vector4.zero,Vector4.zero,Vector4.zero,Vector4.zero});
                mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
                mesh.triangles=new[]{0,2,1,0,3,2};commands.SetRenderTarget(target);commands.ClearRenderTarget(false,true,clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);commands.DrawMesh(mesh,Matrix4x4.identity,material);
                Graphics.ExecuteCommandBuffer(commands);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,16,16),0,0);image.Apply();
                var actual=image.GetPixel(8,8);var premult=color*(color.a*brightness*opacity);float alpha=color.a*opacity;
                for(int channel=0;channel<3;channel++)
                {
                    float sourceFactor=src==BlendMode.DstColor?clear[channel]:1f;
                    float destinationFactor=dst==BlendMode.One?1f:dst==BlendMode.OneMinusSrcColor?1f-premult[channel]:1f-alpha;
                    Assert.That(actual[channel],Is.EqualTo(premult[channel]*sourceFactor+clear[channel]*destinationFactor).Within(.009f));
                }
                Assert.That(actual.a,Is.EqualTo(1f).Within(.008f),"Separate alpha blending must remain valid for compositing.");
            }
            finally
            {
                RenderTexture.active=previous;commands.Release();RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ExplicitBodyDoesNotTintRetiredOrAuxiliaryRenderers()
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDeathPresentation, AstralOath.Client.BattleScene",true);
            var root=new GameObject("Explicit death owner");
            var retired=new GameObject("Retired body",typeof(MeshRenderer)).GetComponent<MeshRenderer>();retired.transform.SetParent(root.transform);
            var body=new GameObject("Current body",typeof(MeshRenderer)).GetComponent<MeshRenderer>();body.transform.SetParent(root.transform);
            var block=new MaterialPropertyBlock();block.SetFloat("_PresentationBrightness",.8f);block.SetFloat("_PresentationOpacity",.6f);body.SetPropertyBlock(block);
            block.Clear();block.SetFloat("_PresentationBrightness",.3f);retired.SetPropertyBlock(block);
            var presentation=(IDisposable)Activator.CreateInstance(type,root.transform,null,new Dictionary<string,Texture2D>(),null,body);
            try
            {
                type.GetMethod("Begin").Invoke(presentation,null);type.GetMethod("Tick").Invoke(presentation,new object[]{.1f});
                retired.GetPropertyBlock(block);Assert.That(block.GetFloat("_PresentationBrightness"),Is.EqualTo(.3f));
                Assert.That(block.HasFloat("_PresentationOpacity"),Is.False);
                body.GetPropertyBlock(block);Assert.That(block.GetFloat("_PresentationBrightness"),Is.EqualTo(.4f).Within(.0001f));
                type.GetMethod("Reset").Invoke(presentation,null);body.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_PresentationBrightness"),Is.EqualTo(.8f));
                Assert.That(block.GetFloat("_PresentationOpacity"),Is.EqualTo(.6f));
            }
            finally {presentation.Dispose();Object.DestroyImmediate(root);}
        }

        [TestCase("Assets/Game/Modules/BattleScene/Res/Effects/Death/Particles/death_pati_02.particle.json", .1019608f, .0627451f, .1372549f, 1f)]
        [TestCase("Assets/Game/Modules/BattleScene/Res/Effects/Hud/Particles/soul_burn_loop_pati.particle.json", .5019608f, .9843137f, 1f, 0f)]
        public void OriginalParticleColorRampStoresAlphaBeforeRgb(string path,float r,float g,float b,float a)
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path);Assert.That(source,Is.Not.Null);
            var groups=JObject.Parse(source.text)["emitters"][0]["colorBlends"].ToObject<string[]>();
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleHudParticles, AstralOath.Client.BattleScene",true);
            var keys=(ValueTuple<float,Color>[])type.GetMethod("ReadRamp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{groups});
            Assert.That(keys[0].Item1,Is.Zero);
            var first=keys[0].Item2;
            Assert.That(first.a,Is.EqualTo(a).Within(.00001f),"The second source column is alpha, not red.");
            Assert.That(first.r,Is.EqualTo(r).Within(.00001f));Assert.That(first.g,Is.EqualTo(g).Within(.00001f));Assert.That(first.b,Is.EqualTo(b).Within(.00001f));
            Assert.That(keys[keys.Length-1].Item2.a,Is.Zero.Within(.00001f),"Native death/Burn ramps end transparent; blue is not opacity.");
        }

        [Test]
        public void OriginalDeathSmokeEmitsDarkPurpleOpaqueVerticesRatherThanBrightRedClouds()
        {
            const string directory="Assets/Game/Modules/BattleScene/Res/Effects/Death/Particles/";
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(directory+"death_pati_02.particle.json");
            var document=JObject.Parse(source.text);
            document["emitters"]=new JArray(document["emitters"][0].DeepClone());
            var definition=new TextAsset(document.ToString());
            var root=new GameObject("Native death smoke color regression");
            var texture=UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(directory+"soft_smoke.png");
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleHudParticles, AstralOath.Client.BattleScene",true);
            var particles=(IDisposable)Activator.CreateInstance(type,definition,new Dictionary<string,Texture2D>{{"soft_smoke",texture}},root.transform,Vector2.zero,.012f,0,true);
            try
            {
                type.GetMethod("Play").Invoke(particles,null);
                var colors=root.GetComponentInChildren<MeshFilter>(true).sharedMesh.colors;
                Assert.That(colors,Is.Not.Empty);
                Assert.That(colors[0].a,Is.EqualTo(1f).Within(.00001f));
                Assert.That(colors[0].r,Is.EqualTo(.1019608f).Within(.00001f));
                Assert.That(colors[0].g,Is.EqualTo(.0627451f).Within(.00001f));
                Assert.That(colors[0].b,Is.EqualTo(.1372549f).Within(.00001f));
            }
            finally {particles.Dispose();Object.DestroyImmediate(definition);Object.DestroyImmediate(root);}
        }

        [Test]
        public void OriginalDeathClosureUsesNormalSmokeAndAdditiveGlowAndRestoresShadow()
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDeathPresentation, AstralOath.Client.BattleScene",true);
            const string directory="Assets/Game/Modules/BattleScene/Res/Effects/Death/Particles/";
            var definition=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(directory+"death_pati_02.particle.json");
            Assert.That(definition,Is.Not.Null);
            var textures=new Dictionary<string,Texture2D>();
            foreach(string name in new[]{"soft_smoke","ticle_01","tri_pati_01","ticle_02","flash"})
                textures.Add(name,UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(directory+name+".png"));
            var root=new GameObject("Source death regression",typeof(MeshRenderer));
            var shadow=new GameObject("Shadow",typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            shadow.color=new Color(.2f,.3f,.4f,.65f);var original=shadow.color;
            var presentation=(IDisposable)Activator.CreateInstance(type,root.transform,definition,textures,shadow);
            void Call(string name,params object[] args)=>type.GetMethod(name).Invoke(presentation,args);
            try
            {
                Call("Begin");Call("Tick",.4f);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();
                int normal=0,additive=0;
                foreach(var renderer in renderers)
                {
                    var material=renderer.sharedMaterial;if(!material)continue;
                    Assert.That(material.GetFloat("_PremultipliedAlpha"),Is.EqualTo(1));
                    int dst=material.GetInt("_DstBlend");if(dst==(int)BlendMode.OneMinusSrcAlpha)normal++;else if(dst==(int)BlendMode.One)additive++;
                }
                Assert.That(normal,Is.EqualTo(6));Assert.That(additive,Is.EqualTo(1));
                Assert.That(shadow.color.a,Is.InRange(.001f,original.a-.001f));
                Call("Reset");Assert.That(shadow.color,Is.EqualTo(original));
                Call("Begin");Call("Tick",10f);Assert.That((bool)type.GetProperty("IsComplete").GetValue(presentation),Is.True);
                Assert.That(shadow.color.a,Is.Zero);
            }
            finally {presentation.Dispose();Assert.That(shadow.color,Is.EqualTo(original));Object.DestroyImmediate(root);Object.DestroyImmediate(shadow.gameObject);}
        }

        [Test]
        public void DeathClockCanPauseResetAndReplayWithoutReplacingMaterialsOrLeakingProperties()
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDeathPresentation, AstralOath.Client.BattleScene");
            Assert.That(type,Is.Not.Null,"A death presentation must own gradual visibility instead of immediately hiding the slot.");
            var root=new GameObject("Death regression",typeof(MeshRenderer));
            var renderer=root.GetComponent<MeshRenderer>();
            var material=new Material(Resources.Load<Shader>("AstralOathSkeleton"));renderer.sharedMaterial=material;
            var block=new MaterialPropertyBlock();block.SetFloat("_ExternalFlag",37f);renderer.SetPropertyBlock(block);
            var presentation=(IDisposable)Activator.CreateInstance(type,root.transform,null,new Dictionary<string,Texture2D>());
            void Call(string name,params object[] args)=>type.GetMethod(name).Invoke(presentation,args);
            float Value(string name){renderer.GetPropertyBlock(block);return block.GetFloat(name);}
            try
            {
                Call("Begin");Call("Tick",.1f);
                float brightness=Value("_PresentationBrightness");
                Assert.That(brightness,Is.InRange(.01f,.99f));
                Assert.That(Value("_PresentationOpacity"),Is.EqualTo(1f));
                Call("Tick",0f);Assert.That(Value("_PresentationBrightness"),Is.EqualTo(brightness));
                Call("Begin");Assert.That(Value("_PresentationBrightness"),Is.EqualTo(brightness),"Duplicate defeat must not restart the effect.");
                Call("Tick",.3f);Assert.That(Value("_PresentationBrightness"),Is.Zero);
                Assert.That(Value("_PresentationOpacity"),Is.InRange(.001f,.999f));
                Assert.That(renderer.sharedMaterial,Is.SameAs(material));
                Call("Reset");Assert.That(Value("_PresentationBrightness"),Is.EqualTo(1f));
                Assert.That(Value("_PresentationOpacity"),Is.EqualTo(1f));
                Assert.That(Value("_ExternalFlag"),Is.EqualTo(37f));
                Call("Begin");Call("Tick",10f);
                Assert.That((bool)type.GetProperty("IsComplete").GetValue(presentation),Is.True);
                Assert.That(Value("_PresentationOpacity"),Is.Zero);
            }
            finally {presentation.Dispose();Assert.That(Value("_ExternalFlag"),Is.EqualTo(37f));Object.DestroyImmediate(root);Object.DestroyImmediate(material);}
        }
    }
}
