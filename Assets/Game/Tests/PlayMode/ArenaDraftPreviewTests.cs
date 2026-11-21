#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftPreviewTests
    {
        private sealed class DraftAssets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                string path = address switch
                {
                    ArenaDraft.Address => "Res/Prefab/ArenaDraft.prefab",
                    ArenaLabels.Address => "Configs/arena-zh-hans.json",
                    _ => throw new ArgumentException(address)
                };
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Arena/" + path));
            }
        }

        [Test]
        public void PrebanSortsByOriginalStarsAndPlaysTheSelectedPortraitStrike()
        {
            var canvas = new GameObject("Preban selection test", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            using var lifetime = new BindingScope(CancellationToken.None);
            using var windows = new WindowCoordinator(lifetime.Token);
            var assets = new DraftAssets();
            var registry = new UIViewRegistry();
            registry.Register<ArenaDraft>();
            var view = registry.CreateAsync<ArenaDraft>(assets, canvas.transform, lifetime).GetAwaiter().GetResult();
            var locale = ArenaLabels.LoadAsync(assets, lifetime, "zh-Hans", "ja").GetAwaiter().GetResult();
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            var binder = new ArenaDraftBinder(view, null, locale, art, windows, null, null, null);
            var bindings = view.Root.GetComponent<BindingMap>();
            var heroes = art.Heroes.OrderBy(hero => hero.BaseStars).Take(3)
                .Concat(art.Heroes.OrderByDescending(hero => hero.BaseStars).Take(3)).ToArray();
            ArenaDraftSeat Seat() => new ArenaDraftSeat { player = new ArenaPlayer { avatarId = heroes[0].Id, displayName = "test" },
                picks = Array.Empty<string>(), prebans = Array.Empty<string>(), formation = Array.Empty<string>() };
            var state = new ArenaDraftState { id = "preban-test", roomId = "room", version = 1, phase = "preban",
                mySide = "host", activeSide = "both", firstPick = "host", host = Seat(), guest = Seat(),
                eligibleHeroIds = heroes.Select(hero => hero.Id).ToArray(), prebanCount = 2 };
            try
            {
                windows.ShowPageAsync(page => binder.OpenAsync(state, page)).GetAwaiter().GetResult();
                var grid = bindings.Require<ScrollRect>("grid").content;
                var filters = bindings.Require<GameObject>("rosterFilters").GetComponent<BindingMap>();
                var expected = heroes.OrderByDescending(hero => hero.BaseStars).ThenBy(hero => hero.Id, StringComparer.Ordinal);
                Assert.That(grid.Cast<Transform>().Select(row => row.name), Is.EqualTo(expected.Select(hero => hero.Id)));
                bindings.Require<GameButton>("preSort").onClick.Invoke();
                Assert.That(filters.gameObject.activeInHierarchy, Is.True);
                filters.Require<GameButton>("sort_stars").onClick.Invoke();
                filters.Require<GameButton>("close").onClick.Invoke();
                Assert.That(grid.GetChild(0).name, Is.EqualTo(heroes[0].Id));
                Canvas.ForceUpdateCanvases();
                var choice = grid.GetChild(0).GetComponent<BindingMap>();
                choice.Require<GameButton>("button").onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                var effect = choice.Require<RawImage>("prebanEffect");
                Assert.That(effect.enabled && effect.texture, Is.True);
                Assert.That(grid.GetChild(1).GetComponent<BindingMap>().Require<RawImage>("prebanEffect").enabled, Is.False);
                var tick = typeof(ArenaDraftBinder).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
                for (int frame = 0; frame < 3; frame++) tick.Invoke(binder, new object[] { 1f / 30 });
                var camera = Resources.FindObjectsOfTypeAll<Camera>().Single(candidate => candidate.targetTexture == effect.texture);
                Assert.That(camera.transform.parent.GetComponentsInChildren<MeshRenderer>().Count(), Is.EqualTo(6),
                    "The stamp skeleton and all five emitters from the three CFX tracks must share the selected card's camera.");
                bool graphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
                if (graphics)
                {
                    var impact = ReadStrike(camera, "preban-impact");
                    Assert.That(impact.Count(pixel => pixel.r > 230 && pixel.g > 215 && pixel.b > 190),
                        Is.GreaterThan(500), "The initial impact must contain a visible white-hot center.");
                }
                for (int frame = 0; frame < 21; frame++) tick.Invoke(binder, new object[] { 1f / 30 });
                Assert.That(effect.enabled, Is.True, "The native intro must settle into its red loop.");
                if (graphics)
                {
                    var idle = ReadStrike(camera, "preban-loop");
                    Assert.That(idle.Count(pixel => pixel.r > pixel.g * 1.7f && pixel.r > 80 && pixel.a > 160),
                        Is.GreaterThan(500), "The settled stamp requires an opaque red slash across the portrait.");
                }
                Assert.That(bindings.Require<GameText>("preCount").text, Is.EqualTo("(1/2)"));
                choice.Require<GameButton>("button").onClick.Invoke();
                Assert.That(effect.enabled, Is.False);
                Assert.That(effect.texture, Is.Null);
                Assert.That(bindings.Require<GameText>("preCount").text, Is.EqualTo("(0/2)"));
            }
            finally
            {
                lifetime.Dispose();
                binder.Dispose();
                Object.DestroyImmediate(canvas);
            }
        }

        private static Color32[] ReadStrike(Camera camera, string name)
        {
            var target = camera.targetTexture;
            var prior = RenderTexture.active;
            var scan = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                scan.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                scan.Apply();
                var path = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, name + ".png"), scan.EncodeToPNG());
                }
                return scan.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prior;
                Object.DestroyImmediate(scan);
            }
        }

        [Test]
        public void DraftGuidesFollowPickOwnershipWithoutReplayingOnRefresh()
        {
            var canvas = new GameObject("Draft guide regression", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            using var lifetime = new BindingScope(CancellationToken.None);
            using var windows = new WindowCoordinator(lifetime.Token);
            var assets = new DraftAssets();
            var registry = new UIViewRegistry();
            registry.Register<ArenaDraft>();
            var view = registry.CreateAsync<ArenaDraft>(assets, canvas.transform, lifetime).GetAwaiter().GetResult();
            var labels = ArenaLabels.LoadAsync(assets, lifetime, "zh-Hans", "ja").GetAwaiter().GetResult();
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            var binder = new ArenaDraftBinder(view, null, labels, art, windows, null, null, null);
            var bindings = view.Root.GetComponent<BindingMap>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var update = typeof(ArenaDraftBinder).GetMethod("Adopt", flags);
            void Advance(ArenaDraftState next) => update.Invoke(binder, new object[] { next });
            void Tick(float dt) => typeof(ArenaDraftBinder).GetMethod("Tick", flags).Invoke(binder, new object[] { dt });
            object Guide(string side) => typeof(ArenaDraftBinder).GetField(side + "Guide", flags).GetValue(binder);
            ISkeletonPlayer Player(string side)
            {
                var guide = Guide(side);
                return (ISkeletonPlayer)guide.GetType().GetField("player", flags).GetValue(guide);
            }

            void CheckTrough(string side, RawImage surface)
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    TestContext.WriteLine("No graphics device available to sample the " + side + " guide.");
                    return;
                }
                var guide = Guide(side);
                Player(side).Seek(.666f);
                guide.GetType().GetMethod("Tick", flags).Invoke(guide, new object[] { .001f });
                AssertLitGuideCore(surface, side == "blue");
            }

            var blue = bindings.Require<RawImage>("blueGuide");
            var red = bindings.Require<RawImage>("redGuide");
            var pool = art.Heroes.Take(16).Select(hero => hero.Id).ToArray();
            ArenaDraftSeat Seat(string name, int count) => new ArenaDraftSeat {
                player = new ArenaPlayer { avatarId = pool[0], displayName = name },
                picks = pool.Skip(name == "我方" ? 0 : 5).Take(count).ToArray(),
                prebans = Array.Empty<string>(), formation = Array.Empty<string>()
            };
            ArenaDraftState State(int version, string side, int ownPicks = 0, int enemyPicks = 0, string phase = "pick") =>
                new ArenaDraftState { id = "guide-test", roomId = "room", version = version, phase = phase,
                    mySide = "host", activeSide = side, firstPick = "host", picksRemainingInWindow = 1,
                    host = Seat("我方", ownPicks), guest = Seat("对手", enemyPicks), eligibleHeroIds = pool, banProtection = true };
            try
            {
                binder.OpenAsync(State(1, "host"), lifetime).GetAwaiter().GetResult();
                Tick(.38f);
                Assert.That(blue.enabled && blue.texture && !red.enabled, Is.True, "Only the allied first-pick guide is lit.");
                CheckTrough("blue", blue);
                Advance(State(2, "guest", ownPicks: 1));
                Tick(.38f);
                Assert.That(red.enabled && red.texture && !blue.enabled, Is.True, "The guide must switch on handover.");
                Assert.That(bindings.Require<BindingMap>("blue0").Require<RawImage>("confirm").enabled, Is.True,
                    "The previous pick's card feedback can overlap the next side's turn guide.");
                CheckTrough("red", red);
                var previous = Player("red");
                float time = previous.Time;
                var preview = State(2, "guest", ownPicks: 1);
                preview.previewSequence = 1;
                preview.previewHeroId = pool[6];
                Advance(preview);
                Advance(State(3, "guest", ownPicks: 1, enemyPicks: 1));
                Assert.That(Player("red"), Is.SameAs(previous), "A preview or consecutive pick restarted the guide.");
                Assert.That(Player("red").Time, Is.EqualTo(time).Within(.001f));
                Advance(State(4, "host", ownPicks: 1, enemyPicks: 2));
                Assert.That(blue.enabled && !red.enabled, Is.True, "The next allied pick must return the light to blue.");
                Advance(State(5, "", ownPicks: 5, enemyPicks: 5, phase: "ban"));
                Assert.That(blue.enabled || red.enabled, Is.False, "Final ban must clear both pick guides.");
            }
            finally
            {
                lifetime.Dispose();
                binder.Dispose();
                Object.DestroyImmediate(canvas);
            }
        }

        [TestCase("blue2")]
        [TestCase("red2")]
        public void ProtectedSlotKeepsFullPortraitHeightAcrossDifferentSourceWidths(string slot)
        {
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Arena/Res/Prefab/ArenaDraft.prefab"));
            try
            {
                var card = instance.GetComponent<BindingMap>().Require<BindingMap>(slot);
                var portrait = card.Require<Image>("portrait");
                var rect = portrait.rectTransform;
                var originalEdge = rect.anchorMax.x;
                var originalHeight = rect.rect.height;
                Assert.That(originalHeight, Is.GreaterThan(0));

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var choice = typeof(ArenaDraftBinder).GetNestedType("Choice", BindingFlags.NonPublic);
                var item = Activator.CreateInstance(choice, flags, null,
                    new object[] { card, art.LevelDigits, true, true, false, false, false }, null);
                foreach (string id in new[] { "c1088", "c1001", "c1088" })
                {
                    var hero = art.Heroes.Single(entry => entry.Id == id);
                    choice.GetMethod("Set", flags).Invoke(item, new object[] { id, hero, true, true, true });
                    Assert.That(portrait.sprite, Is.SameAs(hero.Card));
                    Assert.That(rect.rect.width, Is.EqualTo(originalHeight * hero.Card.rect.width / hero.Card.rect.height)
                        .Within(.01f), id + " must use the native face width at the slot's full height.");
                    Assert.That(rect.anchorMax.x, Is.EqualTo(originalEdge));
                    Assert.That(rect.rect.height, Is.EqualTo(originalHeight).Within(.01f));
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertLitGuideCore(RawImage surface, bool blue)
        {
            var target = (RenderTexture)surface.texture;
            var camera = Resources.FindObjectsOfTypeAll<Camera>().Single(item => item.targetTexture == target);
            var previous = RenderTexture.active;
            var scan = new Texture2D(target.width, 1, TextureFormat.RGBA32, false);
            try
            {
                camera.Render();
                RenderTexture.active = target;
                scan.ReadPixels(new Rect(0, target.height / 2, target.width, 1), 0, 0);
                scan.Apply();
                // In the original footage even the dimmest phase retains a colored core over gold.
                // Sample the emitted light without the underlying frame or the hero portrait.
                bool lit = scan.GetPixels32().Any(pixel => blue
                    ? pixel.b >= 220 && pixel.g >= 100 && pixel.b - pixel.r >= 60
                    : pixel.r >= 220 && pixel.g >= 140 && pixel.b >= 80);
                Assert.That(lit, Is.True, "The " + (blue ? "blue" : "red") + " guide lost its bright core at the trough.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(scan);
            }
        }

        [UnityTest]
        public IEnumerator EveryRosterMiniatureKeepsItsFeetAnchoredWithoutClipping()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            var type = typeof(ArenaDraftArt).Assembly.GetType("AstralOath.Client.Arena.ArenaDraftPortrait", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Preview regression", typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta = new Vector2(240, 240);
            var image = new GameObject("Battle model", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            var surface = image.rectTransform;
            surface.SetParent(root.transform, false);
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = surface.offsetMax = Vector2.zero;
            var expectedFoot = root.transform.TransformPoint(new Vector3(0, -70f, 0));
            var preview = (IDisposable)Activator.CreateInstance(type, flags, null, new object[] { image, true }, null);
            try
            {
                foreach (var hero in art.Heroes)
                {
                    var source = typeof(ArenaDraftArt.Hero).GetMethod("MakeModel", flags).Invoke(hero, null);
                    Assert.That((bool)type.GetMethod("Show", flags).Invoke(preview, new[] { source, null, null }), Is.True, hero.Id);
                    var camera = (Camera)type.GetField("camera", flags).GetValue(preview);
                    var player = (ISkeletonPlayer)type.GetField("player", flags).GetValue(preview);
                    if (player.Animations.Any(clip => clip.Name == "idle"))
                        Assert.That(player.CurrentAnimation, Is.EqualTo("idle"), hero.Id + " formation pose");
                    Vector3 foot = camera.WorldToViewportPoint(player.Root.transform.position);
                    Assert.That(foot.x, Is.EqualTo(.5f).Within(.001f), hero.Id);
                    Assert.That(foot.y, Is.EqualTo(50f / 240f).Within(.001f), hero.Id);
                    Assert.That(surface.rect.width, Is.EqualTo(360f).Within(.1f), hero.Id + " display width");
                    Assert.That(surface.rect.height, Is.EqualTo(360f).Within(.1f), hero.Id + " display height");
                    var drawnFoot = surface.TransformPoint(surface.rect.min + Vector2.Scale(surface.rect.size, foot));
                    Assert.That(Vector3.Distance(drawnFoot, expectedFoot), Is.LessThan(.1f), hero.Id + " floor anchor");
                    for (int frame = 0; frame < 8; frame++)
                    {
                        type.GetMethod("Tick", flags).Invoke(preview, new object[] { .12f });
                        var bounds = player.Root.GetComponent<MeshFilter>().sharedMesh.bounds;
                        var min = camera.WorldToViewportPoint(player.Root.transform.TransformPoint(bounds.min));
                        var max = camera.WorldToViewportPoint(player.Root.transform.TransformPoint(bounds.max));
                        Assert.That(min.x, Is.GreaterThanOrEqualTo(0), hero.Id + " left crop");
                        Assert.That(min.y, Is.GreaterThanOrEqualTo(0), hero.Id + " feet crop");
                        Assert.That(max.x, Is.LessThanOrEqualTo(1), hero.Id + " right crop");
                        Assert.That(max.y, Is.LessThanOrEqualTo(1), hero.Id + " head crop");
                    }
                    yield return null;
                }
            }
            finally { preview.Dispose(); Object.DestroyImmediate(root); }
        }
    }
}
#endif
