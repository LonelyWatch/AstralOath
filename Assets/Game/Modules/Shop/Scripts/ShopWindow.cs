using System;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace AstralOath.Client.Shop
{
    public sealed class ShopWindow : UIBaseView
    {
        public const string Address = "shop/window";
        public override string PrefabPath => Address;
        internal T Get<T>(string key) where T : UnityEngine.Object => Bindings.Require<T>(key);
        public override void Validate()
        {
            _ = Get<GameButton>("back");
            _ = Get<ScrollRect>("list");
            _ = Get<BindingMap>("item");
            _ = Get<BindingMap>("confirmation");
        }
    }

    [Serializable, Preserve] public sealed class ShopPageDto { public long balance; public ShopOfferDto[] offers; }
    [Serializable, Preserve] public sealed class ShopOfferDto
    {
        public string id, name, material, period;
        public string[] entries;
        public int quantity, price, limit, remaining, owned;
    }
}
