using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.PlayerProfile;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace AstralOath.Client.Tests
{
    public sealed class PlayerProfileSessionTests
    {
        private sealed class Api : IPlayerProfileApi
        {
            public readonly List<ProfileActionRequest> calls=new List<ProfileActionRequest>();
            public PlayerProfileData read=Own(1);public TaskCompletionSource<PlayerProfileData> gate;public bool failOnce;
            public Task<PlayerProfileData> MeAsync(CancellationToken token)=>Task.FromResult(read);
            public Task<PlayerProfileData> PlayerAsync(string id,CancellationToken token)=>Task.FromResult(read);
            public Task<NicknameCheck> CheckNicknameAsync(string name,CancellationToken token)=>Task.FromResult(new NicknameCheck{available=true});
            public Task<PlayerProfileData> ActAsync(ProfileActionRequest request,CancellationToken token)
            {request.dispatched=true;calls.Add(request);if(failOnce){failOnce=false;return Task.FromException<PlayerProfileData>(new IOException("timeout"));}return gate?.Task??Task.FromResult(Own(2));}
            public void Dispose(){}
        }
        private sealed class Transport:HttpMessageHandler
        {
            internal int posts,failures;internal Stream readStream;internal HttpStatusCode status=HttpStatusCode.OK;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();if(request.Method==HttpMethod.Post)posts++;
                if(request.Method==HttpMethod.Post&&failures>0){failures--;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"code\":\"profile_request_failed\"}")});}
                return Task.FromResult(new HttpResponseMessage(status){Content=readStream==null?(HttpContent)new StringContent("{\"playerId\":\"a\",\"realmId\":\"r\",\"isSelf\":true,\"canEdit\":true,\"version\":2}"):new StreamContent(readStream)});
            }
        }
        private sealed class AbortedSocketStream:Stream
        {
            internal readonly TaskCompletionSource<int> waiting=new TaskCompletionSource<int>();internal bool closed;
            public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token)=>waiting.Task;
            protected override void Dispose(bool disposing){closed=true;waiting.TrySetException(new SocketException(10004));base.Dispose(disposing);}
            public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        }
        private static PlayerProfileData Own(long version)=>new PlayerProfileData{playerId="a",realmId="r",isSelf=true,canEdit=true,version=version,displayName="Player",visibility=new ProfileVisibility()};
        private static IEnumerator Done(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.InnerException;Assert.That(task.IsCanceled,Is.False);}
        [TestCase("Abcd",true)] [TestCase("abcdefghijkl",true)] [TestCase("abc",false)] [TestCase("中文",true)]
        [TestCase("名字A1",true)] [TestCase("1234",false)] [TestCase("Ab cd",false)] [TestCase("名字!",false)]
        [TestCase("中文一二三四五六七",false)] [TestCase("名字\ud800",false)]
        public void NicknameRulesMatchServer(string value,bool expected)=>Assert.That(PlayerProfileRules.ValidNickname(value),Is.EqualTo(expected));
        [Test] public void GreetingCountsScalarsNormalizesNewlinesAndRejectsControls()
        {Assert.That(PlayerProfileRules.ValidGreeting(new string('中',50)),Is.True);Assert.That(PlayerProfileRules.ValidGreeting(new string('中',51)),Is.False);Assert.That(PlayerProfileRules.ValidGreeting("a\0b"),Is.False);Assert.That(PlayerProfileRules.NormalizeGreeting(" hi\r\nthere "),Is.EqualTo("hi\nthere"));Assert.That(PlayerProfileRules.ValidGreeting(""),Is.True);}
        [UnityTest] public IEnumerator OtherPlayerCannotSubmit()=>Done(OtherCannotWrite());
        private async Task OtherCannotWrite()
        {var api=new Api();using var s=new PlayerProfileSession(api);var other=Own(1);other.isSelf=false;other.canEdit=false;try{await s.SubmitAsync(other,new ProfileActionRequest{kind="greeting",greeting="x"},default);Assert.Fail();}catch(InvalidOperationException){}Assert.That(api.calls,Is.Empty);}
        [UnityTest] public IEnumerator StaleSelfReadCannotRollBackReceipt()=>Done(StaleRead());
        private async Task StaleRead()
        {var api=new Api();using var s=new PlayerProfileSession(api);var self=await s.ReadAsync(null,default);await s.SubmitAsync(self,new ProfileActionRequest{kind="greeting",greeting="x"},default);await s.ReadAsync(null,default);Assert.That(s.Self.version,Is.EqualTo(2));}
        [UnityTest] public IEnumerator UncertainRetryKeepsExactIntentAndIgnoresInputMutation()=>Done(Retry());
        private async Task Retry()
        {var api=new Api{failOnce=true};using var s=new PlayerProfileSession(api);var input=new ProfileActionRequest{kind="greeting",greeting="hello"};try{await s.SubmitAsync(Own(1),input,default);}catch(IOException){}input.greeting="changed";Assert.That(s.HasPending,Is.True);await s.RetryPendingAsync(default);Assert.That(api.calls.Count,Is.EqualTo(2));Assert.That(api.calls[1].requestId,Is.EqualTo(api.calls[0].requestId));Assert.That(api.calls[1].expectedVersion,Is.EqualTo(1));Assert.That(api.calls[1].greeting,Is.EqualTo("hello"));Assert.That(s.HasPending,Is.False);}
        [UnityTest] public IEnumerator ClosingPageReleasesWaiterButKeepsAuthorityReceipt()=>Done(PageClose());
        private async Task PageClose()
        {var api=new Api{gate=new TaskCompletionSource<PlayerProfileData>()};using var s=new PlayerProfileSession(api);int changes=0;s.Changed+=_=>changes++;using var page=new CancellationTokenSource();var task=s.SubmitAsync(Own(1),new ProfileActionRequest{kind="greeting",greeting="hi"},page.Token);page.Cancel();try{await task;Assert.Fail();}catch(OperationCanceledException){}api.gate.SetResult(Own(2));await Task.Yield();Assert.That(s.Self.version,Is.EqualTo(2));Assert.That(changes,Is.EqualTo(1));}
        [UnityTest] public IEnumerator ClosingBeforeBearerDispatchNeverPostsUnsentProfileAction()=>Done(BeforeDispatch());
        private async Task BeforeDispatch()
        {
            var bearer=new TaskCompletionSource<string>();var transport=new Transport();
            using var api=new PlayerProfileApi(new Uri("http://127.0.0.1:7728"),ct=>bearer.Task,transport);
            using var session=new PlayerProfileSession(api);using var page=new CancellationTokenSource();
            var submitting=session.SubmitAsync(Own(1),new ProfileActionRequest{kind="greeting",greeting="unsent"},page.Token);
            Assert.That(transport.posts,Is.Zero);page.Cancel();try{await submitting;Assert.Fail();}catch(OperationCanceledException){}
            bearer.SetResult("controlled-bearer");await Task.Delay(50);
            Assert.That(transport.posts,Is.Zero,"Closing the editor before the bearer resolves must cancel an unsent action.");
            Assert.That(session.HasPending,Is.False);Assert.That(session.Self,Is.Null);
        }
        [UnityTest] public IEnumerator ClosingProfileReadMapsAbortedSocketToCancellation()=>Done(CancelledProfileRead());
        private async Task CancelledProfileRead()
        {
            var stream=new AbortedSocketStream();var transport=new Transport{readStream=stream};
            using var api=new PlayerProfileApi(new Uri("http://127.0.0.1:7728"),ct=>Task.FromResult("controlled-bearer"),transport);
            using var closed=new CancellationTokenSource();var reading=api.MeAsync(closed.Token);closed.Cancel();
            try{await reading;Assert.Fail();}catch(OperationCanceledException){}
            Assert.That(stream.closed,Is.True);
        }
        [UnityTest] public IEnumerator CancelledUndispatchedRetryRetainsPreviouslyDispatchedIntent()=>Done(CancelledRetry());
        private async Task CancelledRetry()
        {
            int bearers=0;var secondBearer=new TaskCompletionSource<string>();var transport=new Transport{failures=1};
            using var api=new PlayerProfileApi(new Uri("http://127.0.0.1:7728"),ct=>++bearers==2?secondBearer.Task:Task.FromResult("controlled-bearer"),transport);
            using var session=new PlayerProfileSession(api);
            try{await session.SubmitAsync(Own(1),new ProfileActionRequest{kind="greeting",greeting="original"},default);Assert.Fail();}catch(PlayerProfileRequestException e){Assert.That(e.Status,Is.EqualTo(HttpStatusCode.ServiceUnavailable));}
            Assert.That(session.HasPending,Is.True);Assert.That(transport.posts,Is.EqualTo(1));
            using var page=new CancellationTokenSource();var retry=session.RetryPendingAsync(page.Token);page.Cancel();try{await retry;Assert.Fail();}catch(OperationCanceledException){}
            secondBearer.SetResult("controlled-bearer");await Task.Delay(50);
            Assert.That(transport.posts,Is.EqualTo(1),"A fresh retry attempt still honors its own pre-dispatch cancellation.");
            Assert.That(session.HasPending,Is.True,"The earlier dispatched attempt remains uncertain and must not be discarded.");
            Assert.That(session.Self,Is.Null);await session.RetryPendingAsync(default);
            Assert.That(transport.posts,Is.EqualTo(2));Assert.That(session.HasPending,Is.False);Assert.That(session.Self.version,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator UncancelledSocketBecomesRecoverableIoAndKnownHttpRejectStaysKnown()=>Done(SocketAndRejection());
        private async Task SocketAndRejection()
        {
            var stream=new AbortedSocketStream();stream.waiting.SetException(new SocketException(10054));
            using(var api=new PlayerProfileApi(new Uri("http://127.0.0.1:7728"),ct=>Task.FromResult("controlled-bearer"),new Transport{readStream=stream}))
            {try{await api.MeAsync(default);Assert.Fail();}catch(IOException error){Assert.That(error,Is.Not.TypeOf<PlayerProfileRequestException>());}}
            var transport=new Transport{status=HttpStatusCode.BadRequest};
            using var rejectedApi=new PlayerProfileApi(new Uri("http://127.0.0.1:7728"),ct=>Task.FromResult("controlled-bearer"),transport);
            using var session=new PlayerProfileSession(rejectedApi);
            try{await session.SubmitAsync(Own(1),new ProfileActionRequest{kind="greeting",greeting="rejected"},default);Assert.Fail();}catch(PlayerProfileRequestException error){Assert.That(error.Status,Is.EqualTo(HttpStatusCode.BadRequest));}
            Assert.That(session.HasPending,Is.False);Assert.That(session.Self,Is.Null);
        }
        [UnityTest] public IEnumerator MeReadRejectsNonSelfAndMissingIdentityBeforePublishing()=>Done(InvalidMe());
        private async Task InvalidMe()
        {
            foreach(var corrupt in new Action<PlayerProfileData>[] {value=>value.isSelf=false,value=>value.playerId=null,value=>value.realmId="",value=>value.realmId=" "})
            {
                var value=Own(1);corrupt(value);var api=new Api{read=value};using var session=new PlayerProfileSession(api);int changed=0;session.Changed+=_=>changed++;
                try{await session.ReadAsync(null,default);Assert.Fail("A Me response must identify the authenticated self in a realm.");}catch(IOException){}
                Assert.That(session.Self,Is.Null);Assert.That(changed,Is.Zero);
            }
        }
    }
}
