using System.Globalization;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
namespace AstralOath.Client.ChannelChat
{
    public sealed class ChatEntry:UIBaseView
    {
        public const string Address="chat/header-entry";
        public override string PrefabPath=>Address;
        public GameButton Button=>Bindings.Require<GameButton>("button");
        public AudioSource Source=>Bindings.Require<AudioSource>("source");
        public BindingMap UnreadBadge=>Bindings.Require<BindingMap>("unreadBadge");
        public override void Validate(){_=Button;_=Source;_=UnreadBadge;}
        public static void BindUnread(BindingMap badge,ChannelChatSession session,BindingScope scope)
        {
            var label=badge.Require<GameText>("label");var group=badge.Require<RectTransform>("sourceGroup");
            void Render()
            {
                if(scope.Token.IsCancellationRequested||!badge)return;
                int count=session.UnreadCount;badge.gameObject.SetActive(count>0);
                label.text=count.ToString(CultureInfo.InvariantCulture);
                group.sizeDelta=new Vector2(Mathf.Max(38,12+label.text.Length*11),30)*1.5f;
            }
            session.UnreadChanged+=Render;Render();scope.Add(()=>session.UnreadChanged-=Render);
        }
    }
}
