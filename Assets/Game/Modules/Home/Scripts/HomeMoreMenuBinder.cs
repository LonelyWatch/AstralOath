using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
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
            GameUiFeedback.Play(view.Close, GameUiFeedback.Sound.QuickMenuOpen);
            scope.Add(() =>
            {
                if (!view.Root) return;
                GameUiFeedback.Play(view.Close, GameUiFeedback.Sound.QuickMenuClose);
                view.Hide();
            });
            view.HeroesLabel.Bind(locale, scope, "home.hero_inventory.heroes");
            view.Close.FeedbackSound = view.Outside.FeedbackSound = GameUiFeedback.Sound.Silent;
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
