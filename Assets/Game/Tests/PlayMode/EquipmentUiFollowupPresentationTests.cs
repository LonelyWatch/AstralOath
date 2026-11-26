#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    // Offline fixture captures of production prefabs and binders, not reconstructed UI.
    public sealed class EquipmentUiFollowupPresentationTests
    {
        private const string Home = "Assets/Game/Modules/Home/";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly int[] Main = { 1200, 1400, 1600, 1900, 2100, 2400, 2600, 2800, 3100, 3400, 3600, 3900, 4300, 4600, 5000, 6000 };
        private static readonly long[] Xp = { 0, 525, 1182, 1969, 3019, 4594, 6694, 9582, 13257, 17719, 23232, 30319, 39244, 50269, 64182, 82032 };

        [Serializable] private sealed class SweepSample { public float requestedSeconds; public int sourceFrame; public bool visible; }
        [Serializable] private sealed class Evidence
        {
            public string kind = "offline fixture / production prefabs and binders";
            public string network = "No requests; fixture snapshot and rules supplied in memory.";
            public string sweepClock = "Production Binder elapsed-time sampling, 0..1.1s in 1/30s steps; controlled phase rather than real-time network flow.";
            public string sourceAnimation = "itemupgrade_statup, original curves, 32 frames at30fps, 1.0333s";
            public string equipmentData = "Project training_critical_boots ID and description; Lv85 values and receipts are controlled visual fixture data, not that server template's reproduction.";
            public int[] sweepCropBottomLeft = { 160, 360, 520, 440 };
            public string[] fullFrames = { "0000", "0200", "0600", "1100" };
            public string[] viewports = { "1920x1080", "1400x1080" };
            public string boundary = "Fixture economy, characters and receipts do not replace real-server, original-account or Player acceptance.";
            public SweepSample[] samples;
        }

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

        private static object Field(object target, string key) => target.GetType().GetField(key, Hidden).GetValue(target);
        private static void Set(object target, string key, object value) => target.GetType().GetField(key, Hidden).SetValue(target, value);
        private static object Call(object target, string key, params object[] args) => target.GetType().GetMethod(key, Hidden).Invoke(target, args);
        private static Sprite Art(string key) => AssetDatabase.LoadAssetAtPath<Sprite>(Home + "Res/Textures/Inventory/Equipment/" + key + ".png");
        private static string Stat(EquipmentStatValue stat) => stat.kind == "attack" ? stat.value.ToString(CultureInfo.InvariantCulture)
            : (stat.value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        private static void PaintCard(EquipmentItemCardBinder card, EquipmentInventoryItem item) => card.Set(
            Art("icon_eq_boot_arena026"), Art("_itembg_equip_5"), Art("icon_set_cri"), Art("cm_icon_stat_max_hp"),
            null, item.itemLevel, item.enhanceLevel, "");

        private static EquipmentInventoryItem Item(int level, string id = "followup-current", string hero = null) => new EquipmentInventoryItem
        {
            instanceId = id, templateId = "training_critical_boots", slot = "boots", setId = "critical", grade = "epic", itemLevel = 85,
            equippedHeroId = hero, enhanceLevel = level, xp = level == 3 ? 2000 : level == 6 ? 6700 : Xp[level], enhancementSupported = true,
            mainStat = new EquipmentStatValue { kind = "healthPct", value = Main[level] },
            substats = new[] { new EquipmentStatValue { kind = "attack", value = 34 },
                new EquipmentStatValue { kind = "critChanceBp", value = 400 },
                new EquipmentStatValue { kind = "defensePct", value = level >= 3 ? 1000 : 500, enhancementCount = level >= 3 ? 1 : 0, enhancementCountKnown = true },
                new EquipmentStatValue { kind = "critDamageBp", value = level >= 6 ? 1200 : 700, enhancementCount = level >= 6 ? 1 : 0, enhancementCountKnown = true } }
        };

        private sealed class Capture : IDisposable
        {
            internal readonly GameObject Root;
            private readonly Canvas surface;
            private readonly Camera camera;
            private RenderTexture target;
            private readonly string directory = Environment.GetEnvironmentVariable("ASTRAL_ENHANCEMENT_CAPTURE_DIR");
            internal Capture()
            {
                Root = new GameObject("Offline equipment followup capture", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                surface = Root.GetComponent<Canvas>(); surface.sortingOrder = 30000;
                camera = new GameObject("Followup capture camera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.01f, .02f, .04f, 1);
                camera.cullingMask = 1 << 31; camera.enabled = false;
                surface.renderMode = RenderMode.ScreenSpaceCamera; surface.worldCamera = camera; surface.planeDistance = 1;
                var scaler = Root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
                Resize(1920);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            }
            internal void Resize(int width)
            {
                camera.targetTexture = null;
                if (target) RenderTexture.ReleaseTemporary(target);
                target = RenderTexture.GetTemporary(width, 1080, 24);
                camera.targetTexture = target;
            }
            internal GameObject Instantiate(string name) => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                Home + "Res/Prefab/" + name + ".prefab"), Root.transform, false);

            internal void Frame(string name, bool cropped = false)
            {
                foreach (var child in Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases(); camera.Render();
                var previous = RenderTexture.active;
                var region = cropped ? new Rect(160, 360, 520, 440) : new Rect(0, 0, target.width, target.height);
                var picture = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target; picture.ReadPixels(region, 0, 0); picture.Apply();
                    // A real camera/Canvas rendering must contain more than the clear color.
                    var pixels = picture.GetPixels32();
                    Assert.That(pixels.Any(pixel => Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) > 100), Is.True, name + " rendered blank");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        var path = Path.Combine(directory, name + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
                    }
                }
                finally { RenderTexture.active = previous; Object.Destroy(picture); }
            }

            internal void Manifest(string text)
            {
                if (!string.IsNullOrEmpty(directory)) System.IO.File.WriteAllText(Path.Combine(directory, "followup-evidence.json"), text);
            }

            internal Color32[] Pixels(Rect region)
            {
                var previous = RenderTexture.active;
                var picture = new Texture2D((int)region.width, (int)region.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target; picture.ReadPixels(region, 0, 0); picture.Apply();
                    return picture.GetPixels32();
                }
                finally { RenderTexture.active = previous; Object.Destroy(picture); }
            }

            internal Color32 Pixel(RectTransform rect, Vector2 normalized)
            {
                var local = new Vector3(rect.rect.xMin + normalized.x * rect.rect.width,
                    rect.rect.yMin + normalized.y * rect.rect.height, 0);
                var point = camera.WorldToScreenPoint(rect.TransformPoint(local));
                var previous = RenderTexture.active;
                var picture = new Texture2D(1, 1, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    picture.ReadPixels(new Rect(Mathf.Floor(point.x), Mathf.Floor(point.y), 1, 1), 0, 0); picture.Apply();
                    return picture.GetPixels32()[0];
                }
                finally { RenderTexture.active = previous; Object.Destroy(picture); }
            }

            public void Dispose()
            {
                camera.targetTexture = null;
                if (target) RenderTexture.ReleaseTemporary(target);
                Object.Destroy(camera.gameObject); Object.Destroy(Root);
            }
        }

        private sealed class Enhancement : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly BindingMap Map;
            private readonly BindingScope scope = new BindingScope(CancellationToken.None);
            private readonly object binder;
            internal Enhancement(Capture capture)
            {
                Root = capture.Instantiate("EquipmentEnhancement"); Map = Root.GetComponent<BindingMap>();
                var view = (EquipmentEnhancement)Activator.CreateInstance(typeof(EquipmentEnhancement), Hidden, null, new object[] { Map }, null);
                var type = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementBinder");
                var locale = new Labels();
                binder = Activator.CreateInstance(type, Hidden, null, new object[] { view, scope, locale, null,
                    (Action)(() => { }), (Action<EquipmentInventorySnapshot>)(_ => { }),
                    (Action<EquipmentItemCardBinder, EquipmentInventoryItem>)PaintCard,
                    (Func<EquipmentInventoryItem, string>)(_ => GameText.Format(locale, "home.gear.title", locale.Resolve("home.gear.boots"))),
                    (Func<EquipmentStatValue, Sprite>)(_ => Art("cm_icon_stat_max_hp")), (Func<EquipmentStatValue, string>)Stat,
                    (Func<EquipmentInventoryItem, string>)(_ => locale.Resolve("home.gear.epic") + locale.Resolve("home.gear.boots")),
                    (Func<EquipmentInventoryItem, string>)(_ => locale.Resolve("home.gear.criticalSet") + " (0/2)") }, null);
                var opening = Item(0);
                Set(binder, "target", opening);
                Set(binder, "profile", new EquipmentEnhancementProfile { supported = true, mainValues = Main, xpThresholds = Xp });
                var materials = new[] { ("ma_est1e", 100, 1600), ("ma_est2e", 1500, 14400), ("ma_est4e", 4500, 43200), ("ma_est6e", 13500, 129600) }
                    .Select(row => new EquipmentEnhancementMaterialRule { id = row.Item1, experience = row.Item2, goldPerUnit = row.Item3, slots = new[] { "boots" } }).ToArray();
                Set(binder, "materials", materials);
                Set(binder, "inventory", Snapshot(opening, materials));
                var display = typeof(EquipmentEnhancement).Assembly.GetType("AstralOath.Client.Home.EquipmentEnhancementResultDisplay");
                Set(binder, "resultDisplay", Activator.CreateInstance(display, Hidden, null, new object[] { opening }, null));
                view.Show(); Call(binder, "Refresh");
            }

            private static EquipmentInventorySnapshot Snapshot(EquipmentInventoryItem item, EquipmentEnhancementMaterialRule[] materials) =>
                new EquipmentInventorySnapshot { capacity = 500, used = 1, version = item.enhanceLevel + 1, items = new[] { item },
                    materials = new EquipmentEnhancementBalances { gold = 148608996,
                        items = materials.Select(row => new EquipmentEnhancementBalance { kind = row.id, quantity = 10000 }).ToArray() } };

            internal void Click(string key)
            {
                var button = Map.Require<GameButton>(key);
                Assert.That(button.IsInteractable() && button.gameObject.activeInHierarchy, Is.True, key);
                button.onClick.Invoke();
            }
            internal void Reveal(int level)
            {
                var item = Item(level);
                Set(binder, "presentedReceipt", Snapshot(item, (EquipmentEnhancementMaterialRule[])Field(binder, "materials")));
                string changed = level == 3 ? "defensePct" : "critDamageBp";
                Set(binder, "lastResult", new EquipmentEnhancementResult { changes = new[] { new EquipmentEnhancementChange
                    { kind = changed, before = level == 3 ? 500 : 700, after = level == 3 ? 1000 : 1200 } } });
                Set(binder, "animating", true); Set(binder, "startPlayed", true); Set(binder, "finishPlayed", true);
                Set(binder, "animationStart", Time.unscaledTime - 2.3f);
                Call(binder, "Animate");
            }
            internal void Pose(float elapsed)
            {
                Set(binder, "resultSweepStart", Time.unscaledTime - elapsed);
                Call(binder, "UpdateResultSweeps");
            }
            public void Dispose() { scope.Dispose(); Object.Destroy(Root); }
        }

        private sealed class Inventory : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly BindingMap Single;
            private readonly object binder;
            private readonly BindingScope scope = new BindingScope(CancellationToken.None);
            private readonly EquipmentInventoryApi api;
            internal Inventory(Capture capture)
            {
                Root = capture.Instantiate("HeroInventory");
                // Keep the fixture focused on the production equipment page and its overlays.
                foreach (Transform child in Root.transform) child.gameObject.SetActive(false);
                var map = Root.GetComponent<BindingMap>();
                Single = map.Require<BindingMap>("gearBrowser").Require<BindingMap>("singleSlot");
                var hero = new HeroInventoryItem { instanceId = "followup-hero", templateId = "c1001", level = 50, stars = 5 };
                var snapshot = new EquipmentInventorySnapshot { capacity = 500, used = 2, version = 1,
                    items = new[] { Item(3, hero: hero.instanceId), Item(15, "followup-candidate") }, materials = new EquipmentEnhancementBalances() };
                // No request can pass authentication; LoadAsync receives an already available fixture snapshot.
                api = new EquipmentInventoryApi(new Uri("http://127.0.0.1:1/"),
                    _ => throw new InvalidOperationException("Network is forbidden in this offline Canvas fixture."), scope.Token);
                var heroArt = AssetDatabase.LoadAssetAtPath<HomeHeroArt>(Home + "Configs/HomeHeroArt.asset").heroes.First(row => row.id == "c1001");
                var type = typeof(HeroInventoryBinder).Assembly.GetType("AstralOath.Client.Home.EquipmentInventoryBinder");
                binder = Activator.CreateInstance(type, Hidden, null, new object[] { map, new Labels(), api,
                    (Func<CancellationToken, Task<EquipmentInventorySnapshot>>)(_ => Task.FromResult(snapshot)),
                    (Func<HeroInventoryItem[]>)(() => new[] { hero }), (Func<HeroInventoryItem>)(() => hero),
                    (Func<string, string>)(_ => heroArt.name), (Func<HeroInventoryItem, BattleStats>)(_ => new BattleStats(100, 1000, 100, 100)),
                    (Func<string, (int attack, int health)>)(_ => (0, 0)), (Func<string, Sprite>)(_ => heroArt.portrait), (Action)(() => { }) }, null);
                Call(binder, "Bind", scope);
                ((Task)Call(binder, "LoadAsync", snapshot, scope.Token)).GetAwaiter().GetResult();
                Call(binder, "Show", true, "boots");
            }
            internal void Compare()
            {
                Single.Require<GameButton>("change").onClick.Invoke();
                var row = Single.Require<ScrollRect>("candidates").content.GetComponentsInChildren<BindingMap>(true)
                    .First(map => map.gameObject.name == "followup-candidate");
                row.Require<GameButton>("select").onClick.Invoke();
            }
            internal void Worn() => Call(binder, "Show", true, "boots");
            public void Dispose() { scope.Dispose(); api.Dispose(); Object.Destroy(Root); }
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator AutomaticRegistrationOccludesBrightContentAcrossItsWholeBody()
        {
            using var capture = new Capture();
            using var page = new Enhancement(capture);
            page.Click("auto");
            var panel = (RectTransform)page.Map.Require<GameObject>("autoPopup").transform;
            // A bright test probe behind the actual production panel isolates its coverage.
            // This is an occlusion regression fixture, not a replacement panel or gameplay reference.
            var probe = new GameObject("Automatic menu occlusion probe", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)probe.transform;
            rect.SetParent(panel.parent, false); rect.anchorMin = panel.anchorMin; rect.anchorMax = panel.anchorMax;
            rect.pivot = panel.pivot; rect.sizeDelta = panel.sizeDelta; rect.anchoredPosition = panel.anchoredPosition;
            rect.SetSiblingIndex(panel.GetSiblingIndex());
            probe.GetComponent<Image>().color = Color.magenta; probe.GetComponent<Image>().raycastTarget = false;
            yield return null;
            capture.Frame("followup-auto-occlusion-probe");
            // Stay inside the native gold trim and outside the stage button outlines.
            foreach (float x in new[] { .035f, .965f })
                foreach (float y in new[] { .22f, .45f, .72f })
                {
                    var pixel = capture.Pixel(panel, new Vector2(x, y));
                    Assert.That(Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)), Is.LessThanOrEqualTo(10),
                        $"Opaque black menu body must hide underlying content at ({x},{y}); got RGB({pixel.r},{pixel.g},{pixel.b}).");
                }
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator AutomaticRegistrationSelectedStageRetainsTheOriginalGoldBrownFill()
        {
            using var capture = new Capture();
            using var page = new Enhancement(capture);
            page.Click("auto"); yield return null;
            capture.Frame("followup-auto-selected-color");
            var selected = page.Map.Require<Image>("auto3Selected");
            Assert.That(selected.enabled, Is.True);
            var pixel = capture.Pixel(selected.rectTransform, new Vector2(.2f, .25f));
            // Original 74s crop flat selected pixels are RGB112,80,38. Avoid text and corner antialiasing.
            Assert.That(pixel.r, Is.InRange(104, 124), "Selected fill was darkened by a second RGB linear conversion.");
            Assert.That(pixel.g, Is.InRange(72, 90));
            Assert.That(pixel.b, Is.InRange(28, 46));
        }

        [Test]
        public void FixtureEquipmentUsesAnExistingDescriptionKey()
        {
            string key = "home.gear.description." + Item(0).templateId;
            Assert.That(new Labels().Resolve(key), Does.Not.StartWith("home.gear.description."),
                "A missing fixture description must not render its raw key in the acceptance screenshots.");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator ResultSweepBrightFlameMatchesTheOriginal1080Projection()
        {
            using var capture = new Capture();
            using var page = new Enhancement(capture);
            // Ensure the controlled source phase can be represented by the Binder's nonnegative start time.
            yield return new WaitForSecondsRealtime(.25f);
            page.Reveal(3); page.Reveal(6);
            page.Pose(1.1f); capture.Frame("followup-sweep-projection-baseline");
            // Bounds use bottom-left pixels in the actual 1920x1080 production Canvas.
            var region = new Rect(340, 490, 330, 140);
            var baseline = capture.Pixels(region);
            page.Pose(.205f);
            // Isolate the changed substat from the separate score sweep; do not redraw either control.
            page.Map.Require<RawImage>("scoreSweep").enabled = false;
            capture.Frame("followup-sweep-projection-0205");
            var active = capture.Pixels(region);
            int left = 330, right = -1, bottom = 140, top = -1;
            for (int y = 0; y < 140; y++)
                for (int x = 0; x < 330; x++)
                {
                    int index = y * 330 + x;
                    int energy = Math.Max(active[index].g - baseline[index].g, active[index].b - baseline[index].b);
                    if (energy < 50) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
            // Original92.309/92.350s upper bright bounds normalize to216x93.6 /220.5x95.4.
            // Allow screenshot compression and gamma blending around glyphs; old projection was~62x27.
            Assert.That(right - left + 1, Is.InRange(190, 245), "Source flame is projected too small or too wide on the actual Canvas.");
            Assert.That(top - bottom + 1, Is.InRange(78, 110), "Keep the original flame height and uniform source proportions.");
            float centerX = region.x + (left + right + 1) * .5f;
            Assert.That(centerX, Is.InRange(505, 540), "Source root should sit slightly left of the current+delta text visual center.");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ProductionControlsResultsAndFixedDetailsRenderWithOfflineData()
        {
            using var capture = new Capture();
            using (var inventory = new Inventory(capture))
            {
                inventory.Compare(); yield return null;
                foreach (var key in new[] { "currentEnhance", "candidateEnhance", "equip" })
                    Assert.That(inventory.Single.Require<GameButton>(key).gameObject.activeInHierarchy, Is.True);
                Assert.That(inventory.Single.Require<BindingMap>("current").Require<BindingMap>("card").Require<GameText>("enhancement").text, Is.EqualTo("+3"));
                Assert.That(inventory.Single.Require<BindingMap>("candidate").Require<BindingMap>("card").Require<GameText>("enhancement").text, Is.EqualTo("+15"));
                capture.Frame("followup-controls-plus3-plus15");
                inventory.Worn(); yield return null;
                var panel = (RectTransform)inventory.Single.Require<GameObject>("popup").transform.Find("Detail safe area/Equipment details");
                Assert.That(panel.rect.size, Is.EqualTo(new Vector2(585, 900)));
                capture.Frame("followup-detail-wide");
                capture.Resize(1400); yield return null;
                Assert.That(panel.rect.size, Is.EqualTo(new Vector2(585, 900)));
                capture.Frame("followup-detail-narrow");
                inventory.Root.SetActive(false);
            }
            yield return null; capture.Resize(1920); yield return null;
            using (var page = new Enhancement(capture))
            {
                page.Click("auto"); page.Click("auto3"); yield return null;
                Assert.That(page.Map.Require<GameText>("cost").text, Is.EqualTo("22,400"));
                capture.Frame("followup-enhancement-green-cost");
                page.Click("auto"); page.Click("powderOnly"); yield return null;
                Assert.That(page.Map.Require<Image>("powderOnlyCheck").enabled, Is.True);
                foreach (int stage in new[] { 3, 6, 9, 12, 15 })
                    Assert.That(page.Map.Require<GameButton>("auto" + stage).gameObject.activeInHierarchy, Is.True);
                capture.Frame("followup-auto-checked"); page.Click("auto");
                page.Reveal(3);
                Assert.That(page.Map.Require<GameText>("mainValue").text, Is.EqualTo("19%"));
                var samples = new List<SweepSample>();
                for (int frame = 0; frame <= 33; frame++)
                {
                    yield return null;
                    float elapsed = frame / 30f;
                    page.Pose(elapsed);
                    capture.Frame("followup-sweep/f" + frame.ToString("D3"), cropped: true);
                    var sweep = page.Map.Require<RawImage>("subSweep2");
                    int sourceFrame = Mathf.RoundToInt((1 - sweep.uvRect.y) * 4 - 1) * 8 + Mathf.RoundToInt(sweep.uvRect.x * 8);
                    samples.Add(new SweepSample { requestedSeconds = elapsed, sourceFrame = sweep.enabled ? sourceFrame : -1, visible = sweep.enabled });
                    if (frame == 0 || frame == 6 || frame == 18 || frame == 33)
                        capture.Frame("followup-result-" + Mathf.RoundToInt(elapsed * 1000).ToString("D4"));
                    if (frame == 6 || frame == 18) Assert.That(page.Map.Require<RawImage>("subSweep2").enabled, Is.True);
                }
                Assert.That(page.Map.Require<RawImage>("subSweep2").enabled, Is.False);
                Assert.That(page.Map.Require<RawImage>("scoreSweep").enabled, Is.False);
                page.Reveal(6); yield return null; page.Pose(.2f);
                Assert.That(page.Map.Require<GameText>("mainValue").text, Is.EqualTo("26%"));
                Assert.That(page.Map.Require<GameText>("subValue2").text, Does.Contain("(+5%)"));
                Assert.That(page.Map.Require<GameText>("score").text, Does.Contain("(+16)"));
                capture.Frame("followup-result-plus6");
                capture.Manifest(JsonUtility.ToJson(new Evidence { samples = samples.ToArray() }, true));
            }
            yield return null;
        }
    }
}
#endif
