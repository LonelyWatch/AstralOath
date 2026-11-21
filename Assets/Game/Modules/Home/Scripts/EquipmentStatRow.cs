using AstralOath.Client.Common;
namespace AstralOath.Client.Home
{
    public sealed class EquipmentStatRow : UIBaseView
    {
        public override string PrefabPath => "common/equipment-stat-row";
        internal GameText Label { get; private set; }
        internal GameText Value { get; private set; }

        public EquipmentStatRow() { }
        internal EquipmentStatRow(BindingMap instance) => BindTo(instance.gameObject);

        public override void Validate()
        {
            Label = Bindings.Require<GameText>("label");
            Value = Bindings.Require<GameText>("value");
        }
    }
}
