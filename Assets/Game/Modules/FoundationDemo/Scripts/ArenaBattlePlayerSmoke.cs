using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    internal static class ArenaBattlePlayerSmoke
    {
        private const string Option = "-astral-arena-battle-smoke";
        private const string FixtureVariable = "ASTRAL_ARENA_BATTLE_SMOKE_FIXTURE";

        [Serializable, Preserve]
        private sealed class Fixture
        {
            public string roomId, battleId, contentHash, bearer;
        }

        internal static void StartIfRequested(GameLaunchContext context, CancellationToken lifetime)
        {
            if (Application.isEditor || !Debug.isDebugBuild ||
                Array.IndexOf(Environment.GetCommandLineArgs(), Option) < 0) return;
            Application.runInBackground = true;
            _ = RunAsync(context, lifetime);
        }

        private static async Task RunAsync(GameLaunchContext context, CancellationToken lifetime)
        {
            Fixture fixture = null;
            try
            {
                // Let FoundationBootstrap complete service registration before leaving Home.
                await Task.Yield();
                var endpoint = context.Services.Get<GameServerEndpoint>().Address;
                if (!endpoint.IsLoopback)
                    throw new InvalidOperationException("The Player smoke requires a loopback server.");
                string json = Environment.GetEnvironmentVariable(FixtureVariable);
                Environment.SetEnvironmentVariable(FixtureVariable, null);
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidOperationException("The Player smoke requires a prepared room fixture.");
                fixture = JsonUtility.FromJson<Fixture>(json);
                if (fixture == null || string.IsNullOrWhiteSpace(fixture.bearer))
                    throw new InvalidOperationException("The Player smoke fixture is incomplete.");

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                await BattleOnlineLaunch.LoadAsync(context.Services.Get<IAssetService>(),
                    context.Services.Get<ISceneAssetService>(), endpoint, fixture.roomId, fixture.battleId,
                    fixture.contentHash, fixture.bearer, true, deadline.Token);

                var scene = SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath);
                if (!scene.isLoaded || SceneManager.GetActiveScene() != scene)
                    throw new InvalidOperationException("The packed battle scene did not become active.");
                var roots = scene.GetRootGameObjects();
                var actors = roots.SelectMany(root => root.GetComponentsInChildren<BattlePreviewActor>()).ToArray();
                if (actors.Length != 8 || actors.Any(actor => !actor.isActiveAndEnabled ||
                    !actor.GetComponentsInChildren<MeshFilter>().Any(mesh => mesh.sharedMesh && mesh.sharedMesh.vertexCount > 0)))
                    throw new InvalidOperationException("The battle did not present all eight actors.");
                var hud = roots.SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>()).Single();
                var clock = hud.transform.Find("Online PvP HUD/Authoritative Turn Clock");
                if (!clock) throw new InvalidOperationException("The online battle clock is unavailable.");
                Debug.Log("ASTRAL_ARENA_BATTLE_ENTRY_OK actors=8");

                await WaitUntilAsync(() => clock.gameObject.activeInHierarchy, deadline.Token);
                Debug.Log("ASTRAL_ARENA_BATTLE_CLOCK_OK");

                // Reuse the simulation snapshot inspected by the Editor integration test.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var simulation = typeof(BattleHudPreview).GetField("simulation", flags)?.GetValue(hud);
                var snapshot = simulation?.GetType().GetProperty("Snapshot", flags);
                if (snapshot == null)
                    throw new InvalidOperationException("The online simulation snapshot is unavailable.");
                await WaitUntilAsync(() => snapshot.GetValue(simulation) is BattleSnapshot value &&
                    value.LastCommandSequence >= 1, deadline.Token);

                await BattleOnlineLaunch.ExitAsync();
                Debug.Log("ASTRAL_ARENA_BATTLE_PLAYER_OK commandSequence>=1");
                Application.Quit(0);
            }
            catch (Exception error)
            {
                string details = error.ToString();
                if (!string.IsNullOrEmpty(fixture?.bearer))
                    details = details.Replace(fixture.bearer, "[redacted]");
                Debug.LogError("ASTRAL_ARENA_BATTLE_PLAYER_FAILED " + details);
                Application.Quit(1);
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
        {
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }
}
