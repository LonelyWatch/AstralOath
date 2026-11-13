#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace AstralOath.Client.Tests
{
    public sealed class BattleCinematicStatusTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene scene;
        BattleCinematicStage stage;
        void PlayNode(string json)
        {
            var method = typeof(BattleCinematicStage).GetMethod("Play");
            var parse = method.GetParameters()[0].ParameterType.GetMethod("Parse", new[] { typeof(string) });
            method.Invoke(stage, new[] { parse.Invoke(null, new object[] { json }) });
        }
        static void Capture(Camera camera, string name, Action refresh, Action validate)
        {
            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;
            string directory = Path.Combine(output, "status-visibility");
            Directory.CreateDirectory(directory);
            var target = RenderTexture.GetTemporary(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousAspect = camera.aspect;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = 1920f / 1080f;
                Canvas.ForceUpdateCanvases();
                refresh();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                validate();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                UnityEngine.Object.Destroy(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }
        [UnityTearDown] public IEnumerator Unload()
        {
            stage?.Dispose(); stage = null;
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
        [UnityTest] public IEnumerator ExcludedBodiesHideTheirStatusAndRestoreWithoutRevivingDefeatedUnits()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null; yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.SelectMany(x => x.GetComponentsInChildren<BattleHudPreview>()).Single();
            var map = roots.SelectMany(x => x.GetComponentsInChildren<BindingMap>()).Single();
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            var views = (IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation);
            var units = roots.Single(x => x.name == "Units").transform;
            var all = units.GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var spectator = all.Single(x => x.name == "Angelica [c1062]");
            var spectatorMarker = map.transform.Find("Unit Status/Allies/" + spectator.transform.parent.name);
            var camera = host.GetComponent<Canvas>().worldCamera;
            var background = (GameObject)typeof(BattleHudPreview).GetField("stageBackground", Private).GetValue(host);
            stage = new BattleCinematicStage(camera, null, background);
            stage.SetParticipants(all.Select(x => x.transform.parent).ToArray());
            void Refresh() => simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { 0f });
            Renderer Body(BattlePreviewActor actor) => (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            void ValidateRenderedAnchors()
            {
                foreach (var actor in all)
                {
                    string team = actor.transform.parent.parent.name;
                    var marker = map.transform.Find("Unit Status/" + team + "/" + actor.transform.parent.name);
                    if (!marker.gameObject.activeInHierarchy) continue;
                    object[] args = { "top", Vector3.zero };
                    Assert.That((bool)typeof(BattlePreviewActor).GetMethod("TryGetPresentationAnchor", Private).Invoke(actor, args), Is.True);
                    var frame = (RectTransform)marker.Find("Frame");
                    var actual = RectTransformUtility.WorldToScreenPoint(camera, frame.TransformPoint(frame.rect.center));
                    var expected = (Vector2)camera.WorldToScreenPoint((Vector3)args[1]);
                    Assert.That(Vector2.Distance(actual, expected), Is.LessThan(2f), actor.name + " must remain aligned after the same camera render.");
                }
            }
            foreach (string name in new[] { "Ras [c1001]", "Mercedes [c1005]" })
            {
                var caster = all.Single(x => x.name == name);
                stage.Begin(caster.transform, enemies.Select(x => x.transform).ToArray(), false);
                bool offscreen = name == "Ras [c1001]";
                PlayNode(offscreen
                    ? "{'etty':'LAYOUT','mode':0,'away_x':5000,'time':200,'fade':200}"
                    : "{'etty':'LAYOUT','mode':0,'away_x':150,'time':200,'fade':200}");
                stage.Tick(.21f); Refresh();
                Assert.That(Body(spectator).enabled, Is.False);
                Assert.That(spectatorMarker.gameObject.activeSelf, Is.False, "An excluded body's HP/level/status cannot remain floating over the close-up.");
                if (offscreen)
                {
                    foreach (var enemy in enemies)
                    {
                        Assert.That(Body(enemy).enabled, Is.False);
                        Assert.That(map.transform.Find("Unit Status/Enemies/" + enemy.transform.parent.name).gameObject.activeSelf, Is.False);
                    }
                    Capture(camera, "ras-layout-offscreen", Refresh, ValidateRenderedAnchors);
                    PlayNode("{'etty':'CAM','location':42,'time':200,'y':-50}");
                    stage.Tick(.21f); Refresh();
                }
                Capture(camera, offscreen ? "ras-target-focus" : "mercedes-layout", Refresh, ValidateRenderedAnchors);
                foreach (var enemy in enemies)
                {
                    Assert.That(Body(enemy).enabled, Is.True);
                    Assert.That(map.transform.Find("Unit Status/Enemies/" + enemy.transform.parent.name).gameObject.activeSelf, Is.True);
                }
                stage.Reset(); Refresh();
                Assert.That(Body(spectator).enabled, Is.True);
                Assert.That(spectatorMarker.gameObject.activeSelf, Is.True);
            }
            var view = views["enemy-1"];
            view.GetType().GetMethod("BeginDeath").Invoke(view, null);
            // A dying unit keeps its status bar through the authored fade.
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { 2f });
            stage.Reset(); Refresh();
            Assert.That(map.transform.Find("Unit Status/Enemies/" + enemies[0].transform.parent.name).gameObject.activeSelf, Is.False,
                "Restoring a cinematic mask cannot revive a defeated unit's status.");
            host.RestartBattle(); yield return null;
            Assert.That(spectatorMarker.gameObject.activeSelf, Is.True);
        }
    }
}
#endif
