using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailApiTests
    {
        [Test] public void NativePreviewDiagnosticsRequireDevelopmentLoopbackAndExplicitSmokeFlag()
        {
            var local = new Uri("http://127.0.0.6:7728");
            var requested = new[] { "-astral-mail-smoke" };
            Assert.That(DevelopmentGmMailApi.PreviewDiagnosticsAllowed(true, local, requested), Is.True);
            Assert.That(DevelopmentGmMailApi.PreviewDiagnosticsAllowed(false, local, requested), Is.False);
            Assert.That(DevelopmentGmMailApi.PreviewDiagnosticsAllowed(true, new Uri("https://example.invalid"), requested), Is.False);
            Assert.That(DevelopmentGmMailApi.PreviewDiagnosticsAllowed(true, local, Array.Empty<string>()), Is.False);
            Assert.That(DevelopmentGmMailApi.PreviewDiagnosticsAllowed(true, null, requested), Is.False);
        }
#if UNITY_EDITOR
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator PreviewUsesTypedSelfDraftAndSendContainsOnlyItsFrozenIntentUnity() { var task = PreviewUsesTypedSelfDraftAndSendContainsOnlyItsFrozenIntent(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator StableRejectionDoesNotPutDraftOrResponseBodyInExceptionTextUnity() { var task = StableRejectionDoesNotPutDraftOrResponseBodyInExceptionText(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
#endif

        private sealed class Transport : HttpMessageHandler
        {
            internal string path, body;
            internal string response = "{\"previewId\":\"preview\",\"realmId\":\"realm\",\"playerId\":\"owner\",\"recipientCount\":1,\"mail\":{\"letterType\":\"standard\",\"title\":\"<b>plain</b>\"},\"totalRewards\":[]}";
            internal HttpStatusCode status = HttpStatusCode.OK;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                path = request.RequestUri.AbsolutePath;
                body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                Assert.That(request.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
                return new HttpResponseMessage(status) { Content = new StringContent(response) };
            }
        }
        public async Task PreviewUsesTypedSelfDraftAndSendContainsOnlyItsFrozenIntent()
        {
            var handler = new Transport();
            using var api = new DevelopmentGmMailApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("test-only"), handler);
            await api.PreviewAsync(new GmMailDraft { templateId = "maintenance", title = "<b>plain</b>", attachments = new[] { new GmMailAttachment { kind = "currency", key = "bookmarks", quantity = 5 } } }, CancellationToken.None);
            Assert.That(handler.path, Is.EqualTo("/api/development/gm/mail/preview"));
            foreach (string forbidden in new[] { "playerId", "realmId", "target", "allow", "deny", "scope" })
                Assert.That(handler.body.Contains("\"" + forbidden + "\""), Is.False, forbidden);
            handler.response = "{\"requestId\":\"request\",\"deliveryId\":\"delivery\",\"summary\":{\"effectiveCount\":1}}";
            var receipt = await api.SendAsync(new GmMailSendRequest { requestId = "request", previewId = "preview" }, CancellationToken.None);
            Assert.That(handler.path, Is.EqualTo("/api/development/gm/mail/send"));
            Assert.That(handler.body.Contains("\"requestId\""), Is.True);
            Assert.That(handler.body.Contains("\"previewId\""), Is.True);
            Assert.That(handler.body.Contains("title"), Is.False);
            Assert.That(handler.body.Contains("quantity"), Is.False);
            Assert.That(receipt.summary.effectiveCount, Is.EqualTo(1));
        }
        public async Task StableRejectionDoesNotPutDraftOrResponseBodyInExceptionText()
        {
            var handler = new Transport { status = HttpStatusCode.Conflict, response = "{\"error\":\"mailbox_full\",\"body\":\"private draft\"}" };
            using var api = new DevelopmentGmMailApi(new Uri("http://127.0.0.6:7728"), _ => Task.FromResult("test-only"), handler);
            try { await api.SendAsync(new GmMailSendRequest(), CancellationToken.None); Assert.That(false, Is.True, "Expected rejection"); }
            catch (GmMailRejectedException error)
            {
                Assert.That(error.Code, Is.EqualTo("mailbox_full"));
                Assert.That(error.Message.Contains("private draft"), Is.False);
            }
        }
    }
}
