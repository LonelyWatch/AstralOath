namespace AstralOath.Shared.Battle
{
    public interface IBattleContext
    {
        string BattleId { get; }
        uint RulesVersion { get; }
        ulong RandomSeed { get; }
    }

    public interface IBattleCommand
    {
        ulong Sequence { get; }
        string ActorId { get; }
    }

    public interface IBattleEvent
    {
        ulong Sequence { get; }
    }

    public interface IBattleSnapshot
    {
        ulong LastEventSequence { get; }
    }

    public interface IBattleResult
    {
        string BattleId { get; }
        ulong LastEventSequence { get; }
    }
}
