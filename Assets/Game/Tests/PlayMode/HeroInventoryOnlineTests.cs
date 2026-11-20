#if UNITY_EDITOR
using System;
using System.Collections;
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
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class HeroInventoryOnlineTests
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
        public IEnumerator NewOwnedHeroesOpenInTheInventoryDetail()
        {
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            string[] ids = {"c1043","c1044","c1046","c1047","c1048","c1049","c1050","c1053","c1054","c1055"};
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

            var search = roster.Require<TMP_InputField>("search");
            search.text = StarterNames[2];
            yield return Wait(() => VisibleRows(roster, "rosterScroll").Length == 1, "Search did not reduce the owned roster.");
            Assert.That(VisibleRows(roster, "rosterScroll").Single().Require<GameText>("name").text, Is.EqualTo(StarterNames[2]));
            search.text = "";
            yield return Wait(() => VisibleRows(roster, "rosterScroll").Length == StarterNames.Length, "Clearing search did not restore the roster.");
            Click(roster, "expand");
            yield return Wait(() => !roster.Require<RectTransform>("rosterDetail").gameObject.activeInHierarchy,
                "The expanded grid did not replace the detailed panel.");
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

        private static bool ShowsEmptyTab(BindingMap bag, string chrome)
        {
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

        private static BindingMap[] VisibleRows(BindingMap screen, string scroll) =>
            screen.Require<ScrollRect>(scroll).content.GetComponentsInChildren<BindingMap>()
                .Where(map => map != screen.Require<ScrollRect>(scroll).content.GetComponent<BindingMap>())
                .ToArray();

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
