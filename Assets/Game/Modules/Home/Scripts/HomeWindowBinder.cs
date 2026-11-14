using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindowBinder : IDisposable
    {
        private readonly HomeWindow view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task> openArena;
        private readonly CancellationToken lifetime;
        private BindingScope screen;

        public HomeWindowBinder(HomeWindow view, ILocaleService locale, Func<CancellationToken, Task> openArena, CancellationToken lifetime)
        {
            this.view = view;
            this.locale = locale;
            this.openArena = openArena;
            this.lifetime = lifetime;
        }

        public void Show()
        {
            if (screen != null) return;
            screen = new BindingScope(lifetime);
            view.Show();
            view.ArenaLabel.Bind(locale, screen, "home.arena");
            view.Profile.Bind(locale, screen, "home.profile");
            view.Arena.Bind(screen, openArena);
        }

        public void Hide()
        {
            screen?.Dispose();
            screen = null;
            if (view.Root) view.Hide();
        }

        public void Dispose() => Hide();
    }
}
