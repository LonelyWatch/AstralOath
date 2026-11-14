using System;

namespace AstralOath.Shared.Battle
{
    // Transport-only timing data. No Unity, networking, database or wall-clock access.
    public sealed class BattleTurnClockView
    {
        public const int CurrentProtocolVersion = 1;
        public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
        public ulong CommandSequence { get; set; }
        public ulong EventSequence { get; set; }
        public string ActorId { get; set; }
        public long ServerNowUnixMilliseconds { get; set; }
        public long? DeadlineUnixMilliseconds { get; set; }
        public int DecisionSeconds { get; set; }

        public double RemainingSeconds(double elapsedSinceServerSample) => DeadlineUnixMilliseconds.HasValue
            ? Math.Max(0d, Math.Min(DecisionSeconds,
                (DeadlineUnixMilliseconds.Value - ServerNowUnixMilliseconds) / 1000d - Math.Max(0d, elapsedSinceServerSample)))
            : 0d;
    }
}
