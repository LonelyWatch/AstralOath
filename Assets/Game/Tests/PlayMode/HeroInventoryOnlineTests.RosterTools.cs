#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using TMPro;
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
        [Test]
        public void WornArtifactDetailReservesSpaceAroundBodyTabsAndActions()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var popup = root.GetComponent<BindingMap>().Require<BindingMap>("artifactHeroEquipment");
            var body = popup.Require<GameText>("popupBody").rectTransform;
            var scroll = body.GetComponentInParent<ScrollRect>(true);
            Assert.That(scroll, Is.Not.Null);
            var viewport = scroll.viewport;
            var contents = (RectTransform)viewport.parent;
            var tabs = (RectTransform)popup.Require<GameButton>("skillTab").transform;
            var remove = (RectTransform)popup.Require<GameButton>("remove").transform;
            Assert.That(contents.anchorMin.x, Is.GreaterThanOrEqualTo(.06f));
            Assert.That(1f - contents.anchorMax.x, Is.GreaterThanOrEqualTo(.06f));
            Assert.That(tabs.anchorMin.y - viewport.anchorMax.y, Is.GreaterThanOrEqualTo(.018f));
            Assert.That(viewport.anchorMin.y - remove.anchorMax.y, Is.GreaterThanOrEqualTo(.019f));
            Assert.That(body.GetComponent<ContentSizeFitter>().verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
        }

        [UnityTest]
        public IEnumerator CaptureWornArtifactCopyAndViewport()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var longestSkill = art.Catalog.Templates.OrderByDescending(t => t.DescriptionAt(0).Length).First();
            var account = new HeroInventorySnapshot { heroes = new [] {
                new HeroInventoryItem { instanceId="hero-test", templateId="c1001", level=1, stars=3 } }, used=1, capacity=100 };
            var inventory = new ArtifactInventorySnapshot { capacity=100, used=1, materials = new ArtifactInventoryMaterials(),
                artifacts=new [] { new ArtifactInventoryItem { instanceId="artifact-test", templateId=longestSkill.Id,
                    heroInstanceId="hero-test" } } };
            using var scope = new BindingScope(CancellationToken.None);
            var url = new System.Uri("http://127.0.0.1:7728");
            Task<string> NoRequest(CancellationToken _) => throw new System.InvalidOperationException("The snapshots are complete.");
            using var heroes = new HeroInventoryApi(url,NoRequest,scope.Token);
            using var artifacts = new ArtifactInventoryApi(url,NoRequest,scope.Token);
            var open = OpenArtifactWindow(true,scope,heroes,artifacts,art,initialHeroes:account,initialArtifacts:inventory);
            yield return new WaitUntil(() => open.IsCompleted);
            var page = open.GetAwaiter().GetResult();
            var popup = page.Require<BindingMap>("artifactHeroEquipment");
            Click(page,"artifactSlot");
            Assert.That(popup.Require<GameObject>("popup").activeInHierarchy,Is.True);
            yield return null;
            var body = popup.Require<GameText>("popupBody");
            var scroll = body.GetComponentInParent<ScrollRect>();
            Assert.That(body.text, Is.EqualTo(longestSkill.DescriptionAt(0)));
            yield return Capture("artifact-worn-skill",1478,720);
            Canvas.ForceUpdateCanvases();
            Assert.That(body.rectTransform.rect.height, Is.GreaterThanOrEqualTo(body.preferredHeight - 1f));
            body.text = string.Join("\n", Enumerable.Repeat(longestSkill.DescriptionAt(0), 5));
            Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0;
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0));
            Click(popup,"storyTab");
            Assert.That(body.text, Is.Not.Empty.And.Not.EqualTo(longestSkill.DescriptionAt(0)));
            Assert.That(Mathf.Abs(scroll.content.anchoredPosition.y), Is.LessThan(1f));
            yield return Capture("artifact-worn-story",1478,720);
        }

        private static void ClickRosterControl(BindingMap map, string key)
        {
            Canvas.ForceUpdateCanvases();
            var button = map.Require<GameButton>(key);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer,hits);
            var hit = hits.FirstOrDefault().gameObject;
            Assert.That(hit,Is.Not.Null,key + " at " + pointer.position);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit),Is.EqualTo(button.gameObject),
                key + " at " + pointer.position + ": " + string.Join(", ",hits.Select(h => h.gameObject.name)));
            ExecuteEvents.ExecuteHierarchy(hit,pointer,ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator RosterSearchAndFilterKeepTheSelectedInstanceAndConsumeBack()
        {
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var account = new HeroInventorySnapshot { capacity = 100, used = 3, heroes = new[] {
                new HeroInventoryItem { instanceId = "old", templateId = "c1001", level = 1, stars = 3 },
                new HeroInventoryItem { instanceId = "chosen", templateId = "c1001", level = 25, stars = 4 },
                new HeroInventoryItem { instanceId = "new", templateId = "c1062", level = 7, stars = 5 }
            }};
            priorScene = SceneManager.GetActiveScene();
            home = SceneManager.CreateScene("Hero roster test");
            SceneManager.SetActiveScene(home);
            new GameObject("Roster camera", typeof(Camera));
            var canvas = new GameObject("Roster tools canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.matchWidthOrHeight = 1;
            var events = EventSystem.current ? null : new GameObject("Roster input", typeof(EventSystem));
            try
            {
                int exits = 0;
                using var scope = new BindingScope(CancellationToken.None);
                var registry = new UIViewRegistry();
                registry.Register<HeroInventory>();
                var load = registry.CreateAsync<HeroInventory>(new InventoryPrefab(), canvas.transform, scope);
                yield return new WaitUntil(() => load.IsCompleted);
                var view = load.GetAwaiter().GetResult();
                using var equipmentApi = new EquipmentInventoryApi(new System.Uri("http://127.0.0.1:7728"),
                    _ => Task.FromResult("unused"), scope.Token);
                var gear = new EquipmentInventorySnapshot { version = 1, capacity = 100, used = 5,
                    items = new[] { "weapon", "helmet", "armor", "necklace", "ring" }.Select((slot, index) =>
                        new EquipmentInventoryItem
                        {
                            instanceId = "gear-" + index, slot = slot, setId = index == 4 ? "speed" : "critical",
                            equippedHeroId = "chosen", grade = "good", itemLevel = 15,
                            mainStat = new EquipmentStatValue { kind = "attack", value = 20 },
                            substats = System.Array.Empty<EquipmentStatValue>()
                        }).ToArray()
                };
                var locale = AstralOath.Client.Arena.ArenaLabels.LoadAsync(new InventoryPrefab(), scope, "zh-Hans", "ja").GetAwaiter().GetResult();
                var binder = new HeroInventoryBinder(view, art, locale, _ => Task.FromResult(account),
                    () => { exits++; return Task.CompletedTask; }, equipmentApi: equipmentApi);
                binder.OpenAsync(true, scope, account, null, gear).GetAwaiter().GetResult();
                yield return null;
                var page = view.Root.GetComponent<BindingMap>();
                var tools = page.Require<BindingMap>("rosterTools");
                var stars = page.Require<GameText>("detailStars");
                Assert.That(stars.text.Length, Is.EqualTo(4));
                Assert.That(page.Require<GameText>("heroSetLabel0").text, Is.EqualTo(locale.Resolve("home.gear.criticalSet")));
                Assert.That(page.Require<GameText>("heroSetLabel1").text, Is.EqualTo(locale.Resolve("home.gear.criticalSet")));
                Assert.That(page.Require<GameText>("heroSetLabel2").text, Is.EqualTo(locale.Resolve("home.hero_inventory.roster_no_set")));
                Assert.That(page.Require<Image>("heroSetIcon0").overrideSprite.name, Is.EqualTo("icon_set_cri"));
                ClickRosterControl(page, "rosterFilterButton");
                Assert.That(tools.Require<GameObject>("filters").activeSelf, Is.True);
                yield return null;
                ClickRosterControl(tools, "sort_acquired");
                yield return Capture("hero-roster-filter",1478,720);
                Assert.That(stars.text.Length, Is.EqualTo(4), "Reordering must preserve the selected copy.");
                var rows = VisibleRows(page,"rosterScroll");
                Assert.That(rows.OrderBy(t => -((RectTransform)t.transform).anchoredPosition.y).First().name, Is.EqualTo("new"));
                ClickRosterControl(tools,"element_ice");
                Assert.That(VisibleRows(page,"rosterScroll").Length, Is.EqualTo(1));
                Assert.That(page.Require<GameText>("heroSetLabel0").text, Is.EqualTo(locale.Resolve("home.hero_inventory.roster_no_set")));
                ClickRosterControl(tools,"role_knight");
                Assert.That(VisibleRows(page,"rosterScroll"), Is.Empty);
                ClickRosterControl(tools,"reset");
                Assert.That(stars.text.Length, Is.EqualTo(4), "Restoring the query restores the originally selected copy.");
                page.Require<GameButton>("back").onClick.Invoke();
                Assert.That(tools.Require<GameObject>("filters").activeSelf, Is.False);
                ClickRosterControl(page, "rosterSearchButton");
                Assert.That(tools.Require<GameObject>("searchPanel").activeSelf, Is.True);
                var input = page.Require<TMP_InputField>("search");
                input.text = "missing hero";
                Assert.That(VisibleRows(page,"rosterScroll"), Is.Empty);
                yield return null;
                ClickRosterControl(tools, "clear");
                yield return Capture("hero-roster-search",1478,720);
                Assert.That(stars.text.Length, Is.EqualTo(4));
                page.Require<GameButton>("back").onClick.Invoke();
                Assert.That(tools.Require<GameObject>("searchPanel").activeSelf, Is.False);
                Assert.That(exits, Is.Zero);
                var inspect = page.Require<RectTransform>("rosterDetail").Find("Inspect equipment").GetComponent<Image>();
                Assert.That(inspect.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(inspect.transform.GetChild(0).GetComponent<Image>().preserveAspect, Is.True);
                Assert.That(page.Require<Image>("heroIdentityShade").raycastTarget, Is.False);
                for (int i = 0; i < 3; ++i)
                    Assert.That(page.Require<GameText>("heroSetLabel" + i).gameObject.activeInHierarchy, Is.True);
                // Scene teardown can destroy the visible instance before the owner closes its scope.
                Object.Destroy(view.Root);
                yield return null;
                Canvas.ForceUpdateCanvases();
            }
            finally { if (events) Object.Destroy(events); Object.Destroy(canvas); }
        }
    }
}
#endif
