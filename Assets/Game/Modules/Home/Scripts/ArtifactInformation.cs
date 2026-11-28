using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class ArtifactInformation : UIBaseView
    {
        public const string Address = "home/artifact-information";
        public override string PrefabPath => Address;
        internal BindingMap Map => Bindings;
        public override void Validate()
        {
            Bindings.Require<GameButton>("close");
            Bindings.Require<GameObject>("painting");
            Bindings.Require<Image>("card");
        }
    }
}
