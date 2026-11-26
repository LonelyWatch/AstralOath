using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class GmMailIntentTests
    {
#if UNITY_EDITOR
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator PreviewAndSendLockTheFormAndDoubleClickSharesOnePostUnity() { var task = PreviewAndSendLockTheFormAndDoubleClickSharesOnePost(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator ClosingAndTimeoutKeepTheOriginalPreviewAndRequestUntilExplicitAnotherUnity() { var task = ClosingAndTimeoutKeepTheOriginalPreviewAndRequestUntilExplicitAnother(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator ClosingDoesNotCancelLoginWriteAndLateReceiptNotifiesSharedMailboxUnity() { var task = ClosingDoesNotCancelLoginWriteAndLateReceiptNotifiesSharedMailbox(); while (!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
#endif

        private sealed class Fake : IDevelopmentGmMailApi
        {
            internal TaskCompletionSource<GmMailPreview> preview;
            internal TaskCompletionSource<GmMailReceipt> send;
            internal readonly List<GmMailSendRequest> requests = new List<GmMailSendRequest>();
            internal CancellationToken sendToken;
            public Task<GmMailCatalog> CatalogAsync(CancellationToken token) => Task.FromResult(new GmMailCatalog());
            public Task<GmMailPreview> PreviewAsync(GmMailDraft draft, CancellationToken token) => preview?.Task ?? Task.FromResult(new GmMailPreview { previewId = "preview-1" });
            public Task<GmMailReceipt> SendAsync(GmMailSendRequest request, CancellationToken token)
            {
                requests.Add(request);
                sendToken = token;
                return send.Task;
            }
            public void Dispose() { }
        }

        public async Task PreviewAndSendLockTheFormAndDoubleClickSharesOnePost()
        {
            var api = new Fake { preview = new TaskCompletionSource<GmMailPreview>(), send = new TaskCompletionSource<GmMailReceipt>() };
            using var intent = new DevelopmentGmMailIntent(api);
            var preview = intent.PreviewAsync(new GmMailDraft(), CancellationToken.None);
            Assert.That(intent.FormLocked, Is.True, "preview submission must freeze editable controls");
            api.preview.SetResult(new GmMailPreview { previewId = "preview-1" }); await preview;
            var first = intent.SendAsync(CancellationToken.None);
            var second = intent.SendAsync(CancellationToken.None);
            Assert.That(api.requests.Count, Is.EqualTo(1));
            Assert.That(intent.FormLocked, Is.True);
            api.send.SetResult(new GmMailReceipt { requestId = intent.Request.requestId });
            await Task.WhenAll(first, second);
            Assert.That(intent.FormLocked, Is.True, "a successful send needs explicit send-another");
            await intent.SendAsync(CancellationToken.None);
            Assert.That(api.requests.Count, Is.EqualTo(1));
        }

        public async Task ClosingAndTimeoutKeepTheOriginalPreviewAndRequestUntilExplicitAnother()
        {
            var api = new Fake { send = new TaskCompletionSource<GmMailReceipt>() };
            using var intent = new DevelopmentGmMailIntent(api);
            await intent.PreviewAsync(new GmMailDraft(), CancellationToken.None);
            using var page = new CancellationTokenSource();
            var closed = intent.SendAsync(page.Token);
            string original = intent.Request.requestId;
            page.Cancel();
            try { await closed; } catch (OperationCanceledException) { }
            api.send.SetException(new IOException("lost response"));
            await Task.Yield();
            Assert.That(intent.Request.requestId, Is.EqualTo(original));
            Assert.That(intent.FormLocked, Is.True);
            Assert.Throws<InvalidOperationException>(() => intent.EditDraft());
            api.send = new TaskCompletionSource<GmMailReceipt>();
            var retry = intent.SendAsync(CancellationToken.None);
            Assert.That(api.requests[1].requestId, Is.EqualTo(original));
            Assert.That(api.requests[1].previewId, Is.EqualTo("preview-1"));
            api.send.SetResult(new GmMailReceipt { requestId = original }); await retry;
            intent.SendAnother();
            Assert.That(intent.FormLocked, Is.False);
            Assert.That(intent.Request, Is.Null);
            await intent.PreviewAsync(new GmMailDraft(), CancellationToken.None);
            api.send = new TaskCompletionSource<GmMailReceipt>();
            var another = intent.SendAsync(CancellationToken.None);
            Assert.That(intent.Request.requestId, Is.Not.EqualTo(original));
            api.send.SetResult(new GmMailReceipt()); await another;
        }

        public async Task ClosingDoesNotCancelLoginWriteAndLateReceiptNotifiesSharedMailbox()
        {
            var api = new Fake { send = new TaskCompletionSource<GmMailReceipt>() };
            using var intent = new DevelopmentGmMailIntent(api);
            await intent.PreviewAsync(new GmMailDraft(), CancellationToken.None);
            GmMailReceipt observed = null;
            intent.Sent += receipt => observed = receipt;
            using var page = new CancellationTokenSource();
            var waiter = intent.SendAsync(page.Token); page.Cancel();
            try { await waiter; } catch (OperationCanceledException) { }
            Assert.That(api.sendToken.IsCancellationRequested, Is.False);
            Assert.That(intent.IsBusy, Is.True);
            var success = new GmMailReceipt { summary = new Mail.MailSummary { effectiveCount = 1 } };
            api.send.SetResult(success);
            await Task.Yield();
            Assert.That(observed, Is.EqualTo(success));
            Assert.That(intent.Receipt, Is.EqualTo(success));
            Assert.That(intent.IsBusy, Is.False);
        }
    }
}
