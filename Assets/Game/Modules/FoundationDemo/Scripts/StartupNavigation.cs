using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed class StartupNavigation
    {
        private readonly WindowCoordinator windows;
        private readonly HomeWindowBinder home;
        private readonly ArenaDirectoryBinder directory;
        private readonly ArenaRoomBinder room;
        private readonly UIViewRegistry views;
        private readonly IAssetService assets;
        private readonly Transform parent;
        private readonly BindingScope lifetime;
        private readonly ILocaleService locale;
        private readonly ArenaApi api;
        private readonly CancellationToken token;
        private string roomId;

        private StartupNavigation(BindingScope lifetime, HomeWindow homeView, ArenaDirectory directoryView,
            ArenaCreateRoom createView, ArenaRoom roomView, UIViewRegistry views, IAssetService assets, Transform parent,
            ILocaleService locale, ArenaApi api, string playerName, DesktopDisplayMode display)
        {
            this.api = api;
            this.views = views;
            this.assets = assets;
            this.parent = parent;
            this.lifetime = lifetime;
            this.locale = locale;
            token = lifetime.Token;
            windows = new WindowCoordinator(token);
            lifetime.Add(windows.Dispose);
            home = new HomeWindowBinder(homeView, locale, OpenDirectory, display);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, windows, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, windows, ReturnToDirectory, OpenDraft);
            BattleOnlineLaunch.Exited += OnBattleExited;
            lifetime.Add(() => BattleOnlineLaunch.Exited -= OnBattleExited);
        }

        internal static async Task<StartupNavigation> StartAsync(GameLaunchContext context, BindingScope owner)
        {
            var views = new UIViewRegistry();
            views.Register<HomeWindow>();
            views.Register<ArenaDirectory>();
            views.Register<ArenaCreateRoom>();
            views.Register<ArenaRoom>();
            views.Register<ArenaDraft>();
            var assets = context.Services.Get<IAssetService>();
            var locale = await ArenaLabels.LoadAsync(assets, owner, context.TextLocale, context.VoiceLocale);
            var home = await views.CreateAsync<HomeWindow>(assets, context.ViewRoot, owner);
            var directory = await views.CreateAsync<ArenaDirectory>(assets, context.ViewRoot, owner);
            var create = await views.CreateAsync<ArenaCreateRoom>(assets, context.ViewRoot, owner);
            var room = await views.CreateAsync<ArenaRoom>(assets, context.ViewRoot, owner);
            var api = new ArenaApi(context.Services.Get<GameServerEndpoint>().Address, owner.Token);
            string name = Environment.GetEnvironmentVariable("ASTRAL_ARENA_PLAYER_NAME");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = PlayerPrefs.GetString("arena.guest-name", "");
                if (string.IsNullOrEmpty(name))
                {
                    name = GameText.Format(locale, "arena.guest_name", Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant());
                    PlayerPrefs.SetString("arena.guest-name", name);
                    PlayerPrefs.Save();
                }
            }
            var display = context.ViewRoot.GetComponentInParent<DesktopDisplayMode>();
            var navigation = new StartupNavigation(owner, home, directory, create, room, views, assets, context.ViewRoot, locale, api, name, display);
            await navigation.ShowHome();
            context.Services.Add<ILocaleService>(locale);
            context.OpenButton.gameObject.SetActive(false);
            context.StartupStatus.gameObject.SetActive(false);
            return navigation;
        }

        private Task OpenDirectory(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ReturnToDirectory();
        }

        private Task OpenRoom(ArenaRoomState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (state?.room == null) throw new InvalidOperationException("Arena room response is missing.");
            roomId = state.room.id;
            return windows.ShowPageAsync(scope => room.OpenAsync(state, scope));
        }

        private async Task OpenDraft(ArenaDraftState state)
        {
            var loading = new BindingScope(token);
            var release = lifetime.Add(loading.Dispose);
            try
            {
                // Wait for the draft art without adding its model pages to initial home loading.
                var draftView = await views.CreateAsync<ArenaDraft>(assets, parent, loading);
                var draftArt = await assets.LoadAsync<ArenaDraftArt>(ArenaDraftArt.Address, loading);
                loading.Token.ThrowIfCancellationRequested();
                if (roomId != state.roomId) return;
                await windows.ShowPageAsync(scope =>
                {
                    scope.Add(loading.Dispose);
                    scope.Add(release);
                    var draft = new ArenaDraftBinder(draftView, api, locale, draftArt, ReopenRoomAsync, LeaveDraftRoomAsync);
                    scope.Add(draft.Dispose);
                    return draft.OpenAsync(state, scope);
                });
                loading = null;
            }
            finally
            {
                if (loading != null) { release(); loading.Dispose(); }
            }
        }

        private async Task ReopenRoomAsync()
        {
            try { await OpenRoom(await api.GetAsync(roomId, token), token); }
            catch (ArenaRequestException) { await ReturnToDirectory(); }
        }

        private async Task LeaveDraftRoomAsync(CancellationToken cancellation)
        {
            await api.LeaveAsync(roomId, cancellation);
            await ReturnToDirectory();
        }

        private async void OnBattleExited()
        {
            if (token.IsCancellationRequested || string.IsNullOrEmpty(roomId)) return;
            try { await LeaveDraftRoomAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ArenaRequestException) { await ReturnToDirectory(); }
        }

        private Task ReturnToDirectory()
        {
            roomId = null;
            return windows.ShowPageAsync(directory.OpenAsync);
        }

        private Task ShowHome() => windows.ShowPageAsync(home.OpenAsync);
    }
}
