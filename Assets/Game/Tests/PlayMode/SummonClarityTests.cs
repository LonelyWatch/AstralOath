#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Client.Summon;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    // A controlled state exercises the real prefab/Binder/cameras without writing an account or contacting a server.
    public sealed class SummonClarityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object =>
                Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Summon/Res/Prefab/SummonWindow.prefab"));
        }
        private sealed class Locale : ILocaleService
        {
            public string TextLocale => "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged { add { } remove { } }
            public void SelectText(string locale) { }
            public void SelectVoice(string locale) { }
            public string Resolve(string key) => key == "summon.wish.total" ? "愿望单（{0}/14）" : key;
        }
        private sealed class Page : IDisposable
        {
            internal readonly GameObject canvas;
            internal readonly BindingScope scope = new BindingScope(CancellationToken.None);
            internal readonly SummonWindow view;
            internal readonly SummonWindowBinder binder;
            internal readonly BindingMap map;
            private readonly SummonApi api;
            internal Page()
            {
                canvas = new GameObject("Summon clarity controlled page", typeof(RectTransform), typeof(Canvas));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var registry = new UIViewRegistry(); registry.Register<SummonWindow>();
                view = registry.CreateAsync<SummonWindow>(new Assets(), canvas.transform, scope).GetAwaiter().GetResult();
                map = view.Root.GetComponent<BindingMap>();
                foreach (var sound in view.Root.GetComponentsInChildren<AudioSource>(true)) sound.mute = true;
                var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
                api = new SummonApi(new Uri("http://127.0.0.1:1"), _ => Task.FromResult("unused"), scope.Token);
                binder = new SummonWindowBinder(view, api, art,
                    AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset"),
                    AssetDatabase.LoadAssetAtPath<SummonPresentation>("Assets/Game/Modules/Summon/Res/Presentation/SummonPresentation.asset"),
                    AssetDatabase.LoadAssetAtPath<SummonSkillLoop>("Assets/Game/Modules/Summon/Res/Presentation/IseriaSkillLoop/IseriaSkillLoop.asset"),
                    new Locale(), () => Task.CompletedTask);
                var ids = art.heroes.Take(7).Select(hero => hero.id).ToArray();
                binder.OpenAsync(scope, new SummonState
                {
                    banners = new[] { new SummonBanner { id = "covenant", title = "圣约召唤", usesWishlist = true, entries = Array.Empty<SummonEntry>() } },
                    wishHeroes = ids, wishArtifacts = Array.Empty<string>(), wishHeroCandidates = ids,
                    wishArtifactCandidates = Array.Empty<string>(), ownedWishIds = Array.Empty<string>()
                }).GetAwaiter().GetResult();
            }
            internal RawImage Image(int index) => map.Require<RawImage>("wishHero" + index);
            internal HeroRosterPortrait[] Portraits => (HeroRosterPortrait[])typeof(SummonWindowBinder).GetField("wishPortraits", Private).GetValue(binder);
            public void Dispose() { scope.Dispose(); api.Dispose(); Object.Destroy(canvas); }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SevenPortraitsMatchProjectedPixelsAndResizeFromNewCameraFrames()
        {
            using var page = new Page();
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (float scale in new[] { 1f, 2f, .8f })
            {
                page.view.Root.transform.localScale = Vector3.one * scale;
                foreach (var image in Enumerable.Range(0, 7).Select(page.Image))
                    if (image.texture is RenderTexture old) Clear(old, Color.magenta);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                for (int i = 0; i < 7; i++)
                {
                    var image = page.Image(i);
                    var surface = image.texture as RenderTexture;
                    Assert.That(surface && surface.IsCreated(), Is.True, "Seven-person page must own a live camera surface.");
                    var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
                    int projected = Mathf.RoundToInt(Mathf.Max(Vector3.Distance(corners[0], corners[1]), Vector3.Distance(corners[0], corners[3])));
                    int expected = Mathf.Clamp(Mathf.Max(480, projected), 1, Mathf.Min(4096, SystemInfo.maxTextureSize));
                    Assert.That(surface.width, Is.EqualTo(expected).Within(1), $"Portrait {i}: fixed 480-pixel surfaces upscale the source at scale {scale}.");
                    Assert.That(surface.height, Is.EqualTo(surface.width));
                    var pixels = Sample(surface);
                    Assert.That(pixels.Any(color => color.a > 20 && (color.r < 230 || color.g > 20 || color.b < 230)), Is.True,
                        "Resize must render the source skeleton; copying an old surface is not new detail.");
                }
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RosterIdleAdvancesOnlyWhileVisibleAndClosingReleasesEverySurface()
        {
            using var page = new Page();
            yield return null;
            Canvas.ForceUpdateCanvases();
            var portraits = page.Portraits;
            var camera = CameraFor(page.Image(0));
            var mesh = camera.transform.parent.GetComponentInChildren<MeshFilter>().sharedMesh;
            var before = mesh.vertices;
            yield return new WaitForSecondsRealtime(.25f);
            Canvas.ForceUpdateCanvases();
            Assert.That(mesh.vertices.SequenceEqual(before), Is.False, "The reference roster blinks and breathes; its idle must advance.");

            page.map.Require<GameObject>("mainPage").SetActive(false);
            yield return null;
            Canvas.ForceUpdateCanvases();
            var hidden = mesh.vertices;
            yield return new WaitForSecondsRealtime(.15f);
            Canvas.ForceUpdateCanvases();
            Assert.That(mesh.vertices.SequenceEqual(hidden), Is.True, "Hidden rosters must stop updating.");
            Assert.That(Enumerable.Range(0, 7).Select(i => CameraFor(page.Image(i))).All(item => !item.gameObject.activeInHierarchy), Is.True,
                "A hidden main page must not keep seven offscreen cameras drawing.");
            page.map.Require<GameObject>("mainPage").SetActive(true);
            yield return new WaitForSecondsRealtime(.15f);
            Canvas.ForceUpdateCanvases();
            Assert.That(mesh.vertices.SequenceEqual(hidden), Is.False, "Returning to the pool must resume its original idle.");
            var surfaces = Enumerable.Range(0, 7).Select(i => page.Image(i).texture as RenderTexture).ToArray();
            var cameras = Enumerable.Range(0, 7).Select(i => CameraFor(page.Image(i))).ToArray();
            page.scope.Dispose();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(surfaces.All(surface => !surface), Is.True, "Closing must release all seven render targets.");
            Assert.That(cameras.All(item => !item), Is.True, "Closing must release all seven cameras and stop its canvas callbacks.");
        }

        [UnityTest]
        public IEnumerator RosterNamesUseTheOriginalLocalTenDegreeSlant()
        {
            using var page = new Page();
            yield return null;
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 7; i++)
            {
                var label = page.map.Require<GameText>("wishHero" + i + "Name");
                label.ForceMeshUpdate();
                var glyph = label.textInfo.characterInfo.Take(label.textInfo.characterCount).First(item => item.isVisible);
                float shear = (glyph.topLeft.x - glyph.bottomLeft.x) / (glyph.topLeft.y - glyph.bottomLeft.y);
                Assert.That(shear, Is.EqualTo(.18f).Within(.01f), "The source uses a local 10-degree skew, not the shared font's 35% italic shear.");
                Assert.That(label.fontStyle.HasFlag(TMPro.FontStyles.Bold), Is.False);
                Assert.That(label.font.italicStyle, Is.EqualTo(35), "Do not alter the shared main font to fix only this roster.");
            }
        }

        [UnityTest]
        public IEnumerator OtherStaticPortraitsKeepTheirOriginalBudget()
        {
            var canvas = new GameObject("Unchanged static list budget", typeof(Canvas));
            var image = new GameObject("Static portrait", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.transform.SetParent(canvas.transform, false);
            image.rectTransform.sizeDelta = Vector2.one * 1200;
            var art = AssetDatabase.LoadAssetAtPath<HomeHeroArt>("Assets/Game/Modules/Home/Configs/HomeHeroArt.asset");
            try
            {
                using var portrait = new HeroRosterPortrait(image, 480, animated: false, positionOffset: 12500);
                Assert.That(portrait.Show(art.heroes[0]), Is.True);
                yield return null; portrait.Tick();
                Assert.That(image.texture.width, Is.EqualTo(480), "A large summon fix must not silently upscale all static list cards.");
            }
            finally { Object.Destroy(canvas); }
        }

        private static Camera CameraFor(RawImage image) => Resources.FindObjectsOfTypeAll<Camera>().Single(camera => camera.targetTexture == image.texture);
        private static void Clear(RenderTexture surface, Color color)
        {
            var prior = RenderTexture.active;
            try { RenderTexture.active = surface; GL.Clear(true, true, color); }
            finally { RenderTexture.active = prior; }
        }
        private static Color32[] Sample(Texture surface)
        {
            var prior = RenderTexture.active;
            var reduced = RenderTexture.GetTemporary(48, 48, 0);
            var texture = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(surface, reduced); RenderTexture.active = reduced;
                texture.ReadPixels(new Rect(0, 0, 48, 48), 0, 0); return texture.GetPixels32();
            }
            finally { RenderTexture.active = prior; RenderTexture.ReleaseTemporary(reduced); Object.Destroy(texture); }
        }
    }
}
#endif
