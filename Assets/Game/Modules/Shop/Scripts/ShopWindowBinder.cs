using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Shop
{
    public sealed class ShopWindowBinder
    {
        readonly ShopWindow view;
        readonly ILocaleService locale;
        readonly WindowCoordinator windows;
        readonly ShopExchangeRequests requests;
        readonly Func<CancellationToken,Task<ShopPageDto>> read;
        readonly Func<string,string,CancellationToken,Task<ShopPageDto>> buy;
        readonly Func<Task> back;
        BindingScope page, rows;
        ShopPageDto state;
        const string Confirmation = "shop/confirm-exchange";
        const string Directory = "shop/season-directory";
        public Func<string,CancellationToken,Task> Navigate { get; set; }

        public void ShowCurrencies(long? gold,long? crystals)
        {
            view.Get<GameText>("gold").text=gold?.ToString("N0")??"—";
            view.Get<GameText>("crystals").text=crystals?.ToString("N0")??"—";
        }

        public ShopWindowBinder(ShopWindow view, ILocaleService locale, WindowCoordinator windows, ShopExchangeRequests requests,
            Func<CancellationToken,Task<ShopPageDto>> read, Func<string,string,CancellationToken,Task<ShopPageDto>> buy,
            Func<Task> back)
        {
            this.view=view; this.locale=locale; this.windows=windows; this.requests=requests; this.read=read;
            this.buy=buy; this.back=back;
        }

        public async Task OpenAsync(BindingScope scope)
        {
            page=scope;
            view.Show();
            scope.Add(() => { rows?.Dispose(); view.Hide(); });
            view.Get<GameButton>("back").BindBack(scope, () => windows.TryCloseOverlay() ? Task.CompletedTask : back());
            foreach(string key in new[]{"refresh","featured1","featured2","packages","charge","store","skin","events",
                "glory","trials","constellation","bell","bag","more","goldShop","crystalsShop"})
                view.Get<GameButton>(key).Bind(scope,ct=>Navigate(key,ct));
            var status=view.Get<GameText>("status");
            status.text=locale.Resolve("arena.loading");
            try
            {
                var result=await read(scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                status.text="";
                Render(result);
                view.Get<ScrollRect>("list").horizontalNormalizedPosition=0;
            }
            catch (System.IO.IOException) { if(!scope.Token.IsCancellationRequested) status.text=locale.Resolve("shop.network"); }
        }

        void Render(ShopPageDto next)
        {
            state=next;
            view.Get<GameText>("balance").text=state.balance.ToString("N0");
            rows?.Dispose();
            rows=new BindingScope(page.Token);
            var list=view.Get<ScrollRect>("list");
            var template=view.Get<BindingMap>("item");
            foreach(var offer in state.offers)
            {
                var instance=UnityEngine.Object.Instantiate(template,list.content,false);
                rows.Add(() => { if(instance){instance.gameObject.SetActive(false); UnityEngine.Object.Destroy(instance.gameObject);} });
                var item=new ShopItem(instance);
                int discount=ReferenceDiscount(offer.id);
                var badge=discount>0?view.Get<Image>("sale."+discount).sprite:null;
                new ShopItemBinder(item).Open(rows,offer,view.Get<Image>("art."+offer.id).sprite,badge,locale,
                    ct => offer.period=="directory" ? OpenDirectory(offer) : Confirm(offer));
            }
        }

        // Archival badges observed at 2:13 and 2:16. They do not price a transaction.
        static int ReferenceDiscount(string id) => id switch
        {
            "season0" or "season1" or "season8" or "season9" => 70,
            "season2" => 60, "season3" => 50, "season10" => 40,
            "season4" => 30, "season5" => 20, "season6" => 10, _ => 0
        };

        Task OpenDirectory(ShopOfferDto offer)
        {
            windows.ShowOverlay(Directory, scope =>
            {
                var panel=view.Get<GameObject>("modal");
                var controls=view.Get<BindingMap>("confirmation");
                var close=controls.Require<GameButton>("confirm");
                var cancel=controls.Require<GameButton>("cancel");
                var rect=(RectTransform)close.transform;
                var originalPosition=rect.anchoredPosition;
                rect.anchoredPosition=new Vector2(0,originalPosition.y);
                cancel.gameObject.SetActive(false);
                close.interactable=true;
                panel.SetActive(true);
                controls.Require<GameText>("title").text=offer.name;
                controls.Require<GameText>("confirmLabel").text=locale.Resolve("shop.close");
                var names=new StringBuilder();
                for(int i=0;i<offer.entries.Length;i++)
                {
                    if(i>0) names.Append(i%2==0 ? "\n" : "　　　");
                    names.Append(offer.entries[i]);
                }
                int discount=ReferenceDiscount(offer.id);
                if(discount>0) names.Append('\n').AppendFormat(locale.Resolve("shop.archiveDiscount"),discount);
                controls.Require<GameText>("body").text=names.ToString();
                scope.Add(() =>
                {
                    if(!panel) return;
                    panel.SetActive(false);
                    cancel.gameObject.SetActive(true);
                    rect.anchoredPosition=originalPosition;
                });
                close.Bind(scope,_ => { windows.CloseOverlay(Directory); return Task.CompletedTask; });
            });
            return Task.CompletedTask;
        }

        Task Confirm(ShopOfferDto offer)
        {
            windows.ShowOverlay(Confirmation,scope =>
            {
                var panel=view.Get<GameObject>("modal");
                var controls=view.Get<BindingMap>("confirmation");
                var cancel=controls.Require<GameButton>("cancel");
                var confirm=controls.Require<GameButton>("confirm");
                var body=controls.Require<GameText>("body");
                panel.SetActive(true);
                scope.Add(() => { if(panel) panel.SetActive(false); });
                controls.Require<GameText>("title").text=string.Format(locale.Resolve("shop.confirm"),offer.name);
                controls.Require<GameText>("cancelLabel").text=locale.Resolve("arena.cancel");
                controls.Require<GameText>("confirmLabel").text=locale.Resolve("shop.exchange");
                body.text=string.Format(locale.Resolve("shop.cost"),offer.price,state.balance,offer.owned);
                Action close=() => windows.CloseOverlay(Confirmation);
                cancel.Bind(scope,_ => { close(); return Task.CompletedTask; });
                confirm.interactable=state.balance>=offer.price;
                bool sending=false;
                confirm.Bind(scope,async token =>
                {
                    if(sending) return;
                    sending=true;
                    try
                    {
                        var result=await requests.SubmitAsync(offer.id,buy,token);
                        token.ThrowIfCancellationRequested();
                        close();
                        Render(result);
                        view.Get<GameText>("status").text=string.Format(locale.Resolve("shop.received"),offer.name);
                    }
                    catch(System.IO.IOException ex)
                    {
                        if(!token.IsCancellationRequested) body.text=locale.Resolve(
                            ex.Message=="shop_limit" ? "shop.limit" : ex.Message=="insufficient_conquest" ? "shop.insufficient" : "shop.network");
                    }
                    finally { sending=false; }
                });
            });
            return Task.CompletedTask;
        }
    }
}
