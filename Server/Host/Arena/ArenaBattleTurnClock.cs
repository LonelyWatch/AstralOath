using AstralOath.Shared.Battle;

namespace AstralOath.Server.Arena;

// Persisted with the draft under the room's SQL lock. Explicit milliseconds keep
// transitions deterministic and testable without reading the wall clock here.
internal sealed class ArenaBattleTurnClock
{
    public ulong CommandSequence { get; set; }
    public ulong EventSequence { get; set; }
    public string ActorId { get; set; } = "";
    public long PresentationDeadlineUnixMilliseconds { get; set; }
    public long? DeadlineUnixMilliseconds { get; set; }
    public int DecisionSeconds { get; set; }
    public int PresentationWaitSeconds { get; set; }
    public bool HostReady { get; set; }
    public bool GuestReady { get; set; }

    public ArenaBattleTurnClock() { }

    public ArenaBattleTurnClock(ulong commandSequence, ulong eventSequence, string actorId,
        long now, int decisionSeconds, int presentationWaitSeconds)
    {
        CommandSequence = commandSequence;
        EventSequence = eventSequence;
        ActorId = actorId;
        DecisionSeconds = decisionSeconds;
        PresentationWaitSeconds = presentationWaitSeconds;
        PresentationDeadlineUnixMilliseconds = checked(now + presentationWaitSeconds * 1000L);
    }

    public bool Matches(ulong sequence, ulong events, string actor) =>
        CommandSequence == sequence && EventSequence == events && ActorId == actor;

    public bool Advance(long now)
    {
        if (DeadlineUnixMilliseconds.HasValue || now < PresentationDeadlineUnixMilliseconds) return false;
        DeadlineUnixMilliseconds = checked(PresentationDeadlineUnixMilliseconds + DecisionSeconds * 1000L);
        return true;
    }

    public bool Ready(bool host, ulong sequence, ulong events, string actor, long now)
    {
        if (!Matches(sequence, events, actor)) return false;
        bool advanced = Advance(now);
        if (DeadlineUnixMilliseconds.HasValue) return advanced;
        if (host ? HostReady : GuestReady) return false;
        if (host) HostReady = true; else GuestReady = true;
        if (HostReady && GuestReady) DeadlineUnixMilliseconds = checked(now + DecisionSeconds * 1000L);
        return true;
    }

    public bool IsExpired(long now) => DeadlineUnixMilliseconds.HasValue && now >= DeadlineUnixMilliseconds.Value;

    public BattleTurnClockView View(long now) => new()
    {
        CommandSequence = CommandSequence, EventSequence = EventSequence, ActorId = ActorId,
        DecisionSeconds = DecisionSeconds, ServerNowUnixMilliseconds = now,
        DeadlineUnixMilliseconds = DeadlineUnixMilliseconds
    };
}
