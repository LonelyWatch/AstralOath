using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Scripting;
namespace AstralOath.Client.Mail
{
    [Serializable, Preserve] public sealed class MailEnergy { public long balance, naturalCap, absoluteCap, revision; }
    [Serializable, Preserve] public sealed class MailBalances { public long revision, gold, crystals; }
    [Serializable, Preserve] public sealed class MailSummary { public string realmId, playerId, serverUtc, nextExpiryAt; public long version; public int effectiveCount, capacity; public MailEnergy energy; public MailBalances balances; }
    [Serializable, Preserve] public sealed class MailReward { public string kind, key, name, icon, presentation, cover; public int quantity; public MailReward[] children = Array.Empty<MailReward>(); }
    [Serializable, Preserve] public sealed class MailLetter
    {
        public string deliveryId, letterType, templateId, title, body, senderId, senderName, senderIcon, deliveredAt, expiresAt, readAt, historyAt, outcome, presentation, cover;
        public bool bulkEligible;
        public MailReward[] attachments = Array.Empty<MailReward>(), rewards = Array.Empty<MailReward>();
    }
    [Serializable, Preserve] public sealed class MailPage { public MailLetter[] items = Array.Empty<MailLetter>(); public string nextCursor; public MailSummary summary; }
    [Serializable, Preserve] public sealed class MailSkipped { public string deliveryId, reason; }
    [Serializable, Preserve] public sealed class MailClaimReceipt { public string requestId, presentation, cover; public string[] claimed = Array.Empty<string>(), affectedKinds = Array.Empty<string>(); public MailSkipped[] skipped = Array.Empty<MailSkipped>(); public MailReward[] rewards = Array.Empty<MailReward>(); public MailSummary summary; }
    [Serializable, Preserve] public sealed class MailClaimRequest { public string requestId, deliveryId; [NonSerialized] public bool dispatched; }
    [Serializable, Preserve] public sealed class MailClaimAllRequest { public string requestId; public string[] deliveryIds; [NonSerialized] public bool dispatched; }
    public interface IMailApi : IDisposable
    {
        Task<MailSummary> SummaryAsync(CancellationToken token);
        Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token);
        Task<MailLetter> ReadAsync(string deliveryId, CancellationToken token);
        Task<MailClaimReceipt> ClaimAsync(MailClaimRequest request, CancellationToken token);
        Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest request, CancellationToken token);
        Task WatchAsync(Action<long> version, CancellationToken token);
    }
}
