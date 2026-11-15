using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace AstralOath.Client.BattleScene
{
    public static class BattleOnlineLaunch
    {
        public const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private static readonly List<Behaviour> SuspendedHome = new List<Behaviour>();
        private static bool loading, stagingScene;
        public const string SceneAddress = "battle/online-scene";
        private static Scene onlineScene, home;
        private static ILoadedScene sceneHandle;
        private static Preparation pendingPreparation, activePreparation;

        public static event Action Exited;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            loading = stagingScene = false;
            onlineScene = default;
            home = default;
            sceneHandle = null;
            pendingPreparation = activePreparation = null;
            SuspendedHome.Clear();
            Exited = null;
        }

        public static bool IsActive => loading || onlineScene.IsValid() && onlineScene.isLoaded;

        internal static bool IsStaging(Scene scene) => stagingScene && scene.path == ScenePath;

        public static Preparation BeginPreload(IAssetService assets, ISceneAssetService scenes, CancellationToken token)
        {
            if (assets == null || scenes == null || token.IsCancellationRequested || IsActive ||
                pendingPreparation != null || SceneManager.GetSceneByPath(ScenePath).isLoaded) return null;
            return pendingPreparation = new Preparation(assets, scenes, token);
        }

        // The draft owns this lease until launch consumes it or the draft closes.
        public sealed class Preparation : IDisposable
        {
            private readonly BindingScope scope;
            private readonly Task<StagedScene> scene;
            private bool sceneTaken, disposed;
            internal Task<BattleOnlineTransitionAssets> Assets { get; }
            internal Task Cleanup { get; private set; } = Task.CompletedTask;
            internal bool IsDisposed => disposed;
            internal double SceneLoadSeconds { get; private set; }

            internal Preparation(IAssetService assets, ISceneAssetService scenes, CancellationToken token)
            {
                scope = new BindingScope(token);
                Assets = BattleOnlineTransitionAssets.LoadAsync(assets, scope);
                scene = StageSceneAsync(scenes);
                ObserveFault(Assets);
                ObserveFault(scene);
            }

            private async Task<StagedScene> StageSceneAsync(ISceneAssetService scenes)
            {
                double started = Time.realtimeSinceStartupAsDouble;
                BattleHudPreview host = null;
                GameObject[] activeRoots = null;
                void HideScene(Scene loaded, LoadSceneMode mode)
                {
                    if (mode != LoadSceneMode.Additive || loaded.path != ScenePath) return;
                    activeRoots = loaded.GetRootGameObjects().Where(root => root.activeSelf).ToArray();
                    foreach (var root in activeRoots)
                    {
                        var preview = root.GetComponentInChildren<BattleHudPreview>(true);
                        if (preview) { host = preview; host.AutoStartBattle = false; }
                        root.SetActive(false);
                    }
                }

                stagingScene = true;
                SceneManager.sceneLoaded += HideScene;
                try
                {
                    var handle = await scenes.LoadAdditiveAsync(SceneAddress, scope.Token);
                    if (!host)
                    {
                        await handle.UnloadAsync();
                        throw new InvalidOperationException("The battle scene has no active battle host.");
                    }
                    SceneLoadSeconds = Time.realtimeSinceStartupAsDouble - started;
                    return new StagedScene(handle, host, activeRoots);
                }
                finally
                {
                    SceneManager.sceneLoaded -= HideScene;
                    stagingScene = false;
                }
            }

            public void WarmRoster(IEnumerable<string> heroIds)
            {
                if (disposed || sceneTaken) return;
                var roster = heroIds.ToArray();
                if (roster.Length > 0) ObserveFault(WarmRosterAsync(roster));
            }

            private async Task WarmRosterAsync(string[] roster)
            {
                var staged = await scene;
                if (!disposed && !sceneTaken)
                    await staged.Host.WarmOnlineModelsAsync(roster, scope.Token);
            }

            internal async Task<StagedScene> TakeSceneAsync()
            {
                var staged = await scene;
                sceneTaken = true;
                return staged;
            }

            internal bool IsSceneReady => scene.IsCompleted;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { scope.Dispose(); }
                finally { Cleanup = DiscardAsync(); }
            }

            private async Task DiscardAsync()
            {
                try
                {
                    StagedScene staged;
                    try { staged = await scene; }
                    catch (Exception) when (scene.IsCanceled || scene.IsFaulted) { return; }
                    if (!sceneTaken) await staged.Handle.UnloadAsync();
                }
                catch (Exception error) { Debug.LogException(error); }
                finally { if (pendingPreparation == this) pendingPreparation = null; }
            }
        }

        internal sealed class StagedScene
        {
            internal readonly ILoadedScene Handle;
            internal readonly BattleHudPreview Host;
            private readonly GameObject[] activeRoots;

            internal StagedScene(ILoadedScene handle, BattleHudPreview host, GameObject[] activeRoots)
            {
                Handle = handle; Host = host; this.activeRoots = activeRoots;
            }

            internal void Activate()
            {
                SceneManager.SetActiveScene(Handle.Scene);
                foreach (var root in activeRoots) root.SetActive(true);
            }
        }

        public static async Task LoadAsync(IAssetService assets, ISceneAssetService scenes, Uri endpoint,
            string roomId, string battleId, string contentHash, string arenaBearer, bool localIsHost,
            CancellationToken cancellationToken, BattleOnlinePlayers players = null, Preparation prepared = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assets == null || scenes == null)
                throw new ArgumentException("The active Foundation package must supply battle assets and scenes.");
            if (prepared?.IsDisposed == true)
                throw new ObjectDisposedException(nameof(prepared));
            if (IsActive || pendingPreparation != prepared || (prepared == null && SceneManager.GetSceneByPath(ScenePath).isLoaded))
                throw new InvalidOperationException("A battle scene is already active or being prepared.");
            loading = true;
            home = SceneManager.GetActiveScene();
            activePreparation = prepared ?? new Preparation(assets, scenes, cancellationToken);
            pendingPreparation = null;
            BattleOnlineSession session = null;
            BattleOnlineGate gate = null;
            double started = Time.realtimeSinceStartupAsDouble;
            try
            {
                session = new BattleOnlineSession(endpoint, roomId, battleId, contentHash, arenaBearer, localIsHost)
                {
                    Players = players
                };
                var opening = session.FetchOpeningAsync(cancellationToken);
                ObserveFault(opening);
                var transition = await activePreparation.Assets;
                gate = new BattleOnlineGate(transition);
                await gate.CloseAsync(cancellationToken);
                double closed = Time.realtimeSinceStartupAsDouble;
                SuspendHome();
                while (!activePreparation.IsSceneReady || !opening.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (opening.IsFaulted || opening.IsCanceled) await opening;
                    gate.TickClosed();
                    await Task.Yield();
                }
                var response = await opening;
                var staged = await activePreparation.TakeSceneAsync();
                sceneHandle = staged.Handle;
                onlineScene = sceneHandle.Scene;
                cancellationToken.ThrowIfCancellationRequested();
                double installing = Time.realtimeSinceStartupAsDouble;
                var catalog = staged.Host.AcceptOnlineSnapshot(session, response);
                double authenticated = Time.realtimeSinceStartupAsDouble;
                var roster = staged.Host.ConfigureOnlineRosterAsync(session.Snapshot, cancellationToken);
                ObserveFault(roster);
                while (!roster.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    gate.TickClosed();
                    await Task.Yield();
                }
                await roster;
                double rosterReady = Time.realtimeSinceStartupAsDouble;
                staged.Activate();
                double activated = Time.realtimeSinceStartupAsDouble;
                staged.Host.PrepareOnline(session, transition, catalog);
                session = null;
                if (Debug.isDebugBuild)
                {
                    static string Ms(double seconds) => (seconds * 1000).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                    double installed = Time.realtimeSinceStartupAsDouble;
                    Debug.Log("ASTRAL_ARENA_LOADING preloaded=" + (prepared != null) +
                        " sceneMs=" + Ms(activePreparation.SceneLoadSeconds) + " openingRequestMs=" + Ms(response.ReceivedAt - response.SentAt) +
                        " postCloseWaitMs=" + Ms(installing - closed) + " installMs=" + Ms(installed - installing) +
                        " snapshotMs=" + Ms(authenticated - installing) + " modelWaitMs=" + Ms(rosterReady - authenticated) +
                        " activationMs=" + Ms(activated - rosterReady) +
                        " gateClosedMs=" + Ms(installed - closed) + " launchToOpenMs=" + Ms(installed - started));
                }
                await gate.OpenAsync(cancellationToken);
                staged.Host.BeginOnlineArrival();
                while (!staged.Host.OnlineArrivalIgnitionReady)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                await gate.IgniteAsync(cancellationToken);
                while (!staged.Host.OnlineArrivalComplete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                staged.Host.FinishOnlineEntry();
            }
            catch
            {
                session?.Dispose();
                await UnloadAsync();
                throw;
            }
            finally
            {
                gate?.Dispose();
                loading = false;
            }
        }

        private static void ObserveFault(Task task) =>
            _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        public static async Task ExitAsync()
        {
            if (loading) throw new InvalidOperationException("The battle is still loading.");
            loading = true;
            try { await UnloadAsync(); }
            finally { loading = false; }
            Exited?.Invoke();
        }

        private static void SuspendHome()
        {
            if (!home.IsValid() || !home.isLoaded) return;
            foreach (var root in home.GetRootGameObjects())
            {
                Suspend(root.GetComponentsInChildren<Canvas>());
                Suspend(root.GetComponentsInChildren<Camera>());
                Suspend(root.GetComponentsInChildren<EventSystem>());
                Suspend(root.GetComponentsInChildren<AudioListener>());
            }
        }

        private static void Suspend(IEnumerable<Behaviour> components)
        {
            foreach (var item in components)
            {
                if (!item.enabled) continue;
                SuspendedHome.Add(item);
                item.enabled = false;
            }
        }

        private static async Task UnloadAsync()
        {
            try
            {
                if (home.IsValid() && home.isLoaded) SceneManager.SetActiveScene(home);
                if (sceneHandle != null)
                    await sceneHandle.UnloadAsync();
            }
            finally
            {
                onlineScene = default;
                sceneHandle = null;
                foreach (var item in SuspendedHome)
                    if (item) item.enabled = true;
                SuspendedHome.Clear();
                home = default;
                var preparation = activePreparation;
                activePreparation = null;
                if (preparation != null)
                {
                    preparation.Dispose();
                    await preparation.Cleanup;
                }
            }
        }
    }
}
