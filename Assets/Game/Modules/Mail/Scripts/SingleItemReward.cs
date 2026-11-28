using System;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Mail
{
    // Ordinary view in an already registered hot assembly. The Common prefab has no business component.
    public sealed class SingleItemReward:UIBaseView
    {
        public const string Address="common/single-item-reward";
        public override string PrefabPath=>Address;
        public BindingMap Map=>Bindings;
        public override void Validate()
        {
            var effect=Bindings.Require<Image>("effect");
            if(!effect.sprite)throw new InvalidOperationException("The original reward atlas sprite is missing.");
            Bindings.Require<Image>("icon");Bindings.Require<RectTransform>("particles");
            foreach(string key in new[]{"quantity","kind","text"})Bindings.Require<GameText>(key);
        }
        public static SingleItemReward ForInstance(GameObject root){var view=new SingleItemReward();view.BindTo(root);return view;}
    }
}
