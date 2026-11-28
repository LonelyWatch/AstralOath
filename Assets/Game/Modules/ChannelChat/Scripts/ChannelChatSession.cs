using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.ChannelChat
{
    public sealed class ChannelChatSession:IDisposable
    {
        private sealed class SendIntent{internal int channel;internal ChatSendRequest request;internal Task<ChatMessage> running;internal bool notified,everDispatched;}
        private sealed class PreferenceIntent{internal ChatPreferenceRequest request;internal Task<ChatPreferences> running;internal bool notified,everDispatched;}
        private sealed class ReadingState
        {internal bool initialized;internal string received="0";internal readonly HashSet<string> unread=new HashSet<string>(StringComparer.Ordinal),ownReceipts=new HashSet<string>(StringComparer.Ordinal);}
        private sealed class IdComparer:IComparer<string>
        {internal static readonly IdComparer Instance=new IdComparer();public int Compare(string a,string b)=>CompareId(a,b);}
        private readonly IChannelChatApi api;
        private readonly CancellationTokenSource lifetime;
        private readonly CancellationToken login;
        private readonly Dictionary<string,ChatMessage> messages=new Dictionary<string,ChatMessage>(StringComparer.Ordinal);
        private readonly Dictionary<int,ReadingState> reading=new Dictionary<int,ReadingState>();
        private readonly Func<string> ownPlayerId;
        private CancellationTokenSource joining,watching;
        private long joinGeneration,channelGeneration;
        // Only a confirmed List page advances this cursor. Locally shown send receipts
        // may jump over messages by other players which have not been read yet.
        private string readCursor="0";
        private SendIntent pendingSend;
        private PreferenceIntent pendingPreferences;
        private Task observing,pumping;
        private bool dirty,disposed;
        public ChannelChatSession(IChannelChatApi api,CancellationToken loginToken=default,Func<string> ownPlayerId=null)
        {this.api=api??throw new ArgumentNullException(nameof(api));this.ownPlayerId=ownPlayerId;lifetime=CancellationTokenSource.CreateLinkedTokenSource(loginToken);login=lifetime.Token;}
        public int Channel{get;private set;}=1;
        public IReadOnlyList<ChatMessage> Messages{get;private set;}=Array.Empty<ChatMessage>();
        public ChatPreferences Preferences{get;private set;}
        public bool HasPendingSend=>pendingSend!=null;
        public bool HasPendingPreferences=>pendingPreferences!=null;
        public int UnreadCount=>reading.Values.Sum(value=>value.unread.Count);
        public int UnreadFor(int channel)=>reading.TryGetValue(channel,out var value)?value.unread.Count:0;
        public event Action UnreadChanged;
        public void MarkRead(int channel,string throughMessageId)
        {
            if(disposed||login.IsCancellationRequested||channel!=Channel||string.IsNullOrEmpty(throughMessageId)||!messages.ContainsKey(throughMessageId)||!reading.TryGetValue(channel,out var value))return;
            if(value.unread.RemoveWhere(id=>CompareId(id,throughMessageId)<=0)>0)UnreadChanged?.Invoke();
        }
        public event Action Changed;
        public event Action<ChatPreferences> PreferencesChanged;
        public event Action Uncertain;
        public event Action<string> Faulted;
        public async Task JoinAsync(int channel,CancellationToken page)
        {
            Alive(page);if(channel<1||channel>9999)throw new ArgumentOutOfRangeException(nameof(channel));
            long generation=++joinGeneration;joining?.Cancel();using var request=CancellationTokenSource.CreateLinkedTokenSource(login,page);request.CancelAfter(TimeSpan.FromSeconds(20));joining=request;
            try
            {
                string after=channel==Channel?readCursor:"0";var incoming=new List<ChatMessage>();var received=new List<ChatMessage>();
                while(true)
                {
                    var result=await api.ListAsync(channel,after,0,request.Token);request.Token.ThrowIfCancellationRequested();
                    if(generation!=joinGeneration)throw new OperationCanceledException(new CancellationToken(true));
                    ValidatePage(result,channel);string next=ConfirmedCursor(result,after);
                    if(result.hasMore&&CompareId(next,after)<=0)throw new IOException("Invalid chat cursor.");
                    incoming.AddRange(result.messages);
                    // Keep only ID/identity metadata for unread accounting when the visual cache trims older pages.
                    received.AddRange(result.messages.Select(message=>new ChatMessage{id=message.id,player=new ChatSender{id=message.player.id}}));
                    if(incoming.Count>200)incoming=incoming.OrderBy(m=>m.id,IdComparer.Instance).Skip(incoming.Count-200).ToList();
                    after=next;if(!result.hasMore)break;
                }
                if(channel!=Channel){Channel=channel;channelGeneration++;messages.Clear();readCursor="0";watching?.Cancel();}
                TrackIncoming(received,channel,after);Merge(incoming,channel);AdvanceReadCursor(after);Changed?.Invoke();
            }
            finally{if(ReferenceEquals(joining,request))joining=null;}
        }
        public async Task RefreshAsync(CancellationToken page)
        {
            Alive(page);int channel=Channel;long generation=channelGeneration;
            using var request=CancellationTokenSource.CreateLinkedTokenSource(login,page);request.CancelAfter(TimeSpan.FromSeconds(20));
            string after=readCursor;
            while(true)
            {
                var result=await api.ListAsync(channel,after,0,request.Token);request.Token.ThrowIfCancellationRequested();
                if(channel!=Channel||generation!=channelGeneration)return;
                ValidatePage(result,channel);string next=ConfirmedCursor(result,after);
                if(result.hasMore&&CompareId(next,after)<=0)throw new IOException("Invalid chat cursor.");
                TrackIncoming(result.messages,channel,next);Merge(result.messages,channel);AdvanceReadCursor(next);Changed?.Invoke();
                if(!result.hasMore)return;
                if(channel!=Channel||generation!=channelGeneration)return;after=readCursor;
            }
        }
        public async Task RefreshPreferencesAsync(CancellationToken page)
        {
            Alive(page);using var request=CancellationTokenSource.CreateLinkedTokenSource(login,page);
            var result=await api.PreferencesAsync(request.Token);request.Token.ThrowIfCancellationRequested();ApplyPreferences(result);
        }
        public Task<ChatMessage> SendAsync(ChatSendRequest values,CancellationToken page)
        {
            Alive(page);if(values==null)throw new ArgumentNullException(nameof(values));
            if(pendingSend!=null)throw new InvalidOperationException("A chat send awaits its receipt.");
            var intent=new SendIntent{channel=Channel,request=new ChatSendRequest{requestId=Guid.NewGuid().ToString("N"),kind=values.kind,text=values.text,stickerId=values.stickerId}};
            pendingSend=intent;return StartSend(intent,page);
        }
        public Task<ChatMessage> RetryPendingSendAsync(CancellationToken page)
        {Alive(page);return pendingSend==null?Task.FromResult<ChatMessage>(null):StartSend(pendingSend,page);}
        private Task<ChatMessage> StartSend(SendIntent intent,CancellationToken page)
        {if(intent.running==null||intent.running.IsCompleted)intent.running=SendIntentAsync(intent,page);return AwaitPage(intent.running,page);}
        private async Task<ChatMessage> SendIntentAsync(SendIntent intent,CancellationToken page)
        {
            var call=new ChatSendRequest{requestId=intent.request.requestId,kind=intent.request.kind,text=intent.request.text,stickerId=intent.request.stickerId};
            using var attempt=CancellationTokenSource.CreateLinkedTokenSource(login);
            using var cancelled=page.Register(()=>{if(!call.dispatched)attempt.Cancel();});
            try
            {
                var result=await api.SendAsync(intent.channel,call,attempt.Token);attempt.Token.ThrowIfCancellationRequested();login.ThrowIfCancellationRequested();
                ValidateMessage(result,intent.channel);Reading(intent.channel).ownReceipts.Add(DecimalId(result.id,false));if(ReferenceEquals(pendingSend,intent))pendingSend=null;
                if(Channel==intent.channel){Merge(new[]{result},intent.channel);Changed?.Invoke();}return result;
            }
            catch(ChannelChatRequestException e)when((int)e.Status>=400&&(int)e.Status<500){if(ReferenceEquals(pendingSend,intent))pendingSend=null;throw;}
            catch(Exception e)when(Recoverable(e))
            {intent.everDispatched|=call.dispatched;if(!intent.everDispatched||login.IsCancellationRequested){if(ReferenceEquals(pendingSend,intent))pendingSend=null;}else if(!intent.notified){intent.notified=true;Uncertain?.Invoke();}throw;}
        }
        public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest values,CancellationToken page)
        {
            Alive(page);if(values==null)throw new ArgumentNullException(nameof(values));
            if(Preferences==null)throw new InvalidOperationException("Read chat preferences before editing.");
            if(pendingPreferences!=null)throw new InvalidOperationException("Chat preferences await their receipt.");
            var intent=new PreferenceIntent{request=new ChatPreferenceRequest{requestId=Guid.NewGuid().ToString("N"),expectedVersion=Preferences.version,kind=values.kind,
                favoriteChannel=values.favoriteChannel,notifications=Copy(values.notifications),keywords=values.keywords?.ToArray(),transparentBackground=values.transparentBackground}};
            pendingPreferences=intent;return StartPreferences(intent,page);
        }
        public Task<ChatPreferences> RetryPendingPreferencesAsync(CancellationToken page)
        {Alive(page);return pendingPreferences==null?Task.FromResult(Preferences):StartPreferences(pendingPreferences,page);}
        private Task<ChatPreferences> StartPreferences(PreferenceIntent intent,CancellationToken page)
        {if(intent.running==null||intent.running.IsCompleted)intent.running=SaveIntentAsync(intent,page);return AwaitPage(intent.running,page);}
        private async Task<ChatPreferences> SaveIntentAsync(PreferenceIntent intent,CancellationToken page)
        {
            var frozen=intent.request;
            var call=new ChatPreferenceRequest{requestId=frozen.requestId,kind=frozen.kind,expectedVersion=frozen.expectedVersion,favoriteChannel=frozen.favoriteChannel,
                notifications=Copy(frozen.notifications),keywords=frozen.keywords?.ToArray(),transparentBackground=frozen.transparentBackground};
            using var attempt=CancellationTokenSource.CreateLinkedTokenSource(login);
            using var cancelled=page.Register(()=>{if(!call.dispatched)attempt.Cancel();});
            try
            {
                var result=await api.SavePreferencesAsync(call,attempt.Token);attempt.Token.ThrowIfCancellationRequested();login.ThrowIfCancellationRequested();ValidatePreferences(result);
                if(ReferenceEquals(pendingPreferences,intent))pendingPreferences=null;ApplyPreferences(result);return Preferences;
            }
            catch(ChannelChatRequestException e)when((int)e.Status>=400&&(int)e.Status<500){if(ReferenceEquals(pendingPreferences,intent))pendingPreferences=null;throw;}
            catch(Exception e)when(Recoverable(e))
            {intent.everDispatched|=call.dispatched;if(!intent.everDispatched||login.IsCancellationRequested){if(ReferenceEquals(pendingPreferences,intent))pendingPreferences=null;}else if(!intent.notified){intent.notified=true;Uncertain?.Invoke();}throw;}
        }
        public Task ObserveAsync(CancellationToken token)
        {Alive(token);if(observing==null||observing.IsCompleted)observing=ObserveLoop(token);return observing;}
        private async Task ObserveLoop(CancellationToken token)
        {
            using var observer=CancellationTokenSource.CreateLinkedTokenSource(login,token);int retry=0;
            while(!observer.IsCancellationRequested)
            {
                int channel=Channel;long generation=channelGeneration;using var stream=CancellationTokenSource.CreateLinkedTokenSource(observer.Token);watching=stream;
                try{await api.WatchAsync(channel,()=>Invalidate(channel,generation,observer.Token),stream.Token);}
                catch(OperationCanceledException)when(observer.IsCancellationRequested){break;}
                catch(OperationCanceledException)when(stream.IsCancellationRequested){retry=0;continue;}
                catch(ChannelChatRequestException e)when((int)e.Status==401||(int)e.Status==403){Faulted?.Invoke(e.Code);break;}
                catch(Exception e)when(Recoverable(e)){if(!disposed)Faulted?.Invoke("chat_connection_unavailable");}
                finally{if(ReferenceEquals(watching,stream))watching=null;}
                if(observer.IsCancellationRequested)break;
                if(channel!=Channel||generation!=channelGeneration){retry=0;continue;}
                Invalidate(channel,generation,observer.Token);
                try{await Task.Delay(Math.Min(8000,1000*(1<<Math.Min(retry++,3))),observer.Token);}catch(OperationCanceledException)when(observer.IsCancellationRequested){break;}
            }
        }
        private void Invalidate(int channel,long generation,CancellationToken token)
        {
            if(disposed||token.IsCancellationRequested||channel!=Channel||generation!=channelGeneration)return;
            dirty=true;if(pumping==null||pumping.IsCompleted)pumping=Pump(token);
        }
        private async Task Pump(CancellationToken token)
        {
            while(dirty&&!token.IsCancellationRequested&&!disposed)
            {
                dirty=false;try{await RefreshAsync(token);}
                catch(OperationCanceledException)when(token.IsCancellationRequested||login.IsCancellationRequested){return;}
                catch(Exception e)when(Recoverable(e)){if(!disposed)Faulted?.Invoke("chat_refresh_failed");return;}
            }
        }
        private void ApplyPreferences(ChatPreferences value)
        {
            ValidatePreferences(value);if(disposed||(Preferences!=null&&value.version<=Preferences.version))return;
            Preferences=new ChatPreferences{version=value.version,favoriteChannel=value.favoriteChannel,notifications=Copy(value.notifications),keywords=value.keywords.ToArray(),transparentBackground=value.transparentBackground};
            PreferencesChanged?.Invoke(Preferences);
        }
        private static ChatNotifications Copy(ChatNotifications value)=>value==null?null:new ChatNotifications{hero5=value.hero5,artifact5=value.artifact5,guildPromotion=value.guildPromotion,generalChat=value.generalChat};
        private static void ValidatePreferences(ChatPreferences value)
        {if(value==null||value.version<0||value.notifications==null||value.keywords==null||value.keywords.Length>5||(value.favoriteChannel.HasValue&&(value.favoriteChannel<1||value.favoriteChannel>9999)))throw new IOException("Invalid chat preferences.");}
        private static void ValidatePage(ChatPage page,int channel)
        {if(page==null||page.channel!=channel||page.messages==null)throw new IOException("Invalid chat page.");foreach(var message in page.messages)ValidateMessage(message,channel);}
        private static void ValidateMessage(ChatMessage message,int channel)
        {if(message==null||message.channel!=channel||message.player==null||string.IsNullOrEmpty(message.player.id))throw new IOException("Invalid chat message.");DecimalId(message.id,false);}
        private static string ConfirmedCursor(ChatPage page,string after)
        {
            string highest=after;
            foreach(var message in page.messages){string id=DecimalId(message.id,false);if(CompareId(id,highest)>0)highest=id;}
            if(DecimalId(page.nextAfter,true)!=highest)throw new IOException("Invalid chat cursor.");
            return highest;
        }
        private void AdvanceReadCursor(string confirmed){if(CompareId(confirmed,readCursor)>0)readCursor=confirmed;}
        private ReadingState Reading(int channel)
        {if(!reading.TryGetValue(channel,out var value)){value=new ReadingState();reading.Add(channel,value);}return value;}
        private void TrackIncoming(IEnumerable<ChatMessage> incoming,int channel,string confirmed)
        {
            var value=Reading(channel);bool changed=false;string own=ownPlayerId?.Invoke();
            foreach(var message in incoming)
            {
                string id=DecimalId(message.id,false);
                if(value.initialized&&CompareId(id,value.received)>0&&!value.ownReceipts.Contains(id)&&(string.IsNullOrEmpty(own)||message.player.id!=own))changed|=value.unread.Add(id);
            }
            if(CompareId(confirmed,value.received)>0)value.received=confirmed;
            value.initialized=true;value.ownReceipts.RemoveWhere(id=>CompareId(id,value.received)<=0);
            if(changed)UnreadChanged?.Invoke();
        }
        private void Merge(IEnumerable<ChatMessage> incoming,int channel)
        {
            if(disposed||channel!=Channel)return;
            foreach(var value in incoming)
            {
                ValidateMessage(value,channel);string id=DecimalId(value.id,false);
                messages[id]=new ChatMessage{id=id,channel=channel,kind=value.kind,text=value.text,stickerId=value.stickerId,createdAt=value.createdAt,
                    player=new ChatSender{id=value.player.id,displayName=value.player.displayName,avatarId=value.player.avatarId}};
            }
            var sorted=messages.Values.OrderBy(m=>m.id,IdComparer.Instance).ToArray();
            foreach(var old in sorted.Take(Math.Max(0,sorted.Length-200)))messages.Remove(old.id);
            Messages=Array.AsReadOnly(sorted.Skip(Math.Max(0,sorted.Length-200)).ToArray());
        }
        private static string DecimalId(string value,bool zero)
        {
            if(string.IsNullOrEmpty(value)||value.Length>19||value.Any(c=>c<'0'||c>'9'))throw new IOException("Invalid chat id.");
            string result=value.TrimStart('0');if(result.Length==0){if(zero)return "0";throw new IOException("Invalid chat id.");}return result;
        }
        private static int CompareId(string a,string b)=>a.Length!=b.Length?a.Length.CompareTo(b.Length):string.CompareOrdinal(a,b);
        private static bool Recoverable(Exception e)=>e is IOException||e is HttpRequestException||e is OperationCanceledException;
        private void Alive(CancellationToken token){login.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();if(disposed)throw new ObjectDisposedException(nameof(ChannelChatSession));}
        private static async Task<T> AwaitPage<T>(Task<T> task,CancellationToken page)
        {
            if(!page.CanBeCanceled)return await task;var cancelled=new TaskCompletionSource<bool>();
            using(page.Register(()=>cancelled.TrySetResult(true)))
            {if(await Task.WhenAny(task,cancelled.Task)!=task){_=ObserveAbandoned(task);throw new OperationCanceledException(page);}page.ThrowIfCancellationRequested();return await task;}
        }
        private static async Task ObserveAbandoned(Task task){try{await task;}catch(Exception){}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;lifetime.Cancel();joining?.Cancel();watching?.Cancel();pendingSend=null;pendingPreferences=null;
            messages.Clear();reading.Clear();readCursor="0";Messages=Array.Empty<ChatMessage>();Preferences=null;Changed=null;UnreadChanged=null;PreferencesChanged=null;Uncertain=null;Faulted=null;api.Dispose();lifetime.Dispose();
        }
    }
}
