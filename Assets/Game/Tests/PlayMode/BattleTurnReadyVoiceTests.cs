#if UNITY_EDITOR && UNITY_EDITOR_WIN
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
    public sealed class BattleTurnReadyVoiceTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;

        [UnityTest]
        public IEnumerator OnlyTheCurrentTurnStartsOneReadyVoiceAndRefreshDoesNotRepeatIt()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var simulation = typeof(BattleHudPreview).GetField("simulation", PrivateInstance).GetValue(host);
            var voice = simulation.GetType().GetField("voice", PrivateInstance).GetValue(simulation);
            var count = voice.GetType().GetProperty("StartedTurnReadyCount");
            Assert.That(count, Is.Not.Null);
            Assert.That((bool)voice.GetType().GetProperty("IsAvailable").GetValue(voice), Is.True);
            int ReadyVoices() => (int)count.GetValue(voice);
            var refresh = simulation.GetType().GetMethod("Refresh", PrivateInstance);
            var initial = host.SimulationSnapshot;
            Assert.That(initial.GetUnit(initial.CurrentActorId).Slot.Side, Is.EqualTo(BattleSide.Ally));
            Assert.That(ReadyVoices(), Is.EqualTo(1));
            Assert.That(host.StartedSkillVoices, Is.Zero);

            refresh.Invoke(simulation, null);
            host.PauseBattle();
            yield return null;
            host.ResumeBattle();
            refresh.Invoke(simulation, null);
            Assert.That(ReadyVoices(), Is.EqualTo(1));

            map.Require<GameButton>("skill1").onClick.Invoke();
            var target = initial.Units.First(unit => unit.Slot.Side == BattleSide.Enemy);
            Assert.That(host.TrySelectBattleTarget(target.InstanceId), Is.True);
            Assert.That(host.SimulationSnapshot.TurnNumber, Is.GreaterThan(initial.TurnNumber));
            Assert.That(ReadyVoices(), Is.EqualTo(1), "The next turn waits for the current action presentation.");
            float deadline = Time.realtimeSinceStartup + 12f;
            while (ReadyVoices() < 2 && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(ReadyVoices(), Is.EqualTo(2));
            Assert.That(host.StartedSkillVoices, Is.EqualTo(1));
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
