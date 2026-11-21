#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattleActiveTurnFocusTests
    {
        [UnityTest]
        public IEnumerator FallenActorsAndTheCompletedMatchKeepNoGroundFocus()
        {
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            try
            {
                yield return null;
                var units = scene.GetRootGameObjects().Single(item => item.name == "Units").transform;
                var host = scene.GetRootGameObjects().Select(item => item.GetComponent<BattleHudPreview>()).Single(item => item);
                var simulation = typeof(BattleHudPreview).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var tick = simulation.GetType().GetMethod("Tick");
                var ring = units.Find("Active Turn Focus").GetComponent<SpriteRenderer>();
                simulation.GetType().GetMethod("ToggleAuto").Invoke(simulation, null);
                bool sawFallenActor = false;
                for (int i = 0; i < 2400 && host.SimulationSnapshot.Outcome == BattleOutcome.InProgress; ++i)
                {
                    tick.Invoke(simulation, new object[] { .5f });
                    var snapshot = host.SimulationSnapshot;
                    sawFallenActor |= snapshot.Units.Any(unit => !unit.IsAlive);
                    if (ring.enabled)
                    {
                        var current = snapshot.GetUnit(snapshot.CurrentActorId);
                        Assert.That(current.IsAlive, Is.True);
                        var team = units.Find(current.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies");
                        var actor = team.GetChild(current.Slot.Position - 1).GetComponentInChildren<BattlePreviewActor>();
                        Assert.That(Vector3.Distance(ring.transform.position, actor.transform.Find("Shadow").position), Is.LessThan(.001f));
                    }
                    if (i % 50 == 0) yield return null;
                }
                Assert.That(sawFallenActor, Is.True);
                Assert.That(host.SimulationSnapshot.Outcome, Is.Not.EqualTo(BattleOutcome.InProgress));
                Assert.That(ring.enabled, Is.False);
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }

        [UnityTest]
        public IEnumerator WaitingActorHasOnePulsingGroundFocusAndActionHidesIt()
        {
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            try
            {
                yield return null;
                var units = scene.GetRootGameObjects().Single(item => item.name == "Units").transform;
                var host = scene.GetRootGameObjects().Select(item => item.GetComponent<BattleHudPreview>()).Single(item => item);
                var simulation = typeof(BattleHudPreview).GetField("simulation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(host);
                var tick = simulation.GetType().GetMethod("Tick");
                var map = host.GetComponentInChildren<BindingMap>();
                var first = host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId);
                var slot = units.Find(first.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies").GetChild(first.Slot.Position - 1);
                var floor = slot.GetComponentInChildren<BattlePreviewActor>().transform.Find("Shadow").GetComponent<SpriteRenderer>();
                var focus = units.Find("Active Turn Focus");
                Assert.That(focus, Is.Not.Null, "An active actor needs a separate ground sprite.");
                var ring = focus.GetComponent<SpriteRenderer>();
                Assert.That(ring.sprite.name, Is.EqualTo("focus"));
                Assert.That(ring.enabled, Is.True);
                Assert.That(ring.bounds.size.x, Is.EqualTo(3.08f).Within(.04f));
                Assert.That(Vector3.Distance(focus.position, floor.transform.position), Is.LessThan(.001f));
                Assert.That(ring.sortingOrder, Is.GreaterThan(floor.sortingOrder));
                var meshes = units.GetComponentsInChildren<MeshRenderer>().Where(mesh => mesh.GetComponentInParent<BattlePreviewActor>()).ToArray();
                Assert.That(meshes.Length, Is.GreaterThan(0));
                Assert.That(meshes.All(mesh => mesh.sortingOrder > ring.sortingOrder), Is.True);
                float low = 1f, high = 0f;
                for (int i = 0; i < 60; ++i)
                {
                    tick.Invoke(simulation, new object[] { 1f / 60f });
                    low = Mathf.Min(low, ring.color.a);
                    high = Mathf.Max(high, ring.color.a);
                }
                Assert.That(high, Is.GreaterThan(.9f));
                Assert.That(low, Is.InRange(.1f, .4f));
                simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
                float pausedAlpha = ring.color.a;
                tick.Invoke(simulation, new object[] { .37f });
                Assert.That(ring.color.a, Is.EqualTo(pausedAlpha));
                simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { false });
                var target = host.SimulationSnapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Enemy);
                map.Require<GameButton>("skill1").onClick.Invoke();
                Assert.That(host.TrySelectBattleTarget(target.InstanceId), Is.True);
                tick.Invoke(simulation, new object[] { 0f });
                Assert.That(ring.enabled, Is.False, "Hide the old ring as soon as the action starts.");
                for (int i = 0; i < 240 && !ring.enabled; ++i)
                    tick.Invoke(simulation, new object[] { .1f });
                Assert.That(ring.enabled, Is.True);
                var second = host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId);
                Assert.That(second.InstanceId, Is.Not.EqualTo(first.InstanceId));
                var secondSlot = units.Find(second.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies").GetChild(second.Slot.Position - 1);
                var secondFloor = secondSlot.GetComponentInChildren<BattlePreviewActor>().transform.Find("Shadow");
                Assert.That(Vector3.Distance(focus.position, secondFloor.position), Is.LessThan(.001f));
                host.RestartBattle();
                yield return null;
                Assert.That(focus == null, Is.True);
                Assert.That(units.Cast<Transform>().Count(t => t.name == "Active Turn Focus"), Is.EqualTo(1));
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
#endif
