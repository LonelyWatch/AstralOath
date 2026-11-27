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
        private Func<CancellationToken,Task> openArena,openBag;
        private Func<string,CancellationToken,Task> unavailable;

        public HomeMoreMenuBinder(HomeMoreMenu view, ILocaleService locale,
            Func<CancellationToken, Task> openHeroes, Func<CancellationToken, Task> openSummon, Action close)
        {
            this.view = view;
            this.locale = locale;
            this.openHeroes = openHeroes;
            this.openSummon = openSummon;
            this.close = close;
        }

        public void ConfigureNavigation(Func<CancellationToken,Task> arena,Func<string,CancellationToken,Task> unavailable,Func<CancellationToken,Task> bag=null)
        {openArena=arena;this.unavailable=unavailable;openBag=bag;}

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
            view.Close.FeedbackSound = view.Outside.FeedbackSound = GameUiFeedback.Sound.Silent;
            view.Heroes.Bind(scope, openHeroes);
            view.Summon.Bind(scope, openSummon);
            view.Feature("arena").Bind(scope,openArena??(token=>Unavailable("arena",token)));
            view.Bag.FeedbackSound=GameUiFeedback.Sound.InventoryOpen;view.Bag.Bind(scope,openBag??(token=>Unavailable("bag",token)));
            foreach(var id in HomeFeatureLabels.Main)
            {if(HomeFeatureLabels.MainAvailable(id))continue;string feature=id;view.Feature(feature).Bind(scope,token=>Unavailable(feature,token));}
            foreach(var id in HomeFeatureLabels.Drawer)
            {if(id=="heroes")continue;string feature=id;view.Feature(feature).Bind(scope,token=>Unavailable(feature,token));}
            foreach(var id in HomeFeatureLabels.Bottom)
            {string feature=id;view.Feature(feature).Bind(scope,token=>Unavailable(feature,token));}
            view.Close.Bind(scope, CloseAsync);
            view.Outside.Bind(scope, CloseAsync);
            _ = view.AnimateOpeningAsync(scope.Token);
        }

        private Task Unavailable(string id,CancellationToken token)=>unavailable?.Invoke(HomeFeatureLabels.Key(id),token)??Task.CompletedTask;

        private Task CloseAsync(CancellationToken token)
        {
            close();
            return Task.CompletedTask;
        }
    }
}
