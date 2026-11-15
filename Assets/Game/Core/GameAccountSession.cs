using System;

namespace AstralOath.Client.Core
{
    // Process-local identity from realm selection; never written to preferences or logs.
    public sealed class GameAccountSession
    {
        public GameAccountSession(string token, string playerId, string displayName, string avatarId)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            AvatarId = avatarId ?? "";
        }

        public string Token { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarId { get; }
    }
}
