using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    public sealed class NotificationBadge : UIBaseView
    {
        public override string PrefabPath => "common/notification-badge";
        internal Image Mark { get; private set; }
        internal GameText Label { get; private set; }
        internal Image NormalPlate { get; private set; }
        internal Image MaximumPlate { get; private set; }

        internal NotificationBadge(BindingMap instance) => Attach(instance.gameObject);
        public NotificationBadge() { }

        public override void Validate()
        {
            Mark = Bindings.Require<Image>("mark");
            Label = Bindings.Require<GameText>("label");
            NormalPlate = Bindings.Require<Image>("normalPlate");
            MaximumPlate = Bindings.Require<Image>("maximumPlate");
        }
    }
}
