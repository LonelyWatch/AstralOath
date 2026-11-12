#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Profiling;

namespace AstralOath.Client.Tests
{
    public sealed class BattleRuntimePerformanceTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private Scene scene;
        private readonly List<Recorder> recorders = new List<Recorder>();
        private readonly List<string> recorderNames = new List<string>();
        [UnityTest]
        public IEnumerator NineAcceptedPresentationFixturesIncludeVoiceHudAndRealFrameIntervals()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null; yield return null;
            var host = scene.GetRootGameObjects().Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            var simulation = Field(host, "simulation");
            Assert.That(simulation, Is.Not.Null);
            var present = simulation.GetType().GetMethod("Present", Flags);
            var queue = (Queue<BattleFlowEvent>)Field(simulation, "playback");
            var watch = new Stopwatch();
            var names = new List<string>();
            Sampler.GetNames(names);
            foreach (string name in names.Where(name => name.Contains("Shader") || name == "GC.Collect" || name == "Camera.Render" || name == "PlayerLoop" || name == "EditorLoop"))
            {
                var recorder = Recorder.Get(name);
                if (!recorder.isValid) continue;
                recorder.enabled = true;
                recorders.Add(recorder);
                recorderNames.Add(name);
            }
            foreach (var hero in new[] { ("ally-1", "c1001", "ras"), ("ally-3", "c1005", "mercedes"), ("ally-2", "c1062", "angelica") })
            for (int slot = 1; slot <= 3; slot++)
            {
                string skill = hero.Item2 + ".s" + slot;
                string id = $"c_{hero.Item3}_skill_{slot:D2}";
                // The first post-load delta can include scene preparation. Wait until two real
                // frames settle before starting an action, so LateUpdate cannot consume it at once.
                double settleDeadline = Time.realtimeSinceStartupAsDouble + 10d;
                int stableFrames = 0;
                while (stableFrames < 2)
                {
                    yield return null;
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(settleDeadline),
                        id + " did not reach two consecutive frames below 50ms within 10 seconds.");
                    stableFrames = Time.unscaledDeltaTime > 0f && Time.unscaledDeltaTime < .05f ? stableFrames + 1 : 0;
                }
                // Read-only accepted-event fixtures exercise the visual/audio consumer, not the battle resolver.
                queue.Enqueue(BattleDualAttackTimingTests.Event(BattleFlowEventKind.Damage, hero.Item1, skill, null, BattleActionOrigin.Primary));
                watch.Restart();
                present.Invoke(simulation, new object[] { BattleDualAttackTimingTests.Event(BattleFlowEventKind.Action, hero.Item1, skill, id, BattleActionOrigin.Primary) });
                watch.Stop();
                double startMs = watch.Elapsed.TotalMilliseconds;
                double deadline = Time.realtimeSinceStartupAsDouble + 30d;
                var intervals = new List<float>();
                int collections = GC.CollectionCount(0);
                do
                {
                    yield return null;
                    intervals.Add(Time.unscaledDeltaTime * 1000f);
                    if (Time.unscaledDeltaTime > .0333f)
                    {
                        string markers = string.Join(";", recorders.Select((value, index) => (value, index))
                            .Where(pair => pair.value.elapsedNanoseconds > 100000)
                            .Select(pair => recorderNames[pair.index] + "=" + (pair.value.elapsedNanoseconds / 1000000d).ToString("F3")));
                        UnityEngine.Debug.Log($"PERF SlowFrame {id} FrameMs={Time.unscaledDeltaTime*1000f:F3} SourceAt={Field(simulation, "actionElapsed")} GC0Delta={GC.CollectionCount(0)-collections} MarkersMs={markers}");
                    }
                    collections = GC.CollectionCount(0);
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), id);
                } while ((bool)simulation.GetType().GetProperty("Busy", Flags).GetValue(simulation));
                Assert.That(intervals.Count, Is.GreaterThanOrEqualTo(10),
                    id + " needs at least ten real frames for a valid runtime sample.");
                intervals.Sort();
                UnityEngine.Debug.Log($"PERF Runtime {id} StartWithVoiceMs={startMs:F3} FrameP95Ms={intervals[(int)((intervals.Count-1)*.95f)]:F3} FrameMaxMs={intervals.Last():F3} Samples={intervals.Count}");
            }
            Assert.That(host.StartedSkillVoices, Is.GreaterThanOrEqualTo(9), "All source voice events start through the real FMOD consumer.");
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero, "Presentation fixtures cannot submit authoritative commands.");
        }
        private static object Field(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var recorder in recorders) recorder.enabled = false;
            recorders.Clear(); recorderNames.Clear();
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
