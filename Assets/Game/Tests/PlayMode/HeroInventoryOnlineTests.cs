#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using UnityEditor;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed partial class HeroInventoryOnlineTests
    {
        private const string HomePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private static readonly string[] StarterNames = { "拉斯", "梅赛德丝", "安洁莉卡" };
        private Scene home, priorScene;
        private string priorEndpoint, priorSkipEntry;
        private bool settingsChanged;

        private sealed class InventoryPrefab : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                if (address == AstralOath.Client.Arena.ArenaLabels.Address)
                    return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Arena/Configs/arena-zh-hans.json"));
                Assert.That(address, Is.EqualTo(HeroInventory.Address));
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab"));
            }
        }

        private sealed class InventoryText : ILocaleService
        {
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add {} remove {} }
            public void SelectText(string locale) { }
            public void SelectVoice(string locale) { }
            public string Resolve(string key) => key;
        }

        [UnityTest]
        public IEnumerator FocusedRosterSkipsUnavailableRowsAndHandlesEmptyLayout()
        {
            var panel = new GameObject("Focused roster", typeof(RectTransform), typeof(ScrollRect));
            try
            {
                using var scope = new BindingScope(CancellationToken.None);
                var scroll = panel.GetComponent<ScrollRect>();
                scroll.viewport = (RectTransform)panel.transform;
                scroll.content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
                scroll.content.SetParent(scroll.viewport, false);
                scroll.content.anchorMin = scroll.content.anchorMax = scroll.content.pivot = Vector2.up;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                int choice = -1;
                bool allowLast = true;
                var list = FocusedRosterScroll.Bind(scroll, scope, index => allowLast && index == 2, index => choice = index);
                Assert.That(list.Nearest, Is.EqualTo(-1));
                list.Layout(3, 0f);
                Assert.That(list.Nearest, Is.EqualTo(-1));
                list.Layout(3, 80f);
                scroll.content.anchoredPosition = new Vector2(0f, 80f);
                yield return Wait(() => choice == 2, "The focused list did not skip the unavailable row.");
                Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(160f).Within(1f));
                allowLast = false;
                choice = -1;
                scroll.content.anchoredPosition = new Vector2(0f, 80f);
                yield return new WaitForSecondsRealtime(.5f);
                Assert.That(choice, Is.EqualTo(-1), "Unavailable items must never become the selection.");
                list.Layout(0, 80f);
                Assert.That(list.Nearest, Is.EqualTo(-1));
            }
            finally { UnityEngine.Object.Destroy(panel); }
        }

        [UnityTest]
        public IEnumerator ResizingHeroDetailKeepsEquipmentAndRosterInProportion()
        {
            var canvas = new GameObject("Hero detail resize", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var frame = new GameObject("Window", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(canvas.transform, false);
            frame.sizeDelta = new Vector2(1920, 1080);
            using var scope = new BindingScope(CancellationToken.None);
            try
            {
                var registry = new UIViewRegistry();
                registry.Register<HeroInventory>();
                var view = registry.CreateAsync<HeroInventory>(new InventoryPrefab(), frame, scope).GetAwaiter().GetResult();
                var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
                var heroes = new HeroInventorySnapshot { capacity = 1000, used = 3, heroes = Enumerable.Range(0, 3)
                    .Select(i => new HeroInventoryItem { instanceId = "resize-" + i, templateId = "c1001", level = 3 - i, stars = 3 }).ToArray() };
                var binder = new HeroInventoryBinder(view, art, new InventoryText(), _ => Task.FromResult(heroes), () => Task.CompletedTask);
                binder.OpenAsync(true, scope, heroes, null).GetAwaiter().GetResult();
                var map = view.Root.GetComponent<BindingMap>();
                var detail = map.Require<RectTransform>("rosterDetail");
                var list = map.Require<ScrollRect>("rosterScroll");
                var corners = new Vector3[4];
                Rect InWindow(RectTransform rect)
                {
                    rect.GetWorldCorners(corners);
                    Vector2 min = frame.InverseTransformPoint(corners[0]), max = frame.InverseTransformPoint(corners[2]);
                    return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }
                foreach (var size in new[] { new Vector2(1040, 1108), new Vector2(1600, 1000), new Vector2(2520, 1080) })
                {
                    frame.sizeDelta = size;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(InWindow(detail).yMin, Is.EqualTo(frame.rect.yMin).Within(1f),
                        "The hero section and its vignette must reach the bottom of the window at " + size);
                    foreach (var name in new[] { "weapon", "helm", "armor", "neck", "ring", "boots", "artifact" })
                    {
                        bool artifact = name == "artifact";
                        var plate = (RectTransform)detail.Find("Hero equipment slots/Empty " + name + " slot");
                        Rect outer = InWindow(plate);
                        float width = artifact ? 123 : 98, height = artifact ? 137 : 99;
                        Assert.That(outer.width / outer.height, Is.EqualTo(width / height).Within(.01f), name + " plate " + size);
                        var slot = plate.GetComponent<BindingMap>();
                        Assert.That(slot, Is.Not.Null, name + " must use the shared equipment slot.");
                        Rect icon = InWindow(slot.Require<Image>("placeholder").rectTransform);
                        Assert.That(icon.width / icon.height, Is.EqualTo(1f).Within(.01f), name + " icon " + size);
                        Assert.That(icon.width / outer.width, Is.EqualTo(33f / width).Within(.01f), name);
                        float x = artifact ? 61 : name is "weapon" or "helm" or "armor" ? 23 : 71;
                        Assert.That((icon.center.x - outer.xMin) / outer.width, Is.EqualTo(x / width).Within(.01f), name);
                        Assert.That((icon.center.y - outer.yMin) / outer.height,
                            Is.EqualTo((artifact ? 32f : 28f) / height).Within(.01f), name);
                    }
                    var manage = (RectTransform)detail.Find("roster_manage_equipment");
                    Assert.That(manage.Find("Action caption"), Is.Not.Null, "Caption must follow the equipment control at " + size);
                    Rect focused = InWindow((RectTransform)list.content.Find("resize-0"));
                    Rect necklace = InWindow((RectTransform)detail.Find("Hero equipment slots/Empty neck slot"));
                    Assert.That(focused.xMin, Is.GreaterThan(necklace.xMax - necklace.width * .15f),
                        "The roster obscured the right equipment column at " + size);
                    Assert.That(InWindow(map.Require<GameText>("title").rectTransform).center.y,
                        Is.GreaterThan(frame.rect.yMax - .10f * size.y), "Header must stay at the top at " + size);
                }
            }
            finally { UnityEngine.Object.Destroy(canvas); }
        }

        [UnityTest]
        public IEnumerator OwnedRosterScrollsShortListsAndSelectsEachInstanceOnRelease()
        {
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var snapshot = new HeroInventorySnapshot { capacity = 1000, used = 3,
                heroes = Enumerable.Range(0,3).Select(i => new HeroInventoryItem { instanceId = "copy-" + i,
                    templateId = "c1001", level = 3 - i, stars = 3 + i }).ToArray() };
            var canvas = new GameObject("Owned roster pointer canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var events = EventSystem.current;
            var eventObject = events ? null : new GameObject("Roster input",typeof(EventSystem));
            if (!events) events = eventObject.GetComponent<EventSystem>();
            try
            {
                using var scope = new BindingScope(CancellationToken.None);
                var registry = new UIViewRegistry();
                registry.Register<HeroInventory>();
                var load = registry.CreateAsync<HeroInventory>(new InventoryPrefab(),canvas.transform,scope);
                yield return new WaitUntil(() => load.IsCompleted);
                var view = load.GetAwaiter().GetResult();
                var binder = new HeroInventoryBinder(view,art,new InventoryText(),_ => Task.FromResult(snapshot),() => Task.CompletedTask);
                var open = binder.OpenAsync(true,scope);
                yield return new WaitUntil(() => open.IsCompleted);
                open.GetAwaiter().GetResult();
                Canvas.ForceUpdateCanvases();
                var map = view.Root.GetComponent<BindingMap>();
                var scroll = map.Require<ScrollRect>("rosterScroll");
                var stars = map.Require<GameText>("detailStars");
                var row = (RectTransform)scroll.content.Find("copy-0");
                var pointer = new PointerEventData(events) {
                    position = RectTransformUtility.WorldToScreenPoint(null,row.TransformPoint(row.rect.center)),
                    button = PointerEventData.InputButton.Left, scrollDelta = Vector2.down };
                var hits = new List<RaycastResult>();
                events.RaycastAll(pointer,hits);
                var target = hits.First().gameObject;
                Assert.That(ExecuteEvents.GetEventHandler<IScrollHandler>(target),
                    Is.EqualTo(scroll.gameObject), "The visible hero card must deliver wheel input to its roster.");
                ExecuteEvents.ExecuteHierarchy(target,pointer,ExecuteEvents.scrollHandler);
                yield return null;
                Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0f), "Even three owned heroes must be scrollable.");
                yield return Wait(() => stars.text == "★★★★","Wheel input did not focus the second instance.");
                var drag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
                ExecuteEvents.Execute(drag,pointer,ExecuteEvents.initializePotentialDrag);
                ExecuteEvents.Execute(drag,pointer,ExecuteEvents.beginDragHandler);
                pointer.position += Vector2.up * 300f;
                ExecuteEvents.Execute(drag,pointer,ExecuteEvents.dragHandler);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(stars.text,Is.EqualTo("★★★★"),"A held drag cannot commit the next instance.");
                ExecuteEvents.Execute(drag,pointer,ExecuteEvents.endDragHandler);
                yield return Wait(() => stars.text == "★★★★★","Release did not select the last owned instance.");
                Assert.That(scroll.content.anchoredPosition.y,Is.EqualTo(scroll.content.rect.height - scroll.viewport.rect.height).Within(1f));
                AssertThreeCardStack(scroll);
                var list = scroll.GetComponent<FocusedRosterScroll>();
                list.OnBeginDrag(pointer);
                scroll.content.anchoredPosition = new Vector2(0, scroll.scrollSensitivity * .8f);
                yield return null;
                yield return null;
                list.OnEndDrag(pointer);
                var panel = (RectTransform)view.Root.transform;
                panel.offsetMin += Vector2.up * 32f;
                yield return Wait(() => stars.text == "★★★★", "Resizing while the roster settles must finish the pending selection.");
            }
            finally
            {
                if (eventObject) UnityEngine.Object.Destroy(eventObject);
                UnityEngine.Object.Destroy(canvas);
            }
        }

        [UnityTest]
        public IEnumerator HeroCardKeepsLongNamesAndLevelArtInsideItsPlate()
        {
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var snapshot = new HeroInventorySnapshot { capacity = 1000, used = 3, heroes = new[] {
                new HeroInventoryItem { instanceId = "first-level", templateId = "c1001", level = 1, stars = 3 },
                new HeroInventoryItem { instanceId = "two-digits", templateId = "c1005", level = 59, stars = 6 },
                new HeroInventoryItem { instanceId = "max-level", templateId = "c1062", level = 60, stars = 6 }
            }};
            var canvas = new GameObject("Roster card samples", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                using var scope = new BindingScope(CancellationToken.None);
                var registry = new UIViewRegistry();
                registry.Register<HeroInventory>();
                var load = registry.CreateAsync<HeroInventory>(new InventoryPrefab(), canvas.transform, scope);
                yield return new WaitUntil(() => load.IsCompleted);
                var view = load.GetAwaiter().GetResult();
                var binder = new HeroInventoryBinder(view, art, new InventoryText(), _ => Task.FromResult(snapshot), () => Task.CompletedTask);
                var open = binder.OpenAsync(true, scope);
                yield return new WaitUntil(() => open.IsCompleted);
                open.GetAwaiter().GetResult();
                Canvas.ForceUpdateCanvases();
                var rows = view.Root.GetComponent<BindingMap>().Require<ScrollRect>("rosterScroll").content;
                var first = rows.Find("first-level").GetComponent<BindingMap>();
                var twoDigits = rows.Find("two-digits").GetComponent<BindingMap>();
                var max = rows.Find("max-level").GetComponent<BindingMap>();
                var name = first.Require<GameText>("name");
                name.text = "永劫漂流者鲁特比";
                name.ForceMeshUpdate();
                Assert.That(name.fontStyle.HasFlag(FontStyles.Bold), Is.False, "The roster name uses the regular face.");
                Assert.That(name.fontSize, Is.EqualTo(20.4f).Within(.1f));
                Assert.That(name.isTextTruncated, Is.False);
                Assert.That(name.textInfo.characterCount, Is.EqualTo(name.text.Length));
                var portrait = first.Require<Image>("portrait").rectTransform;
                Assert.That(portrait.anchorMax.y, Is.EqualTo(1f - 10f / 112f).Within(.001f));
                Assert.That(portrait.anchorMin.y, Is.EqualTo(1f - 99f / 112f).Within(.001f));
                var digit = first.Require<Image>("rosterLevelFirst");
                Assert.That(digit.enabled, Is.True);
                Assert.That(digit.sprite.name, Is.EqualTo("hero_lv_1"));
                Assert.That(first.Require<Image>("rosterLevelSecond").enabled, Is.False);
                Assert.That(first.Require<Image>("rosterLevelMax").enabled, Is.False);
                var units = twoDigits.Require<Image>("rosterLevelFirst");
                var tens = twoDigits.Require<Image>("rosterLevelSecond");
                Assert.That(units.enabled && tens.enabled, Is.True);
                Assert.That(units.sprite.name, Is.EqualTo("hero_lv_9"));
                Assert.That(tens.sprite.name, Is.EqualTo("hero_lv_5"));
                Assert.That(twoDigits.Require<Image>("rosterLevelMax").enabled, Is.False);
                float digitPitch = (units.rectTransform.anchorMin.x - tens.rectTransform.anchorMin.x) * 361f;
                Assert.That(digitPitch, Is.EqualTo(13.73f).Within(.1f), "Native level digits run left-to-right within the number field.");
                var maximum = max.Require<Image>("rosterLevelMax");
                Assert.That(maximum.enabled, Is.True);
                Assert.That(maximum.sprite.name, Is.EqualTo("hero_lv_max"));
                Assert.That(max.Require<Image>("rosterLevelFirst").enabled, Is.False);
                foreach (var graphic in new[] { digit, tens, units, maximum })
                {
                    var rect = graphic.rectTransform;
                    Assert.That(rect.anchorMin.y, Is.GreaterThanOrEqualTo(1f - 99f / 112f),
                        "Level art must fit within the solid bottom edge.");
                    Assert.That(rect.anchorMax.x, Is.LessThanOrEqualTo(first.Require<Image>("element").rectTransform.anchorMin.x + .02f));
                }
            }
            finally { UnityEngine.Object.Destroy(canvas); }
        }

        private static void AssertThreeCardStack(ScrollRect scroll)
        {
            var rows = scroll.content.Cast<RectTransform>().Where(row => row.gameObject.activeInHierarchy)
                .OrderByDescending(row => row.anchoredPosition.y).ToArray();
            Assert.That(rows.Length, Is.EqualTo(3));
            float previousBottom = float.PositiveInfinity;
            for (int i = 0; i < rows.Length; ++i)
            {
                var row = rows[i];
                // The solid part of hero_bg_normal is y=10..98 from the top of its 361 x 112 image.
                float Edge(float fromTop) => scroll.viewport.InverseTransformPoint(row.TransformPoint(
                    new Vector3(row.rect.center.x, row.rect.yMax - fromTop))).y;
                float top = Edge(10f);
                Assert.That(top, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + .5f),
                    "Even the second card above the focus must show its name and plate below the mask.");
                Assert.That(Edge(99f), Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - .5f));
                if (i > 0) Assert.That(top, Is.GreaterThanOrEqualTo(previousBottom - .5f),
                    "The opaque plates of adjacent heroes must meet after scaling.");
                previousBottom = Edge(99f);
            }
            Assert.That(rows[2].GetSiblingIndex(), Is.GreaterThan(rows[1].GetSiblingIndex()));
        }

        [UnityTest]
        public IEnumerator NewOwnedHeroesOpenInTheInventoryDetail()
        {
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            string[] ids = {"c1043","c1044","c1046","c1047","c1048","c1049","c1050","c1053","c1054","c1055",
                "c1060","c1065","c1066","c1067","c1069","c1070","c1071","c1072","c1073","c1074",
                "c1076","c1079","c1080","c1081","c1082","c1083","c1085","c1086","c1087","c1088"};
            var snapshot = new HeroInventorySnapshot { capacity = 50, used = ids.Length,
                heroes = ids.Select(id => new HeroInventoryItem { instanceId = "owned-" + id, templateId = id, level = 1, stars = 4 }).ToArray() };
            var registry = new UIViewRegistry();
            registry.Register<HeroInventory>();
            var canvas = new GameObject("Inventory catalog test", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                using var scope = new BindingScope(CancellationToken.None);
                var load = registry.CreateAsync<HeroInventory>(new InventoryPrefab(), canvas.transform, scope);
                yield return new WaitUntil(() => load.IsCompleted);
                var view = load.GetAwaiter().GetResult();
                var binder = new HeroInventoryBinder(view, art, new InventoryText(), _ => Task.FromResult(snapshot), () => Task.CompletedTask);
                var open = binder.OpenAsync(true, scope);
                yield return new WaitUntil(() => open.IsCompleted);
                open.GetAwaiter().GetResult();
                var screen = view.Root.GetComponent<BindingMap>();
                foreach (var id in ids)
                {
                    var expected = art.heroes.Single(hero => hero.id == id);
                    screen.Require<TMP_InputField>("search").text = expected.name;
                    yield return null;
                    Assert.That(VisibleRows(screen, "rosterScroll").Length, Is.EqualTo(1), id);
                    Assert.That(screen.Require<GameText>("detailName").text, Is.EqualTo(expected.name), id);
                    var portrait = screen.Require<RawImage>("detailPortrait");
                    Assert.That(portrait.gameObject.activeInHierarchy && portrait.texture is RenderTexture, Is.True, id);
                }
            }
            finally { UnityEngine.Object.Destroy(canvas); }
        }

        [UnityTest, Timeout(165000)]
        public IEnumerator HomeRoutesToOwnedHeroesAndDistinctEmptyBagPages()
        {
            string endpoint = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Set ASTRAL_INVENTORY_TEST_URL to an isolated local server with migration 010 and developer guests enabled.");
            Assert.That(new Uri(endpoint).IsLoopback, Is.True, "Use a local development test database.");

            priorEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            priorSkipEntry = Environment.GetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY");
            settingsChanged = true;
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", "1");
            priorScene = SceneManager.GetActiveScene();
            home = EditorSceneManager.LoadSceneInPlayMode(HomePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Wait(() => home.IsValid() && home.isLoaded && FindWindow("HomeWindow"), "Home did not load.");
            Assert.That(SceneManager.SetActiveScene(home), Is.True);

            Click(FindWindow("HomeWindow"), "more");
            yield return Wait(() => FindWindow("HomeMoreMenu"), "The More menu did not open.");
            yield return new WaitForSecondsRealtime(.20f);
            yield return Capture("inventory-more", 1478, 720);
            Click(FindWindow("HomeMoreMenu"), "heroes");
            yield return Wait(() =>
            {
                var screen = FindWindow("HeroInventory");
                return screen && screen.Require<GameObject>("roster").activeInHierarchy &&
                    VisibleRows(screen, "rosterScroll").Length == StarterNames.Length;
            }, "The hero page did not show the three initial owned hero records.");
            Assert.That(FindWindow("HomeMoreMenu"), Is.Null, "Opening the hero page should dismiss its menu.");
            var roster = FindWindow("HeroInventory");
            Assert.That(roster.Require<GameObject>("bag").activeInHierarchy, Is.False);
            AssertOwnedHeroes(roster, "rosterScroll");
            Assert.That(roster.Require<Image>("detailIllustration").sprite, Is.Not.Null);
            Assert.That(StarterNames, Does.Contain(roster.Require<GameText>("detailName").text));
            Assert.That(int.TryParse(roster.Require<GameText>("detailStat_attack").text, out var attack) && attack > 0);
            Assert.That(roster.Require<Image>("detailStar0").sprite, Is.Not.Null);
            yield return Capture("inventory-hero-detail", 1478, 720);
            yield return Capture("inventory-hero-detail-16x9", 1920, 1080);
            yield return Capture("inventory-hero-detail-narrow", 1040, 1108);
            yield return Capture("inventory-hero-detail-16x10", 1600, 1000);
            yield return Capture("inventory-hero-detail-ultrawide", 2520, 1080);

            var compactScroll = roster.Require<ScrollRect>("rosterScroll");
            var third = VisibleRows(roster, "rosterScroll").OrderByDescending(
                row => ((RectTransform)row.transform).anchoredPosition.y).Last();
            Click(third.GetComponent<GameButton>(), "last owned hero");
            yield return null;
            yield return Capture("inventory-hero-last-16x10", 1560, 960);
            yield return Capture("inventory-hero-last-16x9", 1920, 1080);
            AssertThreeCardStack(compactScroll);

            var search = roster.Require<TMP_InputField>("search");
            search.text = StarterNames[2];
            yield return Wait(() => VisibleRows(roster, "rosterScroll").Length == 1, "Search did not reduce the owned roster.");
            Assert.That(VisibleRows(roster, "rosterScroll").Single().Require<GameText>("name").text, Is.EqualTo(StarterNames[2]));
            search.text = "";
            yield return Wait(() => VisibleRows(roster, "rosterScroll").Length == StarterNames.Length, "Clearing search did not restore the roster.");
            Click(roster, "expand");
            yield return Wait(() => !roster.Require<RectTransform>("rosterDetail").gameObject.activeInHierarchy,
                "The expanded grid did not replace the detailed panel.");
            Assert.That(compactScroll.scrollSensitivity, Is.EqualTo(68f), "Restore the grid's own wheel sensitivity.");
            AssertOwnedHeroes(roster, "rosterScroll");
            var grid = VisibleRows(roster, "rosterScroll").Select(row => (RectTransform)row.transform).ToArray();
            Assert.That(grid.Select(card => card.anchorMax.x), Is.EquivalentTo(new[] { .25f, .5f, .75f }));
            Assert.That(VisibleRows(roster, "rosterScroll").All(row => !row.Require<Image>("selection").enabled));
            yield return Capture("inventory-hero-grid", 1478, 720);
            string otherHero = StarterNames.First(hero => hero != roster.Require<GameText>("detailName").text);
            var other = VisibleRows(roster, "rosterScroll").Single(row =>
                row.Require<GameText>("name").text == otherHero);
            Click(other.GetComponent<GameButton>(), otherHero);
            yield return Wait(() => roster.Require<RectTransform>("rosterDetail").gameObject.activeInHierarchy,
                "Selecting a hero in the expanded grid did not return to that hero's detail page.");
            Assert.That(roster.Require<GameText>("detailName").text, Is.EqualTo(otherHero));
            Click(roster, "expand");
            yield return Wait(() => !roster.Require<RectTransform>("rosterDetail").gameObject.activeInHierarchy,
                "The expanded grid did not reopen.");
            Click(roster, "back");
            yield return Wait(() => roster.Require<RectTransform>("rosterDetail").gameObject.activeInHierarchy,
                "Back from the expanded grid should return to the detail page.");
            Click(roster, "back");

            yield return Wait(() => FindWindow("HomeWindow") && !FindWindow("HeroInventory"), "Back did not return to Home.");
            Click(FindWindow("HomeWindow"), "bag");
            yield return Wait(() =>
            {
                var screen = FindWindow("HeroInventory");
                return screen && screen.Require<GameObject>("bag").activeInHierarchy &&
                    VisibleRows(screen, "bagScroll").Length == StarterNames.Length;
            }, "The Bag entry did not show the hero rows.");
            var bag = FindWindow("HeroInventory");
            Assert.That(bag.Require<GameObject>("roster").activeInHierarchy, Is.False);
            AssertOwnedHeroes(bag, "bagScroll");
            foreach (var row in VisibleRows(bag, "bagScroll"))
            {
                for (int slot = 0; slot < 6; ++slot)
                    Assert.That(row.Require<Image>("emptyGear" + slot).sprite.name, Is.EqualTo("cm_item_slot"));
                Assert.That(row.Require<Image>("emptyArtifact").sprite.name, Is.EqualTo("cm_item_slot_arti"));
                Assert.That(row.Require<Image>("emptyExclusive").sprite.name, Is.EqualTo("cm_item_slot_private"));
                Assert.That(row.Require<Image>("bagLevelFirst").sprite.name, Is.EqualTo("hero_lv_1"));
            }
            yield return Capture("inventory-bag-heroes", 1478, 720);
            yield return Capture("inventory-bag-heroes-16x9", 1920, 1080);
            Click(bag, "filter_knight");
            yield return Wait(() => VisibleRows(bag, "bagScroll").Length == 1, "The knight filter should show one starter.");
            Assert.That(VisibleRows(bag, "bagScroll").Single().Require<GameText>("name").text, Is.EqualTo(StarterNames[0]));
            Click(bag, "filter_all");

            Click(bag, "tabEquipment");
            yield return Wait(() => ShowsEmptyTab(bag, "emptyEquipmentChrome"), "The equipment tab did not display its empty state.");
            yield return Capture("inventory-bag-equipment-empty", 1478, 720);
            Click(bag, "tabArtifacts");
            yield return Wait(() => ShowsEmptyTab(bag, "emptyArtifactChrome"), "The artifact tab did not display its empty state.");
            yield return Capture("inventory-bag-artifacts-empty", 1478, 720);
            Click(bag, "tabHeroes");
            yield return Wait(() => VisibleRows(bag, "bagScroll").Length == StarterNames.Length,
                "Switching back to Heroes did not restore the owned records.");
            AssertOwnedHeroes(bag, "bagScroll");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator ArtifactTrialEntersAuthoritativeBattleAndReturnsToSelectedHero()
        {
            string endpoint = Environment.GetEnvironmentVariable("ASTRAL_TRIAL_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Set ASTRAL_TRIAL_TEST_URL to the isolated Development host with BattlePreview enabled.");
            Assert.That(new Uri(endpoint).IsLoopback, Is.True);

            priorEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            priorSkipEntry = Environment.GetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY");
            settingsChanged = true;
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", "1");
            priorScene = SceneManager.GetActiveScene();
            home = EditorSceneManager.LoadSceneInPlayMode(HomePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Wait(() => FindWindow("HomeWindow"), "Home did not load.");
            Assert.That(SceneManager.SetActiveScene(home), Is.True);
            Click(FindWindow("HomeWindow"), "more");
            yield return Wait(() => FindWindow("HomeMoreMenu"), "The More menu did not open.");
            yield return new WaitForSecondsRealtime(.20f);
            Click(FindWindow("HomeMoreMenu"), "heroes");
            yield return Wait(() =>
            {
                var page = FindWindow("HeroInventory");
                return page && page.Require<GameButton>("artifactTrial").IsInteractable();
            }, "The selected hero trial did not become available.");
            var roster = FindWindow("HeroInventory");
            string selected = roster.Require<GameText>("detailName").text;
            Click(roster, "artifactTrial");

            BattleHudPreview battle = null;
            Scene battleScene = default;
            yield return Wait(() =>
            {
                battleScene = SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath);
                if (!battleScene.isLoaded) return false;
                battle = battleScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>())
                    .FirstOrDefault();
                return battle && battle.SimulationSnapshot != null;
            }, "The real trial did not load the battle scene.", 30);
            var opening = battle.SimulationSnapshot;
            var enemy = opening.Units.Single(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(opening.Units.Count, Is.EqualTo(2), "The selected hero must be launched alone against the training opponent.");
            ulong sequence = opening.LastCommandSequence;
            bool submitted = false;
            yield return Wait(() => submitted || (submitted = battle.TrySelectBattleTarget(enemy.InstanceId)),
                "The owned hero could not target and submit their first skill.");
            yield return Wait(() => battle.SimulationSnapshot.LastCommandSequence > sequence,
                "The trial server did not advance the authoritative command sequence.");

            var battleHud = battleScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>())
                .Single(map => map.name == "BattleHud");
            Click(battleHud, "pause");
            yield return Wait(() => !BattleOnlineLaunch.IsActive, "The trial did not exit.");
            Assert.That(FindWindow("HeroInventory"), Is.SameAs(roster));
            Assert.That(roster.Require<GameText>("detailName").text, Is.EqualTo(selected));
        }

        [UnityTest, Timeout(100000)]
        public IEnumerator OwnedArtifactsUseTwoBannersAndTheSelectedFullCard()
        {
            string bearer = Environment.GetEnvironmentVariable("ASTRAL_ARTIFACT_LAYOUT_FIXTURE");
            string url = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_TEST_URL");
            if (string.IsNullOrEmpty(bearer) || !Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a disposable loopback inventory with at least three unequipped artifacts.");
            using var scope = new BindingScope(CancellationToken.None);
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(new Uri(url), Token, scope.Token);
            using var api = new ArtifactInventoryApi(new Uri(url), Token, scope.Token);
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var open = OpenArtifactWindow(false, scope, heroes, api, art);
            yield return Wait(() => open.IsCompleted, "Inventory did not open.");
            var map = open.GetAwaiter().GetResult();
            Click(map, "tabArtifacts");
            BindingMap[] Cards() => VisibleRows(map, "artifactItems");
            yield return Wait(() => Cards().Length >= 3, "The populated artifact tab did not open.");
            var get = api.FetchAsync(scope.Token);
            yield return Wait(() => get.IsCompleted, "The server did not return the owned inventory.");
            var items = get.Result.artifacts.ToDictionary(item => item.instanceId);
            var cards = Cards();
            var first = (RectTransform)cards[0].transform;
            var second = (RectTransform)cards[1].transform;
            var third = (RectTransform)cards[2].transform;
            Assert.That(first.anchoredPosition.y, Is.EqualTo(second.anchoredPosition.y));
            Assert.That(third.anchoredPosition.y, Is.LessThan(first.anchoredPosition.y));
            foreach (var row in cards.Take(3))
            {
                Click(row, "artifactCardButton");
                var entry = art.Find(items[row.name].templateId);
                Assert.That(row.Require<Image>("artifactCardPortrait").sprite, Is.SameAs(entry.combatIcon));
                Assert.That(row.Require<Image>("artifactCardBanner").sprite, Is.SameAs(entry.portrait));
                Assert.That(row.Require<GameText>("artifactCardName").text, Is.EqualTo(entry.template.Name));
                Assert.That(map.Require<Image>("artifactPortrait").sprite, Is.SameAs(entry.card));
                Assert.That(map.Require<GameText>("artifactAttack").text, Does.Contain(entry.template.AttackAt(items[row.name].level).ToString()));
                Assert.That(map.Require<GameText>("artifactHealth").text, Does.Contain(entry.template.HealthAt(items[row.name].level).ToString()));
                Assert.That(map.Require<GameText>("artifactSkill").text, Is.EqualTo(entry.template.DescriptionAt(items[row.name].level)));
            }
            yield return Capture("owned-artifact-bag", 1478, 720);
            yield return Capture("owned-artifact-bag-16x10", 1536, 960);
            Click(map, "artifactAction_lock");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => !map.Require<GameObject>("artifactConfirmation").activeSelf, "Lock did not complete.");
            Assert.That(map.Require<GameButton>("artifactAction_sell").interactable, Is.False);
            Click(map, "artifactAction_lock");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => !map.Require<GameObject>("artifactConfirmation").activeSelf, "Unlock did not complete.");
            Click(map, "artifactAction_enhance");
            Click(map, "artifactAction_chooseCopy");
            yield return Wait(() => Cards().Length > 0 && map.Require<GameButton>("artifactPickerClose").gameObject.activeSelf, "Material picker did not open.");
            yield return Capture("artifact-material-picker", 1478, 720);
            Click(Cards()[0], "artifactCardButton");
            Assert.That(map.Require<GameButton>("artifactAction_confirm").interactable, Is.True);
            Click(map, "artifactAction_cancel");
            int beforeSale = Cards().Length;
            Click(map, "artifactAction_sell");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => Cards().Length < beforeSale, "Sale did not refresh the owned cards.");
            var balance = api.FetchAsync(scope.Token);
            yield return Wait(() => balance.IsCompleted, "Could not read the sale proceeds.");
            while (balance.Result.materials.gold < art.Catalog.EnhancementCost(
                balance.Result.artifacts.Max(item => art.Catalog.MaterialExperience(art.Find(item.templateId).template.Stars, item.experience))))
            {
                int remaining = Cards().Length;
                Assert.That(remaining, Is.GreaterThan(2), "The disposable fixture needs enough sale proceeds for one enhancement.");
                Click(map, "artifactAction_sell");
                Click(map, "artifactAction_confirm");
                yield return Wait(() => Cards().Length < remaining, "The second sale did not refresh inventory.");
                balance = api.FetchAsync(scope.Token);
                yield return Wait(() => balance.IsCompleted, "Could not read the sale proceeds.");
            }
            int beforeUpgrade = Cards().Length;
            Click(map, "artifactAction_enhance");
            Click(map, "artifactAction_chooseCopy");
            yield return Wait(() => Cards().Length > 0 && map.Require<GameButton>("artifactPickerClose").gameObject.activeSelf, "The picker must reopen.");
            string fodder = Cards()[0].name;
            Click(Cards()[0], "artifactCardButton");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => !map.Require<GameObject>("artifactConfirmation").activeSelf && Cards().Length == beforeUpgrade - 1, "Enhancement did not consume one material.");
            var upgraded = api.FetchAsync(scope.Token);
            yield return Wait(() => upgraded.IsCompleted, "Could not verify the enhanced artifact.");
            Assert.That(upgraded.Result.artifacts.Any(item => item.instanceId == fodder), Is.False);
            Assert.That(upgraded.Result.artifacts.Any(item => item.experience > 0 || item.limitBreaks > 0), Is.True);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ArtifactPickerEquipsAndUnequipsAServerOwnedInstance()
        {
            string bearer = Environment.GetEnvironmentVariable("ASTRAL_ARTIFACT_LAYOUT_FIXTURE");
            string url = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_TEST_URL");
            if (string.IsNullOrEmpty(bearer) || !Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a disposable loopback inventory with an artifact usable by its starter hero.");
            using var scope = new BindingScope(CancellationToken.None);
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(new Uri(url), Token, scope.Token);
            using var api = new ArtifactInventoryApi(new Uri(url), Token, scope.Token);
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var open = OpenArtifactWindow(true, scope, heroes, api, art);
            yield return Wait(() => open.IsCompleted, "Hero inventory did not open.");
            var map = open.GetAwaiter().GetResult();
            Click(map, "artifactSlot");
            yield return Wait(() => VisibleRows(map, "artifactItems").Length > 0, "No compatible artifacts in the equipment picker.");
            var row = VisibleRows(map, "artifactItems")[0];
            Click(row, "artifactCardButton");
            var emblem = row.Require<Image>("artifactCardPortrait").sprite;
            yield return Capture("artifact-equip-picker", 1478, 720);
            Click(map, "artifactAction_equip");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => map.Require<Image>("artifactSlotImage").sprite == emblem, "Equipped slot must show the newly equipped emblem.");
            Click(map, "artifactAction_unequip");
            Click(map, "artifactAction_confirm");
            yield return Wait(() => !map.Require<Image>("artifactSlotImage").enabled, "Unequipped artifact still occupies the hero slot.");
            var persisted = api.FetchAsync(scope.Token);
            yield return Wait(() => persisted.IsCompleted, "Could not read the updated inventory.");
            Assert.That(string.IsNullOrEmpty(persisted.Result.artifacts.Single(item => item.instanceId == row.name).heroInstanceId), Is.True);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ArtifactBagWithNoOwnedItemsShowsOnlyTheEmptyDetail()
        {
            string url = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_TEST_URL");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a loopback service with fresh, empty guest inventories.");
            using var scope = new BindingScope(CancellationToken.None);
            using var arena = new AstralOath.Client.Arena.ArenaApi(new Uri(url), scope.Token);
            var login = arena.GetSessionTokenAsync("Artifact empty layout", scope.Token);
            yield return Wait(() => login.IsCompleted, "A fresh guest session is required.");
            string bearer = login.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(new Uri(url), Token, scope.Token);
            using var api = new ArtifactInventoryApi(new Uri(url), Token, scope.Token);
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var open = OpenArtifactWindow(false, scope, heroes, api, art);
            yield return Wait(() => open.IsCompleted, "The empty inventory did not open.");
            var map = open.GetAwaiter().GetResult();
            Click(map, "tabArtifacts");
            yield return Wait(() => map.Require<GameObject>("artifactEmptyMessage").activeInHierarchy &&
                !map.Require<GameText>("artifactStatus").gameObject.activeInHierarchy, "The empty tab must finish loading.");
            Assert.That(VisibleRows(map, "artifactItems"), Is.Empty);
            Assert.That(map.Require<Image>("artifactEmptyPlate").isActiveAndEnabled, Is.True);
            Assert.That(map.Require<GameObject>("artifactDetail").activeSelf, Is.False);
            yield return Capture("artifact-bag-empty", 1478, 720);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TrainingBagChoosesARecipientAndPersistsThePiece()
        {
            yield return ExerciseTrainingGear(false);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TrainingHeroReplacesAndRemovesOneWeaponAndRestoresStats()
        {
            yield return ExerciseTrainingGear(true);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator EquipmentReadFailureLeavesHeroesAvailableAndRetriesInTheBag()
        {
            if (!Uri.TryCreate(Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_TEST_URL"),UriKind.Absolute,out var endpoint) ||
                !endpoint.IsLoopback) Assert.Ignore("Requires the local training equipment fixture.");
            using var scope = new BindingScope(CancellationToken.None);
            using var arena = new AstralOath.Client.Arena.ArenaApi(endpoint,scope.Token);
            var admission = arena.GetSessionTokenAsync("Gear retry",scope.Token);
            yield return Wait(() => admission.IsCompleted,"Test guest could not sign in.");
            string bearer = admission.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(endpoint,Token,scope.Token);
            using var equipment = new EquipmentInventoryApi(endpoint,Token,scope.Token);
            int reads = 0;
            Task<EquipmentInventorySnapshot> Read(CancellationToken ct) => ++reads == 1
                ? Task.FromException<EquipmentInventorySnapshot>(new IOException("Test error")) : equipment.FetchAsync(ct);
            var open = OpenArtifactWindow(false,scope,heroes,null,null,equipment,Read);
            yield return Wait(() => open.IsCompleted,"A failed equipment read blocked the hero bag.");
            var page = open.GetAwaiter().GetResult();
            Assert.That(VisibleRows(page,"bagScroll"),Is.Not.Empty);
            Click(page,"tabEquipment");
            var browser = page.Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
            Assert.That(browser.Require<GameText>("status").text,Is.EqualTo("装备数据加载失败。"));
            Click(browser,"retry");
            yield return Wait(() => VisibleRows(browser,"items").Length == 7,"Equipment retry did not render the inventory.");
            Assert.That(browser.Require<GameButton>("retry").gameObject.activeSelf,Is.False);
        }

        private IEnumerator ExerciseTrainingGear(bool roster)
        {
            string url = Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_TEST_URL");
            if (!Uri.TryCreate(url,UriKind.Absolute,out var endpoint) || !endpoint.IsLoopback)
                Assert.Ignore("Requires a loopback service with training equipment enabled.");
            using var scope = new BindingScope(CancellationToken.None);
            using var arena = new AstralOath.Client.Arena.ArenaApi(endpoint,scope.Token);
            var admission = arena.GetSessionTokenAsync("Gear QA",scope.Token);
            yield return Wait(() => admission.IsCompleted,"Test guest could not sign in.");
            string bearer = admission.GetAwaiter().GetResult();
            Task<string> Token(CancellationToken _) => Task.FromResult(bearer);
            using var heroes = new HeroInventoryApi(endpoint,Token,scope.Token);
            using var equipment = new EquipmentInventoryApi(endpoint,Token,scope.Token);
            var opening = OpenArtifactWindow(roster,scope,heroes,null,null,equipment);
            yield return Wait(() => opening.IsCompleted,"Equipment inventory did not open.");
            var page = opening.GetAwaiter().GetResult();
            var browser = page.Require<BindingMap>("gearBrowser");
            var bag = browser.Require<BindingMap>("bagPane");
            var single = browser.Require<BindingMap>("singleSlot");
            var load = equipment.FetchAsync(scope.Token);
            yield return Wait(() => load.IsCompleted,"Could not verify the fixture.");
            var items = load.GetAwaiter().GetResult().items;
            Assert.That(items.Length,Is.EqualTo(7));
            var weapons = items.Where(i => i.slot == "weapon").OrderBy(i => i.mainStat.value).ToArray();
            Assert.That(weapons.Length,Is.EqualTo(2));
            var trainingWeaponIcons = new[] { "icon_eq_weapon_orb001", "icon_eq_weapon_insect006" };
            var weaponSlot = page.Require<BindingMap>("equipmentSlot_weapon").Require<Image>("item");
            string initialAttack = page.Require<GameText>("detailStat_attack").text;
            if (!roster)
            {
                Click(page,"tabEquipment");
                Assert.That(VisibleRows(bag,"items").Length,Is.EqualTo(7));
                Assert.That(page.Require<GameObject>("emptyEquipmentChrome").activeInHierarchy,Is.False);
                Assert.That(VisibleRows(bag,"items").All(row => row.Require<Image>("icon").isActiveAndEnabled &&
                    row.Require<Image>("icon").sprite && row.Require<Image>("icon").color.a >= .99f), Is.True);
                Assert.That(bag.Require<Image>("portrait").isActiveAndEnabled,Is.True);
                Assert.That(bag.Require<GameText>("description").text,Is.Not.Empty);
                Assert.That(bag.Require<GameText>("description").text,Does.Not.StartWith("home."));
                Assert.That(int.TryParse(browser.Require<BindingMap>("bagScore").Require<GameText>("value").text,out _),Is.True);
                yield return Capture("equipment-bag",1478,720);
                yield return Capture("equipment-bag-narrow",1040,960);
                Click(bag,"filter_weapon");
                Assert.That(VisibleRows(bag,"items").Length,Is.EqualTo(2));
            }
            string wearer = null;
            for (int index = 0; index < (roster ? 2 : 1); ++index)
            {
                var item = weapons[index];
                if (roster)
                {
                    Click(page,"gearOpen_weapon");
                    if (index > 0)
                    {
                        Assert.That(single.Require<GameObject>("popup").activeInHierarchy, Is.True);
                        Click(single,"change");
                    }
                    Assert.That(single.Require<BindingMap>("candidate").Require<GameObject>("empty").activeInHierarchy, Is.True);
                    var candidate = VisibleRows(single,"candidates").Single(m => m.name == item.instanceId);
                    Assert.That(candidate.Require<BindingMap>("card").Require<Image>("icon").sprite.name,
                        Is.EqualTo(trainingWeaponIcons[index]));
                    Click(candidate,"select");
                    Assert.That(single.Require<BindingMap>("candidate").Require<GameObject>("detail").activeInHierarchy, Is.True);
                    if (index > 0)
                    {
                        yield return Capture("equipment-slot-compare",1478,720);
                        yield return Capture("equipment-slot-compare-narrow",1040,960);
                    }
                    Click(single,"equip");
                    yield return Wait(() => !browser.gameObject.activeSelf, "Single-slot equip did not return to the hero.");
                }
                else
                {
                    Click(VisibleRows(bag,"items").Single(m => m.name == item.instanceId),"select");
                    Click(bag,"apply");
                    Click(VisibleRows(browser,"targets")[0],"select");
                    Assert.That(browser.Require<GameText>("change").text,Does.Contain("→"));
                    Click(browser,"confirm");
                    yield return Wait(() => !browser.Require<GameObject>("dialog").activeSelf,"The server did not confirm equipment.");
                }
                var read = equipment.FetchAsync(scope.Token);
                yield return Wait(() => read.IsCompleted,"Could not read server equipment after the change.");
                var saved = read.GetAwaiter().GetResult().items;
                string nextWearer = saved.Single(value => value.instanceId == item.instanceId).equippedHeroId;
                Assert.That(nextWearer,Is.Not.Null.And.Not.Empty);
                if (wearer != null) Assert.That(nextWearer,Is.EqualTo(wearer));
                wearer = nextWearer;
                Assert.That(saved.Count(value => value.slot == "weapon" && value.equippedHeroId == wearer),Is.EqualTo(1));
                if (roster)
                {
                    Assert.That(weaponSlot.enabled,Is.True);
                    Assert.That(page.Require<GameText>("detailStat_attack").text,Is.Not.EqualTo(initialAttack));
                    Assert.That(weaponSlot.sprite.name,Is.EqualTo(trainingWeaponIcons[index]));
                }
            }
            if (roster)
            {
                yield return Capture("equipment-equipped-hero",1478,720);
                yield return Capture("equipment-equipped-hero-narrow",1040,960);
                Click(page,"gearOpen_weapon");
                Assert.That(single.Require<GameObject>("popup").activeInHierarchy, Is.True);
                yield return Capture("equipment-equipped-detail",1478,720);
                Click(single,"popupRemove");
                yield return Wait(() => !weaponSlot.enabled,"Unequipping the weapon must clear its icon.");
                Assert.That(page.Require<GameText>("detailStat_attack").text,Is.EqualTo(initialAttack));
            }
            else
            {
                Click(bag,"remove");
                Assert.That(browser.Require<GameObject>("dialog").activeSelf,Is.True);
                using var otherClient = new EquipmentInventoryApi(endpoint,Token,scope.Token);
                var pending = otherClient.FetchAsync(scope.Token);
                yield return Wait(() => pending.IsCompleted,"Second client could not read the equipped weapon.");
                long previous = pending.GetAwaiter().GetResult().version;
                var remove = otherClient.ChangeAsync("unequip",Guid.NewGuid().ToString("N"),wearer,weapons[0].instanceId,"weapon",previous,scope.Token);
                yield return Wait(() => remove.IsCompleted,"Second client did not finish unequipping.");
                remove.GetAwaiter().GetResult();
                var refresh = equipment.RefreshIfChangedAsync(previous,scope.Token);
                yield return Wait(() => refresh.IsCompleted,"HUD inventory did not refresh on returning to the foreground.");
                refresh.GetAwaiter().GetResult();
                Assert.That(browser.Require<GameObject>("dialog").activeSelf,Is.False,"The old confirmation stayed open after a remote mutation.");
                Assert.That(bag.Require<GameText>("wearer").text,Is.EqualTo("未佩戴"));
            }
        }

        private async Task<BindingMap> OpenArtifactWindow(bool roster, BindingScope scope, HeroInventoryApi heroes,
            ArtifactInventoryApi api, ArtifactArtCatalog art, EquipmentInventoryApi equipment = null,
            Func<CancellationToken,Task<EquipmentInventorySnapshot>> readEquipment = null)
        {
            priorScene = SceneManager.GetActiveScene();
            home = SceneManager.CreateScene("Artifact inventory test");
            SceneManager.SetActiveScene(home);
            new GameObject("Inventory camera", typeof(Camera));
            var canvas = new GameObject("Inventory canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            var assets = new InventoryPrefab();
            var registry = new UIViewRegistry();
            registry.Register<HeroInventory>();
            var view = await registry.CreateAsync<HeroInventory>(assets, canvas.transform, scope);
            var locale = await AstralOath.Client.Arena.ArenaLabels.LoadAsync(assets, scope, "zh-Hans", "ja");
            var heroArt = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            var binder = new HeroInventoryBinder(view, heroArt, locale, heroes.FetchAsync, () => Task.CompletedTask, art, api,
                equipmentApi: equipment,readEquipment: readEquipment);
            await binder.OpenAsync(roster, scope);
            return view.Root.GetComponent<BindingMap>();
        }

        private static bool ShowsEmptyTab(BindingMap bag, string chrome)
        {
            if (chrome == "emptyEquipmentChrome")
            {
                var browser = bag.Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
                if (browser.gameObject.activeInHierarchy)
                    return !browser.Require<GameObject>("detail").activeSelf && VisibleRows(browser,"items").Length == 0 &&
                        !browser.Require<GameButton>("retry").gameObject.activeSelf;
            }
            return bag.Require<GameObject>(chrome).activeInHierarchy && !bag.Require<GameText>("status").gameObject.activeInHierarchy &&
                VisibleRows(bag, "bagScroll").Length == 0;
        }

        private static void AssertOwnedHeroes(BindingMap screen, string scroll)
        {
            var rows = VisibleRows(screen, scroll);
            var names = rows.Select(row => row.Require<GameText>("name").text).ToArray();
            Assert.That(names, Is.EquivalentTo(StarterNames));
            Assert.That(rows.Select(row => row.gameObject.name), Is.Unique, "Hero rows use unique instance IDs.");
            Assert.That(rows.All(row => row.Require<Image>("portrait").sprite), Is.True, "All three heroes require actual portraits.");
            string capacity = string.Concat(screen.Require<GameText>(scroll == "rosterScroll" ? "rosterCapacity" : "capacity").text.Where(c => !char.IsWhiteSpace(c)));
            Assert.That(capacity, Is.EqualTo("3/1000"));
        }

        private static BindingMap[] VisibleRows(BindingMap screen, string scroll)
        {
            var content = screen.Require<ScrollRect>(scroll).content;
            return content.GetComponentsInChildren<BindingMap>()
                .Where(map => map.transform.parent == content).ToArray();
        }

        private BindingMap FindWindow(string name) => !home.IsValid() || !home.isLoaded ? null : home.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BindingMap>()).FirstOrDefault(map =>
                map.gameObject.name == name || map.gameObject.name == name + "(Clone)");

        private static void Click(BindingMap window, string key) => Click(window.Require<GameButton>(key), key);

        private static void Click(GameButton button, string key)
        {
            Assert.That(button, Is.Not.Null, key);
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, key);
            button.onClick.Invoke();
        }

        private static IEnumerator Wait(Func<bool> condition, string message, float seconds = 15f)
        {
            float expiry = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < expiry)
                yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private IEnumerator Capture(string name, int width, int height)
        {
            string path = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(path)) yield break;
            Directory.CreateDirectory(path);
            var roots = home.GetRootGameObjects();
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>()).First(item => item.enabled);
            var canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).First(item => item.isRootCanvas);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var priorTarget = camera.targetTexture;
            var priorActive = RenderTexture.active;
            var priorMask = camera.cullingMask;
            var priorMode = canvas.renderMode;
            var priorCamera = canvas.worldCamera;
            var priorDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = ~0;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render();
                if (name.StartsWith("inventory-hero-last-", StringComparison.Ordinal))
                {
                    var scroll = FindWindow("HeroInventory").Require<ScrollRect>("rosterScroll");
                    AssertThreeCardStack(scroll);
                    var last = scroll.content.Cast<RectTransform>().Where(row => row.gameObject.activeInHierarchy)
                        .OrderBy(row => row.anchoredPosition.y).First();
                    Assert.That(last.GetComponent<BindingMap>().Require<Image>("selection").enabled, Is.True);
                    float PixelY(float y) => RectTransformUtility.WorldToScreenPoint(camera,
                        last.TransformPoint(new Vector3(last.rect.center.x, last.rect.yMax - y))).y;
                    Assert.That((PixelY(10f) - PixelY(99f)) * 720f / height, Is.EqualTo(89f).Within(2f),
                        "The selected hero must be the full-height native plate in both window shapes.");
                }
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(path, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = priorTarget;
                camera.cullingMask = priorMask;
                canvas.renderMode = priorMode;
                canvas.worldCamera = priorCamera;
                canvas.planeDistance = priorDistance;
                RenderTexture.active = priorActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (BattleOnlineLaunch.IsActive && SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath).isLoaded)
            {
                var exit = BattleOnlineLaunch.ExitAsync();
                while (!exit.IsCompleted) yield return null;
            }
            if (priorScene.IsValid() && priorScene.isLoaded)
                SceneManager.SetActiveScene(priorScene);
            if (home.IsValid() && home.isLoaded)
                yield return SceneManager.UnloadSceneAsync(home);
            if (settingsChanged)
            {
                Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", priorEndpoint);
                Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", priorSkipEntry);
            }
        }
    }
}
#endif
