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
        public GameButton Start => Bindings.Require<GameButton>("start");
        public GameObject Toast => Bindings.Require<GameObject>("toast");
        public Image RoomLock => Bindings.Require<Image>("roomLock");
        public GameButton Send => Bindings.Require<GameButton>("send");
        public GameButton Emoji => Bindings.Require<GameButton>("emoji");
        public GameObject EmojiPicker => Bindings.Require<GameObject>("emojiPicker");
        public GameText RoomName => Bindings.Require<GameText>("name");
        public GameText Rule => Bindings.Require<GameText>("rule");
        public GameText Count => Bindings.Require<GameText>("count");
        public GameText HostName => Bindings.Require<GameText>("hostName");
        public GameText GuestName => Bindings.Require<GameText>("guestName");
        public GameText Status => Bindings.Require<GameText>("status");
        public GameObject StatusBoard => Bindings.Require<GameObject>("statusBoard");
        public TMP_InputField Message => Bindings.Require<TMP_InputField>("message");
        public BindingMap Host => Bindings.Require<BindingMap>("host");
        public BindingMap Guest => Bindings.Require<BindingMap>("guest");
        public Image Vacant => Bindings.Require<Image>("vacant");
        public GameObject EmptyInfo => Bindings.Require<GameObject>("emptyInfo");
        public ScrollRect Members => Bindings.Require<ScrollRect>("members");
        public GameObject MemberRow => Bindings.Require<GameObject>("memberRow");
        public ScrollRect Messages => Bindings.Require<ScrollRect>("messages");
        public GameObject ChatRow => Bindings.Require<GameObject>("chatRow");
        public override void Validate()
        {
            _ = Leave; _ = Send; _ = RoomName; _ = Rule; _ = Count; _ = HostName; _ = GuestName; _ = Status; _ = StatusBoard;
            _ = Emoji; _ = EmojiPicker; _ = Start; _ = Toast; _ = RoomLock;
            _ = Message; _ = Host; _ = Guest; _ = Vacant; _ = EmptyInfo; _ = Members; _ = MemberRow; _ = Messages; _ = ChatRow;
        }
    }
}
