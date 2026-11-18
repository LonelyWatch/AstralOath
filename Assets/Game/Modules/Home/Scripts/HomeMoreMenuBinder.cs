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
        private readonly Func<CancellationToken, Task> openHeroes, openSummon;
        private readonly Action close;

        public HomeMoreMenuBinder(HomeMoreMenu view, ILocaleService locale,
            Func<CancellationToken, Task> openHeroes, Func<CancellationToken, Task> openSummon, Action close)
        {
            this.view = view;
            this.locale = locale;
            this.openHeroes = openHeroes;
            this.openSummon = openSummon;
            this.close = close;
        }

        public void Open(BindingScope scope)
        {
            view.Show();
            scope.Add(() => { if (view.Root) view.Hide(); });
            view.HeroesLabel.Bind(locale, scope, "home.hero_inventory.heroes");
            view.Heroes.Bind(scope, openHeroes);
            view.Summon.Bind(scope, openSummon);
            view.Close.Bind(scope, CloseAsync);
            view.Outside.Bind(scope, CloseAsync);
            _ = view.AnimateOpeningAsync(scope.Token);
        }

        private Task CloseAsync(CancellationToken token)
        {
            close();
            return Task.CompletedTask;
        }
    }
}
