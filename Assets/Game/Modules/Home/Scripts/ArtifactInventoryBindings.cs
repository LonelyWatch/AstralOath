using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed class ArtifactInventoryBindings
    {
        internal static readonly string[] Actions =
        {
            "equip", "unequip", "lock", "enhance", "sell", "lesserCharm", "greaterCharm", "epicCharm", "bottle",
            "chooseCopy", "cancel", "confirm"
        };

        private readonly BindingMap map;
        internal ArtifactInventoryBindings(BindingMap map) => this.map = map;
        internal T Get<T>(string key) where T : Object => map.Require<T>("artifact" + key);
        internal GameButton Action(string key) => Get<GameButton>("Action_" + key);
        internal GameText ActionLabel(string key) => Get<GameText>("ActionLabel_" + key);

        internal void Validate()
        {
            _ = Get<GameButton>("Slot");
            _ = Get<GameButton>("PickerClose");
            _ = Get<GameButton>("PickerShade");
            _ = Get<GameButton>("Sort");
            _ = Get<GameButton>("Retry");
            _ = Get<GameButton>("ConfirmShade");
            _ = Get<ScrollRect>("Items");
            _ = Get<RectTransform>("Browser");
            _ = Get<Image>("BrowserPlate");
            _ = Get<Image>("EmptyPlate");
            _ = Get<Image>("Portrait");
            _ = Get<Image>("SlotImage");
            foreach (string item in new[] { "Card", "Detail", "Confirmation", "Materials", "EmptyIcon", "EmptyMessage" })
                _ = Get<GameObject>(item);
            foreach (string key in new[] { "Capacity", "Level", "SlotLevel", "Attack", "Health", "Status", "Title", "Wearer",
                "Skill", "PickerTitle", "ConfirmText", "ConfirmTitle", "RetryLabel", "SortLabel" })
                _ = Get<GameText>(key);
            foreach (string key in Actions)
            {
                _ = Action(key);
                _ = ActionLabel(key);
            }
            for (int index = 0; index < 5; ++index)
            {
                _ = Get<Image>("Break" + index);
                _ = Get<Image>("Rarity" + index);
            }
            for (int index = 0; index < 8; index++)
            {
                _ = Get<GameButton>("Filter" + index);
                _ = Get<Image>("FilterActive" + index);
                if (index > 0) _ = Get<Image>("ClassIcon" + index);
            }
        }
    }
}
