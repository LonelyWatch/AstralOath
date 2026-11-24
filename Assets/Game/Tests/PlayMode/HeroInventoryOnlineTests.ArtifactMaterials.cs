#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed partial class HeroInventoryOnlineTests
    {
        [UnityTest]
        public IEnumerator ArtifactMaterialReviewCapturesTheOriginalSixStates()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var template = art.Catalog.Templates.OrderByDescending(t => t.DescriptionAt(0).Length).First();
            var account = new HeroInventorySnapshot { heroes = new[] {
                new HeroInventoryItem { instanceId="hero-review", templateId="c1001", level=1, stars=3 } }, used=1, capacity=1000 };
            var inventory = new ArtifactInventorySnapshot { capacity=400, used=2,
                materials=new ArtifactInventoryMaterials { gold=148663236, epicCharm=8, greaterCharm=29, lesserCharm=182 },
                artifacts=new[] {
                    new ArtifactInventoryItem { instanceId="artifact-review", templateId=template.Id, heroInstanceId="hero-review" },
                    new ArtifactInventoryItem { instanceId="artifact-review-2", templateId=template.Id } } };
            using var scope = new BindingScope(CancellationToken.None);
            var url = new System.Uri("http://127.0.0.1:7728");
            Task<string> NoRequest(CancellationToken _) => throw new System.InvalidOperationException("Complete review snapshots must stay local.");
            using var artifacts = new ArtifactInventoryApi(url,NoRequest,scope.Token);
            var open = OpenArtifactMaterialReview(true,scope,artifacts,art,account,inventory);
            yield return new WaitUntil(() => open.IsCompleted);
            var page = open.GetAwaiter().GetResult();
            var popup = page.Require<BindingMap>("artifactHeroEquipment");
            Click(page,"artifactSlot");
            Assert.That(popup.Require<GameObject>("popup").activeInHierarchy,Is.True);
            var frame = popup.Require<GameObject>("popup").transform.Find("Details panel");
            Assert.That(frame.Find("Detail lining") == null,Is.True,"The source gradient must remain visible.");
            Assert.That(frame.GetComponent<Image>().sprite.name,Is.EqualTo("_box_equip_0_linear"));
            yield return Capture("artifact-material-worn-skill",1478,720);
            Click(popup,"storyTab");
            yield return Capture("artifact-material-worn-story",1478,720);
            Click(popup,"popupZoom");
            Assert.That(popup.Require<GameObject>("painting").activeInHierarchy,Is.True);
            yield return Capture("artifact-material-painting",1478,720);
            Click(popup,"paintBack");
            Click(popup,"change");
            Assert.That(popup.Require<GameObject>("picker").activeInHierarchy,Is.True);
            yield return Capture("artifact-material-picker",1478,720);
            Click(popup,"capacityClick");
            Assert.That(page.Require<GameObject>("artifactCapacityNotice").activeInHierarchy,Is.True);
            yield return Capture("artifact-material-capacity",1478,720);
            Click(page,"artifactCapacityOk");
            Click(popup,"training");
            var enhancement = page.Require<BindingMap>("artifactEnhancement");
            Assert.That(enhancement.gameObject.activeInHierarchy,Is.True);
            yield return null;
            Click(VisibleRows(enhancement,"candidates")[0],"artifactCardButton");
            yield return Capture("artifact-material-enhancement",1478,720);
        }

        [UnityTest]
        public IEnumerator ArtifactMaterialReviewCapturesTheSharedCardsInTheBag()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var account = new HeroInventorySnapshot { heroes = new[] {
                new HeroInventoryItem { instanceId="hero-review", templateId="c1001", level=1, stars=3 } }, used=1, capacity=1000 };
            var inventory = new ArtifactInventorySnapshot { capacity=400, used=6,
                materials=new ArtifactInventoryMaterials(), artifacts=art.Catalog.Templates.Take(6).Select((template,index) =>
                    new ArtifactInventoryItem { instanceId="artifact-bag-review-"+index,templateId=template.Id,level=3 }).ToArray() };
            using var scope = new BindingScope(CancellationToken.None);
            var url = new System.Uri("http://127.0.0.1:7728");
            Task<string> NoRequest(CancellationToken _) => throw new System.InvalidOperationException("Complete review snapshots must stay local.");
            using var artifacts = new ArtifactInventoryApi(url,NoRequest,scope.Token);
            var open = OpenArtifactMaterialReview(false,scope,artifacts,art,account,inventory);
            yield return new WaitUntil(() => open.IsCompleted);
            var page = open.GetAwaiter().GetResult();
            Click(page,"tabArtifacts");
            yield return null;
            var rows=VisibleRows(page,"artifactItems");
            Assert.That(rows.Length,Is.GreaterThanOrEqualTo(3));
            foreach(var row in rows)
            {
                Assert.That(row.Require<GameText>("artifactCardName").fontSize,Is.EqualTo(30));
                Assert.That(row.Require<GameText>("artifactCardLimit").fontSize,Is.EqualTo(31));
            }
            yield return Capture("artifact-material-bag",1478,720);
            yield return Capture("artifact-material-bag-16x10",1536,960);
        }

        private async Task<BindingMap> OpenArtifactMaterialReview(bool roster,BindingScope scope,ArtifactInventoryApi api,
            ArtifactArtCatalog art,HeroInventorySnapshot account,ArtifactInventorySnapshot inventory)
        {
            priorScene=SceneManager.GetActiveScene();
            home=SceneManager.CreateScene("Artifact material review");
            SceneManager.SetActiveScene(home);
            new GameObject("Review camera",typeof(Camera));
            var canvas=new GameObject("Review canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);
            scaler.matchWidthOrHeight=1;
            if(!EventSystem.current) new GameObject("Review events",typeof(EventSystem));
            var assets=new InventoryPrefab();
            var registry=new UIViewRegistry();
            registry.Register<HeroInventory>();
            var view=await registry.CreateAsync<HeroInventory>(assets,canvas.transform,scope);
            var locale=await AstralOath.Client.Arena.ArenaLabels.LoadAsync(assets,scope,"zh-Hans","ja");
            var heroArt=AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var binder=new HeroInventoryBinder(view,heroArt,locale,_=>Task.FromResult(account),()=>Task.CompletedTask,
                art,api,fetchArtifacts:_=>Task.FromResult(inventory));
            await binder.OpenAsync(roster,scope,account,inventory);
            return view.Root.GetComponent<BindingMap>();
        }
    }
}
#endif
