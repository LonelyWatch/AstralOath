#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleResultTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator FinishedSnapshotsShowTheLocalTeamAndActualHealthForBothOutcomes()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/BattleScene/Res/Prefab/BattleResult.prefab");
            Assert.That(prefab, Is.Not.Null, "Online battles require a fixed result screen.");
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            try
            {
                yield return null;
                var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var simulation = typeof(BattleHudPreview).GetField("simulation", Members).GetValue(host);
                var original = (BattleSnapshot)simulation.GetType().GetProperty("Snapshot", Members).GetValue(simulation);
                var sprites = ((Sprite[])typeof(BattleHudPreview).GetField("battleSprites", Members).GetValue(host)).ToDictionary(s => s.name);
                var resources = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>("Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
                var copyType = typeof(BattleOnlineTransitionAssets).GetNestedType("Copy", BindingFlags.NonPublic);
                var copy = copyType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                    new[] { typeof(BattleOnlineTransitionAssets).GetField("zhHans", Members).GetValue(resources) });
                foreach (var outcome in new[] { BattleOutcome.AllyVictory, BattleOutcome.EnemyVictory, BattleOutcome.Draw })
                {
                    var result = (BattleSnapshot)typeof(BattleSnapshot).GetConstructors(Members).Single().Invoke(new object[] {
                        original.BattleId, original.RulesVersion, 40UL, 95UL, 17, original.CurrentActorId, outcome,
                        original.AllySoul, original.EnemySoul, original.Units.ToArray() });
                    var type = typeof(BattleHudPreview).Assembly.GetType("AstralOath.Client.BattleScene.BattleResultBinder", true);
                    int exits = 0;
                    using var panel = (IDisposable)Activator.CreateInstance(type, Members, null, new object[] {
                        host.transform, resources, copy, result, sprites, (Func<string, string>)(key => key), (Action)(() => exits++) }, null);
                    type.GetMethod("Tick", Members).Invoke(panel, new object[] { 2f });
                    var view = host.GetComponentsInChildren<BindingMap>(true).Single(map => map.name == "BattleResult");
                    var sound = view.GetComponent<AudioSource>();
                    Assert.That(sound, Is.Not.Null, "Opening the result must trigger the corresponding native event.");
                    string cue = outcome == BattleOutcome.AllyVictory ? "ResultVictory" : outcome == BattleOutcome.EnemyVictory ? "ResultDefeat" : null;
                    Assert.That(sound.clip ? sound.clip.name : null, Is.EqualTo(cue));
                    if (cue != null)
                    {
                        Assert.That(sound.playOnAwake || sound.loop, Is.False);
                        Assert.That(sound.isPlaying, Is.True);
                    }
                    foreach (string key in new[] { "details", "confirm" })
                    {
                        var button = view.Require<GameButton>(key);
                        var label = view.Require<GameText>(key + "Label");
                        Assert.That(label.font.name, Is.EqualTo("EpicSeven"), "Result labels must use the game's regular UI font.");
                        Assert.That(label.color, Is.EqualTo(Color.white));
                        var shade = view.transform.Find("Shade");
                        Assert.That(button.transform.parent, Is.EqualTo(shade.parent));
                        Assert.That(button.transform.GetSiblingIndex(), Is.GreaterThan(shade.GetSiblingIndex()));
                        Assert.That(button.transform.GetSiblingIndex(), Is.GreaterThan(view.transform.Find("Vignette").GetSiblingIndex()));
                        Assert.That(label.transform.IsChildOf(button.transform));
                        Assert.That(label.canvasRenderer.GetInheritedAlpha(), Is.EqualTo(1f).Within(.005f));
                    }
                    var allies = result.Units.Where(unit => unit.Slot.Side == BattleSide.Ally).OrderBy(unit => unit.Slot.Position).ToArray();
                    for (int i = 0; i < allies.Length; ++i)
                    {
                        Assert.That(view.Require<Image>("face" + i).sprite.name, Is.EqualTo(allies[i].DefinitionId + "_s"));
                        Assert.That(view.Require<GameText>("friendship" + i).text, Is.EqualTo("1"));
                    }
                    var badge = view.Require<RectTransform>("emblem");
                    Assert.That(badge.GetComponentsInChildren<MeshFilter>().Any(mesh => mesh.sharedMesh && mesh.sharedMesh.vertexCount > 0),
                        Is.EqualTo(outcome != BattleOutcome.Draw));
                    Assert.That(view.Require<GameText>("draw").gameObject.activeSelf, Is.EqualTo(outcome == BattleOutcome.Draw));
                    string capture = Environment.GetEnvironmentVariable("ASTRAL_RESULT_CAPTURE_DIR");
                    yield return BattleActionFeedbackVisualTests.CaptureScene(scene, outcome.ToString(), capture);
                    if (outcome == BattleOutcome.AllyVictory && !string.IsNullOrWhiteSpace(capture))
                    {
                        var shade = view.transform.Find("Shade").gameObject;
                        var vignette = view.transform.Find("Vignette").gameObject;
                        shade.SetActive(false);
                        vignette.SetActive(false);
                        yield return BattleActionFeedbackVisualTests.CaptureScene(scene, "VictoryWithoutBackdrop", capture);
                        shade.SetActive(true);
                        vignette.SetActive(true);
                    }
                    view.Require<GameButton>("details").onClick.Invoke();
                    Assert.That(view.Require<GameObject>("statistics").activeSelf);
                    Assert.That(view.Require<GameText>("unit0").text, Does.Contain($"{allies[0].CurrentHealth}/{allies[0].MaximumHealth}"));
                    view.Require<GameButton>("close").onClick.Invoke();
                    Assert.That(view.Require<GameObject>("statistics").activeSelf, Is.False);
                    view.Require<GameButton>("confirm").onClick.Invoke();
                    Assert.That(exits, Is.EqualTo(1));
                }
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
#endif
