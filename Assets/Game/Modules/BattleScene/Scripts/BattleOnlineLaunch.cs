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
        private static bool loading;
        public const string SceneAddress = "battle/online-scene";
        private static Scene onlineScene, home;
        private static ILoadedScene sceneHandle;
        private static BindingScope assetScope;

        public static event Action Exited;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            loading = false;
            onlineScene = default;
            home = default;
            sceneHandle = null;
            assetScope = null;
            SuspendedHome.Clear();
            Exited = null;
        }

        public static bool IsActive => loading || onlineScene.IsValid() && onlineScene.isLoaded;

        public static async Task LoadAsync(IAssetService assets, ISceneAssetService scenes, Uri endpoint,
            string roomId, string battleId, string contentHash, string arenaBearer, bool localIsHost,
            CancellationToken cancellationToken, BattleOnlinePlayers players = null)
        {
            if (IsActive || SceneManager.GetSceneByPath(ScenePath).isLoaded)
                throw new InvalidOperationException("A battle scene is already active.");
            cancellationToken.ThrowIfCancellationRequested();
            if (assets == null || scenes == null)
                throw new ArgumentException("The active Foundation package must supply battle assets and scenes.");
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
                assetScope = new BindingScope(cancellationToken);
                var transition = await BattleOnlineTransitionAssets.LoadAsync(assets, assetScope);
                using var gate = new BattleOnlineGate(transition);
                await gate.CloseAsync(cancellationToken);
                SuspendHome();
                SceneManager.sceneLoaded += PrepareHost;
                var operation = scenes.LoadAdditiveAsync(SceneAddress, cancellationToken);
                while (!operation.IsCompleted)
                {
                    gate.TickClosed();
                    await Task.Yield();
                }
                sceneHandle = await operation;
                onlineScene = sceneHandle.Scene;
                cancellationToken.ThrowIfCancellationRequested();
                if (!host) throw new InvalidOperationException("The battle scene has no battle host.");
                SceneManager.SetActiveScene(onlineScene);
                session = new BattleOnlineSession(endpoint, roomId, battleId, contentHash, arenaBearer, localIsHost);
                session.Players = players;
                var preparing = host.PrepareOnlineAsync(session, transition, cancellationToken);
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
                var scope = assetScope;
                assetScope = null;
                scope?.Dispose();
            }
        }
    }
}
