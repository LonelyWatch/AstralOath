using System.Text.Json.Serialization;

namespace AstralOath.Server.Social;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ProfileAction(string? RequestId, long? ExpectedVersion, string? Kind,
    string? Nickname = null, string? Greeting = null, bool? SeasonVisible = null, bool? BestVisible = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record NicknameCheck(string? Nickname);
internal sealed record ProfileVisibility(bool SeasonVisible, bool BestVisible);
internal sealed record ProfileUnavailable(bool Available = false, int? Value = null);
internal sealed record ProfileRecord(bool Available, bool Visible, object? Data = null);
internal sealed record ProfileHero(string InstanceId, string TemplateId, int Level, int Stars);
internal sealed record PlayerProfileData(string PlayerId, string Uid, string RealmId, string DisplayName, string AvatarId,
    string Greeting, bool GreetingIsDefault, long Version, bool IsSelf, bool CanEdit,
    ProfileHero? RepresentativeHero, bool RepresentativeConfigured, ProfileVisibility Visibility,
    ProfileUnavailable Rank, ProfileRecord SeasonRecord, ProfileRecord BestRecord);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ChatNotifications([property: JsonRequired] bool Hero5, [property: JsonRequired] bool Artifact5,
    [property: JsonRequired] bool GuildPromotion, [property: JsonRequired] bool GeneralChat);
internal sealed record ChatPreferences(long Version, int? FavoriteChannel, ChatNotifications Notifications,
    string[] Keywords, bool TransparentBackground);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ChatPreferenceAction(string? RequestId, long? ExpectedVersion, string? Kind,
    int? FavoriteChannel = null, ChatNotifications? Notifications = null, string[]? Keywords = null,
    bool? TransparentBackground = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ChatSend(string? RequestId, string? Kind, string? Text = null, string? StickerId = null);
internal sealed record ChatSender(string Id, string DisplayName, string AvatarId);
internal sealed record ChatMessage(string Id, int Channel, ChatSender Player, string Kind,
    string? Text, string? StickerId, DateTime CreatedAt);
internal sealed record ChatPage(int Channel, IReadOnlyList<ChatMessage> Messages, string NextAfter, bool HasMore);
internal sealed class SocialProblem(int status, string code) : Exception(code)
{
    internal int Status { get; } = status;
    internal string Code { get; } = code;
}
