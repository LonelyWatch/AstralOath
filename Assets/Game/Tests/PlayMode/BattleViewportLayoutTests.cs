#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleViewportLayoutTests
    {
        [UnityTest]
        public IEnumerator WideAndTabletViewportFillAndKeepTheInteractiveHudInsideTheSafeArea()
        {
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var hud = host.GetComponentInChildren<BindingMap>();
            var units = roots.Single(item => item.name == "Units").transform;
            var simulation = typeof(BattleHudPreview).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(host);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            var tick = simulation.GetType().GetMethod("Tick");
            var statusAnchor = typeof(BattlePreviewActor).GetMethod("TryGetPresentationAnchor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var camera = host.GetComponent<Canvas>().worldCamera;
            var viewport = camera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(apply, Is.Not.Null);
            var screen = camera.targetTexture;
            var gameResolution = new[] { new Vector2Int(1920, 1080), new Vector2Int(2608, 1200), new Vector2Int(1440, 1080) };
            try
            {
                foreach (var size in gameResolution)
                {
                    bool asymmetric = size.x == 2608;
                    var safe = asymmetric ? new Rect(124, 26, size.x - 160, size.y - 60) : new Rect(Vector2.zero, size);
                    var target = RenderTexture.GetTemporary(size.x, size.y, 24);
                    camera.targetTexture = target;
                    try
                    {
                        Canvas.ForceUpdateCanvases();
                        apply.Invoke(viewport, new object[] { size, safe });
                        Canvas.ForceUpdateCanvases();
                        camera.Render();
                        // Camera.Render updates the screen-space Canvas plane. Calculate
                        // coordinates in the current HUD parent, not the previous display rect.
                        apply.Invoke(viewport, new object[] { size, safe });
                        Canvas.ForceUpdateCanvases();
                        tick.Invoke(simulation, new object[] { 0f });
                        camera.Render();
                        Capture(target, size);
                        var top = (RectTransform)hud.transform.Find("Top Bar");
                        var canvas = host.GetComponent<Canvas>();
                        var pause = hud.Require<GameButton>("pause").GetComponent<RectTransform>();
                        Debug.Log($"ViewportMetrics {size}: canvasRect={((RectTransform)canvas.transform).rect} " +
                            $"pixelRect={canvas.pixelRect} canvasScale={canvas.scaleFactor} camera={camera.pixelRect} " +
                            $"group={top.anchoredPosition}/{top.localScale} pause={pause.anchoredPosition} " +
                            $"pausePixel={camera.WorldToScreenPoint(pause.position)}");
                        Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
                        Assert.That(camera.aspect, Is.EqualTo(size.x / (float)size.y).Within(.001f));
                        Assert.That(camera.orthographicSize * camera.aspect, Is.GreaterThanOrEqualTo(9.59f));
                        foreach (string key in new[] { "pause", "speed", "auto", "soulBurn", "skill1", "skill2", "skill3" })
                            AssertInside(hud.Require<GameButton>(key).GetComponent<RectTransform>(), camera, safe, key);
                        AssertStatusFollowsActors(units, hud.transform.Find("Unit Status"), camera, statusAnchor);
                        var sky = roots.Select(root => root.transform.Find("Sky")).First(item => item);
                        var ground = roots.Select(root => root.transform.Find("Ground")).First(item => item);
                        Assert.That(sky.GetComponent<SpriteRenderer>().bounds.max.y,
                            Is.GreaterThanOrEqualTo(camera.transform.position.y + camera.orthographicSize - .01f));
                        Assert.That(ground.GetComponent<SpriteRenderer>().bounds.min.y,
                            Is.LessThanOrEqualTo(camera.transform.position.y - camera.orthographicSize + .04f));
                    }
                    finally
                    {
                        camera.targetTexture = screen;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }

        private static void AssertStatusFollowsActors(Transform units, Transform markers, Camera camera, MethodInfo statusAnchor)
        {
            foreach (string side in new[] { "Allies", "Enemies" })
            foreach (Transform slot in units.Find(side))
            {
                var actor = slot.GetComponentInChildren<BattlePreviewActor>();
                var arguments = new object[] { "top", Vector3.zero };
                Assert.That(statusAnchor.Invoke(actor, arguments), Is.True, actor.name);
                var frame = (RectTransform)markers.Find(side + "/" + slot.name + "/Frame");
                Vector2 projectedActor = camera.WorldToScreenPoint((Vector3)arguments[1]);
                Vector2 projectedFrame = RectTransformUtility.WorldToScreenPoint(camera, frame.position);
                Assert.That(Vector2.Distance(projectedActor, projectedFrame), Is.LessThan(2f), actor.name);
            }
        }

        private static void Capture(RenderTexture target, Vector2Int size)
        {
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var previous = RenderTexture.active;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, $"viewport-{size.x}x{size.y}.png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
            }
        }

        private static void AssertInside(RectTransform element, Camera camera, Rect safe, string key)
        {
            var corners = new Vector3[4];
            element.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector2 pixel = RectTransformUtility.WorldToScreenPoint(camera, corner);
                Assert.That(pixel.x, Is.InRange(safe.xMin - 1, safe.xMax + 1), key + " x");
                Assert.That(pixel.y, Is.InRange(safe.yMin - 1, safe.yMax + 1), key + " y");
            }
        }
    }
}
#endif
