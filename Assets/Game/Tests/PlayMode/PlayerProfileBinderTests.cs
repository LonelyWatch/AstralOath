using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.PlayerProfile;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ProfileView=AstralOath.Client.PlayerProfile.PlayerProfile;
namespace AstralOath.Client.Tests
{
    public sealed class PlayerProfileBinderTests
    {
        private sealed class Assets:IAssetService
        {public GameObject prefab;public Task<T> LoadAsync<T>(string address,BindingScope scope)where T:UnityEngine.Object=>Task.FromResult(prefab as T);}
        private sealed class Api:IPlayerProfileApi
        {
            public TaskCompletionSource<PlayerProfileData> first;public int writes;
            public Task<PlayerProfileData> MeAsync(CancellationToken token)=>Task.FromResult(Data("self",true));
            public Task<PlayerProfileData> PlayerAsync(string id,CancellationToken token)=>id=="slow"?first.Task:Task.FromResult(Data(id,false));
            public Task<NicknameCheck> CheckNicknameAsync(string name,CancellationToken token)=>Task.FromResult(new NicknameCheck{available=true});
            public Task<PlayerProfileData> ActAsync(ProfileActionRequest request,CancellationToken token){writes++;return Task.FromResult(Data("self",true));}
            public void Dispose(){}
        }
        private static PlayerProfileData Data(string id,bool self)=>new PlayerProfileData{playerId=id,realmId="test",displayName="<b>"+id+"</b>",avatarId="c1001",isSelf=self,canEdit=self,version=1,greetingIsDefault=true,visibility=new ProfileVisibility(),seasonRecord=new ProfileRecord{visible=false},bestRecord=new ProfileRecord{visible=false}};
        private static IEnumerator Done(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.InnerException;Assert.That(task.IsCanceled,Is.False);}
        [UnityTest]public IEnumerator LateReadNeverReplacesNewSubjectAndOtherPlayerCannotEdit()=>Done(Late());
        private async Task Late()
        {
            GameObject prefab=null;
#if UNITY_EDITOR
            if(!prefab)prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
#endif
            Assert.That(prefab,Is.Not.Null);var canvas=new GameObject("Profile test canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=1;
            try
            {
                using var scope=new BindingScope(default);var registry=new UIViewRegistry();registry.Register<ProfileView>();
                var view=await registry.CreateAsync<ProfileView>(new Assets{prefab=prefab},canvas.transform,scope);var api=new Api{first=new TaskCompletionSource<PlayerProfileData>()};using var session=new PlayerProfileSession(api);
                HomeHeroArt art=null;
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_PROFILE_CAPTURE_DIR")))art=UnityEditor.AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
#endif
                string copied=null;var binder=new PlayerProfileBinder(view,scope,session,()=>{},_=>{},heroArt:art,copyId:id=>copied=id);
                var first=binder.OpenAsync("slow",default);await binder.OpenAsync("other",default);api.first.SetResult(Data("slow",false));await first;
                Assert.That(view.Map.Require<GameText>("displayName").text,Is.EqualTo("<b>other</b>"));Assert.That(view.Map.Require<GameText>("displayName").richText,Is.False);
                Assert.That(view.Map.Require<GameButton>("nickname").gameObject.activeSelf,Is.False);Assert.That(view.Map.Require<GameObject>("selfActions").activeSelf,Is.False);
                view.Map.Require<GameButton>("greeting").onClick.Invoke();Assert.That(view.Map.Require<GameObject>("greetingLayer").activeSelf,Is.False);Assert.That(api.writes,Is.Zero);
                view.Map.Require<GameButton>("copyId").onClick.Invoke();Assert.That(copied,Is.EqualTo("other"));Assert.That(view.Map.Require<GameText>("seasonArena").text,Is.EqualTo("不公开"));
                if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_PROFILE_CAPTURE_DIR")))
                {
                    await binder.OpenAsync("other",default);await Capture(canvas,"04-other-readonly",1920,1080);
                    await binder.OpenAsync(null,default);Assert.That(view.Map.Require<GameButton>("nickname").gameObject.activeSelf,Is.True);
                    await Capture(canvas,"01-self-default",1920,1080);await Capture(canvas,"05-self-4k",3840,2160);await Capture(canvas,"06-self-narrow",1280,1024);
                    view.Map.Require<GameButton>("nickname").onClick.Invoke();Assert.That(view.Map.Require<GameObject>("nicknameLayer").activeSelf,Is.True);await Capture(canvas,"02-nickname",1920,1080);
                    view.Map.Require<GameButton>("nicknameCancel").onClick.Invoke();view.Map.Require<GameButton>("greeting").onClick.Invoke();Assert.That(view.Map.Require<GameObject>("greetingLayer").activeSelf,Is.True);await Capture(canvas,"03-greeting",1920,1080);
                    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ASTRAL_PROFILE_CAPTURE_DIR"),"capture-scope.txt"),"Controlled Editor fixture; player IDs/names are test inputs, no authoritative Rank/friends supplied. UTF-8 markup in name is intentionally displayed literally. 3840x2160 is a camera render target, not a physical desktop claim. No audio listening or Native proof.\n");
                }
                scope.Dispose();Assert.That(view.Root==null||!view.Root.activeSelf,Is.True);
            }
            finally{UnityEngine.Object.DestroyImmediate(canvas);}
        }
        private static async Task Capture(GameObject root,string name,int width,int height)
        {
            string folder=Environment.GetEnvironmentVariable("ASTRAL_PROFILE_CAPTURE_DIR");if(string.IsNullOrEmpty(folder))return;Directory.CreateDirectory(folder);
            var canvas=root.GetComponent<Canvas>();var originalMode=canvas.renderMode;var originalCamera=canvas.worldCamera;float originalDistance=canvas.planeDistance;
            var camera=new GameObject("Profile capture camera",typeof(Camera)).GetComponent<Camera>();var target=RenderTexture.GetTemporary(width,height,24);var previous=RenderTexture.active;var frame=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                Canvas.ForceUpdateCanvases();await Task.Delay(100);Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;frame.ReadPixels(new Rect(0,0,width,height),0,0);frame.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),frame.EncodeToPNG());
            }
            finally{canvas.renderMode=originalMode;canvas.worldCamera=originalCamera;canvas.planeDistance=originalDistance;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(frame);UnityEngine.Object.Destroy(camera.gameObject);}
        }
    }
}
