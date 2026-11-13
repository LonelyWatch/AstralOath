using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Tests
{
    public sealed class BattlePlayStartupPerformanceTests
    {
        [UnityTest]
        public IEnumerator EnterPlayWithBattleSceneReportsReloadToReady()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            EditorSceneManager.OpenScene("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity", OpenSceneMode.Single);
            Debug.Log($"PERF EditorOpenBattleScene Ms={watch.Elapsed.TotalMilliseconds:F3}");
            SessionState.SetString("Astral.BattlePlayStart", DateTime.UtcNow.Ticks.ToString());
            yield return new EnterPlayMode();
            yield return null; yield return null;
            ReportReady();
            yield return new ExitPlayMode();
        }
        private static void ReportReady()
        {
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleHudPreview, AstralOath.Client.BattleScene", true);
            var host = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(type)).Single();
            Assert.That(type.GetProperty("SimulationSnapshot").GetValue(host), Is.Not.Null);
            double elapsed = (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString("Astral.BattlePlayStart", "0"))) / (double)TimeSpan.TicksPerMillisecond;
            Debug.Log($"PERF EditorPlayToBattleReady Ms={elapsed:F3} DomainReloadEnabled={!EditorSettings.enterPlayModeOptionsEnabled || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0}");
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}