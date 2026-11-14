using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaCreateRoom : UIBaseView
    {
        public const string Address = "arena/create-room";
        public override string PrefabPath => Address;
        public TMP_InputField RoomName => Bindings.Require<TMP_InputField>("name");
        public TMP_InputField Password => Bindings.Require<TMP_InputField>("password");
        public Toggle Locked => Bindings.Require<Toggle>("locked");
        public GameButton Allow => Bindings.Require<GameButton>("allow");
        public GameButton Deny => Bindings.Require<GameButton>("deny");
        public GameButton Confirm => Bindings.Require<GameButton>("confirm");
        public GameButton Cancel => Bindings.Require<GameButton>("cancel");
        public GameButton Dismiss => Bindings.Require<GameButton>("dismiss");
        public GameText Status => Bindings.Require<GameText>("status");
        public ScrollRect Rules => Bindings.Require<ScrollRect>("rules");
        public GameButton Rule(string name) => Bindings.Require<GameButton>("rule_" + name);
        public Image Selection(string name) => Bindings.Require<Image>("selection_" + name);
        public override void Validate()
        {
            _ = RoomName; _ = Password; _ = Locked; _ = Allow; _ = Deny; _ = Confirm; _ = Cancel; _ = Dismiss; _ = Status; _ = Rules;
            foreach (string rule in ArenaLabels.Rules) { _ = Rule(rule); _ = Selection(rule); }
        }
    }
}
