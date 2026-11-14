#if UNITY_EDITOR
using System;
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
    public sealed class BattleSoulGaugeConsumptionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;
        private BattleHudPreview host;
        private BindingMap map;

        [UnitySetUp]
        public IEnumerator Load()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(20));
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator ConfirmedSoulBurnDissolvesTheSpentSlotAndRestartClearsIt()
        {
            var burn = map.Require<GameButton>("soulBurn");
            Assert.That(ActiveSlots(), Is.Empty);
            burn.onClick.Invoke();
            Assert.That(ActiveSlots(), Is.Empty);
            burn.onClick.Invoke();
            Assert.That(ActiveSlots(), Is.Empty);
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(20));
            burn.onClick.Invoke();
            var enemy = host.SimulationSnapshot.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(host.TrySelectBattleTarget(enemy.InstanceId), Is.True);
            Assert.That(ActiveSlots().Select(slot => slot.name), Is.EqualTo(new[] { "Soul Consume 2" }),
                "The confirmed cost starts the effect before the skill's hit or Cut-in.");
            var secondSoul = map.transform.Find("Soul Gauge/Soul Indicators/Soul 2");
            Assert.That(map.transform.Find("Soul Gauge/Soul Indicators/Soul 1").gameObject.activeSelf, Is.True);
            Assert.That(secondSoul.gameObject.activeSelf, Is.False);
            Assert.That(ActiveSlots()[0].position, Is.EqualTo(secondSoul.position));
            yield return new WaitForSecondsRealtime(.25f);
            var slot = ActiveSlots().Single();
            foreach (string name in new[] { "soul_aura", "was_pati_b" })
            {
                var mesh = slot.Find("HUD particle " + name).GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), name);
                Assert.That(mesh.colors.Any(color => color.a > 0f), Is.True, name);
            }
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(12));

            host.PauseBattle();
            var aura = slot.Find("HUD particle soul_aura").GetComponent<MeshFilter>().sharedMesh;
            var frozen = aura.vertices;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(ActiveSlots().Length, Is.EqualTo(1));
            Assert.That(aura.vertices, Is.EqualTo(frozen));
            host.RestartBattle();
            yield return null;
            Assert.That(ActiveSlots(), Is.Empty);
            Assert.That(host.SimulationSnapshot.AllySoul, Is.EqualTo(20));
        }

        [Test]
        public void OnlyDisappearingWholeSlotsPlayAndAnIncomeUpdatePreservesTheBurst()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var gauge = simulation.GetType().GetField("soulGauge", PrivateInstance).GetValue(simulation);
            var show = gauge.GetType().GetMethod("Show", new[] { typeof(int), typeof(bool) });
            var tick = gauge.GetType().GetMethod("Tick");
            Assert.That(show, Is.Not.Null);
            show.Invoke(gauge, new object[] { 79, false });
            show.Invoke(gauge, new object[] { 78, true });
            Assert.That(ActiveSlots(), Is.Empty);

            show.Invoke(gauge, new object[] { 57, true });
            Assert.That(ActiveSlots().Select(slot => slot.name), Is.EqualTo(new[] { "Soul Consume 6", "Soul Consume 7" }));
            show.Invoke(gauge, new object[] { 60, false });
            Assert.That(ActiveSlots().Select(slot => slot.name), Is.EqualTo(new[] { "Soul Consume 6", "Soul Consume 7" }));
            show.Invoke(gauge, new object[] { 10, false });
            Assert.That(ActiveSlots().Select(slot => slot.name), Is.EqualTo(new[] { "Soul Consume 6", "Soul Consume 7" }));
            tick.Invoke(gauge, new object[] { 5f });
            Assert.That(ActiveSlots(), Is.Empty);
        }

        private Transform[] ActiveSlots()
        {
            var effects = map.transform.Find("Soul Gauge/Soul Consumption Effects");
            return effects ? effects.Cast<Transform>().Where(slot => slot.gameObject.activeSelf).ToArray()
                : Array.Empty<Transform>();
        }
    }
}
#endif
