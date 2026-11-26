using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace AstralOath.Client.Mail
{
    // One instance per authenticated login. Write intents outlive page openings, never the login.
    public sealed class MailSession : IDisposable
    {
        private sealed class Intent { public string requestId; public string[] ids; public bool bulk, uncertaintyRaised; public Task<MailClaimReceipt> running; }
        private readonly IMailApi api;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, Intent> pending = new Dictionary<string, Intent>(StringComparer.Ordinal);
        private long minimumVersion;
        private bool disposed;
        public MailSession(IMailApi api) => this.api = api ?? throw new ArgumentNullException(nameof(api));
        public IMailApi Api => api;
        public MailSummary Summary { get; private set; }
        public event Action<MailSummary> Changed;
        public event Action<MailClaimReceipt> Claimed;
        public event Action Uncertain;
        public event Action<long> Invalidated;
        public bool ApplySummary(MailSummary value)
        {
            if (disposed || value == null || (Summary == null && value.version < minimumVersion)) return false;
            bool mailboxFresh = value.version >= minimumVersion && (Summary == null || value.version > Summary.version);
            bool energyFresh = value.energy != null && (Summary?.energy == null || value.energy.revision > Summary.energy.revision);
            bool clockFresh = Summary != null && value.version >= Summary.version && DateTimeOffset.TryParse(value.serverUtc, out var newClock) &&
                (!DateTimeOffset.TryParse(Summary.serverUtc, out var oldClock) || newClock > oldClock);
            bool balancesFresh = value.balances != null && (Summary?.balances == null || value.balances.revision > Summary.balances.revision);
            if (!mailboxFresh && !energyFresh && !balancesFresh && !clockFresh) return false;
            if (mailboxFresh)
            {
                if (Summary?.energy != null && (value.energy == null || value.energy.revision < Summary.energy.revision)) value.energy = Summary.energy;
                if (Summary?.balances != null && (value.balances == null || value.balances.revision < Summary.balances.revision)) value.balances = Summary.balances;
                Summary = value; minimumVersion = value.version;
            }
            else
            {
                var previous = Summary;
                Summary = new MailSummary { realmId = previous.realmId, playerId = previous.playerId, serverUtc = clockFresh ? value.serverUtc : previous.serverUtc,
                    nextExpiryAt = previous.nextExpiryAt, version = previous.version, effectiveCount = previous.effectiveCount,
                    capacity = previous.capacity, energy = energyFresh ? value.energy : previous.energy,
                    balances = balancesFresh ? value.balances : previous.balances };
            }
            Changed?.Invoke(Summary); return true;
        }
        public async Task RefreshAsync(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            ApplySummary(await api.SummaryAsync(linked.Token));
        }
        public async Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var result = await api.ListAsync(history, cursor, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (result.summary == null || result.summary.version < minimumVersion) return null;
            ApplySummary(result.summary); return result;
        }
        public Task<MailClaimReceipt> ClaimAsync(string id, CancellationToken page) => Submit("one:" + id, new[] { id }, false, page);
        public Task<MailClaimReceipt> ClaimAllAsync(string[] ids, CancellationToken page)
        {
            if (ids == null || ids.Length == 0) throw new ArgumentException("A mail snapshot is required.", nameof(ids));
            var snapshot = ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return Submit("all:" + string.Join(",", snapshot), snapshot, true, page);
        }
        private Task<MailClaimReceipt> Submit(string key, string[] ids, bool bulk, CancellationToken page)
        {
            lifetime.Token.ThrowIfCancellationRequested(); page.ThrowIfCancellationRequested();
            if (!pending.TryGetValue(key, out var intent))
            {
                intent = new Intent { requestId = Guid.NewGuid().ToString("N"), ids = ids, bulk = bulk };
                pending.Add(key, intent);
            }
            if (intent.running == null || intent.running.IsCompleted) intent.running = SendAsync(key, intent);
            return AwaitPage(intent.running, page);
        }
        private async Task<MailClaimReceipt> SendAsync(string key, Intent intent)
        {
            var one = new MailClaimRequest { requestId = intent.requestId, deliveryId = intent.ids[0] };
            var all = new MailClaimAllRequest { requestId = intent.requestId, deliveryIds = intent.ids };
            try
            {
                var receipt = intent.bulk ? await api.ClaimAllAsync(all, lifetime.Token) : await api.ClaimAsync(one, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                ApplySummary(receipt.summary); pending.Remove(key); Claimed?.Invoke(receipt); return receipt;
            }
            catch (Exception error) when (!lifetime.IsCancellationRequested && (one.dispatched || all.dispatched) &&
                (error is System.IO.IOException || error is System.Net.Http.HttpRequestException || error is OperationCanceledException))
            {
                bool definiteRejection = error is MailRequestException rejected && (int)rejected.Status >= 400 && (int)rejected.Status < 500;
                if (definiteRejection) { pending.Remove(key); throw; }
                if (!intent.uncertaintyRaised) { intent.uncertaintyRaised = true; Uncertain?.Invoke(); }
                throw;
            }
        }
        public async Task RetryPendingAsync(CancellationToken token)
        {
            foreach (var row in pending.ToArray())
                await Submit(row.Key, row.Value.ids, row.Value.bulk, token);
        }
        private static async Task<T> AwaitPage<T>(Task<T> task, CancellationToken page)
        {
            if (!page.CanBeCanceled) return await task;
            var cancelled = new TaskCompletionSource<bool>();
            using (page.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task) { _ = ObserveAbandonedAsync(task); throw new OperationCanceledException(page); }
                page.ThrowIfCancellationRequested(); return await task;
            }
        }
        private static async Task ObserveAbandonedAsync<T>(Task<T> task) { try { await task; } catch (Exception) { /* The login retains this intent and reports uncertainty; its page waiter has ended. */ } }
        public async Task ObserveAsync(CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            while (!linked.IsCancellationRequested)
            {
                try
                {
                    await api.WatchAsync(version =>
                    {
                        if (disposed || version <= minimumVersion) return;
                        minimumVersion = version; Invalidated?.Invoke(version);
                    }, linked.Token);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
                catch (MailRequestException error) when (error.Code == "unauthorized") { break; }
                catch (Exception error) when (error is System.IO.IOException || error is System.Net.Http.HttpRequestException || error is OperationCanceledException) { }
                if (linked.IsCancellationRequested) break;
                try { await RefreshAsync(linked.Token); }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
                catch (MailRequestException error) when (error.Code == "unauthorized") { break; }
                catch (Exception error) when (error is System.IO.IOException || error is System.Net.Http.HttpRequestException || error is OperationCanceledException) { }
                await Task.Delay(3000, linked.Token);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); pending.Clear(); Summary = null;
            Changed = null; Invalidated = null; Claimed = null; Uncertain = null; api.Dispose(); lifetime.Dispose();
        }
    }
}
