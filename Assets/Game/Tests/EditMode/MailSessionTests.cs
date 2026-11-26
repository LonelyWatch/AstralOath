using System;
using System.Collections;
using UnityEngine.TestTools;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Mail;
using NUnit.Framework;
namespace AstralOath.Client.Tests
{
    public sealed class MailSessionTests
    {
        private static IEnumerator Complete(Task task) { while(!task.IsCompleted) yield return null; task.GetAwaiter().GetResult(); }
        private sealed class Replies : IMailApi
        {
            public long eventVersion;public string firstRequest,retryRequest;public int calls;public bool fail;
            public TaskCompletionSource<MailClaimReceipt> gate;
            public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest r,CancellationToken token)
            {calls++;if(firstRequest==null)firstRequest=r.requestId;else retryRequest=r.requestId;if(fail){fail=false;return Task.FromException<MailClaimReceipt>(new IOException());}return gate?.Task??Task.FromResult(new MailClaimReceipt{requestId=r.requestId,summary=new MailSummary{version=5,effectiveCount=1}});}
            public Task<MailSummary> SummaryAsync(CancellationToken token)=>Task.FromResult(new MailSummary());
            public Task<MailPage> ListAsync(bool h,string c,CancellationToken token)=>Task.FromResult(new MailPage());
            public Task<MailLetter> ReadAsync(string id,CancellationToken token)=>Task.FromResult(new MailLetter());
            public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest r,CancellationToken token)=>throw new NotImplementedException();
            public Task WatchAsync(Action<long> version,CancellationToken token){version(eventVersion);return Task.Delay(Timeout.Infinite,token);}
            public void Dispose(){}
        }
        [Test] public void ResourceRevisionUpdatesSameMailboxVersionAndRejectsStaleEnergy()
        {using var s=new MailSession(new Replies());s.ApplySummary(new MailSummary{version=3,effectiveCount=2,energy=new MailEnergy{revision=10,balance=100}});s.ApplySummary(new MailSummary{version=3,effectiveCount=2,energy=new MailEnergy{revision=11,balance=200}});Assert.That(s.Summary.energy.balance,Is.EqualTo(200));s.ApplySummary(new MailSummary{version=4,effectiveCount=1,energy=new MailEnergy{revision=10,balance=100}});Assert.That(s.Summary.effectiveCount,Is.EqualTo(1));Assert.That(s.Summary.energy.balance,Is.EqualTo(200));}
        [Test] public void BalancesRevisionUpdatesWithoutMailboxVersionChange()
        {using var s=new MailSession(new Replies());s.ApplySummary(new MailSummary{version=3,balances=new MailBalances{revision=10,gold=100}});s.ApplySummary(new MailSummary{version=3,balances=new MailBalances{revision=11,gold=200}});Assert.That(s.Summary.balances.gold,Is.EqualTo(200));s.ApplySummary(new MailSummary{version=4,balances=new MailBalances{revision=10,gold=100}});Assert.That(s.Summary.balances.gold,Is.EqualTo(200));}
        [UnityTest] public IEnumerator InitialStaleSummaryCannotDereferenceMissingResourceSnapshot()=>Complete(InitialStaleAsync());
        private async Task InitialStaleAsync()
        {using var session=new MailSession(new Replies{eventVersion=2});using var life=new CancellationTokenSource();var watching=session.ObserveAsync(life.Token);Assert.That(session.ApplySummary(new MailSummary{version=1,energy=new MailEnergy{revision=10,balance=100}}),Is.False);Assert.That(session.Summary,Is.Null);life.Cancel();try{await watching;}catch(OperationCanceledException){}}
        [Test] public void StaleGetDoesNotReplaceClaimedVersion()
        {using var s=new MailSession(new Replies());s.ApplySummary(new MailSummary{version=4,effectiveCount=5});s.ApplySummary(new MailSummary{version=5,effectiveCount=2});s.ApplySummary(new MailSummary{version=4,effectiveCount=5});Assert.That(s.Summary.effectiveCount,Is.EqualTo(2));}
        [UnityTest] public IEnumerator TimeoutRetryUsesTheOriginalRequestId() => Complete(TimeoutRetryUsesTheOriginalRequestIdAsync());
        private async Task TimeoutRetryUsesTheOriginalRequestIdAsync()
        {var a=new Replies{fail=true};using var s=new MailSession(a);try{await s.ClaimAsync("a",default);}catch(IOException){}await s.ClaimAsync("a",default);Assert.That(a.calls,Is.EqualTo(2));Assert.That(a.retryRequest,Is.EqualTo(a.firstRequest));}
        [UnityTest] public IEnumerator ReceiptNotifiesLoginAfterPageClosedExactlyOnce() => Complete(ReceiptNotifiesLoginAfterPageClosedExactlyOnceAsync());
        private async Task ReceiptNotifiesLoginAfterPageClosedExactlyOnceAsync()
        {var a=new Replies{gate=new TaskCompletionSource<MailClaimReceipt>()};using var s=new MailSession(a);int received=0;string kind=null;s.Claimed+=r=>{received++;kind=r.affectedKinds[0];};using var page=new CancellationTokenSource();var task=s.ClaimAsync("a",page.Token);page.Cancel();try{await task;}catch(OperationCanceledException){}a.gate.SetResult(new MailClaimReceipt{affectedKinds=new[]{"energy"},summary=new MailSummary{version=7,effectiveCount=0}});await Task.Yield();Assert.That(received,Is.EqualTo(1));Assert.That(kind,Is.EqualTo("energy"));Assert.That(a.calls,Is.EqualTo(1));}
        [UnityTest] public IEnumerator ClosingPageDoesNotDiscardTheAuthorityReceipt() => Complete(ClosingPageDoesNotDiscardTheAuthorityReceiptAsync());
        private async Task ClosingPageDoesNotDiscardTheAuthorityReceiptAsync()
        {var a=new Replies{gate=new TaskCompletionSource<MailClaimReceipt>()};using var s=new MailSession(a);using var page=new CancellationTokenSource();var task=s.ClaimAsync("a",page.Token);page.Cancel();try{await task;Assert.Fail("Page should cancel its waiter");}catch(OperationCanceledException){}a.gate.SetResult(new MailClaimReceipt{summary=new MailSummary{version=7,effectiveCount=0}});await Task.Yield();Assert.That(s.Summary.effectiveCount,Is.Zero);}
    }
}
