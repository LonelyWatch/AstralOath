using AstralOath.Client.Common;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    // Shared artwork lives in Common. This ordinary view stays in the existing hot
    // assembly so adding the banner does not change the stable Common/AOT boundary.
    public sealed class FeatureNotice : UIBaseView
    {
        public const string Address="common/feature-notice";
        public override string PrefabPath=>Address;
        public GameButton Dismiss=>Bindings.Require<GameButton>("dismiss");
        public GameText Title=>Bindings.Require<GameText>("title");
        public GameText Message=>Bindings.Require<GameText>("message");
        public GameText CloseLabel=>Bindings.Require<GameText>("closeLabel");
        public override void Validate(){_=Dismiss;_=Title;_=Message;_=CloseLabel;}
        public static FeatureNotice ForInstance(GameObject instance)
        {
            var view=new FeatureNotice();view.BindTo(instance);return view;
        }
    }
}
