using AstralOath.Client.Common;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class EquipmentItemCard : UIBaseView
    {
        public override string PrefabPath => "common/equipment-item-card";
        internal GameButton Select { get; private set; }
        internal Image Icon { get; private set; }
        internal Image GradeFrame { get; private set; }
        internal Image SetIcon { get; private set; }
        internal Image StatIcon { get; private set; }
        internal Image WearerIcon { get; private set; }
        internal Image SelectedPlate { get; private set; }
        internal Image Highlight { get; private set; }
        internal GameText Level { get; private set; }
        internal GameText Stat { get; private set; }
        internal BindingMap Badge { get; private set; }

        public EquipmentItemCard() { }
        internal EquipmentItemCard(BindingMap instance) => BindTo(instance.gameObject);

        public override void Validate()
        {
            Select = Bindings.Require<GameButton>("select");
            Icon = Bindings.Require<Image>("icon");
            GradeFrame = Bindings.Require<Image>("gradeFrame");
            SetIcon = Bindings.Require<Image>("setIcon");
            StatIcon = Bindings.Require<Image>("statIcon");
            WearerIcon = Bindings.Require<Image>("wearerIcon");
            SelectedPlate = Bindings.Require<Image>("selectedPlate");
            Highlight = Bindings.Require<Image>("highlight");
            Level = Bindings.Require<GameText>("level");
            Stat = Bindings.Require<GameText>("stat");
            Badge = Bindings.Require<BindingMap>("badge");
        }
    }
}
