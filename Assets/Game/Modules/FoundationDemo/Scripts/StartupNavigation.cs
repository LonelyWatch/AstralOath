using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
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

        private StartupNavigation(BindingScope lifetime, HomeWindow homeView, ArenaDirectory directoryView,
            ArenaCreateRoom createView, ArenaRoom roomView, ILocaleService locale, ArenaApi api, string playerName)
        {
            windows = new WindowCoordinator(lifetime.Token);
            lifetime.Add(windows.Dispose);
            home = new HomeWindowBinder(homeView, locale, OpenDirectory);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, windows, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, ReturnToDirectory);
        }

        internal static async Task<StartupNavigation> StartAsync(GameLaunchContext context, BindingScope owner)
        {
            var views = new UIViewRegistry();
            views.Register<HomeWindow>();
            views.Register<ArenaDirectory>();
            views.Register<ArenaCreateRoom>();
            views.Register<ArenaRoom>();
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
            var navigation = new StartupNavigation(owner, home, directory, create, room, locale, api, name);
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
            return windows.ShowPageAsync(scope => room.OpenAsync(state, scope));
        }

        private Task ReturnToDirectory() => windows.ShowPageAsync(directory.OpenAsync);

        private Task ShowHome() => windows.ShowPageAsync(home.OpenAsync);
    }
}
