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
        internal GameObject DraftSettings => Bindings.Require<GameObject>("draftSettings");
        internal GameButton PrebanCount => Bindings.Require<GameButton>("prebanCount");
        internal GameText PrebanLabel => Bindings.Require<GameText>("prebanLabel");
        internal GameObject PrebanPicker => Bindings.Require<GameObject>("prebanPicker");
        internal GameButton BanProtection => Bindings.Require<GameButton>("banProtection");
        internal GameText ProtectionLabel => Bindings.Require<GameText>("protectionLabel");
        internal GameObject ProtectionPicker => Bindings.Require<GameObject>("protectionPicker");
        internal GameButton FirstPick => Bindings.Require<GameButton>("firstPick");
        internal Image FirstPickCheck => Bindings.Require<Image>("firstPickCheck");
        internal GameText FirstPickLabel => Bindings.Require<GameText>("firstPickLabel");
        internal GameButton GuestFirstPick => Bindings.Require<GameButton>("guestFirstPick");
        internal Image GuestFirstPickCheck => Bindings.Require<Image>("guestFirstPickCheck");
        internal GameText GuestFirstPickLabel => Bindings.Require<GameText>("guestFirstPickLabel");
        public GameObject Toast => Bindings.Require<GameObject>("toast");
        public Image RoomLock => Bindings.Require<Image>("roomLock");
        public GameButton Send => Bindings.Require<GameButton>("send");
        public GameButton Emoji => Bindings.Require<GameButton>("emoji");
        public GameButton ChannelTab => Bindings.Require<GameButton>("channelTab");
        public GameButton ArenaTab => Bindings.Require<GameButton>("arenaTab");
        public BindingMap ChannelUnreadBadge => Bindings.Require<BindingMap>("channelUnreadBadge");
        internal Image ChannelSelected => Bindings.Require<Image>("channelSelected");
        internal Image ArenaSelected => Bindings.Require<Image>("arenaSelected");
        internal RectTransform PublicChannelHost => Bindings.Require<RectTransform>("publicChannelHost");
        internal GameObject RoomChatContents => Bindings.Require<GameObject>("roomChatContents");
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
        internal GameObject SystemRow => Bindings.Require<GameObject>("systemRow");
        internal GameText StartLabel => Bindings.Require<GameText>("startLabel");
        internal GameText GuestReady => Bindings.Require<GameText>("guestReady");
        public override void Validate()
        {
            _ = Leave; _ = Send; _ = RoomName; _ = Rule; _ = Count; _ = HostName; _ = GuestName; _ = Status; _ = StatusBoard;
            _ = Emoji; _ = EmojiPicker; _ = Start; _ = StartLabel; _ = GuestReady; _ = Toast; _ = RoomLock;
            _ = ChannelTab; _ = ArenaTab; _ = ChannelUnreadBadge; _ = ChannelSelected; _ = ArenaSelected; _ = PublicChannelHost; _ = RoomChatContents;
            _ = DraftSettings; _ = PrebanCount; _ = PrebanLabel; _ = BanProtection; _ = ProtectionLabel;
            _ = FirstPick; _ = FirstPickCheck; _ = FirstPickLabel;
            _ = GuestFirstPick; _ = GuestFirstPickCheck; _ = GuestFirstPickLabel;
            ValidateSettingsPicker(PrebanPicker, 4);
            ValidateSettingsPicker(ProtectionPicker, 2);
            _ = Message; _ = Host; _ = Guest; _ = Vacant; _ = EmptyInfo; _ = Members; _ = MemberRow; _ = Messages; _ = ChatRow; _ = SystemRow;
        }

        private static void ValidateSettingsPicker(GameObject picker, int choices)
        {
            var bindings = picker.GetComponent<BindingMap>();
            _ = bindings.Require<GameButton>("dismiss");
            for (int i = 0; i < choices; i++)
            {
                _ = bindings.Require<GameButton>("choice" + i);
                _ = bindings.Require<GameText>("choiceLabel" + i);
                _ = bindings.Require<Image>("selected" + i);
            }
        }
    }
}
