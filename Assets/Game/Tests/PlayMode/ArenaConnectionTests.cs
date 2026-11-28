using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaConnectionTests
    {
        private sealed class Transport : HttpMessageHandler
        {
            internal readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>();
            internal int Sessions;
            internal bool FailFirst;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/api/arena/sessions"));
                int number = Interlocked.Increment(ref Sessions);
                var cancelled = new TaskCompletionSource<bool>();
                using (token.Register(() => cancelled.TrySetResult(true)))
                {
                    await Task.WhenAny(Release.Task, cancelled.Task);
                    token.ThrowIfCancellationRequested();
                }
                bool fail = FailFirst && number == 1;
                return new HttpResponseMessage(fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(fail ? "{\"code\":\"database_unavailable\"}" :
                        "{\"token\":\"connection-test-session\",\"player\":{\"id\":\"p\",\"displayName\":\"Guest\",\"avatarId\":\"c1001\"}}")
                };
            }
        }

        private static IEnumerator Done(Task task)
        {
            var timer = Stopwatch.StartNew();
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 4000) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Connection operation did not observe cancellation or release within4s.");
            if (task.IsFaulted) throw task.Exception.InnerException;
            Assert.That(task.IsCanceled, Is.False);
        }

        private static async Task Settle(params Task[] tasks)
        { try { await Task.WhenAll(tasks); } catch { /* Observe cleanup failures without replacing the tested assertion. */ } }

        private static async Task MustCancel(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { return; }
            Assert.Fail("Expected caller/owner cancellation, not a successful session.");
        }

        [UnityTest] public IEnumerator ConcurrentBearerRequestsEstablishOnlyOneSession() => Done(Concurrent());
        private static async Task Concurrent()
        {
            var transport = new Transport();
            using var api = new ArenaApi(new Uri("http://localhost:7728"), default, null, transport);
            var requests = Enumerable.Range(0, 8).Select(_ => api.GetSessionTokenAsync("Guest", default)).ToArray();
            try
            {
                Assert.That(transport.Sessions, Is.EqualTo(1), "Parallel prefetch must share one guest-identity establishment.");
                transport.Release.TrySetResult(true);
                var results = await Task.WhenAll(requests);
                Assert.That(results.All(value => value == "connection-test-session"), Is.True);
                Assert.That(await api.GetSessionTokenAsync("Guest", default), Is.EqualTo("connection-test-session"));
                Assert.That(transport.Sessions, Is.EqualTo(1), "A successful session must be reused.");
            }
            finally { transport.Release.TrySetResult(true); await Settle(requests); }
        }

        [UnityTest] public IEnumerator FailedFirstConnectionCanBeRetried() => Done(FailureThenRetry());
        private static async Task FailureThenRetry()
        {
            var transport = new Transport { FailFirst = true };
            using var api = new ArenaApi(new Uri("http://localhost:7728"), default, null, transport);
            transport.Release.TrySetResult(true);
            try { await api.GetSessionTokenAsync("Guest", default); Assert.Fail("First503 must remain a failure."); }
            catch (ArenaRequestException) { }
            Assert.That(await api.GetSessionTokenAsync("Guest", default), Is.EqualTo("connection-test-session"));
            Assert.That(transport.Sessions, Is.EqualTo(2), "Failure must release the gate so a later call can retry.");
        }

        [UnityTest] public IEnumerator CancellingAWaiterDoesNotCancelTheCreator() => Done(CancelWaiter());
        private static async Task CancelWaiter()
        {
            var transport = new Transport();
            using var api = new ArenaApi(new Uri("http://localhost:7728"), default, null, transport);
            using var waiterCancellation = new CancellationTokenSource();
            var creator = api.GetSessionTokenAsync("Guest", default);
            var waiter = api.GetSessionTokenAsync("Guest", waiterCancellation.Token);
            try
            {
                waiterCancellation.Cancel();
                await MustCancel(waiter);
                Assert.That(creator.IsCompleted, Is.False, "One waiting page must not cancel the identity creator.");
                Assert.That(transport.Sessions, Is.EqualTo(1), "A cancelled waiting page must never send a second session request.");
                transport.Release.TrySetResult(true);
                Assert.That(await creator, Is.EqualTo("connection-test-session"));
            }
            finally { transport.Release.TrySetResult(true); await Settle(creator, waiter); }
        }

        [UnityTest] public IEnumerator CancelledCreatorReleasesTheGateForTheNextCaller() => Done(CancelCreator());
        private static async Task CancelCreator()
        {
            var transport = new Transport();
            using var api = new ArenaApi(new Uri("http://localhost:7728"), default, null, transport);
            using var creatorCancellation = new CancellationTokenSource();
            var creator = api.GetSessionTokenAsync("Guest", creatorCancellation.Token);
            var next = api.GetSessionTokenAsync("Guest", default);
            try
            {
                creatorCancellation.Cancel();
                await MustCancel(creator);
                transport.Release.TrySetResult(true);
                Assert.That(await next, Is.EqualTo("connection-test-session"));
                Assert.That(transport.Sessions, Is.EqualTo(2));
            }
            finally { transport.Release.TrySetResult(true); await Settle(creator, next); }
        }

        [UnityTest] public IEnumerator OwnerCancellationCancelsTheCreatorAndQueuedCallers() => Done(CancelOwner());
        private static async Task CancelOwner()
        {
            var transport = new Transport();
            using var owner = new CancellationTokenSource();
            using var api = new ArenaApi(new Uri("http://localhost:7728"), owner.Token, null, transport);
            var requests = Enumerable.Range(0, 4).Select(_ => api.GetSessionTokenAsync("Guest", default)).ToArray();
            try
            {
                owner.Cancel();
                foreach (var request in requests) await MustCancel(request);
                Assert.That(transport.Sessions, Is.EqualTo(1), "Owner cancellation must remove queued callers without creating identities.");
            }
            finally { transport.Release.TrySetResult(true); await Settle(requests); }
        }

        [UnityTest] public IEnumerator DisposeDuringConnectionCancelsWaitersWithoutAReleaseRace() => Done(DisposePending());
        private static async Task DisposePending()
        {
            var transport = new Transport();
            var api = new ArenaApi(new Uri("http://localhost:7728"), default, null, transport);
            var creator = api.GetSessionTokenAsync("Guest", default);
            var waiter = api.GetSessionTokenAsync("Guest", default);
            try
            {
                api.Dispose();
                await MustCancel(creator);
                await MustCancel(waiter);
                Assert.That(transport.Sessions, Is.EqualTo(1));
                api.Dispose(); // Lifetime teardown may repeat disposal; no gate-release ObjectDisposedException.
            }
            finally { transport.Release.TrySetResult(true); await Settle(creator, waiter); api.Dispose(); }
        }
    }
}
