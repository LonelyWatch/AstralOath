using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.ChannelChat;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatSessionTests
    {
        private sealed class Api:IChannelChatApi
        {
            internal readonly Dictionary<int,TaskCompletionSource<ChatPage>> joins=new Dictionary<int,TaskCompletionSource<ChatPage>>();
            internal readonly List<ChatSendRequest> sends=new List<ChatSendRequest>();internal readonly List<int> sendChannels=new List<int>();
            internal readonly List<ChatPreferenceRequest> saves=new List<ChatPreferenceRequest>();
            internal readonly List<(int channel,string after)> reads=new List<(int,string)>();
            internal Func<int,string,CancellationToken,Task<ChatPage>> list;
            internal ChatPreferences prefs=Prefs(0);internal ChatPage page=Page(1,"1");
            internal bool failSend,failPrefs,reject;internal TaskCompletionSource<ChatMessage> sendGate;internal TaskCompletionSource<bool> beforeDispatch;
            internal Action invalidated;internal int watches;internal bool disposed;
            public Task<ChatPreferences> PreferencesAsync(CancellationToken ct)=>Task.FromResult(prefs);
            public Task<ChatPreferences> SavePreferencesAsync(ChatPreferenceRequest request,CancellationToken ct)
            {request.dispatched=true;saves.Add(request);if(failPrefs){failPrefs=false;return Task.FromException<ChatPreferences>(new IOException("uncertain"));}return Task.FromResult(Prefs(1));}
            public Task<ChatPage> ListAsync(int channel,string after,int waitMs,CancellationToken ct)
            {reads.Add((channel,after));return list!=null?list(channel,after,ct):joins.TryGetValue(channel,out var gate)?gate.Task:Task.FromResult(page.channel==channel?page:Page(channel,"1"));}
            public async Task<ChatMessage> SendAsync(int channel,ChatSendRequest request,CancellationToken ct)
            {if(beforeDispatch!=null)await beforeDispatch.Task;ct.ThrowIfCancellationRequested();request.dispatched=true;sends.Add(request);sendChannels.Add(channel);if(reject)throw new ChannelChatRequestException(HttpStatusCode.Conflict,"request_conflict");if(failSend){failSend=false;throw new IOException("uncertain");}return sendGate==null?Message(channel,"99"):await sendGate.Task;}
            public async Task WatchAsync(int channel,Action invalidate,CancellationToken ct){watches++;invalidated=invalidate;invalidate();await Task.Delay(Timeout.Infinite,ct);}
            public void Dispose(){disposed=true;}
        }
        private sealed class Transport:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);
        }
        private sealed class CancelledSocketStream:Stream
        {
            internal readonly TaskCompletionSource<int> waiting=new TaskCompletionSource<int>();internal bool closed;
            public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token)=>waiting.Task;
            protected override void Dispose(bool disposing){closed=true;waiting.TrySetException(new SocketException(10004));base.Dispose(disposing);}
            public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        }
        private static ChatPreferences Prefs(long version)=>new ChatPreferences{version=version,notifications=new ChatNotifications(),keywords=Array.Empty<string>()};
        private static ChatMessage Message(int channel,string id)=>new ChatMessage{channel=channel,id=id,kind="text",text="fixture",player=new ChatSender{id="player"}};
        private static ChatPage Page(int channel,params string[] ids)=>new ChatPage{channel=channel,messages=ids.Select(id=>Message(channel,id)).ToArray(),nextAfter=ids.OrderBy(id=>id.Length).ThenBy(id=>id,StringComparer.Ordinal).LastOrDefault()??"0"};
        private static IEnumerator Done(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.InnerException;Assert.That(task.IsCanceled,Is.False);}

        [UnityTest] public IEnumerator JoinKeepsOldChannelUntilReadAndIgnoresLateResult()=>Done(Join());
        private async Task Join()
        {
            var api=new Api();api.joins[2]=new TaskCompletionSource<ChatPage>();api.joins[3]=new TaskCompletionSource<ChatPage>();using var s=new ChannelChatSession(api);
            var second=s.JoinAsync(2,default);Assert.That(s.Channel,Is.EqualTo(1));var third=s.JoinAsync(3,default);api.joins[3].SetResult(Page(3,"3"));await third;
            api.joins[2].SetResult(Page(2,"2"));try{await second;}catch(OperationCanceledException){}
            Assert.That(s.Channel,Is.EqualTo(3));Assert.That(s.Messages.All(m=>m.channel==3),Is.True);
        }
        [UnityTest] public IEnumerator CacheUsesDecimalIdsDeduplicatesAndBoundsTo200()=>Done(Cache());
        private async Task Cache()
        {
            var api=new Api{page=Page(1,Enumerable.Range(1,230).Reverse().Select(x=>x.ToString()).Concat(new[]{"9","10"}).ToArray())};using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            Assert.That(s.Messages.Count,Is.EqualTo(200));Assert.That(s.Messages[0].id,Is.EqualTo("31"));Assert.That(s.Messages[199].id,Is.EqualTo("230"));
        }
        [UnityTest] public IEnumerator OwnSendReceiptCannotSkipUnreadOtherPlayerMessage()=>Done(UnreadBeforeOwnReceipt());
        private async Task UnreadBeforeOwnReceipt()
        {
            var api=new Api{page=Page(1,"1"),sendGate=new TaskCompletionSource<ChatMessage>()};
            using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            // Another player commits id2 while this client's invalidation is delayed.
            // Our own id3 is committed afterwards, but its HTTP receipt arrives first.
            var sending=s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            var own=Message(1,"3");own.player.id="self";api.sendGate.SetResult(own);await sending;
            Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"1","3"}));
            api.list=(channel,after,ct)=>Task.FromResult(after=="1"?Page(channel,"2","3"):
                new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            await s.RefreshAsync(default);
            Assert.That(api.reads.Last().after,Is.EqualTo("1"),"A send receipt must not advance the authoritative read cursor.");
            Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"1","2","3"}));
        }
        [UnityTest] public IEnumerator FailedAndLateJoinCannotReplaceCurrentReadCursor()=>Done(CursorAfterJoinFailure());
        private async Task CursorAfterJoinFailure()
        {
            var api=new Api{page=Page(1,"10")};using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            api.joins[2]=new TaskCompletionSource<ChatPage>();var late=s.JoinAsync(2,default);
            api.page=Page(3,"30");await s.JoinAsync(3,default);api.joins[2].SetResult(Page(2,"20"));try{await late;}catch(OperationCanceledException){}
            api.list=(channel,after,ct)=>channel==4?Task.FromException<ChatPage>(new IOException("failed join")):
                Task.FromResult(after=="30"?Page(channel,"31"):new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            try{await s.JoinAsync(4,default);Assert.Fail();}catch(IOException){}
            api.sendGate=new TaskCompletionSource<ChatMessage>();api.sendGate.SetResult(Message(3,"99"));await s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            await s.RefreshAsync(default);
            Assert.That(s.Channel,Is.EqualTo(3));Assert.That(api.reads.Last().after,Is.EqualTo("30"));
            Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"30","31","99"}));
        }
        [UnityTest] public IEnumerator CacheTrimAndHighOwnReceiptDoNotMoveReadCursor()=>Done(CursorAfterTrim());
        private async Task CursorAfterTrim()
        {
            var api=new Api{page=Page(1,Enumerable.Range(1,230).Select(i=>i.ToString()).ToArray()),sendGate=new TaskCompletionSource<ChatMessage>()};
            using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);api.sendGate.SetResult(Message(1,"1000"));
            await s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            api.list=(channel,after,ct)=>Task.FromResult(after=="230"?Page(channel,"231","232"):after=="232"?Page(channel,"233"):
                new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            await s.RefreshAsync(default);Assert.That(api.reads.Last().after,Is.EqualTo("230"));
            await s.RefreshAsync(default);Assert.That(api.reads.Last().after,Is.EqualTo("232"));
            Assert.That(s.Messages.Count,Is.EqualTo(200));Assert.That(s.Messages.Any(m=>m.id=="233"),Is.True);Assert.That(s.Messages.Last().id,Is.EqualTo("1000"));
        }
        [UnityTest] public IEnumerator LateConcurrentReadCannotRollCursorBack()=>Done(CursorAfterConcurrentRead());
        private async Task CursorAfterConcurrentRead()
        {
            var api=new Api{page=Page(1,"1")};using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            var delayed=new TaskCompletionSource<ChatPage>();int reads=0;
            api.list=(channel,after,ct)=>++reads==1?delayed.Task:Task.FromResult(Page(channel,"2","3"));
            var first=s.RefreshAsync(default);await s.RefreshAsync(default);delayed.SetResult(Page(1,"2"));await first;
            api.sendGate=new TaskCompletionSource<ChatMessage>();api.sendGate.SetResult(Message(1,"99"));await s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            api.list=(channel,after,ct)=>Task.FromResult(new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            await s.RefreshAsync(default);Assert.That(api.reads.Last().after,Is.EqualTo("3"));
            Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"1","2","3","99"}));
        }
        [UnityTest] public IEnumerator SameChannelReopenFollowsUnreadPagesRatherThanLatestSnapshot()=>Done(CursorAfterReopen());
        private async Task CursorAfterReopen()
        {
            var api=new Api{page=Page(1,"1"),sendGate=new TaskCompletionSource<ChatMessage>()};using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            api.sendGate.SetResult(Message(1,"99"));await s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            api.list=(channel,after,ct)=>{var value=Page(channel,after=="1"?"2":"3");value.hasMore=after=="1";return Task.FromResult(value);};
            await s.JoinAsync(1,default);
            Assert.That(api.reads.Skip(1).Select(r=>r.after),Is.EqualTo(new[]{"1","2"}));
            Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"1","2","3","99"}));
            api.list=(channel,after,ct)=>Task.FromResult(new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            await s.RefreshAsync(default);Assert.That(api.reads.Last().after,Is.EqualTo("3"));
        }
        [UnityTest] public IEnumerator CancelledJoinNeverPublishesPartialPageCursor()=>Done(CursorAfterCancelledJoin());
        private async Task CursorAfterCancelledJoin()
        {
            var api=new Api{page=Page(1,"1")};using var s=new ChannelChatSession(api);await s.JoinAsync(1,default);
            var secondPage=new TaskCompletionSource<ChatPage>();api.list=(channel,after,ct)=>{
                if(after!="0")return secondPage.Task;var value=Page(channel,"20");value.hasMore=true;return Task.FromResult(value);};
            using var closed=new CancellationTokenSource();var joining=s.JoinAsync(2,closed.Token);closed.Cancel();secondPage.SetResult(Page(2,"21"));
            try{await joining;Assert.Fail();}catch(OperationCanceledException){}
            Assert.That(s.Channel,Is.EqualTo(1));Assert.That(s.Messages.Select(m=>m.id),Is.EqualTo(new[]{"1"}));
            api.sendGate=new TaskCompletionSource<ChatMessage>();api.sendGate.SetResult(Message(1,"99"));await s.SendAsync(new ChatSendRequest{kind="text",text="own"},default);
            api.list=(channel,after,ct)=>Task.FromResult(new ChatPage{channel=channel,messages=Array.Empty<ChatMessage>(),nextAfter=after});
            await s.RefreshAsync(default);Assert.That(api.reads.Last().after,Is.EqualTo("1"));
        }
        [UnityTest] public IEnumerator UncertainSendRetainsFrozenChannelAndInput()=>Done(RetrySend());
        private async Task RetrySend()
        {
            var api=new Api{failSend=true};using var s=new ChannelChatSession(api);var value=new ChatSendRequest{kind="text",text="original"};
            try{await s.SendAsync(value,default);}catch(IOException){}value.text="changed";await s.JoinAsync(2,default);Assert.That(s.HasPendingSend,Is.True);await s.RetryPendingSendAsync(default);
            Assert.That(api.sendChannels,Is.EqualTo(new[]{1,1}));Assert.That(api.sends[1].requestId,Is.EqualTo(api.sends[0].requestId));Assert.That(api.sends[1].text,Is.EqualTo("original"));Assert.That(s.Messages.All(m=>m.channel==2),Is.True);
        }
        [UnityTest] public IEnumerator ClosingPageDoesNotCancelDispatchedReceipt()=>Done(ClosePage());
        private async Task ClosePage()
        {
            var api=new Api{sendGate=new TaskCompletionSource<ChatMessage>()};using var s=new ChannelChatSession(api);using var page=new CancellationTokenSource();var send=s.SendAsync(new ChatSendRequest{kind="text",text="hello"},page.Token);page.Cancel();
            try{await send;Assert.Fail();}catch(OperationCanceledException){}api.sendGate.SetResult(Message(1,"4"));await Task.Yield();Assert.That(s.HasPendingSend,Is.False);Assert.That(s.Messages.Single().id,Is.EqualTo("4"));
        }
        [UnityTest] public IEnumerator ClosingBeforeDispatchCancelsUnsentIntent()=>Done(BeforeDispatch());
        private async Task BeforeDispatch()
        {
            var api=new Api{beforeDispatch=new TaskCompletionSource<bool>()};using var s=new ChannelChatSession(api);using var page=new CancellationTokenSource();
            var sending=s.SendAsync(new ChatSendRequest{kind="text",text="unsent"},page.Token);page.Cancel();try{await sending;Assert.Fail();}catch(OperationCanceledException){}
            api.beforeDispatch.SetResult(true);for(int i=0;i<5&&s.HasPendingSend;i++)await Task.Yield();Assert.That(s.HasPendingSend,Is.False);Assert.That(api.sends,Is.Empty);
        }
        [UnityTest] public IEnumerator PreferenceRetryFreezesVersionAndDoesNotRollbackNewerRead()=>Done(PrefsRetry());
        private async Task PrefsRetry()
        {
            var api=new Api{failPrefs=true};using var s=new ChannelChatSession(api);await s.RefreshPreferencesAsync(default);var input=new ChatPreferenceRequest{kind="keywords",keywords=new[]{"original"}};
            try{await s.SavePreferencesAsync(input,default);}catch(IOException){}input.keywords[0]="mutated";api.prefs=Prefs(5);await s.RefreshPreferencesAsync(default);await s.RetryPendingPreferencesAsync(default);
            Assert.That(api.saves[1].keywords[0],Is.EqualTo("original"));Assert.That(api.saves[1].expectedVersion,Is.EqualTo(0));Assert.That(api.saves[1].requestId,Is.EqualTo(api.saves[0].requestId));Assert.That(s.Preferences.version,Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator DefiniteRejectionClearsIntentAndLogoutReleasesApi()=>Done(Reject());
        private async Task Reject()
        {
            var api=new Api{reject=true};using var login=new CancellationTokenSource();var s=new ChannelChatSession(api,login.Token);
            try{await s.SendAsync(new ChatSendRequest{kind="text",text="hello"},default);}catch(ChannelChatRequestException){}Assert.That(s.HasPendingSend,Is.False);
            login.Cancel();s.Dispose();Assert.That(api.disposed,Is.True);Assert.That(s.Messages,Is.Empty);
        }
        [UnityTest] public IEnumerator SyncInvalidationRefreshesAndWatchStopsOnLogout()=>Done(Watch());
        private async Task Watch()
        {
            var api=new Api();using var login=new CancellationTokenSource();using var s=new ChannelChatSession(api,login.Token);var observing=s.ObserveAsync(default);await Task.Yield();await Task.Yield();
            Assert.That(api.watches,Is.EqualTo(1));Assert.That(s.Messages.Count,Is.EqualTo(1));login.Cancel();try{await observing;}catch(OperationCanceledException){}
        }
        [UnityTest] public IEnumerator ApiParsesInvalidationsAndKeepsDispatchFlagOutOfJson()=>Done(ApiContract());
        private async Task ApiContract()
        {
            string sent=null;var handler=new Transport{send=async(request,token)=>
            {
                if(request.Method==HttpMethod.Post){sent=await request.Content.ReadAsStringAsync();return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"id\":\"1\",\"channel\":1,\"kind\":\"text\",\"player\":{\"id\":\"p\"}}")};}
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"kind\":\"sync\"}\n{\"kind\":\"heartbeat\"}\n{\"kind\":\"chat\"}\n",Encoding.UTF8,"application/x-ndjson")};
            }};
            using var api=new ChannelChatApi(new Uri("http://localhost:7728"),_=>Task.FromResult("fixture"),handler);var input=new ChatSendRequest{requestId="a",kind="text",text="private fixture"};await api.SendAsync(1,input,default);
            Assert.That(input.dispatched,Is.True);Assert.That(sent,Does.Not.Contain("dispatched"));int count=0;await api.WatchAsync(1,()=>count++,default);Assert.That(count,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator ApiRejectsRedirectAndBoundsEventLines()=>Done(ApiBounds());
        private async Task ApiBounds()
        {
            var redirect=new Transport{send=(r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect){Content=new StringContent("{}")})};
            using(var api=new ChannelChatApi(new Uri("http://localhost:7728"),_=>Task.FromResult("fixture"),redirect))
            {try{await api.PreferencesAsync(default);Assert.Fail();}catch(ChannelChatRequestException e){Assert.That(e.Status,Is.EqualTo(HttpStatusCode.Redirect));}}
            var large=new Transport{send=(r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',4097)+"\n",Encoding.UTF8,"application/x-ndjson")})};
            using(var api=new ChannelChatApi(new Uri("http://localhost:7728"),_=>Task.FromResult("fixture"),large))
            {try{await api.WatchAsync(1,()=>{},default);Assert.Fail();}catch(IOException e){Assert.That(e.Message,Is.EqualTo("Chat event exceeds limit."));}}
            var oversized=new Transport{send=(r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',1024*1024+1))})};
            using(var api=new ChannelChatApi(new Uri("http://localhost:7728"),_=>Task.FromResult("fixture"),oversized))
            {try{await api.PreferencesAsync(default);Assert.Fail();}catch(IOException e){Assert.That(e.Message,Is.EqualTo("Chat response exceeds limit."));}}
        }
        [UnityTest] public IEnumerator ClosingWatchMapsAbortedSocketToCancellation()=>Done(SocketCancellation());
        private async Task SocketCancellation()
        {
            var stream=new CancelledSocketStream();var handler=new Transport{send=(r,t)=>{var content=new StreamContent(stream);content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-ndjson");return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});}};
            using var api=new ChannelChatApi(new Uri("http://localhost:7728"),_=>Task.FromResult("fixture"),handler);using var cancelled=new CancellationTokenSource();var watching=api.WatchAsync(1,()=>{},cancelled.Token);cancelled.Cancel();
            try{await watching;Assert.Fail();}catch(OperationCanceledException){}Assert.That(stream.closed,Is.True);
        }
    }
}
