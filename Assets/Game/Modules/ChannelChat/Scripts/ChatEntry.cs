using AstralOath.Client.Common;
using UnityEngine;
namespace AstralOath.Client.ChannelChat
{
    public sealed class ChatEntry:UIBaseView
    {
        public const string Address="chat/header-entry";
        public override string PrefabPath=>Address;
        public GameButton Button=>Bindings.Require<GameButton>("button");
        public AudioSource Source=>Bindings.Require<AudioSource>("source");
        public override void Validate(){_=Button;_=Source;}
    }
}
