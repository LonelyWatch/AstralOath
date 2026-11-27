using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.PlayerProfile
{
    public sealed class PlayerProfile:UIBaseView
    {
        public const string Address="profile/window";
        public override string PrefabPath=>Address;
        public BindingMap Map=>Bindings;
        public override void Validate()
        {
            foreach(string key in new[]{"close","closeLabelButton","copyId","nickname","greeting","seasonPrivacy","bestPrivacy","support","representative","frame","customCard","nicknameCancel","nicknameCheck","nicknameSave","greetingCancel","greetingSave"})Bindings.Require<GameButton>(key);
            foreach(string key in new[]{"serverId","displayName","rank","greetingText","seasonArena","seasonWorld","bestArena","bestWorld"})Bindings.Require<GameText>(key);
            foreach(string key in new[]{"nicknameInput","greetingInput"})Bindings.Require<TMP_InputField>(key);
            foreach(string key in new[]{"profileContent","selfActions","nicknameLayer","greetingLayer","messageChrome"})Bindings.Require<GameObject>(key);
            Bindings.Require<RawImage>("portrait");Bindings.Require<Image>("avatar");
        }
    }
}
