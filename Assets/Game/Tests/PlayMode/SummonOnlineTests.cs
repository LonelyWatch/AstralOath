#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class SummonOnlineTests
    {
        private const string HomePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private Scene home, priorScene;
        private string priorEndpoint, priorSkipEntry;
        private bool changedSettings;

        [UnityTest, Timeout(165000)]
        public IEnumerator MoreOpensNativeSummonAndSkipShowsRecoverableTenResult()
        {
            string endpoint = Environment.GetEnvironmentVariable("ASTRAL_SUMMON_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Set ASTRAL_SUMMON_TEST_URL to a local Development service with migrations 011 and 012.");
            Assert.That(new Uri(endpoint).IsLoopback, Is.True);

            priorEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            priorSkipEntry = Environment.GetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY");
            changedSettings = true;
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", "1");
            priorScene = SceneManager.GetActiveScene();
            home = EditorSceneManager.LoadSceneInPlayMode(HomePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Until(() => home.IsValid() && home.isLoaded && FindWindow("HomeWindow"), "Home did not load.");
            Assert.That(SceneManager.SetActiveScene(home), Is.True);

            Click(FindWindow("HomeWindow"), "more");
            yield return Until(() => FindWindow("HomeMoreMenu"), "More did not open.");
            yield return new WaitForSecondsRealtime(.20f);
            yield return Capture("home-more");
            Click(FindWindow("HomeMoreMenu"), "summon");
            yield return Until(() =>
            {
                var current = FindWindow("SummonWindow");
                return current && current.Require<GameText>("balance").text.Contains("60");
            }, "The real server did not initialize the developer's 60 bookmarks.");

            Assert.That(FindWindow("HomeMoreMenu"), Is.Null);
            var summon = FindWindow("SummonWindow");
            foreach (string key in new[] { "single", "ten", "free", "resultClose", "resultAgain" })
            {
                var button = summon.Require<GameButton>(key);
                var face = (Image)button.targetGraphic;
                Assert.That(face.type, Is.EqualTo(Image.Type.Sliced), key);
                Assert.That(face.sprite.border, Is.Not.EqualTo(Vector4.zero), key);
                Assert.That(button.spriteState.pressedSprite, Is.Not.Null, key);
                Assert.That(button.spriteState.pressedSprite.border, Is.EqualTo(face.sprite.border), key);
            }
            Assert.That(summon.Require<GameButton>("single").transform.Find("Price badge").GetComponent<Image>().type,
                Is.EqualTo(Image.Type.Sliced));
            Assert.That(summon.Require<GameButton>("resultAgain").transform.Find("Native repeat price plate").GetComponent<Image>().type,
                Is.EqualTo(Image.Type.Sliced));
            var preview = summon.Require<RawImage>("featuredSkillCanvas");
            if (summon.Require<GameButton>("iseria").interactable)
            {
                Assert.That(summon.Require<GameText>("bannerTitle").text, Is.EqualTo("召唤几率UP"));
                Assert.That(summon.Require<GameText>("featuredHero").text, Does.Contain("伊赛丽亚"));
                Assert.That(summon.Require<GameText>("pity").text, Does.Contain("120"));
                Assert.That(summon.Require<Image>("artifactArt").sprite, Is.Not.Null);
                Assert.That(summon.Require<GameText>("eventCountdown").text, Is.Not.Empty);
                yield return Capture("summon-iseria-up");
                yield return Until(() => preview.enabled && preview.texture, "The UP pool did not start the real Iseria S3 preview.");
                yield return new WaitForSecondsRealtime(3.15f);
                Assert.That(summon.Require<GameText>("featuredHero").GetComponentInParent<CanvasGroup>().alpha, Is.LessThan(.05f));
                Assert.That(summon.Require<Image>("artifactArt").GetComponentInParent<CanvasGroup>().alpha, Is.LessThan(.05f));
                Assert.That(preview.rectTransform.rect.width, Is.EqualTo(((RectTransform)summon.transform).rect.width).Within(1));
                yield return Capture("summon-iseria-s3-cutin");
                Click(summon, "ten");
                Assert.That(Panel(summon, "confirmPanel"), Is.True);
                var portrait = summon.Require<Image>("confirmHeroPortrait");
                Assert.That(portrait.sprite.name, Is.EqualTo("c1024_s"));
                yield return Capture("summon-featured-confirm");
                var pausedFrame = preview.uvRect;
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(preview.uvRect, Is.EqualTo(pausedFrame), "The running skill preview should pause while confirming.");
                Click(summon, "confirmNo");
                yield return Until(() => preview.uvRect != pausedFrame, "The skill preview did not resume.");
                var heroLayer = summon.Require<GameText>("featuredHero").GetComponentInParent<CanvasGroup>();
                var artifactLayer = summon.Require<Image>("artifactArt").GetComponentInParent<CanvasGroup>();
                yield return Until(() => preview.enabled && heroLayer.alpha > .8f && artifactLayer.alpha < .05f,
                    "The hero did not return over the skill background before the artifact.");
                yield return Capture("summon-iseria-return");
                yield return Until(() => !preview.enabled && artifactLayer.alpha > .98f, "The poster did not return.");
            }
            else
            {
                Assert.That(summon.Require<GameObject>("eventTimerRow").activeInHierarchy, Is.False);
                Assert.That(summon.Require<GameText>("bannerTitle").text, Does.Contain("誓约"));
            }
            Click(summon, "covenant");
            Assert.That(preview.enabled, Is.False);
            Assert.That(summon.Require<GameText>("bannerTitle").text, Does.Contain("誓约"));
            Assert.That(summon.Require<GameButton>("free").IsInteractable(), Is.True);
            SummonCardLayoutChecks.Verify(summon, (RectTransform)summon.Require<GameObject>("wishHero0Card").transform);
            foreach (var key in new[] { "info", "storage", "settings" })
            {
                var control = summon.Require<GameButton>(key);
                var icon = (RectTransform)control.transform.Find("Native " + key + " icon");
                var rest = icon.anchoredPosition;
                var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                control.OnPointerEnter(pointer);
                control.OnPointerDown(pointer);
                Assert.That(control.image.overrideSprite, Is.EqualTo(control.spriteState.pressedSprite));
                Assert.That(icon.anchoredPosition.y, Is.LessThan(rest.y));
                control.OnPointerUp(pointer);
                control.OnPointerExit(pointer);
                Assert.That(icon.anchoredPosition, Is.EqualTo(rest));
            }
            yield return Capture("summon-covenant");
            yield return Capture("summon-covenant-4x3", 960, 720);
            yield return Capture("summon-covenant-21x9", 1680, 720);
            Click(summon, "info");
            Assert.That(Panel(summon, "infoNotes"), Is.True);
            Assert.That(Panel(summon, "infoRates"), Is.False);
            yield return Capture("summon-probability-notes");
            Click(summon, "infoRatesTab");
            Assert.That(Panel(summon, "infoRates"), Is.True);
            foreach (var column in new[] { "infoHeroColumn", "infoArtifactColumn" })
            {
                var rows = Rows(summon, column);
                Assert.That(rows.Length, Is.GreaterThan(3));
                Assert.That(rows.Any(row => row.Require<GameText>("detail").text.EndsWith("%")), Is.True);
            }
            Assert.That(summon.Require<GameText>("infoHeroTotal").text + summon.Require<GameText>("infoArtifactTotal").text,
                Does.Contain("%"));
            yield return Capture("summon-probability");
            Click(summon, "infoClose");
            Click(summon, "wish");
            Assert.That(Rows(summon, "wishSelectedItems").Length, Is.EqualTo(14));
            Assert.That(summon.Require<RectTransform>("wishItems").GetComponent<GridLayoutGroup>().constraintCount, Is.EqualTo(5));
            Click(summon, "wishArtifact");
            Assert.That(Rows(summon, "wishItems").Length, Is.GreaterThan(7));
            Assert.That(Rows(summon, "wishSelectedHeroes").Length, Is.EqualTo(7));
            Assert.That(Rows(summon, "wishSelectedArtifacts").Length, Is.EqualTo(7));
            SummonCardLayoutChecks.Verify(summon, (RectTransform)Rows(summon, "wishSelectedItems")[0].transform);
            Assert.That(summon.Require<GameObject>("wishArtifactHighlight").activeSelf, Is.True);
            Assert.That(summon.Require<GameObject>("wishHeroHighlight").activeSelf, Is.False);
            Assert.That(summon.Require<GameObject>("wishFilters").activeSelf, Is.False);
            Assert.That(Rows(summon, "wishSelectedHeroes")[0].Require<GameObject>("remove").activeSelf, Is.False);
            Assert.That(Rows(summon, "wishSelectedArtifacts")[0].Require<GameObject>("remove").activeSelf, Is.True);
            yield return Capture("summon-wishlist");
            Click(summon, "wishFilter");
            Click(summon, "wishRole_knight");
            Assert.That(Rows(summon, "wishItems").Length, Is.LessThan(8));
            Click(summon, "wishFilter");
            Click(summon, "wishRole_all");
            Click(Rows(summon, "wishSelectedHeroes")[0], "button");
            Assert.That(Rows(summon, "wishSelectedHeroes").Length, Is.EqualTo(6));
            Assert.That(Rows(summon, "wishSelectedArtifacts").Length, Is.EqualTo(7));
            Assert.That(summon.Require<GameButton>("wishSave").IsInteractable(), Is.False);
            Click(summon, "wishRecommend");
            Assert.That(Rows(summon, "wishSelectedHeroes").Length, Is.EqualTo(7));
            Click(summon, "wishClose");
            Click(summon, "wish");
            Assert.That(Rows(summon, "wishSelectedItems").Length, Is.EqualTo(14));
            Click(summon, "wishClose");

            Click(summon, "ten");
            Assert.That(Panel(summon, "confirmPanel"), Is.True);
            Assert.That(summon.Require<GameText>("confirmCost").text, Is.EqualTo("50"));
            yield return Capture("summon-ten-confirm");
            Click(summon, "confirmNo");
            Assert.That(summon.Require<GameText>("balance").text, Does.Contain("60"));
            Click(summon, "ten");
            Click(summon, "confirmYes");
            yield return Until(() =>
            {
                RawImage effects = summon.Require<RawImage>("effectCanvas");
                return Panel(summon, "revealPanel") && effects.enabled && effects.texture;
            }, "The native summon scene did not render for the authoritative receipt.");
            yield return new WaitForSecondsRealtime(.65f);
            yield return Capture("summon-native-opening");
            Click(summon, "skip");
            yield return Until(() => Panel(summon, "resultPanel"), "Skip did not open the authoritative summary.");
            var result = Rows(summon, "resultItems");
            Assert.That(result.Length, Is.EqualTo(10));
            Assert.That(summon.Require<GameText>("balance").text, Does.Contain("10"));
            Assert.That(summon.Require<GameButton>("free").IsInteractable(), Is.True);
            Assert.That(result.All(row => row.Require<Image>("icon").sprite), Is.True);
            string[] labels = result.Select(row => row.Require<GameText>("name").text).ToArray();
            SummonCardLayoutChecks.Verify(summon, (RectTransform)result[0].transform);
            var bannerPage = summon.Require<GameObject>("mainPage");
            var tenButton = summon.Require<GameButton>("ten").gameObject;
            Assert.That(tenButton.transform.IsChildOf(bannerPage.transform), Is.True);
            Assert.That(tenButton.activeInHierarchy, Is.False, "The banner must not draw behind the result page.");
            Click(summon, "resultAgain");
            Assert.That(Panel(summon, "confirmPanel") && Panel(summon, "resultPanel"), Is.True);
            Assert.That(bannerPage.activeInHierarchy, Is.False, "A repeat quote belongs to the results.");
            Click(summon, "confirmNo");
            Assert.That(Panel(summon, "resultPanel"), Is.True);
            Assert.That(tenButton.activeInHierarchy, Is.False);
            yield return VerifyResultResize(summon);
            yield return Capture("summon-ten-results");
            yield return Capture("summon-ten-results-4x3", 960, 720);
            yield return Capture("summon-ten-results-21x9", 1680, 720);

            Click(result[0], "button");
            Assert.That(Panel(summon, "revealPanel"), Is.True);
            Assert.That(summon.Require<GameText>("revealName").text, Is.EqualTo(labels[0]));
            Click(summon, "next");
            var heroResult = result.FirstOrDefault(row => !row.Require<GameObject>("artifact").activeSelf);
            if (heroResult != null)
            {
                Click(heroResult, "button");
                Assert.That(summon.Require<GameText>("revealLabel").text, Is.Not.Empty);
                Assert.That(summon.Require<GameText>("revealCV").text, Does.StartWith("CV:"));
                Assert.That(Panel(summon, "revealSpeech"), Is.False);
                Canvas.ForceUpdateCanvases();
                foreach (var key in new[] { "revealElement", "revealRole", "revealZodiac" })
                {
                    var icon = summon.Require<Image>(key);
                    Assert.That(icon.sprite, Is.Not.Null, key);
                    Assert.That(icon.rectTransform.rect.width, Is.GreaterThan(15f), key);
                    Assert.That(summon.Require<GameText>(key + "Text").rectTransform.rect.width, Is.GreaterThan(18f), key);
                }
                yield return Capture("summon-hero-reveal");
                Click(summon, "revealVoice");
                Assert.That(Panel(summon, "revealSpeech"), Is.True);
                Assert.That(summon.Require<GameText>("revealSpeaker").text, Is.EqualTo(summon.Require<GameText>("revealName").text));
                Assert.That(summon.Require<GameText>("revealQuote").text, Is.Not.Empty);
                yield return Capture("summon-hero-acquisition");
                Click(summon, "revealDismiss");
                Assert.That(Panel(summon, "revealSpeech"), Is.False);
                Click(summon, "next");
            }
            Click(summon, "resultClose");
            Assert.That(tenButton.activeInHierarchy, Is.True, "Back must restore the banner.");
            Click(summon, "settings");
            Assert.That(Panel(summon, "settingsPanel"), Is.True);
            Click(summon, "recovery");
            yield return Until(() => Panel(summon, "resultPanel"), "The stored receipt did not reopen.");
            Assert.That(Rows(summon, "resultItems").Select(row => row.Require<GameText>("name").text), Is.EqualTo(labels));
            Assert.That(summon.Require<GameText>("balance").text, Does.Contain("10"));
            Click(summon, "resultClose");
            Click(summon, "back");
            yield return Until(() => FindWindow("HomeWindow") && !FindWindow("SummonWindow"), "Back did not restore Home.");
        }

        private static IEnumerator VerifyResultResize(BindingMap summon)
        {
            var root = (RectTransform)summon.transform;
            var output = summon.Require<RawImage>("effectCanvas");
            var banner = summon.Require<GameButton>("ten").gameObject;
            var original = root.sizeDelta;
            var sample = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
                ? null : new Texture2D(1, 1, TextureFormat.RGBA32, false);
            int resized = 0;
            try
            {
                foreach (int width in new[] { 1430, 1560, 1670, 1720, 1840, 1760, 1520, 1400 })
                {
                    var prior = output.texture;
                    root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(banner.activeInHierarchy, Is.False, "The banner was visible during a resize.");
                    Assert.That(summon.Require<GameObject>("resultPanel").activeInHierarchy, Is.True);
                    var frame = (RenderTexture)output.texture;
                    if (frame == prior) continue;
                    resized++;
                    if (!sample) continue;
                    var active = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = frame;
                        sample.ReadPixels(new Rect(frame.width / 2, frame.height / 2, 1, 1), 0, 0);
                        sample.Apply();
                        Color center = sample.GetPixel(0, 0);
                        Assert.That(center.a, Is.GreaterThan(.5f), "A transparent render target reached the results UI.");
                        Assert.That(Mathf.Max(center.r, center.g, center.b), Is.GreaterThan(.02f),
                            "The resized texture has no image from the summon scene.");
                    }
                    finally { RenderTexture.active = active; }
                }
                Assert.That(resized, Is.GreaterThan(0), "The resize test never changed the camera target.");
            }
            finally
            {
                root.sizeDelta = original;
                Canvas.ForceUpdateCanvases();
                if (sample) UnityEngine.Object.Destroy(sample);
            }
        }

        private static BindingMap[] Rows(BindingMap parent, string key) =>
            parent.Require<RectTransform>(key).GetComponentsInChildren<BindingMap>();

        private BindingMap FindWindow(string name) =>
            !home.IsValid() || !home.isLoaded ? null : home.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BindingMap>())
                .FirstOrDefault(map => map.gameObject.name == name || map.gameObject.name == name + "(Clone)");

        private static bool Panel(BindingMap window, string key) => window.Require<GameObject>(key).activeInHierarchy;

        private static void Click(BindingMap window, string key)
        {
            var button = window.Require<GameButton>(key);
            Assert.That(button.gameObject.activeInHierarchy && button.IsInteractable(), Is.True, key);
            button.onClick.Invoke();
        }

        private static IEnumerator Until(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private IEnumerator Capture(string name, int width = 1478, int height = 720)
        {
            string path = Environment.GetEnvironmentVariable("ASTRAL_SUMMON_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(path)) yield break;
            Directory.CreateDirectory(path);
            var roots = home.GetRootGameObjects();
            var canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).First(item => item.isRootCanvas);
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
                .First(item => item.enabled && item.targetTexture == null);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
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
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(path, name + ".png"), pixels.EncodeToPNG());
                if (name.StartsWith("summon-ten-results", StringComparison.Ordinal) || name == "summon-hero-reveal")
                {
                    // These uncovered positions show the illuminated hall floor between the bottom buttons.
                    // Checking the composed canvas also catches an obsolete texture bound by the RawImage.
                    int min = 255, max = 0;
                    foreach (float x in new[] { .42f, .50f, .59f })
                    foreach (float y in new[] { .025f, .047f, .067f })
                    {
                        Color32 color = pixels.GetPixel((int)(width * x), (int)(height * y));
                        min = Math.Min(min, color.b);
                        max = Math.Max(max, color.b);
                    }
                    Assert.That(max, Is.GreaterThan(32), name + " has no illuminated hall floor.");
                    Assert.That(max - min, Is.GreaterThan(6), name + " has a flat backdrop.");
                }
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
                UnityEngine.Object.Destroy(pixels);
            }
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (priorScene.IsValid() && priorScene.isLoaded) SceneManager.SetActiveScene(priorScene);
            if (home.IsValid() && home.isLoaded) yield return SceneManager.UnloadSceneAsync(home);
            if (changedSettings)
            {
                Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", priorEndpoint);
                Environment.SetEnvironmentVariable("ASTRAL_TEST_SKIP_ENTRY", priorSkipEntry);
            }
        }
    }
}
#endif
