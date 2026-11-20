using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class EquipmentSlotBinder
    {
        private readonly EquipmentSlot view;
        private readonly NotificationBadgeBinder badge;

        public EquipmentSlotBinder(BindingMap instance)
        {
            view = new EquipmentSlot(instance);
            badge = new NotificationBadgeBinder(view.Badge);
        }

        public void Set(Sprite item, int enhancement, bool notify = false)
        {
            view.Item.sprite = item;
            view.Item.enabled = item;
            view.Placeholder.enabled = !item && view.Placeholder.sprite;
            badge.Set(notify, item ? enhancement : 0);
        }
    }
}
