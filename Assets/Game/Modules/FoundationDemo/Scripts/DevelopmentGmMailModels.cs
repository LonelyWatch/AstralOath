using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Mail;
using UnityEngine.Scripting;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AstralOath.Client.EditMode")]
[assembly: InternalsVisibleTo("AstralOath.Client.PlayMode")]

namespace AstralOath.Client.FoundationDemo
{
    [Serializable, Preserve] internal sealed class GmMailAttachment { public string kind, key; public int quantity; }
    [Serializable, Preserve] internal sealed class GmMailDraft
    {
        public string templateId, letterType, senderId, title, body;
        public int expiresInDays;
        public GmMailAttachment[] attachments = Array.Empty<GmMailAttachment>();
    }
    [Serializable, Preserve] internal sealed class GmMailCatalog
    {
        public int rulesVersion;
        public LetterType[] letterTypes;
        public Sender[] senders;
        public Resource[] resources;
        public Bundle[] bundles;
        public Template[] templates;
        public Limits limits;
        [Serializable, Preserve] internal sealed class LetterType { public string id; public bool bulkEligible; }
        [Serializable, Preserve] internal sealed class Sender { public string id, name, icon; }
        [Serializable, Preserve] internal sealed class Resource { public string kind, key, name, icon; public int maxQuantity; }
        [Serializable, Preserve] internal sealed class Bundle { public string id, name, icon, presentation, cover; public GmMailAttachment[] attachments; }
        [Serializable, Preserve] internal sealed class Template
        {
            public string id, purpose, letterType, senderId, title, body;
            public int expiresInDays;
            public GmMailAttachment[] attachments;
        }
        [Serializable, Preserve] internal sealed class Limits { public int maxAttachments, maxQuantity, maxTitleLength, maxBodyLength, maxExpiryDays; }
    }
    [Serializable, Preserve] internal sealed class GmMailPreview
    {
        public string previewId, realmId, playerId, previewExpiresAt;
        public int recipientCount, expiresInDays;
        public MailLetter mail;
        public MailReward[] totalRewards;
    }
    [Serializable, Preserve] internal sealed class GmMailSendRequest { public string requestId, previewId; }
    [Serializable, Preserve] internal sealed class GmMailReceipt { public string requestId, batchId, deliveryId; public MailSummary summary; }
    internal sealed class GmMailRejectedException : System.IO.IOException
    {
        internal int Status { get; }
        internal string Code { get; }
        internal GmMailRejectedException(int status, string code) : base("GM mail request rejected.") { Status = status; Code = code; }
    }
    internal interface IDevelopmentGmMailApi : IDisposable
    {
        Task<GmMailCatalog> CatalogAsync(CancellationToken token);
        Task<GmMailPreview> PreviewAsync(GmMailDraft draft, CancellationToken token);
        Task<GmMailReceipt> SendAsync(GmMailSendRequest request, CancellationToken token);
    }
}
