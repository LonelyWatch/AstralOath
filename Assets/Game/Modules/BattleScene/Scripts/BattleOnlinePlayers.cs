using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    // The lobby owns the borrowed avatar sprites until the additive battle is unloaded.
    public sealed class BattleOnlinePlayers
    {
        public string LocalName { get; }
        public string OpponentName { get; }
        public string LocalAvatarId { get; }
        public string OpponentAvatarId { get; }
        public Sprite LocalAvatar { get; }
        public Sprite OpponentAvatar { get; }

        public BattleOnlinePlayers(string localName, string opponentName, string localAvatarId,
            string opponentAvatarId, Sprite localAvatar = null, Sprite opponentAvatar = null)
        {
            LocalName = localName;
            OpponentName = opponentName;
            LocalAvatarId = localAvatarId;
            OpponentAvatarId = opponentAvatarId;
            LocalAvatar = localAvatar;
            OpponentAvatar = opponentAvatar;
        }
    }
}
