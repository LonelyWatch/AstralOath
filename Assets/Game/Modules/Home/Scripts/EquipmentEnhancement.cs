using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class EquipmentEnhancement : UIBaseView
    {
        public override string PrefabPath => "home/equipment-enhancement";
        internal BindingMap Map => Bindings;
        public EquipmentEnhancement() { }
        internal EquipmentEnhancement(BindingMap map) => BindTo(map.gameObject);
        public override void Validate()
        {
            foreach (var key in new[] { "back", "submit", "clear", "auto", "quantityConfirm", "quantityCancel", "quantityMin", "quantityMax", "quantityMinus", "quantityPlus", "powderOnly", "auto3", "auto6", "auto9", "auto12", "auto15" })
                Bindings.Require<GameButton>(key);
            Bindings.Require<Slider>("quantitySlider");
            Bindings.Require<GameObject>("quantityPopup"); Bindings.Require<GameObject>("autoPopup");
            Bindings.Require<BindingMap>("card"); Bindings.Require<BindingMap>("thumbnail");
            for (int i = 0; i < 4; i++) Bindings.Require<BindingMap>("row" + i);
            Bindings.Require<Image>("powderOnlyCheck");
            for (int stage = 3; stage <= 15; stage += 3) Bindings.Require<Image>("auto" + stage + "Selected");
            for (int i = 0; i < 4; i++) Bindings.Require<RawImage>("subSweep" + i);
            Bindings.Require<RawImage>("scoreSweep");
            Bindings.Require<AudioSource>("audio");
        }
    }
}
