#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    // These controlled HTTP replies validate client interaction, rendering and receipt recovery.
    // They do not replace the separate real MySQL/server enhancement tests.
    public sealed class EquipmentEnhancementPresentationTests
    {
        private const string Home = "Assets/Game/Modules/Home/";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Labels : ILocaleService
        {
            [Serializable] private sealed class Row { public string key, text; }
            [Serializable] private sealed class File { public Row[] rows; }
            private readonly Dictionary<string, string> values = JsonUtility.FromJson<File>(System.IO.File.ReadAllText(
                "Assets/Game/Modules/Arena/Configs/arena-zh-hans.json")).rows.ToDictionary(row => row.key, row => row.text);
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string value) { }
            public void SelectVoice(string value) { }
            public string Resolve(string key) => values.TryGetValue(key, out var value) ? value : key;
        }

        private sealed class Replies : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            internal readonly ConcurrentQueue<string> Posts = new ConcurrentQueue<string>();
            internal readonly Uri Endpoint;
            private readonly string rules, receipt;
            internal int Rejection;
            private int posted;
            internal Replies(EquipmentEnhancementRules rules, EquipmentEnhancementReceipt receipt, int rejection = 0)
            {
                this.rules = JsonUtility.ToJson(rules); this.receipt = JsonUtility.ToJson(receipt); Rejection = rejection;
                var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
                int number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
                Endpoint = new Uri("http://127.0.0.1:" + number + "/"); listener.Prefixes.Add(Endpoint.AbsoluteUri); listener.Start();
                _ = Task.Run(Serve);
            }
            private async Task Serve()
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var request = await listener.GetContextAsync();
                        string text = rules; int status = 200;
                        if (request.Request.HttpMethod == "POST")
                        {
                            using var reader = new StreamReader(request.Request.InputStream, Encoding.UTF8);
                            Posts.Enqueue(await reader.ReadToEndAsync());
                            text = receipt;
                            if (++posted == 1 && Rejection != 0) { status = Rejection; text = "{\"code\":\"test_rejection\"}"; }
                        }
                        var data = Encoding.UTF8.GetBytes(text); request.Response.StatusCode = status;
                        request.Response.ContentType = "application/json"; request.Response.ContentLength64 = data.Length;
                        await request.Response.OutputStream.WriteAsync(data, 0, data.Length); request.Response.Close();
                    }
                }
                catch (Exception) when (!listener.IsListening) { }
            }
            public void Dispose() { listener.Stop(); listener.Close(); }
        }

        private sealed class Page : IDisposable
        {
            internal readonly GameObject CanvasRoot;
            internal readonly BindingScope Scope;
            internal readonly BindingMap Map;
            internal readonly object Binder;
            internal Task Open;
            internal int Changes, Back;
            internal EquipmentInventorySnapshot Changed;
            private readonly Type binderType;
            internal Page(EquipmentInventoryApi api, EquipmentInventorySnapshot inventory, CancellationToken token, string targetId = null)
            {
                Scope = new BindingScope(token);
                CanvasRoot = new GameObject("Equipment enhancement controlled capture", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                CanvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasRoot.GetComponent<Canvas>().sortingOrder = 1000;
                var scaler = CanvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Home + "Res/Prefab/EquipmentEnhancement.prefab"), CanvasRoot.transform, false);
                Map = root.GetComponent<BindingMap>();
                var view = (EquipmentEnhancement)Activator.CreateInstance(typeof(EquipmentEnhancement), Members, null, new object[] { Map }, null);
                binderType = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementBinder", true);
                Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Home + "Res/Textures/Inventory/Equipment/" + name + ".png");
                void Card(EquipmentItemCardBinder card, EquipmentInventoryItem item) => card.Set(Art("icon_eq_boot_arena026"), Art("_itembg_equip_5"),
                    Art("icon_set_cri"), Art("cm_icon_stat_max_hp"), null, item.itemLevel, item.enhanceLevel, "");
                string Stat(EquipmentStatValue value) => value.kind.EndsWith("Pct") || value.kind.EndsWith("Bp") ? (value.value / 100f).ToString("0.##") + "%" : value.value.ToString();
                Binder = Activator.CreateInstance(binderType, Members, null, new object[] { view, Scope, new Labels(), api,
                    (Action)(() => Back++), (Action<EquipmentInventorySnapshot>)(value => { Changes++; Changed = value; }),
                    (Action<EquipmentItemCardBinder, EquipmentInventoryItem>)Card,
                    (Func<EquipmentInventoryItem, string>)(_ => "图形验收装备"),
                    (Func<EquipmentStatValue, Sprite>)(s => Art("cm_icon_stat_max_hp")),
                    (Func<EquipmentStatValue, string>)Stat,
                    (Func<EquipmentInventoryItem, string>)(_ => "传说鞋子"),
                    (Func<EquipmentInventoryItem, string>)(_ => "暴击套装 (0/2)") }, null);
                var selected = targetId == null ? inventory.items[0] : inventory.items.Single(row => row.instanceId == targetId);
                view.Show(); Open = (Task)binderType.GetMethod("ShowAsync", Members).Invoke(Binder, new object[] { inventory, selected, Scope.Token });
            }
            internal void Click(string key)
            {
                var button = Map.Require<GameButton>(key);
                Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, key);
                button.onClick.Invoke();
            }
            public void Dispose() { Scope.Dispose(); Object.Destroy(CanvasRoot); }
        }

        private static (EquipmentEnhancementRules Rules, EquipmentInventorySnapshot Before, EquipmentEnhancementReceipt After) Fixture()
        {
            string id = "11111111111111111111111111111111";
            var main = new[] { 1200, 1400, 1600, 1900, 2100, 2400, 2600, 2800, 3100, 3400, 3600, 3900, 4300, 4600, 5000, 6000 };
            var xp = new long[] { 0, 525, 1182, 1969, 3019, 4594, 6694, 9582, 13257, 17719, 23232, 30319, 39244, 50269, 64182, 82032 };
            var rules = new EquipmentEnhancementRules { rulesVersion = "controlled-client-contract",
                materials = new[] { ("ma_est1e", 100, 1600), ("ma_est2e", 1500, 14400), ("ma_est4e", 4500, 43200), ("ma_est6e", 13500, 129600) }
                    .Select(row => new EquipmentEnhancementMaterialRule { id = row.Item1, experience = row.Item2, goldPerUnit = row.Item3, slots = new[] { "boots" } }).ToArray(),
                profiles = new[] { new EquipmentEnhancementProfile { id = "controlled-boots", itemLevel = 85, grade = "epic", slot = "boots", mainKind = "healthPct", mainValues = main, xpThresholds = xp, supported = true } },
                successes = new[] { new EquipmentEnhancementSuccess { tier = "normal", probabilityBp = 10000, experienceMultiplierBp = 10000 } } };
            var item = new EquipmentInventoryItem { instanceId = id, templateId = "training_speed_boots", slot = "boots", setId = "critical", grade = "epic", itemLevel = 85,
                mainStat = new EquipmentStatValue { kind = "healthPct", value = 1200 }, enhancementSupported = true,
                substats = new[] { ("attack", 34), ("critChanceBp", 400), ("defensePct", 500), ("critDamageBp", 700) }
                    .Select(row => new EquipmentStatValue { kind = row.Item1, value = row.Item2 }).ToArray() };
            var before = new EquipmentInventorySnapshot { capacity = 500, used = 1, version = 10, items = new[] { item }, materials = new EquipmentEnhancementBalances { gold = 148608996,
                items = rules.materials.Select(row => new EquipmentEnhancementBalance { kind = row.id, quantity = 10000 }).ToArray() } };
            var after = JsonUtility.FromJson<EquipmentInventorySnapshot>(JsonUtility.ToJson(before)); after.version++;
            after.items[0].enhanceLevel = 3; after.items[0].xp = 2000; after.items[0].mainStat.value = 1900; after.items[0].substats[2].value = 1000;
            after.materials.gold -= 22400; after.materials.items[0].quantity -= 5; after.materials.items[1].quantity--;
            return (rules, before, new EquipmentEnhancementReceipt { inventory = after, result = new EquipmentEnhancementResult { instanceId = id, beforeLevel = 0, afterLevel = 3, beforeXp = 0,
                afterXp = 2000, grantedXp = 2000, consumedGold = 22400, successTier = "normal", changes = new[] { new EquipmentEnhancementChange { kind = "defensePct", before = 500, after = 1000 } } } });
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator QuantityCancelAutoRegistrationAndSuccessUseTheSameReceipt()
        {
            var fixture = Fixture(); using var replies = new Replies(fixture.Rules, fixture.After);
            using var life = new BindingScope(CancellationToken.None);
            using var api = new EquipmentInventoryApi(replies.Endpoint, _ => Task.FromResult("controlled-fixture"), life.Token);
            using var page = new Page(api, fixture.Before, life.Token);
            yield return Wait(() => page.Open.IsCompleted); page.Open.GetAwaiter().GetResult();
            yield return Frame(page.CanvasRoot, "client-idle");
            page.Map.Require<BindingMap>("row0").Require<GameButton>("select").onClick.Invoke();
            Assert.That(page.Map.Require<GameObject>("quantityPopup").activeSelf, Is.True);
            page.Map.Require<Slider>("quantitySlider").value = 2;
            Canvas.ForceUpdateCanvases();
            Assert.That(page.Map.Require<Slider>("quantitySlider").handleRect.rect.width, Is.GreaterThan(10));
            yield return Frame(page.CanvasRoot, "client-quantity");
            page.Click("quantityCancel");
            Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("0"));
            page.Click("auto"); page.Click("auto3");
            Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("22,400"));
            Assert.That(replies.Posts, Is.Empty, "Selection and cancellation must never post rewards or consume materials.");
            yield return Frame(page.CanvasRoot, "client-auto3");
            page.Click("submit");
            yield return Wait(() => page.Changes == 1);
            Assert.That(page.Changed.version, Is.EqualTo(11));
            Assert.That(page.Map.Require<GameButton>("submit").interactable, Is.False);
            yield return new WaitForSecondsRealtime(2.6f);
            Assert.That(replies.Posts.Count, Is.EqualTo(1));
            Assert.That(page.Map.Require<GameText>("mainValue").text, Is.EqualTo("19%"));
            Assert.That(page.Map.Require<GameText>("subValue2").text, Does.Contain("+5%"));
            Assert.That(page.Map.Require<GameText>("subValue2").richText, Is.True, "Result markup must render as an orange delta, never as literal text.");
            yield return Frame(page.CanvasRoot, "client-result-plus3");
            page.Click("back"); Assert.That(page.Back, Is.EqualTo(1));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator UncertainReplySurvivesClosingAndRetriesItsExactRequest() => ExerciseUncertainReply(500);

        [UnityTest, Timeout(60000)]
        public IEnumerator Http408PreservesTheExactRecipeAcrossWindows() => ExerciseUncertainReply(408);

        private static IEnumerator ExerciseUncertainReply(int status)
        {
            var fixture = Fixture(); using var replies = new Replies(fixture.Rules, fixture.After, status);
            using var life = new BindingScope(CancellationToken.None);
            using var api = new EquipmentInventoryApi(replies.Endpoint, _ => Task.FromResult("controlled-fixture"), life.Token);
            using (var first = new Page(api, fixture.Before, life.Token))
            {
                yield return Wait(() => first.Open.IsCompleted); first.Open.GetAwaiter().GetResult();
                first.Click("auto"); first.Click("auto3"); first.Click("submit");
                yield return Wait(() => first.Map.Require<GameText>("status").text.Contains("尚未确认"));
                Assert.That(first.Map.Require<GameButton>("auto").interactable, Is.False);
                Assert.That(api.GetPendingEnhancement(fixture.Before.items[0].instanceId), Is.Not.Null);
            }
            using var second = new Page(api, fixture.Before, life.Token);
            yield return Wait(() => second.Open.IsCompleted); second.Open.GetAwaiter().GetResult();
            Assert.That(second.Map.Require<GameText>("status").text, Does.Contain("尚未确认"));
            Assert.That(second.Map.Require<GameButton>("auto").interactable, Is.False);
            yield return Frame(second.CanvasRoot, "client-pending-reopen");
            second.Click("submit"); yield return Wait(() => second.Changes == 1);
            Assert.That(replies.Posts.Count, Is.EqualTo(2));
            Assert.That(replies.Posts.First(), Is.EqualTo(replies.Posts.Last()));
            Assert.That(api.GetPendingEnhancement(fixture.Before.items[0].instanceId), Is.Null);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ExplicitRejectionRestoresSelectionWithoutPlayingSuccess()
        {
            var fixture = Fixture(); using var replies = new Replies(fixture.Rules, fixture.After, 400);
            using var life = new BindingScope(CancellationToken.None);
            using var api = new EquipmentInventoryApi(replies.Endpoint, _ => Task.FromResult("controlled-fixture"), life.Token);
            using var page = new Page(api, fixture.Before, life.Token);
            yield return Wait(() => page.Open.IsCompleted); page.Open.GetAwaiter().GetResult();
            page.Click("auto"); page.Click("auto3"); page.Click("submit");
            yield return Wait(() => page.Map.Require<GameText>("status").text.Contains("暂时无法强化"));
            Assert.That(page.Changes, Is.Zero); Assert.That(page.Map.Require<Image>("success").color.a, Is.Zero);
            Assert.That(page.Map.Require<GameButton>("auto").interactable, Is.True);
            Assert.That(api.GetPendingEnhancement(fixture.Before.items[0].instanceId), Is.Null);
            page.Click("clear"); Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("0"));
        }

        [Serializable] private sealed class PrivateFixture
        {
            public string endpoint, bearer, instanceId;
            public EquipmentInventorySnapshot inventory;
        }
        [UnityTest, Timeout(60000)]
        public IEnumerator ActualServerReopensTheEnhancedItemWithoutAnotherMutation()
        {
            string fixturePath = Environment.GetEnvironmentVariable("ASTRAL_ENHANCEMENT_PRIVATE_FIXTURE");
            if (string.IsNullOrEmpty(fixturePath)) Assert.Ignore("Requires a disposable loopback equipment fixture.");
            var fixture = JsonUtility.FromJson<PrivateFixture>(System.IO.File.ReadAllText(fixturePath));
            using var life = new BindingScope(CancellationToken.None);
            using var api = new EquipmentInventoryApi(new Uri(fixture.endpoint), _ => Task.FromResult(fixture.bearer), life.Token);
            var read = api.FetchAsync(life.Token); yield return Wait(() => read.IsCompleted);
            var inventory = read.GetAwaiter().GetResult();
            var item = inventory.items.Single(row => row.instanceId == fixture.instanceId);
            if (item.enhanceLevel == 0) Assert.Ignore("The fresh fixture has not yet performed its first enhancement.");
            using var page = new Page(api, inventory, life.Token, item.instanceId);
            yield return Wait(() => page.Open.IsCompleted); page.Open.GetAwaiter().GetResult();
            Assert.That(page.Map.Require<BindingMap>("thumbnail").Require<GameText>("enhancement").text, Is.EqualTo("+" + item.enhanceLevel));
            Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("0"));
            Assert.That(page.Changes, Is.Zero);
            yield return Frame(page.CanvasRoot, "actual-server-reopened");
            page.Click("back"); Assert.That(page.Back, Is.EqualTo(1));
        }
        [UnityTest, Timeout(90000)]
        public IEnumerator ActualIsolatedServerEnhancesAnOwnedItemAndCapturesItsPresentation()
        {
            string fixturePath = Environment.GetEnvironmentVariable("ASTRAL_ENHANCEMENT_PRIVATE_FIXTURE");
            if (string.IsNullOrEmpty(fixturePath)) Assert.Ignore("Requires a disposable loopback server account and owned equipment.");
            var fixture = JsonUtility.FromJson<PrivateFixture>(System.IO.File.ReadAllText(fixturePath));
            Assert.That(new Uri(fixture.endpoint).IsLoopback, Is.True);
            using var life = new BindingScope(CancellationToken.None);
            using var api = new EquipmentInventoryApi(new Uri(fixture.endpoint), _ => Task.FromResult(fixture.bearer), life.Token);
            var read = api.FetchAsync(life.Token); yield return Wait(() => read.IsCompleted);
            var before = read.GetAwaiter().GetResult();
            var target = before.items.Single(row => row.instanceId == fixture.instanceId);
            if (target.enhanceLevel > 0) Assert.Ignore("The disposable equipment has already been enhanced; create a fresh fixture before repeating.");
            using var page = new Page(api, before, life.Token, target.instanceId);
            yield return Wait(() => page.Open.IsCompleted); page.Open.GetAwaiter().GetResult();
            yield return Frame(page.CanvasRoot, "actual-server-idle");
            page.Map.Require<BindingMap>("row0").Require<GameButton>("select").onClick.Invoke();
            page.Click("quantityConfirm");
            Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("1,600"));
            var uncommitted = api.FetchAsync(life.Token); yield return Wait(() => uncommitted.IsCompleted);
            Assert.That(uncommitted.Result.version, Is.EqualTo(before.version), "Registering ingredients is only a local preview.");
            yield return Frame(page.CanvasRoot, "actual-server-manual-powder");
            page.Click("clear"); page.Click("auto"); page.Click("auto3");
            long cost = long.Parse(page.Map.Require<GameText>("cost").text, System.Globalization.NumberStyles.AllowThousands);
            yield return Frame(page.CanvasRoot, "actual-server-auto3");
            int priorCaptureRate = Time.captureFramerate;
            var cues = new List<string>(); var seen = new HashSet<int>();
            try
            {
                Time.captureFramerate = 30;
                page.Click("submit"); yield return Wait(() => page.Changes == 1);
                float origin = Time.unscaledTime;
                for (int i = 0; i < 90; i++)
                {
                    foreach (var cue in Resources.FindObjectsOfTypeAll<UiAudioPlayback>())
                        if (seen.Add(cue.GetInstanceID())) cues.Add((Time.unscaledTime - origin).ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " " + cue.name);
                    yield return Frame(page.CanvasRoot, "actual-sequence-" + i.ToString("D3"));
                }
            }
            finally { Time.captureFramerate = priorCaptureRate; }
            var after = page.Changed.items.Single(row => row.instanceId == target.instanceId);
            Assert.That(page.Changed.version, Is.EqualTo(before.version + 1));
            Assert.That(after.enhanceLevel, Is.GreaterThanOrEqualTo(3));
            Assert.That(after.xp, Is.GreaterThanOrEqualTo(1969));
            Assert.That(page.Changed.materials.gold, Is.EqualTo(before.materials.gold - cost));
            Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("0"));
            yield return Frame(page.CanvasRoot, "actual-server-result");
            string folder = Environment.GetEnvironmentVariable("ASTRAL_ENHANCEMENT_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(folder)) System.IO.File.WriteAllLines(Path.Combine(folder, "actual-audio-cues.txt"), cues);
            page.Click("back"); Assert.That(page.Back, Is.EqualTo(1));
        }

        private static IEnumerator Wait(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "The controlled equipment page did not complete its operation.");
        }
        private static IEnumerator Frame(GameObject root, string name)
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_ENHANCEMENT_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder);
            var canvas = root.GetComponent<Canvas>();
            var camera = new GameObject("Equipment capture camera", typeof(Camera)).GetComponent<Camera>();
            bool sequence = name.StartsWith("actual-sequence-", StringComparison.Ordinal);
            int width = sequence ? 1478 : 2464, height = sequence ? 720 : 1200;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previous = RenderTexture.active;
            var frame = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.clearFlags = CameraClearFlags.SolidColor;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); yield return null; camera.Render();
                RenderTexture.active = target; frame.ReadPixels(new Rect(0, 0, width, height), 0, 0); frame.Apply();
                System.IO.File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.Destroy(frame); Object.Destroy(camera.gameObject);
            }
        }
    }
}
#endif
