using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaRoom : UIBaseView
    {
        public const string Address = "arena/room";
        public override string PrefabPath => Address;
        public GameButton Leave => Bindings.Require<GameButton>("leave");
        public GameButton Send => Bindings.Require<GameButton>("send");
        public GameText RoomName => Bindings.Require<GameText>("name");
        public GameText Rule => Bindings.Require<GameText>("rule");
        public GameText Count => Bindings.Require<GameText>("count");
        public GameText HostName => Bindings.Require<GameText>("hostName");
        public GameText GuestName => Bindings.Require<GameText>("guestName");
        public GameText Status => Bindings.Require<GameText>("status");
        public TMP_InputField Message => Bindings.Require<TMP_InputField>("message");
        public BindingMap Host => Bindings.Require<BindingMap>("host");
        public BindingMap Guest => Bindings.Require<BindingMap>("guest");
        public Image Vacant => Bindings.Require<Image>("vacant");
        public ScrollRect Members => Bindings.Require<ScrollRect>("members");
        public GameObject MemberRow => Bindings.Require<GameObject>("memberRow");
        public ScrollRect Messages => Bindings.Require<ScrollRect>("messages");
        public GameText ChatRow => Bindings.Require<GameText>("chatRow");
        public override void Validate()
        {
            _ = Leave; _ = Send; _ = RoomName; _ = Rule; _ = Count; _ = HostName; _ = GuestName; _ = Status;
            _ = Message; _ = Host; _ = Guest; _ = Vacant; _ = Members; _ = MemberRow; _ = Messages; _ = ChatRow;
        }
    }
}
