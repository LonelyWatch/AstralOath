using System.Reflection;
using System.Text.Json;
using Xunit;

public sealed class ArenaBattleTurnClockTests
{
    private static Type ClockType => typeof(Program).Assembly.GetType("AstralOath.Server.Arena.ArenaBattleTurnClock")
        ?? throw new InvalidOperationException("Authoritative turn clock is not implemented.");
    private static object New() => Activator.CreateInstance(ClockType, 3UL, 19UL, "ally-1", 1000L, 30, 30)!;
    private static object? Call(object clock, string method, params object[] args) => ClockType.GetMethod(method)!.Invoke(clock, args);
    private static long? Deadline(object clock) => (long?)ClockType.GetProperty("DeadlineUnixMilliseconds")!.GetValue(clock);
    private static bool Ready(object clock, bool host, long now, ulong sequence = 3, ulong events = 19, string actor = "ally-1") =>
        (bool)Call(clock, "Ready", host, sequence, events, actor, now)!;

    [Fact]
    public void DecisionClockStartsOnlyWhenBothSidesHavePresentedTheSameWindow()
    {
        var clock = New();
        Assert.Null(Deadline(clock));
        Assert.True(Ready(clock, true, 2000));
        Assert.Null(Deadline(clock));
        Assert.True(Ready(clock, false, 4500));
        Assert.Equal(34500L, Deadline(clock));
        Assert.False(Ready(clock, false, 9000));
        Assert.False(Ready(clock, true, 9000));
        Assert.Equal(34500L, Deadline(clock));
    }

    [Fact]
    public void StaleSequenceEventCursorOrActorCannotReleasePresentationBarrier()
    {
        var clock = New();
        Assert.False(Ready(clock, true, 2000, sequence: 2));
        Assert.False(Ready(clock, true, 2000, events: 18));
        Assert.False(Ready(clock, true, 2000, actor: "enemy-1"));
        Assert.True(Ready(clock, false, 2000));
        Assert.Null(Deadline(clock));
    }

    [Fact]
    public void UnresponsiveSeatCannotFreezeTurnAndLateReadyCannotExtendDeadline()
    {
        var clock = New();
        Assert.False((bool)Call(clock, "Advance", 30999L)!);
        Assert.True((bool)Call(clock, "Advance", 31000L)!);
        Assert.Equal(61000L, Deadline(clock));
        Assert.False(Ready(clock, true, 50000));
        Assert.False((bool)Call(clock, "IsExpired", 60999L)!);
        Assert.True((bool)Call(clock, "IsExpired", 61000L)!);
    }

    [Fact]
    public void PersistedClockRestoresReadySeatsAndOriginalDeadline()
    {
        var clock = New();
        Ready(clock, true, 2000);
        clock = JsonSerializer.Deserialize(JsonSerializer.Serialize(clock, ClockType), ClockType)!;
        Assert.True(Ready(clock, false, 3000));
        Assert.Equal(33000L, Deadline(clock));
        clock = JsonSerializer.Deserialize(JsonSerializer.Serialize(clock, ClockType), ClockType)!;
        Assert.False(Ready(clock, true, 15000));
        Assert.Equal(33000L, Deadline(clock));
    }

    [Fact]
    public void ReadyAtThePresentationLimitUsesOriginalLimitAndDoesNotGrantExtraTime()
    {
        var clock = New();
        Assert.True(Ready(clock, true, 47000));
        Assert.Equal(61000L, Deadline(clock));
        Assert.False(Ready(clock, false, 60000));
        Assert.Equal(61000L, Deadline(clock));
    }
}
