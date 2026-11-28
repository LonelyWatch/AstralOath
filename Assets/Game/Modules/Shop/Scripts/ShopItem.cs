using AstralOath.Client.Common;
using UnityEngine.UI;

namespace AstralOath.Client.Shop
{
    public sealed class ShopItem : UIBaseView
    {
        public override string PrefabPath => "shop/item";
        public ShopItem() { }
        internal ShopItem(BindingMap map) => BindTo(map.gameObject);
        internal T Get<T>(string key) where T : UnityEngine.Object => Bindings.Require<T>(key);
        public override void Validate()
        {
            _ = Get<GameButton>("action");
            _ = Get<GameButton>("directory");
            _ = Get<Image>("icon");
            _ = Get<GameText>("name");
        }
    }
}
