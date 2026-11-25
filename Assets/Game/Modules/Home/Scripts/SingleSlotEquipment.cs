using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class SingleSlotEquipment : UIBaseView
    {
        public override string PrefabPath => "home/single-slot-equipment";
        internal BindingMap Map => Bindings;
        internal GameObject Comparison => Bindings.Require<GameObject>("comparison");
        internal GameObject Popup => Bindings.Require<GameObject>("popup");
        internal ScrollRect Candidates => Bindings.Require<ScrollRect>("candidates");
        internal GameObject Row => Bindings.Require<GameObject>("row");
        public SingleSlotEquipment() { }
        internal SingleSlotEquipment(BindingMap map) => BindTo(map.gameObject);

        public override void Validate()
        {
            _ = Comparison; _ = Popup; _ = Candidates; _ = Row;
            Bindings.Require<Image>("popupPlate");
            foreach (var key in new[] { "back", "equip", "unequip", "change", "popupRemove", "dismiss", "retry",
                "popupEnhance", "currentEnhance", "candidateEnhance" })
                Bindings.Require<GameButton>(key);
            foreach (var key in new[] { "current", "candidate", "popupItem" })
            {
                var item = Bindings.Require<BindingMap>(key);
                item.Require<BindingMap>("card");
                item.Require<BindingMap>("main");
                item.Require<GameText>("description");
                item.Require<BindingMap>("score");
                for (int i = 0; i < 4; i++) item.Require<BindingMap>("sub" + i);
            }
        }
    }
}
