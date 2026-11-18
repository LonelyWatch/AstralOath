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
            Click(FindWindow("HomeMoreMenu"), "summon");
            yield return Until(() =>
            {
                var current = FindWindow("SummonWindow");
                return current && current.Require<GameText>("balance").text.Contains("60");
            }, "The real server did not initialize the developer's 60 bookmarks.");

            Assert.That(FindWindow("HomeMoreMenu"), Is.Null);
            var summon = FindWindow("SummonWindow");
            Assert.That(summon.Require<GameText>("bannerTitle").text, Does.Contain("誓约"));
            Assert.That(summon.Require<GameButton>("free").IsInteractable(), Is.True);
            yield return Capture("summon-covenant");
            Click(summon, "iseria");
            Assert.That(summon.Require<GameText>("bannerTitle").text, Does.Contain("伊赛丽亚"));
            Assert.That(summon.Require<GameText>("pity").text, Does.Contain("120"));
            Assert.That(summon.Require<Image>("artifactArt").sprite, Is.Not.Null);
            yield return Capture("summon-iseria-up");
            var preview = summon.Require<RawImage>("featuredSkillCanvas");
            yield return Until(() => preview.enabled && preview.texture, "The UP pool did not start the real Iseria S3 preview.");
            yield return new WaitForSecondsRealtime(3.15f);
            yield return Capture("summon-iseria-s3-cutin");
            Click(summon, "ten");
            Assert.That(Panel(summon, "confirmPanel"), Is.True);
            var pausedFrame = preview.uvRect;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(preview.uvRect, Is.EqualTo(pausedFrame), "The running skill preview should pause while confirming.");
            Click(summon, "confirmNo");
            yield return Until(() => preview.uvRect != pausedFrame, "The skill preview did not resume.");

            Click(summon, "covenant");
            Assert.That(preview.enabled, Is.False);
            Click(summon, "info");
            Assert.That(Rows(summon, "infoItems").Any(row => row.Require<GameText>("detail").text.Contains("%")), Is.True);
            yield return Capture("summon-probability");
            Click(summon, "infoClose");
            Click(summon, "wish");
            Assert.That(Rows(summon, "wishSelectedItems").Length, Is.EqualTo(7));
            Click(summon, "wishArtifact");
            Assert.That(Rows(summon, "wishItems").Length, Is.GreaterThan(7));
            yield return Capture("summon-wishlist");
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
            yield return Capture("summon-ten-results");

            Click(result[0], "button");
            Assert.That(Panel(summon, "revealPanel"), Is.True);
            Assert.That(summon.Require<GameText>("revealName").text, Is.EqualTo(labels[0]));
            Click(summon, "next");
            Click(summon, "resultClose");
            Click(summon, "recovery");
            yield return Until(() => Panel(summon, "resultPanel"), "The stored receipt did not reopen.");
            Assert.That(Rows(summon, "resultItems").Select(row => row.Require<GameText>("name").text), Is.EqualTo(labels));
            Assert.That(summon.Require<GameText>("balance").text, Does.Contain("10"));
            Click(summon, "resultClose");
            Click(summon, "back");
            yield return Until(() => FindWindow("HomeWindow") && !FindWindow("SummonWindow"), "Back did not restore Home.");
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

        private IEnumerator Capture(string name)
        {
            string path = Environment.GetEnvironmentVariable("ASTRAL_SUMMON_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(path)) yield break;
            Directory.CreateDirectory(path);
            var roots = home.GetRootGameObjects();
            var canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).First(item => item.isRootCanvas);
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
                .First(item => item.enabled && item.targetTexture == null);
            var target = RenderTexture.GetTemporary(1478, 720, 24);
            var pixels = new Texture2D(1478, 720, TextureFormat.RGB24, false);
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
                pixels.ReadPixels(new Rect(0, 0, 1478, 720), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(path, name + ".png"), pixels.EncodeToPNG());
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
