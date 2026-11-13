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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    // Includes real buttons, target selection, rule resolution, voice, delayed CFX and rendering.
    // Timings are observations: Editor/device scans are not a deterministic CI frame-time gate.
    public sealed class BattleInputPerformanceTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private Scene scene;
        [UnityTest]
        public IEnumerator RealSkillInputsMeasureFullActionFrames()
        {
            var watch = Stopwatch.StartNew();
            scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            double sceneMs = watch.Elapsed.TotalMilliseconds;
            yield return null; yield return null;
            var host = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>()).Single();
            var map = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var simulation = typeof(BattleHudPreview).GetField("simulation", Flags).GetValue(host);
            Assert.That(simulation, Is.Not.Null);
            var busy = simulation.GetType().GetProperty("Busy", Flags);
            var canAct = simulation.GetType().GetProperty("CanAct", Flags);
            UnityEngine.Debug.Log($"PERF InputStartup SceneLoadMs={sceneMs:F3} SceneToReadyMs={watch.Elapsed.TotalMilliseconds:F3}");
            for (int input = 0; input < 9; input++)
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 60;
                if (host.SimulationSnapshot.Outcome != BattleOutcome.InProgress)
                {
                    host.RestartBattle();
                    simulation = typeof(BattleHudPreview).GetField("simulation", Flags).GetValue(host);
                    yield return null; yield return null;
                }
                while (!(bool)canAct.GetValue(simulation))
                {
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Battle did not expose a living ally input.");
                    yield return null;
                }
                // Ensure the long startup delta isn't consumed as the new action's first frame.
                yield return null; yield return null;
                var snapshot = host.SimulationSnapshot;
                var actor = snapshot.GetUnit(snapshot.CurrentActorId);
                int desiredSlot = input < 4 ? 3 : input < 8 ? 2 : 1;
                var button = map.Require<GameButton>("skill" + desiredSlot);
                if (!button.interactable) { desiredSlot = 1; button = map.Require<GameButton>("skill1"); }
                bool friendly = actor.DefinitionId == "c1062" && desiredSlot > 1;
                var target = snapshot.Units.First(unit => unit.IsAlive && (unit.Slot.Side == actor.Slot.Side) == friendly);
                string skill = actor.Form.SkillIds[desiredSlot - 1];
                ulong previous = snapshot.LastCommandSequence;
                watch.Restart();
                button.onClick.Invoke();
                bool accepted = host.TrySelectBattleTarget(target.InstanceId);
                double submitMs = watch.Elapsed.TotalMilliseconds;
                Assert.That(accepted, Is.True, skill);
                Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.EqualTo(previous + 1));
                var intervals = new List<double>();
                long frameStart = Stopwatch.GetTimestamp();
                do
                {
                    yield return null;
                    long now = Stopwatch.GetTimestamp();
                    intervals.Add((now - frameStart) * 1000d / Stopwatch.Frequency);
                    frameStart = now;
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), skill);
                } while ((bool)busy.GetValue(simulation));
                Assert.That(intervals.Count, Is.GreaterThan(10), skill);
                intervals.Sort();
                UnityEngine.Debug.Log($"PERF RealInput Index={input} Skill={skill} SubmitMs={submitMs:F3} P95Ms={intervals[(int)((intervals.Count - 1)*.95)]:F3} MaxMs={intervals.Last():F3} Samples={intervals.Count}");
            }
            Assert.That(host.StartedSkillVoices, Is.GreaterThan(0), "Probe must include actual FMOD voice events.");
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif