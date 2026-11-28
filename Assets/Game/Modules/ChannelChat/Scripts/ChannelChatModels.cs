using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace AstralOath.Client.ChannelChat
{
    [Serializable,Preserve] public sealed class ChatNotifications { public bool hero5=true,artifact5=true,guildPromotion=true,generalChat=true; }
    [Serializable,Preserve] public sealed class ChatPreferences
    {
        public long version; public int? favoriteChannel; public ChatNotifications notifications;
        public string[] keywords; public bool transparentBackground;
    }
    [Serializable,Preserve] public sealed class ChatPreferenceRequest
    {
        public string requestId,kind; public long expectedVersion; public int? favoriteChannel;
        public ChatNotifications notifications; public string[] keywords; public bool? transparentBackground;
        [NonSerialized,JsonIgnore] public bool dispatched;
    }
    [Serializable,Preserve] public sealed class ChatSendRequest
    {
        public string requestId,kind,text,stickerId;
        [NonSerialized,JsonIgnore] public bool dispatched;
    }
    [Serializable,Preserve] public sealed class ChatSender { public string id,displayName,avatarId; }
    [Serializable,Preserve] public sealed class ChatMessage
    {
        public string id,kind,text,stickerId,createdAt,templateId;
        public int channel;
        public ChatSender player;
        [JsonIgnore] public bool IsReward => kind == "hero5" || kind == "artifact5";
    }
    [Serializable,Preserve] public sealed class ChatPage
    {
        public int channel; public ChatMessage[] messages; public string nextAfter; public bool hasMore;
    }
    public interface IChannelChatApi:IDisposable
    {
        Task<ChatPreferences> PreferencesAsync(CancellationToken token);
        Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request,CancellationToken token);
        Task<ChatPage> ListAsync(int channel,string after,int waitMs,CancellationToken token);
        Task<ChatMessage> SendAsync(int channel,ChatSendRequest request,CancellationToken token);
        Task WatchAsync(int channel,Action invalidated,CancellationToken token);
    }
}
