#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattlePresentationPerformanceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;
        private IDisposable playback;

        [UnityTest]
        public IEnumerator NineSkillsReportColdAndWarmBeginAndPeakTickCost()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene(useDefaultRoster: true);
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(item => item.GetComponent<BattleHudPreview>()).Single(item => item);
            host.AutoStartBattle = false;
            host.enabled = false;
            yield return null;
            var units = roots.Single(item => item.name == "Units").transform;
            var actors = units.GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var target = enemies[0];
            var type = typeof(BattleHudPreview).Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            var begin = type.GetMethod("Begin");
            var tick = type.GetMethod("Tick");
            var graphs = (TextAsset[])typeof(BattleHudPreview).GetField("battleGraphs", Private).GetValue(host);
            var assets = (BattleSkillPresentationAsset[])typeof(BattleHudPreview).GetField("skillAssets", Private).GetValue(host);
            var camera = host.GetComponent<Canvas>().worldCamera;
            var background = (GameObject)typeof(BattleHudPreview).GetField("stageBackground", Private).GetValue(host);
            playback = (IDisposable)Activator.CreateInstance(type, new object[] { units, graphs, assets, camera, null, background });
            var prepare = type.GetMethod("Prepare");
            var stageTime = type.GetProperty("StageTime");
            var watch = new Stopwatch();
            foreach (var hero in new[] { ("Ras", "ras"), ("Mercedes", "mercedes"), ("Angelica", "angelica") })
            {
                var actor = actors.First(item => item.name.StartsWith(hero.Item1, StringComparison.Ordinal));
                for (int skill = 1; skill <= 3; skill++)
                {
                    string id = $"c_{hero.Item2}_skill_{skill:D2}";
                    watch.Restart();
                    prepare.Invoke(playback, new object[] { actor, id });
                    watch.Stop();
                    UnityEngine.Debug.Log($"PERF Prepared {id} PrepareMs={watch.Elapsed.TotalMilliseconds:F3}");
                    var cache = type.GetField("cache", Private).GetValue(playback);
                    var sources = (System.Collections.IDictionary)cache.GetType().GetField("Skeletons", Private).GetValue(cache);
                    var prepared = sources.Values.Cast<object>().ToDictionary(item => item,
                        item => item.GetType().GetField("ParsedData", Private).GetValue(item));
                    foreach (int targetCount in new[] { 1, enemies.Length })
                    for (int pass = 0; pass < 2; pass++)
                    {
                        watch.Restart();
                        var timing = ((float Impact, float Duration))begin.Invoke(playback, new object[] { id, actor, target, enemies.Take(targetCount).ToArray() });
                        watch.Stop();
                        double beginMs = watch.Elapsed.TotalMilliseconds;
                        double peakMs = 0;
                        float peakAt = 0;
                        for (float elapsed = 0; (float)stageTime.GetValue(playback) < timing.Duration + .05f && elapsed < 45f; elapsed += 1f / 60f)
                        {
                            watch.Restart();
                            tick.Invoke(playback, new object[] { 1f / 60f });
                            watch.Stop();
                            if (watch.Elapsed.TotalMilliseconds > peakMs)
                            {
                                peakMs = watch.Elapsed.TotalMilliseconds;
                                peakAt = elapsed;
                            }
                        }
                        UnityEngine.Debug.Log($"PERF FullScenePrepared {id} targets={targetCount} pass={pass} BeginMs={beginMs:F3} PeakTickMs={peakMs:F3} PeakWallAt={peakAt:F3} SourceDuration={timing.Duration:F3}");
                        Assert.That((float)stageTime.GetValue(playback), Is.GreaterThanOrEqualTo(timing.Duration));
                        foreach (var pair in prepared)
                            Assert.That(pair.Key.GetType().GetField("ParsedData", Private).GetValue(pair.Key), Is.SameAs(pair.Value),
                                "Playback must retain prepared parse data across first and repeated actions.");
                        Assert.That(timing.Duration, Is.InRange(.01f, 30f));
                        yield return null;
                    }
                }
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            playback?.Dispose();
            playback = null;
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
