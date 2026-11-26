using System.Text.Json.Serialization;
using AstralOath.Server.Inventory;

namespace AstralOath.Server.Mail;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailDraft(string? TemplateId = null, string? LetterType = null, string? SenderId = null, string? Title = null, string? Body = null, int? ExpiresInDays = null, MailAttachmentInput[]? Attachments = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailAttachmentInput(string? Kind, string? Key, int Quantity);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailClaimInput(string? RequestId, string? DeliveryId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailClaimAllInput(string? RequestId, string[]? DeliveryIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailSendInput(string? RequestId, string? PreviewId);
internal sealed record MailReward(string Kind, string Key, int Quantity, string Name, string Icon, MailReward[] Children, string Presentation = "item", string Cover = "", int Stars = 0, EquipmentGrant? Equipment = null);
internal sealed record MailFrozen(int RulesVersion, string TemplateId, string LetterType, string SenderId, string SenderName, string SenderIcon, string Title, string Body, int ExpiresInDays, bool BulkEligible, MailReward[] Attachments, MailReward[] Rewards, string Presentation, string Cover);
internal sealed record MailItem(string DeliveryId, string LetterType, string TemplateId, string Title, string Body, string SenderId, string SenderName, string SenderIcon, DateTime DeliveredAt, DateTime ExpiresAt, DateTime? ReadAt, DateTime? HistoryAt, string Outcome, bool BulkEligible, MailReward[] Attachments, MailReward[] Rewards, string Presentation, string Cover);
internal sealed record MailEnergy(int Balance, int NaturalCap, int AbsoluteCap, long Revision = 0);
internal sealed record MailBalances(long Revision, long Gold, long Crystals);
internal sealed record MailSummary(string RealmId, string PlayerId, long Version, int EffectiveCount, int Capacity, DateTime ServerUtc, DateTime? NextExpiryAt, MailEnergy Energy, MailBalances? Balances = null);
internal sealed record MailPage(MailItem[] Items, string? NextCursor, MailSummary Summary);
internal sealed record MailSkip(string DeliveryId, string Reason);
internal sealed record MailClaimReceipt(string RequestId, string[] Claimed, MailSkip[] Skipped, MailReward[] Rewards, string[] AffectedKinds, MailSummary Summary, string Presentation, string Cover);
internal sealed record MailSelfPreview(string PreviewId, string RealmId, string PlayerId, int RecipientCount, int ExpiresInDays, MailFrozen Mail, MailReward[] TotalRewards, DateTime PreviewExpiresAt);
internal sealed record MailSendReceipt(string RequestId, string BatchId, string DeliveryId, MailSummary Summary);
internal sealed record MailRewardTotal(string Kind, string Key, long Quantity, string Name, string Icon);
internal sealed record MailOperatorPreview(string PreviewId, string RealmId, int RecipientCount, int DuplicateCount, int ExcludedCount, int ExpiresInDays, DateTime PreviewExpiresAt, MailFrozen Mail, MailRewardTotal[] TotalRewards);
internal sealed record MailBatchFailure(string Reason, int Count);
internal sealed record MailBatchStatus(string BatchId, string PreviewId, DateTime ExpiresAt, int TargetCount, int DeliveredCount, int PendingCount, int FailedCount, int ClaimedCount, MailBatchFailure[] Failures);
