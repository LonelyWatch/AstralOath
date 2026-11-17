using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Common;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindowBinder
    {
        private readonly HomeWindow view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task> openArena;
        private readonly Func<CancellationToken, Task> openMore;
        private readonly Func<CancellationToken, Task> openBag;
        private readonly DesktopDisplayMode display;

        public HomeWindowBinder(HomeWindow view, ILocaleService locale, Func<CancellationToken, Task> openArena,
            Func<CancellationToken, Task> openMore, Func<CancellationToken, Task> openBag, DesktopDisplayMode display)
        {
            this.view = view;
            this.locale = locale;
            this.openArena = openArena;
            this.openMore = openMore;
            this.openBag = openBag;
            this.display = display;
        }

        public Task OpenAsync(BindingScope screen)
        {
            view.Show();
            screen.Add(() => { if (view.Root) view.Hide(); });
            view.ArenaLabel.Bind(locale, screen, "home.arena");
            view.Profile.Bind(locale, screen, "home.profile");
            view.Arena.Bind(screen, openArena);
            view.More.Bind(screen, openMore);
            view.Bag.Bind(screen, openBag);
            var button = view.DisplayMode;
            button.gameObject.SetActive(display && display.Available);
            if (button.gameObject.activeSelf)
            {
                UpdateDisplayLabel();
                display.Changed += UpdateDisplayLabel;
                screen.Add(() => { if (display) display.Changed -= UpdateDisplayLabel; });
                button.Bind(screen, ignored => { display.Toggle(); return Task.CompletedTask; });
            }
            return Task.CompletedTask;
        }

        public void HideArenaWhile(BindingScope overlay)
        {
            var navigation = view.ArenaNavigation.gameObject;
            bool wasActive = navigation.activeSelf;
            navigation.SetActive(false);
            overlay.Add(() => { if (navigation) navigation.SetActive(wasActive); });
        }

        private void UpdateDisplayLabel() => view.DisplayModeLabel.text = locale.Resolve(display.FullScreen
            ? "home.window.windowed" : "home.window.fullscreen");
    }
}
