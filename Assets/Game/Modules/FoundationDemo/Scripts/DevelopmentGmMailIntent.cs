using System;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.FoundationDemo
{
    // Login-owned send intent. Window cancellation must never discard an ambiguous POST.
    internal sealed class DevelopmentGmMailIntent : IDisposable
    {
        private readonly IDevelopmentGmMailApi api;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task<GmMailReceipt> sending;
        private bool previewing, rejected, sendingNow;
        internal DevelopmentGmMailIntent(IDevelopmentGmMailApi api) => this.api = api;
        internal bool FormLocked => previewing || Preview != null || Request != null || Receipt != null;
        internal GmMailPreview Preview { get; private set; }
        internal GmMailSendRequest Request { get; private set; }
        internal GmMailReceipt Receipt { get; private set; }
        internal bool IsBusy => previewing || sendingNow;
        internal bool CanEdit => !IsBusy && Receipt == null && (Request == null || rejected);
        internal bool Uncertain { get; private set; }
        internal event Action<GmMailReceipt> Sent;
        internal event Action Changed;
        internal async Task PreviewAsync(GmMailDraft draft, CancellationToken token)
        {
            if (FormLocked) throw new InvalidOperationException("Finish or edit the existing preview first.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            previewing = true;
            Changed?.Invoke();
            try
            {
                var result = await api.PreviewAsync(draft, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                Preview = result;
            }
            finally { previewing = false; Changed?.Invoke(); }
        }
        internal Task<GmMailReceipt> SendAsync(CancellationToken page)
        {
            lifetime.Token.ThrowIfCancellationRequested(); page.ThrowIfCancellationRequested();
            if (Receipt != null) return Task.FromResult(Receipt);
            if (Preview == null || previewing) throw new InvalidOperationException("A frozen preview is required.");
            if (Request == null) Request = new GmMailSendRequest { requestId = Guid.NewGuid().ToString("N"), previewId = Preview.previewId };
            if (sending == null || sending.IsCompleted) sending = SendCoreAsync();
            return AwaitPageAsync(sending, page);
        }
        private async Task<GmMailReceipt> SendCoreAsync()
        {
            rejected = false; Uncertain = false; sendingNow = true; Changed?.Invoke();
            try
            {
                var result = await api.SendAsync(Request, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                Receipt = result; Sent?.Invoke(result); return result;
            }
            catch (GmMailRejectedException) { rejected = true; throw; }
            catch (Exception) { Uncertain = true; throw; }
            finally { sendingNow = false; Changed?.Invoke(); }
        }
        private static async Task<GmMailReceipt> AwaitPageAsync(Task<GmMailReceipt> task, CancellationToken page)
        {
            if (!page.CanBeCanceled) return await task;
            var closed = new TaskCompletionSource<bool>();
            using (page.Register(() => closed.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, closed.Task) != task)
                {
                    // Observe a late fault even when the window has gone away.
                    _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new OperationCanceledException(page);
                }
                page.ThrowIfCancellationRequested(); return await task;
            }
        }
        internal void EditDraft()
        {
            if (IsBusy || Receipt != null || Request != null && !rejected)
                throw new InvalidOperationException("Resolve the send before editing its draft.");
            Clear();
        }
        internal void SendAnother()
        {
            if (Receipt == null || IsBusy) throw new InvalidOperationException("The previous send must be confirmed first.");
            Clear();
        }
        private void Clear() { Preview = null; Request = null; Receipt = null; sending = null; Uncertain = false; rejected = false; Changed?.Invoke(); }
        public void Dispose() { lifetime.Cancel(); Sent = null; Changed = null; lifetime.Dispose(); }
    }
}
