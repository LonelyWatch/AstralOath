using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class HeroInformation : UIBaseView
    {
        public const string Address = "home/hero-information";
        public override string PrefabPath => Address;
        internal BindingMap Map => Bindings;
        public override void Validate()
        {
            Bindings.Require<GameButton>("close");
            Bindings.Require<RawImage>("animated");
            Bindings.Require<GameObject>("skillTemplate");
        }
    }
}
