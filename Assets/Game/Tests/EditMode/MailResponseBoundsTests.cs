using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Mail;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class MailResponseBoundsTests
    {
        private sealed class CountingStream : MemoryStream
        {
            public long bytesRead;
            public CountingStream(string text) : base(Encoding.UTF8.GetBytes(text)) { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = base.Read(buffer, offset, count);
                bytesRead += read;
                return read;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
                Task.FromResult(Read(buffer, offset, count));
        }

        private sealed class Replies : HttpMessageHandler
        {
            private readonly string json;
            public CountingStream stream;
            public Replies(string json) => this.json = json;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.That(request.RequestUri.Query, Does.Contain("limit=100"));
                stream = new CountingStream(json);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            }
        }

        private static MailApi Api(Replies transport) => new MailApi(new Uri("http://127.0.0.6:7728/"),
            _ => Task.FromResult("bounds-only-fake-transport"), transport);
        private static IEnumerator Complete(Task task)
        {
            while (!task.IsCompleted) yield return null;
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator LegalHundredLetterPageSupportsEscapedMaximumLengthBodies() => Complete(LegalPageAsync());

        private static async Task LegalPageAsync()
        {
            // The published Host's actual MailCatalog.Freeze accepts this 8000-code-unit body.
            // ASP.NET's default encoder emits U+3000 as \u3000 even with relaxed Chinese encoding.
            string body = new string('\u3000', 3000) + new string('信', 5000);
            var letters = new MailLetter[100];
            for (int i = 0; i < letters.Length; ++i)
                letters[i] = new MailLetter { deliveryId = i.ToString("D32"), letterType = "gm",
                    title = "合法上限正文", body = body, senderId = "gm_yufine", senderName = "GM亚卡", outcome = "active" };
            var expected = new MailPage { items = letters, summary = new MailSummary { version = 100, effectiveCount = 100, capacity = 100 } };
            string json = JsonUtility.ToJson(expected).Replace("\u3000", "\\u3000");
            Assert.That(json.Length, Is.GreaterThan(2 * 1024 * 1024), "The fixture must reproduce the old actual input bound.");
            var transport = new Replies(json);
            using var api = Api(transport);
            var page = await api.ListAsync(false, null, CancellationToken.None);
            Assert.That(page.items.Length, Is.EqualTo(100));
            Assert.That(page.items[99].body, Is.EqualTo(body));
            Assert.That(page.summary.effectiveCount, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator OversizedResponseStillStopsBeforeBufferingTheWholeBody() => Complete(OversizedAsync());

        private static async Task OversizedAsync()
        {
            const int limit = 8 * 1024 * 1024;
            var transport = new Replies(new string('x', limit + 65536));
            using var api = Api(transport);
            try
            {
                await api.ListAsync(false, null, CancellationToken.None);
                Assert.Fail("An oversized response must be rejected before JSON parsing.");
            }
            catch (IOException error) { Assert.That(error.Message, Is.EqualTo("Mail response exceeds limit.")); }
            Assert.That(transport.stream.bytesRead, Is.LessThanOrEqualTo(limit + 8192));
        }
    }
}
