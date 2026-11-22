using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class DevelopmentGmWindow : UIBaseView
    {
        public const string Address = "development/gm-window";
        public override string PrefabPath => Address;

        internal GameObject Panel => Bindings.Require<GameObject>("panel");
        internal GameObject Editor => Bindings.Require<GameObject>("editor");
        internal GameObject InventoryKinds => Bindings.Require<GameObject>("kinds");
        internal ScrollRect Items => Bindings.Require<ScrollRect>("items");
        internal GameButton Row => Bindings.Require<GameButton>("row");
        internal TMP_InputField Amount => Bindings.Require<TMP_InputField>("amount");
        internal TMP_InputField Filter => Bindings.Require<TMP_InputField>("filter");
        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameText Label(string key) => Bindings.Require<GameText>(key);

        public override void Validate()
        {
            _ = Panel; _ = Editor; _ = InventoryKinds; _ = Items; _ = Row; _ = Amount; _ = Filter;
            foreach (string key in new[] { "entry", "close", "resources", "inventory", "diagnostics", "hero", "artifact",
                "equipment", "submit", "refresh", "copy" }) _ = Button(key);
            foreach (string key in new[] { "selection", "status", "report", "submitLabel", "amountLabel" }) _ = Label(key);
        }
    }
}
