using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;

namespace AstralOath.Client.Home
{
    public sealed class HomeMoreMenuBinder
    {
        private readonly HomeMoreMenu view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task> openHeroes;
        private readonly Action close;

        public HomeMoreMenuBinder(HomeMoreMenu view, ILocaleService locale,
            Func<CancellationToken, Task> openHeroes, Action close)
        {
            this.view = view;
            this.locale = locale;
            this.openHeroes = openHeroes;
            this.close = close;
        }

        public void Open(BindingScope scope)
        {
            view.Show();
            scope.Add(() => { if (view.Root) view.Hide(); });
            view.HeroesLabel.Bind(locale, scope, "home.hero_inventory.heroes");
            view.Heroes.Bind(scope, openHeroes);
            view.Close.Bind(scope, CloseAsync);
            view.Outside.Bind(scope, CloseAsync);
        }

        private Task CloseAsync(CancellationToken token)
        {
            close();
            return Task.CompletedTask;
        }
    }
}
