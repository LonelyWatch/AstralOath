using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.ChannelChat
{
    public sealed class ChannelChat:UIBaseView
    {
        public const string Address="chat/window";
        public override string PrefabPath=>Address;
        public BindingMap Map=>Bindings;
        public override void Validate()
        {
            foreach(string key in new[]{"close","notice","favorite","channelSwitch","opacity","settings","publicTab","guildTab","send","emoji","favoriteSave","channelSave","allNotifications","notification0","notification1","notification2","notification3","notificationTab","guildNotificationTab","keywordTab","favoriteClose","channelClose","settingsClose","stickerClose"})Bindings.Require<GameButton>(key);
            foreach(string key in new[]{"input","favoriteInput","channelInput","keyword0","keyword1","keyword2","keyword3","keyword4"})Bindings.Require<TMP_InputField>(key);
            foreach(string key in new[]{"favoriteLayer","channelLayer","settingsLayer","notificationContent","keywordContent","stickerLayer","stickerCell","row","messageChrome"})Bindings.Require<GameObject>(key);
            Bindings.Require<RectTransform>("drawer");Bindings.Require<Image>("drawerSurface");Bindings.Require<ScrollRect>("messages");Bindings.Require<ScrollRect>("stickerScroll");Bindings.Require<BindingMap>("stickerArt");
        }
    }
}
