#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Shop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ShopWindowPresentationTests
    {
        sealed class ShopAssets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                Assert.That(address, Is.EqualTo(ShopWindow.Address));
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Shop/Res/Prefab/ShopWindow.prefab"));
            }
        }

        sealed class Labels : ILocaleService
        {
            readonly System.Collections.Generic.Dictionary<string,string> labels =
                JObject.Parse(File.ReadAllText("Assets/Game/Modules/Arena/Configs/arena-zh-hans.json"))["rows"]
                    .ToDictionary(x => (string)x["key"], x => (string)x["text"]);
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string value) { }
            public void SelectVoice(string value) { }
            public string Resolve(string key) => labels[key];
        }

        [UnityTest]
        public IEnumerator RegularGoodsUpdateFromReceiptAndSeasonDirectoriesAreReadOnly()
        {
            var canvas = new GameObject("Shop capture canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var camera = new GameObject("Shop capture camera",typeof(Camera)).GetComponent<Camera>();
            // Match the visible game area in the 2:13 reference, excluding its device gutter.
            var size=ArenaReferenceCapture.Size(1920,1080);
            var target = RenderTexture.GetTemporary(size.x,size.y,24);
            var owner = new BindingScope(default);
            var coordinator = new WindowCoordinator(owner.Token);
            try
            {
                camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=Color.black;
                var surface=canvas.GetComponent<Canvas>();
                surface.renderMode=RenderMode.ScreenSpaceCamera;
                surface.worldCamera=camera;
                surface.planeDistance=1;
                var scaler=canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080);
                scaler.matchWidthOrHeight=1;
                var registry=new UIViewRegistry();
                registry.Register<ShopWindow>();
                var created=registry.CreateAsync<ShopWindow>(new ShopAssets(),canvas.transform,owner);
                yield return Wait(created);
                var view=created.Result;
                var map=view.Root.GetComponent<BindingMap>();
                var offers=JsonConvert.DeserializeObject<ShopOfferDto[]>(File.ReadAllText("Config/Shop/arena.json"));
                foreach(var offer in offers) offer.remaining=offer.limit;
                var page=new ShopPageDto {balance=8186,offers=offers};
                int exchanges=0;
                Task<ShopPageDto> Exchange(string id,string request,CancellationToken token)
                {
                    exchanges++;
                    Assert.That(id,Is.EqualTo("molagora"));
                    Assert.That(Guid.TryParseExact(request,"N",out _),Is.True);
                    var nextOffers=JsonConvert.DeserializeObject<ShopOfferDto[]>(JsonConvert.SerializeObject(offers));
                    nextOffers[0].remaining=0;
                    nextOffers[0].owned=1;
                    return Task.FromResult(new ShopPageDto {balance=8036,offers=nextOffers});
                }
                var binder=new ShopWindowBinder(view,new Labels(),coordinator,new ShopExchangeRequests(),_=>Task.FromResult(page),Exchange,()=>Task.CompletedTask);
                string navigated=null;
                binder.Navigate=(key,ct)=>{navigated=key;return Task.CompletedTask;};
                binder.ShowCurrencies(100000,21);
                yield return Wait(coordinator.ShowPageAsync(binder.OpenAsync));
                yield return null;
                var list=map.Require<ScrollRect>("list");
                BindingMap[] Items() => list.content.Cast<Transform>().Where(x=>x.gameObject.activeSelf)
                    .Select(x=>x.GetComponent<BindingMap>()).ToArray();
                var items=Items();
                Assert.That(items.Length,Is.EqualTo(17));
                Assert.That(list.horizontal,Is.True);
                Assert.That(list.content.rect.width,Is.GreaterThan(list.viewport.rect.width));
                for(int i=0;i<items.Length;i++)
                {
                    Assert.That(items[i].Require<GameText>("name").text,Is.EqualTo(offers[i].name));
                    Assert.That(items[i].Require<Image>("icon").sprite,Is.Not.Null);
                    var purchase=items[i].Require<GameButton>("action");
                    var directory=items[i].Require<GameButton>("directory");
                    Assert.That(purchase.gameObject.activeSelf,Is.EqualTo(i<6));
                    Assert.That(directory.gameObject.activeSelf,Is.EqualTo(i>=6));
                    var visible=i<6 ? purchase : directory;
                    Assert.That(((Image)visible.targetGraphic).sprite.name,Is.EqualTo(i<6 ? "_btn_green" : "_btn_blue"));
                    Assert.That(visible.GetComponent<BindingMap>().Require<GameText>("label").text,
                        Is.EqualTo(i<6 ? offers[i].price.ToString("N0") : "确认目录"));
                }
                Assert.That(map.Require<GameText>("balance").text,Is.EqualTo(8186.ToString("N0",CultureInfo.CurrentCulture)));
                foreach(var key in new[]{"featured1","featured2","packages","charge","store","skin","events","glory","trials","constellation","refresh","bag","more","bell","goldShop","crystalsShop"})
                {
                    map.Require<GameButton>(key).onClick.Invoke();
                    Assert.That(navigated,Is.EqualTo(key));
                }
                Assert.That(map.Require<GameText>("gold").text,Is.EqualTo(100000.ToString("N0")));
                Assert.That(map.Require<GameText>("crystals").text,Is.EqualTo("21"));
                SaveFrame(camera,target,canvas,"shop-arena-reference-133");
                list.horizontalNormalizedPosition=1;
                yield return null;
                SaveFrame(camera,target,canvas,"shop-arena-reference-136");
                list.horizontalNormalizedPosition=0;
                var dialog=map.Require<BindingMap>("confirmation");
                for(int i=6;i<items.Length;i++)
                {
                    items[i].Require<GameButton>("directory").onClick.Invoke();
                    Assert.That(coordinator.IsOverlayOpen("shop/season-directory"),Is.True);
                    Assert.That(dialog.Require<GameButton>("cancel").gameObject.activeSelf,Is.False);
                    Assert.That(dialog.Require<GameText>("title").text,Is.EqualTo(offers[i].name));
                    Assert.That(offers[i].entries.Length,Is.EqualTo(6));
                    foreach(var entry in offers[i].entries)
                        Assert.That(dialog.Require<GameText>("body").text,Does.Contain(entry));
                    if(i==6) SaveFrame(camera,target,canvas,"shop-season-directory");
                    dialog.Require<GameButton>("confirm").onClick.Invoke();
                }
                Assert.That(exchanges,Is.Zero);
                items[0].Require<GameButton>("action").onClick.Invoke();
                Assert.That(dialog.Require<GameButton>("cancel").gameObject.activeSelf,Is.True);
                Assert.That(dialog.Require<GameText>("confirmLabel").text,Is.EqualTo("兑换"));
                dialog.Require<GameButton>("confirm").onClick.Invoke();
                yield return null;
                Assert.That(exchanges,Is.EqualTo(1));
                Assert.That(map.Require<GameText>("balance").text,Is.EqualTo(8036.ToString("N0",CultureInfo.CurrentCulture)));
                Assert.That(map.Require<GameText>("status").text,Does.Contain("已兑换"));
                Assert.That(Items()[0].Require<GameButton>("action").interactable,Is.False);
                Assert.That(Items()[0].Require<GameText>("quota").text,Is.EqualTo("0/1"));
                SaveFrame(camera,target,canvas,"shop-exchange-receipt");
            }
            finally
            {
                coordinator.Dispose();
                owner.Dispose();
                camera.targetTexture=null;
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(camera.gameObject);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        [Test]
        public void ExchangeAfterLostReceiptReusesRequestUntilTheServerDecides()
        {
            var session = new ShopExchangeRequests();
            var keys = new System.Collections.Generic.List<string>();
            Task<ShopPageDto> Respond(string offer, string id, CancellationToken token)
            {
                keys.Add(id);
                if(keys.Count==1) throw new IOException("request_timeout");
                if(keys.Count==3) throw new IOException("insufficient_conquest");
                return Task.FromResult(new ShopPageDto { offers=Array.Empty<ShopOfferDto>() });
            }
            void Submit() => session.SubmitAsync("energy",Respond,default).GetAwaiter().GetResult();
            Assert.Throws<IOException>(Submit);
            Submit();
            Assert.Throws<IOException>(Submit);
            Submit();
            Assert.That(keys[1],Is.EqualTo(keys[0]));
            Assert.That(keys[2],Is.Not.EqualTo(keys[1]));
            Assert.That(keys[3],Is.Not.EqualTo(keys[2]));
        }

        static IEnumerator Wait(Task task)
        {
            while(!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        static void SaveFrame(Camera camera,RenderTexture target,GameObject canvas,string name)
        {
            var directory=Environment.GetEnvironmentVariable("ASTRAL_SHOP_CAPTURE_DIR");
            if(string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            ArenaReferenceCapture.Measure(directory,name,canvas.transform,camera,target);
            var previous=RenderTexture.active;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory,name+".png"),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                Object.DestroyImmediate(pixels);
            }
        }
    }
}
#endif
