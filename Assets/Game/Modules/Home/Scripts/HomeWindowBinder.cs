using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindowBinder
    {
        private readonly HomeWindow view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task> openArena;

        public HomeWindowBinder(HomeWindow view, ILocaleService locale, Func<CancellationToken, Task> openArena)
        {
            this.view = view;
            this.locale = locale;
            this.openArena = openArena;
        }

        public Task OpenAsync(BindingScope screen)
        {
            view.Show();
            screen.Add(() => { if (view.Root) view.Hide(); });
            view.ArenaLabel.Bind(locale, screen, "home.arena");
            view.Profile.Bind(locale, screen, "home.profile");
            view.Arena.Bind(screen, openArena);
            return Task.CompletedTask;
        }
    }
}
