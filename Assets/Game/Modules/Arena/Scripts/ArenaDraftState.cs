using System;
using UnityEngine.Scripting;

namespace AstralOath.Client.Arena
{
    [Serializable, Preserve]
    public sealed class ArenaDraftState
    {
        public string id, roomId, phase, firstPick, activeSide, mySide, battleId, contentHash, deadlineAt;
        public int version, prebanCount, picksRemainingInWindow;
        public bool banProtection;
        public string[] eligibleHeroIds;
        public ArenaDraftSeat host, guest;

        internal bool IsHost => mySide == "host";
        internal ArenaDraftSeat Own => IsHost ? host : guest;
        internal ArenaDraftSeat Opponent => IsHost ? guest : host;
        internal bool OwnTurn => activeSide == mySide;
        internal bool InProgress => phase != "cancelled" && phase != "battle";
    }

    [Serializable, Preserve]
    public sealed class ArenaDraftSeat
    {
        public ArenaPlayer player;
        public string[] prebans, picks, formation;
        public string ban, protectedHeroId;
        public bool submitted;
    }

    [Serializable, Preserve]
    internal sealed class ArenaDraftAction
    {
        public string requestId, draftId, kind;
        public int version;
        public string[] heroIds;
    }
}
