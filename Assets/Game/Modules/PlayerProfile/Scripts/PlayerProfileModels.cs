using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Scripting;

namespace AstralOath.Client.PlayerProfile
{
    [Serializable, Preserve] public sealed class ProfileHero { public string instanceId, templateId; public int level, stars; }
    [Serializable, Preserve] public sealed class ProfileVisibility { public bool seasonVisible, bestVisible; }
    [Serializable, Preserve] public sealed class ProfileRank { public bool available; public int? value; }
    [Serializable, Preserve] public sealed class ProfileRecord { public bool available, visible; public object data; }
    [Serializable, Preserve] public sealed class PlayerProfileData
    {
        public string playerId, realmId, displayName, avatarId, greeting;
        public long version;
        public bool greetingIsDefault, isSelf, canEdit, representativeConfigured;
        public ProfileHero representativeHero;
        public ProfileVisibility visibility;
        public ProfileRank rank;
        public ProfileRecord seasonRecord, bestRecord;
    }
    [Serializable, Preserve] public sealed class NicknameCheck { public bool available; public string code; public int rulesVersion; }
    [Serializable, Preserve] public sealed class ProfileActionRequest
    {
        public string requestId, kind, nickname, greeting;
        public long expectedVersion;
        public bool? seasonVisible, bestVisible;
        [NonSerialized] public bool dispatched;
    }
    public interface IPlayerProfileApi : IDisposable
    {
        Task<PlayerProfileData> MeAsync(CancellationToken token);
        Task<PlayerProfileData> PlayerAsync(string playerId, CancellationToken token);
        Task<NicknameCheck> CheckNicknameAsync(string nickname, CancellationToken token);
        Task<PlayerProfileData> ActAsync(ProfileActionRequest request, CancellationToken token);
    }
}
