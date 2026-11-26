using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace AstralOath.Client.Tests
{
    public sealed class MailApiTests
    {
        private sealed class TrackingStream : Stream
        {
            private readonly MemoryStream inner;public long read;
            public TrackingStream(byte[] bytes)=>inner=new MemoryStream(bytes);
            public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
            public override long Length=>inner.Length;public override long Position {get=>inner.Position;set=>throw new NotSupportedException();}
            public override int Read(byte[] buffer,int offset,int count){int n=inner.Read(buffer,offset,count);read+=n;return n;}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token)=>Task.FromResult(Read(buffer,offset,count));
            public override void Flush(){}public override long Seek(long x,SeekOrigin o)=>throw new NotSupportedException();public override void SetLength(long x)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        }
        private sealed class Replies : HttpMessageHandler
        {
            public string first,retry;public int calls;public bool drop,deny;public TrackingStream events;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token)
            {
                Assert.That(r.Headers.Authorization.Scheme,Is.EqualTo("Bearer"));Assert.That(r.Headers.Authorization.Parameter,Is.EqualTo("private-test-token"));
                if(r.RequestUri.AbsolutePath.EndsWith("events"))return new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(events)};
                string body=await r.Content.ReadAsStringAsync();Assert.That(body,Does.Not.Contain("dispatched"));var request=JsonUtility.FromJson<MailClaimRequest>(body);
                calls++;if(first==null)first=request.requestId;else retry=request.requestId;
                if(deny)return new HttpResponseMessage(HttpStatusCode.Conflict){Content=new StringContent("{\"error\":\"mail_expired\"}")};
                if(drop){drop=false;throw new HttpRequestException("committed-reply-lost");}
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"requestId\":\""+request.requestId+"\",\"claimed\":[\"lost\"],\"affectedKinds\":[\"energy\"],\"summary\":{\"version\":4,\"effectiveCount\":0,\"capacity\":100}}")};
            }
        }
        private static IEnumerator Complete(Task task){while(!task.IsCompleted)yield return null;task.GetAwaiter().GetResult();}
        [UnityTest] public IEnumerator DroppedHttpReceiptNotifiesUncertaintyAndRetriesTheExactIntent()=>Complete(DroppedAsync());
        private async Task DroppedAsync()
        {
            var transport=new Replies{drop=true};using var session=new MailSession(new MailApi(new Uri("http://127.0.0.6:7728/"),_=>Task.FromResult("private-test-token"),transport));int uncertain=0,claimed=0;session.Uncertain+=()=>uncertain++;session.Claimed+=r=>claimed++;
            try{await session.ClaimAsync("lost",default);Assert.Fail("Expected lost response");}catch(HttpRequestException){}
            Assert.That(uncertain,Is.EqualTo(1));Assert.That(claimed,Is.Zero);
            await session.RetryPendingAsync(default);Assert.That(transport.calls,Is.EqualTo(2));Assert.That(transport.retry,Is.EqualTo(transport.first));Assert.That(claimed,Is.EqualTo(1));Assert.That(uncertain,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator DefiniteClientRejectionDoesNotRaiseUncertain()=>Complete(DefiniteAsync());
        private async Task DefiniteAsync()
        {
            var transport=new Replies{deny=true};using var session=new MailSession(new MailApi(new Uri("http://127.0.0.6:7728/"),_=>Task.FromResult("private-test-token"),transport));int uncertain=0;session.Uncertain+=()=>uncertain++;
            try{await session.ClaimAsync("expired",default);Assert.Fail("Expected expiry rejection");}catch(MailRequestException e){Assert.That(e.Code,Is.EqualTo("mail_expired"));}Assert.That(uncertain,Is.Zero);await session.RetryPendingAsync(default);Assert.That(transport.calls,Is.EqualTo(1),"明确拒绝的请求不应作为未决继续重试");
        }
        [UnityTest] public IEnumerator EventLineLimitStopsBeforeTheWholeUnboundedLineIsBuffered()=>Complete(LimitAsync());
        private async Task LimitAsync()
        {
            var stream=new TrackingStream(System.Text.Encoding.UTF8.GetBytes(new string('x',100000)));using var api=new MailApi(new Uri("http://127.0.0.6:7728/"),_=>Task.FromResult("private-test-token"),new Replies{events=stream});
            try{await api.WatchAsync(_=>{},default);Assert.Fail("Expected bounded event rejection");}catch(IOException){}Assert.That(stream.read,Is.LessThan(20000),"不得先缓存10万字符无换行事件再检查长度");
        }
    }
}
