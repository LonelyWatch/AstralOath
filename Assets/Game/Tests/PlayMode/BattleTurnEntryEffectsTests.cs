#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattleTurnEntryEffectsTests
    {
        [UnityTest]
        public IEnumerator MercedesDustFollowsTheReadyEventAndReusesThePreparedPlayer()
        {
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene(useDefaultRoster: true);
            yield return null;
            var host = scene.GetRootGameObjects().Select(item => item.GetComponent<BattleHudPreview>()).Single(item => item);
            var units = scene.GetRootGameObjects().Single(item => item.name == "Units").transform;
            try
            {
                var actors = units.GetComponentsInChildren<BattlePreviewActor>();
                var mercedes = actors.Single(item => ReadHero(item) == "c1005");
                var setReady = typeof(BattlePreviewActor).GetMethod("SetTurnReady", BindingFlags.Instance | BindingFlags.NonPublic);
                var playerField = typeof(BattlePreviewActor).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic);
                var player = (ISkeletonPlayer)playerField.GetValue(mercedes);
                setReady.Invoke(mercedes, new object[] { false });
                setReady.Invoke(mercedes, new object[] { true });

                var dust = units.Find("Battle Turn Entry Dust");
                Assert.That(dust, Is.Not.Null);
                Assert.That(dust.gameObject.activeSelf, Is.False);
                player.Tick(.45f);
                Assert.That(dust.gameObject.activeSelf, Is.False);
                player.Tick(.12f);
                Assert.That(dust.gameObject.activeSelf, Is.True);
                var shadow = mercedes.transform.parent.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(Vector3.Distance(dust.position, shadow.transform.position), Is.LessThan(.001f));
                Assert.That(dust.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
                var simulation = typeof(BattleHudPreview).GetField("simulation", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(host);
                var effects = simulation.GetType().GetField("turnEntryEffects", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(simulation);
                var dustPlayer = (ISkeletonPlayer)effects.GetType().GetField("dust", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(effects);
                host.PauseBattle();
                simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { .2f });
                Assert.That(dustPlayer.Time, Is.Zero);
                host.ResumeBattle();
                simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { .2f });
                Assert.That(dustPlayer.Time, Is.EqualTo(.2f).Within(.001f));

                setReady.Invoke(mercedes, new object[] { true });
                player.Tick(.4f);
                Assert.That(dustPlayer.Time, Is.EqualTo(.2f).Within(.001f), "Repeated readiness must not start another cue.");
                setReady.Invoke(mercedes, new object[] { false });
                setReady.Invoke(mercedes, new object[] { true });
                player.Tick(.57f);
                Assert.That(dustPlayer.Time, Is.Zero, "The next ready clip restarts the same prewarmed player.");
                Assert.That(units.Cast<Transform>().Count(item => item.name == dust.name), Is.EqualTo(1));

                host.RestartBattle();
                Assert.That(dust.gameObject.activeSelf, Is.False);
                yield return null;
                Assert.That(dust == null, Is.True);
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }

        private static string ReadHero(BattlePreviewActor actor) =>
            (string)typeof(BattlePreviewActor).GetProperty("HeroId", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
    }
}
#endif