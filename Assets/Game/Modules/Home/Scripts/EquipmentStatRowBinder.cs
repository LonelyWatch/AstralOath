using AstralOath.Client.Common;
namespace AstralOath.Client.Home
{
    public sealed class EquipmentStatRowBinder
    {
        private readonly EquipmentStatRow view;

        public EquipmentStatRowBinder(BindingMap instance) => view = new EquipmentStatRow(instance);

        public void Set(string label, string value)
        {
            view.Label.text = label;
            view.Value.text = value;
        }
    }
}
