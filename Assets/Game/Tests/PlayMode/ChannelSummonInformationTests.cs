#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Arena;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ChatView = AstralOath.Client.ChannelChat.ChannelChat;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelSummonInformationTests
    {
        private const string Modules="Assets/Game/Modules/";
        [Serializable] private sealed class Labels { public Label[] rows; }
        [Serializable] private sealed class Label { public string key,text; }
        private sealed class Prefabs : IAssetService
        {
            public Task<T> LoadAsync<T>(string address,BindingScope scope) where T : Object
            {
                string path=address==ArenaTower.Address?"Arena/Res/Prefab/ArenaTower":
                    address==ChatView.Address?"ChannelChat/Res/Prefab/ChannelChat":
                    address==HeroInformation.Address?"Home/Res/Prefab/HeroInformation":"Home/Res/Prefab/ArtifactInformation";
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>(Modules+path+".prefab"));
            }
        }
        private sealed class Feed : IChannelChatApi
        {
            public Task<ChatPreferences> PreferencesAsync(CancellationToken token)=>Task.FromResult(new ChatPreferences{notifications=new ChatNotifications(),keywords=Array.Empty<string>()});
            public Task<ChatPage> ListAsync(int channel,string after,int wait,CancellationToken token)
            {
                ChatMessage Message(string id,string kind,string template)=>new ChatMessage
                {
                    id=id,kind=kind,templateId=template,channel=1,createdAt=DateTime.UtcNow.AddMinutes(-2).ToString("O"),
                    player=new ChatSender{id="other",displayName="圣剑多旺达",avatarId="c1001"}
                };
                return Task.FromResult(new ChatPage{channel=1,nextAfter="2",messages=after=="0"?
                    new[]{Message("1","hero5","c1024"),Message("2","artifact5","efr02")}:Array.Empty<ChatMessage>()});
            }
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest req,CancellationToken token)=>throw new NotSupportedException();
            public Task<ChatMessage> SendAsync(int channel,ChatSendRequest req,CancellationToken token)=>throw new NotSupportedException();
            public Task WatchAsync(int channel,Action changed,CancellationToken token)=>Task.Delay(Timeout.Infinite,token);
            public void Dispose(){}
        }

        // Set ASTRAL_SUMMON_CHAT_CAPTURE_DIR to retain 1920x1080 PNGs of each live state.
        [UnityTest]
        public IEnumerator IconLinksOpenCatalogWithoutOwningEitherReward()
        {
            var root=new GameObject("Channel fixture",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var cam=new GameObject("Capture camera",typeof(Camera)).GetComponent<Camera>();
            var canvas=root.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=cam;
            canvas.planeDistance=1;
            cam.clearFlags=CameraClearFlags.SolidColor;
            cam.backgroundColor=new Color32(18,26,44,255);
            cam.cullingMask=1<<31;
            var size=ArenaReferenceCapture.Size(1920,1080);
            var target=RenderTexture.GetTemporary(size.x,size.y,24);
            cam.targetTexture=target;
            cam.enabled=false;
            var scaler=root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);
            scaler.matchWidthOrHeight=1;
            var scope=new BindingScope(default);
            using var towerWindows=new WindowCoordinator(scope.Token);
            var registry=new UIViewRegistry();
            var prefabs=new Prefabs();
            registry.Register<ChatView>();registry.Register<HeroInformation>();registry.Register<ArtifactInformation>();
            var labels=JsonUtility.FromJson<Labels>(File.ReadAllText(Modules+"Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(x=>x.key,x=>x.text);
            var locale=new LocaleService(new Dictionary<string,IReadOnlyDictionary<string,string>>{{"zh",labels}},"zh","ja","ja");
            var heroes=AssetDatabase.LoadAssetAtPath<HomeHeroArt>(Modules+"Home/Configs/HomeHeroArt.asset");
            var artifact=AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>(Modules+"Home/Configs/ArtifactArtCatalog.asset").Find("efr02");
            var hero=heroes.heroes.Single(h=>h.id=="c1024");
            registry.Register<ArenaTower>();
            var underlay=registry.CreateAsync<ArenaTower>(prefabs,root.transform,scope).GetAwaiter().GetResult();
            var towerData=JsonUtility.FromJson<TowerPageDto>(File.ReadAllText(TowerTable()));
            towerData.highestCleared=54;towerData.currentStage=55;towerData.rankId="master";
            foreach(var stage in towerData.stages){stage.cleared=stage.stage<55;stage.unlocked=stage.stage<=55;}
            var draftArt=AssetDatabase.LoadAssetAtPath<ArenaDraftArt>(Modules+"Arena/Configs/ArenaDraftArt.asset");
            var tower=new ArenaTowerBinder(underlay,locale,towerWindows,_=>Task.FromResult(towerData),
                (stage,token)=>Task.FromResult(new TowerTeamDto()),_=>Task.FromResult(Array.Empty<TowerHeroDto>()),
                (stage,ids,token)=>Task.CompletedTask,id=>draftArt.Heroes.First(h=>h.Id==id).Face,
                ()=>Task.CompletedTask,()=>Task.CompletedTask,()=>Task.CompletedTask,key=>{}, draftArt:draftArt);
            var underlayOpened=towerWindows.ShowPageAsync(tower.OpenAsync);
            while(!underlayOpened.IsCompleted)yield return null;
            underlayOpened.GetAwaiter().GetResult();
            var chat=registry.CreateAsync<ChatView>(prefabs,root.transform,scope).GetAwaiter().GetResult();
            using var session=new ChannelChatSession(new Feed());
            var binder=new ChannelChatBinder(chat,scope,session,()=>{},k=>{},p=>{},avatarArt:id=>heroes.heroes.First(h=>h.id==id).portrait);
            binder.RewardIcon=(kind,id)=>kind=="hero5"?hero.portrait:artifact.combatIcon;
            binder.RewardName=(kind,id)=>kind=="hero5"?hero.name:artifact.template.Name;
            BindingScope detail=null;
            UIBaseView opened=null;
            void Close(){opened.Hide();detail.Dispose(); detail=null;}
            binder.RewardSelected=(kind,id)=>
            {
                detail=new BindingScope(scope.Token);
                if(kind=="hero5")
                {
                    Assert.That(id,Is.EqualTo(hero.id));
                    var v=registry.CreateAsync<HeroInformation>(prefabs,root.transform,detail).GetAwaiter().GetResult();
                    new HeroInformationBinder(v,hero,locale,detail,Close);opened=v;
                }
                else
                {
                    Assert.That(id,Is.EqualTo(artifact.template.Id));
                    var v=registry.CreateAsync<ArtifactInformation>(prefabs,root.transform,detail).GetAwaiter().GetResult();
                    new ArtifactInformationBinder(v,artifact,locale,detail,Close);opened=v;
                }
            };
            try
            {
                var load=binder.OpenAsync(scope.Token);
                while(!load.IsCompleted)yield return null;
                load.GetAwaiter().GetResult();
                yield return null;
                var rows=chat.Map.Require<ScrollRect>("messages").content.GetComponentsInChildren<BindingMap>(false)
                    .Where(m=>m.name.StartsWith("Message ")).ToArray();
                Assert.That(rows.Length,Is.EqualTo(2));
                Assert.That(rows[0].Require<GameText>("rewardText").text,Does.Contain(hero.name));
                Canvas.ForceUpdateCanvases();
                var viewport=chat.Map.Require<ScrollRect>("messages").viewport;
                var corners=new Vector3[4];
                ((RectTransform)rows[1].Require<GameObject>("reward").transform).GetWorldCorners(corners);
                var gap=viewport.InverseTransformPoint(corners[0]).y-viewport.rect.yMin;
                Assert.That(gap,Is.InRange(-1f,18f),"Short history must end immediately above the input divider.");
                Frame("01-channel");
                rows[0].Require<GameButton>("rewardButton").onClick.Invoke();
                yield return null;
                var info=opened.Root.GetComponent<BindingMap>();
                Assert.That(info.Require<GameText>("name").text,Is.EqualTo(hero.name));
                Assert.That(info.Require<Image>("imprintFormation").sprite.name,Is.EqualTo("hero_dedi_p_1234"));
                Assert.That(info.Require<GameText>("imprintLabel").text,Does.Contain("攻击力"));
                Frame("02-hero");
                info.Require<GameButton>("skill2").onClick.Invoke();
                Assert.That(opened.Root.GetComponentsInChildren<GameText>(false).Any(t=>t.text==hero.skills[2].name),Is.True);
                Assert.That(hero.skills[2].upgrades,Does.Contain("强化技能"));
                Frame("03-hero-skill");
                Close();
                rows[1].Require<GameButton>("rewardButton").onClick.Invoke();
                yield return null;
                info=opened.Root.GetComponent<BindingMap>();
                Assert.That(info.Require<GameText>("name").text,Is.EqualTo(artifact.template.Name));
                Assert.That(info.Require<GameText>("description").richText,Is.True);
                Assert.That(info.Require<GameText>("description").text,Does.Contain("<color=#FFAA27>50%</color>"));
                Frame("04-artifact");
                info.Require<GameButton>("story").onClick.Invoke();
                Assert.That(info.Require<GameText>("description").text,Is.EqualTo(locale.Resolve("home.artifacts.story.efr02")));
                Frame("04b-artifact-story");
                info.Require<GameButton>("zoom").onClick.Invoke();
                Assert.That(info.Require<GameObject>("painting").activeSelf,Is.True);
                Frame("05-artifact-painting");
                info.Require<GameButton>("rotate").onClick.Invoke();
                Assert.That(info.Require<RectTransform>("paintingPage").localEulerAngles.z,Is.EqualTo(90f).Within(.1f));
                Frame("06-artifact-rotated");
                info.Require<GameButton>("paintBack").onClick.Invoke();
                Assert.That(info.Require<GameObject>("painting").activeSelf,Is.False);
            }
            finally
            {
                detail?.Dispose();scope.Dispose();cam.targetTexture=null;
                RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(cam.gameObject);Object.DestroyImmediate(root);
            }

            void Frame(string name)
            {
                string directory=Environment.GetEnvironmentVariable("ASTRAL_SUMMON_CHAT_CAPTURE_DIR");
                if(string.IsNullOrEmpty(directory))return;
                Directory.CreateDirectory(directory);
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                Canvas.ForceUpdateCanvases();cam.Render();
                ArenaReferenceCapture.Measure(directory,name,root.transform,cam,target);
                var old=RenderTexture.active;var png=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try
                {
                    RenderTexture.active=target;
                    png.ReadPixels(new Rect(0,0,target.width,target.height),0,0);png.Apply();
                    File.WriteAllBytes(Path.Combine(directory,name+".png"),png.EncodeToPNG());
                }
                finally{RenderTexture.active=old;Object.DestroyImmediate(png);}
            }
        }
        private static string TowerTable()
        {
            for(var dir=Directory.GetParent(Application.dataPath);dir!=null;dir=dir.Parent)
            {
                var path=Path.Combine(dir.FullName,"Config","ArenaTower","catalog.json");
                if(File.Exists(path))return path;
            }
            throw new FileNotFoundException("Config/ArenaTower/catalog.json");
        }
    }
}
#endif
