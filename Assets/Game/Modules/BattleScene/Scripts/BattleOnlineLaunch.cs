using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace AstralOath.Client.BattleScene
{
    public static class BattleOnlineLaunch
    {
        public const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private static readonly List<Behaviour> SuspendedHome = new List<Behaviour>();
        private static bool loading;
        private static Scene onlineScene, home;

        public static event Action Exited;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            loading = false;
            onlineScene = default;
            home = default;
            SuspendedHome.Clear();
            Exited = null;
        }

        public static bool IsActive => loading || onlineScene.IsValid() && onlineScene.isLoaded;

        public static Task LoadAsync(Uri endpoint, string roomId, string battleId, string contentHash,
            string arenaBearer, bool localIsHost, CancellationToken cancellationToken)
            => LoadAsync(endpoint, roomId, battleId, contentHash, arenaBearer, localIsHost, cancellationToken, null);

        public static async Task LoadAsync(Uri endpoint, string roomId, string battleId, string contentHash,
            string arenaBearer, bool localIsHost, CancellationToken cancellationToken, BattleOnlinePlayers players)
        {
            if (IsActive || SceneManager.GetSceneByPath(ScenePath).isLoaded)
                throw new InvalidOperationException("A battle scene is already active.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!Application.CanStreamedLevelBeLoaded(ScenePath))
                throw new InvalidOperationException("The current build does not contain the battle scene.");
            loading = true;
            home = SceneManager.GetActiveScene();
            BattleOnlineSession session = null;
            BattleHudPreview host = null;

            void PrepareHost(Scene scene, LoadSceneMode mode)
            {
                if (mode != LoadSceneMode.Additive || scene.path != ScenePath) return;
                onlineScene = scene;
                host = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>(true)).SingleOrDefault();
                if (host) host.AutoStartBattle = false;
            }

            try
            {
                var assets = BattleOnlineTransitionAssets.Load();
                using var gate = new BattleOnlineGate(assets);
                await gate.CloseAsync(cancellationToken);
                SuspendHome();
                SceneManager.sceneLoaded += PrepareHost;
                var operation = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
                if (operation == null) throw new InvalidOperationException("Unable to start loading the battle scene.");
                // Unity scene loads cannot be aborted. Wait until the scene exists so a canceled launch can unload it.
                while (!operation.isDone)
                {
                    gate.TickClosed();
                    await Task.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!host) throw new InvalidOperationException("The battle scene has no battle host.");
                SceneManager.SetActiveScene(onlineScene);
                session = new BattleOnlineSession(endpoint, roomId, battleId, contentHash, arenaBearer, localIsHost);
                session.Players = players;
                var preparing = host.PrepareOnlineAsync(session, assets, cancellationToken);
                while (!preparing.IsCompleted)
                {
                    gate.TickClosed();
                    await Task.Yield();
                }
                await preparing;
                session = null;
                await gate.OpenAsync(cancellationToken);
                host.BeginOnlineArrival();
                await gate.IgniteAsync(cancellationToken);
                while (!host.OnlineArrivalComplete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                host.FinishOnlineEntry();
            }
            catch
            {
                session?.Dispose();
                await UnloadAsync();
                throw;
            }
            finally
            {
                SceneManager.sceneLoaded -= PrepareHost;
                loading = false;
            }
        }

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
                if (onlineScene.IsValid() && onlineScene.isLoaded)
                {
                    var unloading = SceneManager.UnloadSceneAsync(onlineScene);
                    while (unloading != null && !unloading.isDone) await Task.Yield();
                }
            }
            finally
            {
                onlineScene = default;
                foreach (var item in SuspendedHome)
                    if (item) item.enabled = true;
                SuspendedHome.Clear();
                home = default;
            }
        }
    }
}
