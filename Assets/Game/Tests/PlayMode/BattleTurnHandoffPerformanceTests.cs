#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattleTurnHandoffPerformanceTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;

        [UnityTest]
        public IEnumerator MeasureRenderedFramesAroundAcceptedTurnHandoffs()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = host.GetComponentInChildren<BindingMap>();
            var simulation = typeof(BattleHudPreview).GetField("simulation", Instance).GetValue(host);
            Assert.That(simulation, Is.Not.Null);
            var canAct = simulation.GetType().GetProperty("CanAct", Instance);
            var busy = simulation.GetType().GetProperty("Busy", Instance);
            var displayedActor = simulation.GetType().GetField("displayedActor", Instance);
            var refresh = Recorder.Get("Astral.Battle.Refresh");
            var frame = Recorder.Get("Astral.Battle.Frame");
            frame.enabled = true;
            if (refresh.isValid) refresh.enabled = true;

            try
            {
                float deadline = Time.realtimeSinceStartup + 90f;
                int accepted = 0, observedHandoffs = 0, enemyCommands = 0;
                var intervals = new List<float>();
                var handoffs = new List<(float Frame, double Battle, double Refresh)>();
                long previousTime = Stopwatch.GetTimestamp();
                var priorActor = (BattleUnitSnapshot)displayedActor.GetValue(simulation);
                ulong lastSequence = host.SimulationSnapshot.LastCommandSequence;
                bool previouslyBusy = false;
                while (observedHandoffs < 8)
                {
                    if ((bool)canAct.GetValue(simulation) && accepted < 8)
                    {
                        var target = host.SimulationSnapshot.Units.FirstOrDefault(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy);
                        Assert.That(target, Is.Not.Null);
                        map.Require<GameButton>("skill1").onClick.Invoke();
                        Assert.That(host.TrySelectBattleTarget(target.InstanceId), Is.True);
                        accepted++;
                    }
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Eight actual turn transitions must occur.");
                    long now = Stopwatch.GetTimestamp();
                    float interval = (float)((now - previousTime) * 1000d / Stopwatch.Frequency);
                    previousTime = now;
                    intervals.Add(interval);
                    var currentActor = (BattleUnitSnapshot)displayedActor.GetValue(simulation);
                    bool isBusy = (bool)busy.GetValue(simulation);
                    if ((previouslyBusy && !isBusy) || priorActor?.InstanceId != currentActor?.InstanceId)
                    {
                        handoffs.Add((interval, frame.elapsedNanoseconds / 1_000_000d,
                            refresh.isValid ? refresh.elapsedNanoseconds / 1_000_000d : 0d));
                        observedHandoffs++;
                    }
                    var current = host.SimulationSnapshot;
                    if (current.LastCommandSequence > lastSequence && currentActor?.Slot.Side == BattleSide.Enemy)
                        enemyCommands++;
                    lastSequence = current.LastCommandSequence;
                    previouslyBusy = isBusy;
                    priorActor = currentActor;
                }
                Assert.That(accepted, Is.GreaterThan(0));
                Assert.That(enemyCommands, Is.GreaterThan(0));
                intervals.Sort();
                UnityEngine.Debug.Log($"PERF TurnHandoff Count={observedHandoffs} PlayerCommands={accepted} EnemyCommands={enemyCommands} " +
                    $"WholeFrames={intervals.Count} P95Ms={intervals[(int)((intervals.Count - 1) * .95f)]:F3} MaxMs={intervals.Last():F3} " +
                    $"Transitions={string.Join(";", handoffs.Select(value => $"{value.Frame:F3}/{value.Battle:F3}/{value.Refresh:F3}"))}");
            }
            finally
            {
                frame.enabled = false;
                if (refresh.isValid) refresh.enabled = false;
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
