using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Shop
{
    internal sealed class ShopItemBinder
    {
        readonly ShopItem view;
        internal ShopItemBinder(ShopItem view) => this.view = view;

        internal void Open(BindingScope owner, ShopOfferDto item, Sprite icon, Sprite discount, ILocaleService locale,
            Func<CancellationToken, Task> select)
        {
            view.Get<GameText>("name").text = item.name;
            view.Get<Image>("icon").sprite = icon;
            var badge=view.Get<Image>("sale");
            badge.sprite=discount;
            badge.enabled=discount!=null;
            var quota = view.Get<GameText>("quota");
            quota.transform.parent.gameObject.SetActive(item.limit > 0);
            quota.text = item.remaining + "/" + item.limit;
            bool directory = item.period == "directory";
            view.Get<GameText>("period").text = directory ? string.Format(locale.Resolve("shop.directoryCount"),item.entries.Length)
                : item.limit > 0 ? locale.Resolve("shop."+item.period) : "";
            var action = view.Get<GameButton>("action");
            var browse = view.Get<GameButton>("directory");
            action.gameObject.SetActive(!directory);
            browse.gameObject.SetActive(directory);
            if (directory) browse.Bind(owner, select);
            else
            {
                view.Get<GameText>("price").text = item.price.ToString("N0");
                action.interactable = item.limit == 0 || item.remaining > 0;
                action.Bind(owner, select);
            }
            view.Show();
        }
    }
}
