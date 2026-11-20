using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    public sealed class EquipmentSlot : UIBaseView
    {
        public override string PrefabPath => "common/equipment-slot";
        internal Image Placeholder { get; private set; }
        internal Image Item { get; private set; }
        internal BindingMap Badge { get; private set; }

        internal EquipmentSlot(BindingMap instance) => Attach(instance.gameObject);
        public EquipmentSlot() { }

        public override void Validate()
        {
            _ = Bindings.Require<Image>("frame");
            Placeholder = Bindings.Require<Image>("placeholder");
            Item = Bindings.Require<Image>("item");
            Badge = Bindings.Require<BindingMap>("badge");
        }
    }
}
