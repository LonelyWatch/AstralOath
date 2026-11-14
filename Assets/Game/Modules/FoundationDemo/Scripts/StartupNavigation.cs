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
        private readonly BindingScope lifetime;
        private readonly HomeWindowBinder home;
        private readonly ArenaDirectoryBinder directory;
        private readonly ArenaRoomBinder room;

        private StartupNavigation(BindingScope lifetime, HomeWindow homeView, ArenaDirectory directoryView,
            ArenaCreateRoom createView, ArenaRoom roomView, ILocaleService locale, ArenaApi api, string playerName)
        {
            this.lifetime = lifetime;
            home = new HomeWindowBinder(homeView, locale, OpenDirectory, lifetime.Token);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, ReturnToDirectory);
            lifetime.Add(room.Dispose);
            lifetime.Add(directory.Dispose);
            lifetime.Add(home.Dispose);
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
            var api = new ArenaApi(context.Services.Get<GameServerEndpoint>().Address);
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
            navigation.ShowHome();
            context.Services.Add<ILocaleService>(locale);
            context.OpenButton.gameObject.SetActive(false);
            context.StartupStatus.gameObject.SetActive(false);
            return navigation;
        }

        private async Task OpenDirectory(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            home.Hide();
            await directory.ShowAsync(lifetime.Token);
        }

        private Task OpenRoom(ArenaRoomState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (state?.room == null) throw new InvalidOperationException("Arena room response is missing.");
            directory.Dispose();
            room.Show(state, lifetime.Token);
            return Task.CompletedTask;
        }

        private Task ReturnToDirectory()
        {
            room.Dispose();
            return directory.ShowAsync(lifetime.Token);
        }

        private void ShowHome()
        {
            directory.Dispose();
            room.Dispose();
            home.Show();
        }

    }
}
