using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleMatchTests
{
    [Fact]
    public void SpeedAdvancesReadinessAndOrdersTurns()
    {
        var resolver = new Resolver((_, command, _) => new(new[] { BattleChange.Damage(command.ActorId, command.TargetId, 30) }));
        var ally = Unit("ally", BattleSide.Ally, 120);
        var enemy = Unit("enemy", BattleSide.Enemy, 80);
        var battle = new BattleMatch("match", 1, 42, new BattleFormation(new[] { enemy, ally }), resolver);

        BattleSnapshot start = battle.Snapshot;
        Assert.Equal("ally", start.CurrentActorId);
        Assert.Equal(10080, start.GetUnit("ally").ReadinessBp);
        Assert.Equal(6720, start.GetUnit("enemy").ReadinessBp);
        Assert.Equal(new[] { BattleFlowEventKind.BattleStarted, BattleFlowEventKind.TurnStarted },
            battle.OpeningEvents.Select(item => item.Kind));

        var action = battle.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(BattleCommandStatus.Accepted, action.Status);
        Assert.Equal("enemy", action.Snapshot.CurrentActorId);
        Assert.Equal(2, action.Snapshot.TurnNumber);
        Assert.Equal(4920, action.Snapshot.GetUnit("ally").ReadinessBp);
        Assert.Equal(10000, action.Snapshot.GetUnit("enemy").ReadinessBp);
        Assert.Equal(70, action.Snapshot.GetUnit("enemy").CurrentHealth);
        Assert.Equal(100, start.GetUnit("enemy").CurrentHealth);
        Assert.Equal(100, enemy.CurrentHealth);
        Assert.Equal(new ulong[] { 3, 4, 5, 6 }, action.Events.Select(item => item.Sequence));
    }

    [Fact]
    public void InvalidAndRetriedCommandsDoNotCallResolverAgain()
    {
        var resolver = new Resolver((_, _, _) => new(Array.Empty<BattleChange>()));
        var match = Match(resolver);
        var initial = match.Snapshot;
        Assert.Equal(BattleCommandStatus.WrongBattle,
            match.Submit(new("other", 1, 1, "action", "ally", "strike", "enemy")).Status);
        Assert.Equal(BattleCommandStatus.WrongRulesVersion,
            match.Submit(new("match", 2, 1, "action", "ally", "strike", "enemy")).Status);
        Assert.Equal(BattleCommandStatus.SequenceMismatch, match.Submit(Command(2, "ally", "enemy")).Status);
        Assert.Equal(BattleCommandStatus.WrongActor, match.Submit(Command(1, "enemy", "ally")).Status);
        Assert.Equal(BattleCommandStatus.UnknownSkill,
            match.Submit(new("match", 1, 1, "action", "ally", "other", "enemy")).Status);
        Assert.Equal(BattleCommandStatus.UnknownTarget, match.Submit(Command(1, "ally", "missing")).Status);
        Assert.Same(initial, match.Snapshot);
        Assert.Equal(0, resolver.Calls);

        var original = Command(1, "ally", "enemy");
        var accepted = match.Submit(original);
        var repeated = match.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(BattleCommandStatus.Accepted, accepted.Status);
        Assert.Equal(BattleCommandStatus.Duplicate, repeated.Status);
        Assert.Same(accepted.Snapshot, repeated.Snapshot);
        Assert.Equal(accepted.Events, repeated.Events);
        Assert.Equal(BattleCommandStatus.CommandIdConflict,
            match.Submit(new("match", 1, 1, "action-1", "ally", "strike", "enemy", true)).Status);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void WinningHitEndsBattleAndDeadUnitDoesNotGetTurn()
    {
        var resolver = new Resolver((_, command, _) => new(new[] { BattleChange.Damage(command.ActorId, command.TargetId, 500) }, "strike.fx"));
        var match = Match(resolver);

        var result = match.Submit(Command(1, "ally", "enemy"));

        Assert.Equal(BattleOutcome.AllyVictory, result.Snapshot.Outcome);
        Assert.Null(result.Snapshot.CurrentActorId);
        Assert.False(result.Snapshot.GetUnit("enemy").IsAlive);
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.Action && item.PresentationId == "strike.fx");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.Amount == 100);
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated && item.TargetId == "enemy");
        Assert.Equal(BattleFlowEventKind.BattleEnded, result.Events[^1].Kind);
        Assert.Equal(BattleCommandStatus.BattleEnded, match.Submit(Command(2, "ally", "enemy")).Status);
    }

    [Fact]
    public void ImpossibleAndCappedBattlesEndAsDraws()
    {
        var resolver = new Resolver((_, _, _) => new(Array.Empty<BattleChange>()));
        var halted = Match(resolver, allySpeed: 0, enemySpeed: 0);
        Assert.Equal(BattleOutcome.Draw, halted.Snapshot.Outcome);
        Assert.Null(halted.Snapshot.CurrentActorId);

        var capped = Match(resolver, rules: new BattleFlowRules(maxTurns: 1));
        Assert.Equal(BattleOutcome.Draw, capped.Submit(Command(1, "ally", "enemy")).Snapshot.Outcome);
    }

    [Fact]
    public void TiedReadinessUsesStableSlotOrderAndOpeningRollsAreReproducible()
    {
        var resolver = new Resolver((_, _, _) => new(Array.Empty<BattleChange>()));
        Assert.Equal("ally", Match(resolver, allySpeed: 100, enemySpeed: 100).Snapshot.CurrentActorId);
        var rules = new BattleFlowRules(openingReadinessMaxBp: 500);
        BattleSnapshot first = Match(resolver, rules: rules).Snapshot;
        BattleSnapshot second = Match(resolver, rules: rules).Snapshot;
        Assert.Equal(first.CurrentActorId, second.CurrentActorId);
        Assert.Equal(first.Units.Select(item => item.ReadinessBp), second.Units.Select(item => item.ReadinessBp));
    }

    [Fact]
    public void BarrierAbsorbsDamageAndFreshStatusWaitsForOwnersNextTurn()
    {
        var resolver = new Resolver((_, command, _) => command.Sequence == 1
            ? new(new[]
            {
                BattleChange.ApplyStatus("ally", "ally", BattleStatusKind.Barrier, 2, 50),
                BattleChange.ApplyStatus("ally", "ally", BattleStatusKind.AttackUp, 2, 5000)
            })
            : new(new[] { BattleChange.Damage("enemy", "ally", 70) }));
        var match = Match(resolver);

        var buff = match.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(50, buff.Snapshot.GetUnit("ally").Barrier);
        Assert.Equal(2, buff.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
        var hit = match.Submit(Command(2, "enemy", "ally"));
        Assert.Equal(80, hit.Snapshot.GetUnit("ally").CurrentHealth);
        Assert.Equal(0, hit.Snapshot.GetUnit("ally").Barrier);
        Assert.Equal(2, hit.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
        Assert.Contains(hit.Events, item => item.Kind == BattleFlowEventKind.BarrierAbsorbed && item.Amount == 50);
    }

    [Fact]
    public void CooldownSoulAndStunHaveSeparateTurnBoundaries()
    {
        var resolver = new Resolver((_, command, _) => command.Sequence == 1
            ? new(new[]
            {
                BattleChange.Cooldown("ally", "ally", "strike", 3),
                BattleChange.Soul("ally", BattleSide.Ally, 50),
                BattleChange.ApplyStatus("ally", "enemy", BattleStatusKind.Stun, 1)
            })
            : new(Array.Empty<BattleChange>()));
        var match = Match(resolver, rules: new BattleFlowRules(initialAllySoul: 5, maximumSoul: 20));

        var action = match.Submit(Command(1, "ally", "enemy"));

        Assert.Equal(20, action.Snapshot.GetSoul(BattleSide.Ally));
        Assert.Equal(0, action.Snapshot.GetSoul(BattleSide.Enemy));
        Assert.Equal(2, action.Snapshot.GetUnit("ally").GetCooldown("strike"));
        Assert.Equal(3, action.Snapshot.TurnNumber);
        Assert.Equal("ally", action.Snapshot.CurrentActorId);
        Assert.Null(action.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.Stun));
        Assert.Contains(action.Events, item => item.Kind == BattleFlowEventKind.TurnSkipped && item.SourceId == "enemy");
        Assert.Equal(BattleCommandStatus.SkillOnCooldown, match.Submit(Command(2, "ally", "enemy")).Status);
        Assert.Equal(BattleCommandStatus.Accepted, match.Submit(new("match", 1, 2, "action-2", "ally", "wait", "enemy")).Status);
        Assert.Equal(1, match.Snapshot.GetUnit("ally").GetCooldown("strike"));
    }

    [Fact]
    public void StandardSoulCapacityCapsEachSideAtEightyAndReportsAcceptedAmounts()
    {
        var resolver = new Resolver((_, command, _) => new(new[]
        {
            BattleChange.Soul(command.ActorId, command.ActorId == "ally" ? BattleSide.Ally : BattleSide.Enemy, 100),
            BattleChange.Soul(command.ActorId, command.ActorId == "ally" ? BattleSide.Ally : BattleSide.Enemy, -13)
        }));
        var match = Match(resolver);

        var ally = match.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(new[] { 80, 67 }, ally.Events.Where(e => e.Kind == BattleFlowEventKind.SoulChanged).Select(e => e.Amount));
        Assert.Equal(67, ally.Snapshot.AllySoul);
        Assert.Equal(0, ally.Snapshot.EnemySoul);

        var enemy = match.Submit(Command(2, "enemy", "ally"));
        Assert.Equal(new[] { 80, 67 }, enemy.Events.Where(e => e.Kind == BattleFlowEventKind.SoulChanged).Select(e => e.Amount));
        Assert.Equal(67, enemy.Snapshot.AllySoul);
        Assert.Equal(67, enemy.Snapshot.EnemySoul);
    }

    [Fact]
    public void InvalidResolutionCannotCommitEarlierEffectsOrConsumeRandomness()
    {
        ulong first = 0;
        ulong retry = 0;
        var failing = new Resolver((_, _, random) =>
        {
            first = random.NextUInt64();
            return new(new[]
            {
                BattleChange.Damage("ally", "enemy", 10),
                BattleChange.Healing("ally", "missing", 1)
            });
        });
        var match = Match(failing);
        BattleSnapshot before = match.Snapshot;

        var failure = match.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(BattleCommandStatus.InvalidResolution, failure.Status);
        Assert.Same(before, match.Snapshot);
        Assert.Empty(failure.Events);
        failing.Resolve = (_, _, random) =>
        {
            retry = random.NextUInt64();
            return new(new[] { BattleChange.Damage("ally", "enemy", 10) });
        };
        var success = match.Submit(Command(1, "ally", "enemy"));
        Assert.Equal(first, retry);
        Assert.Equal(BattleCommandStatus.Accepted, success.Status);
        Assert.Equal(90, success.Snapshot.GetUnit("enemy").CurrentHealth);
    }

    private static BattleMatch Match(IBattleActionResolver resolver, int allySpeed = 120, int enemySpeed = 80,
        BattleFlowRules? rules = null) =>
        new("match", 1, 42, new BattleFormation(new[] { Unit("ally", BattleSide.Ally, allySpeed), Unit("enemy", BattleSide.Enemy, enemySpeed) }),
            resolver, rules);

    private static BattleUnit Unit(string instanceId, BattleSide side, int speed) =>
        BattleUnit.CreateMonster(instanceId, new MonsterDefinition("test-monster", "test.monster", BattleElement.Fire,
                new BattleUnitForm("base", "monster", new[] { "strike", "wait" })),
            new BattleSlot(side, 1), 1, MonsterRank.Normal, new BattleStats(20, 100, 10, speed));

    private static BattleSkillCommand Command(ulong sequence, string actor, string target) =>
        new("match", 1, sequence, $"action-{sequence}", actor, "strike", target);

    private sealed class Resolver : IBattleActionResolver
    {
        public Resolver(Func<BattleActionContext, BattleSkillCommand, BattleRandom, BattleActionResolution> resolve) => Resolve = resolve;

        public Func<BattleActionContext, BattleSkillCommand, BattleRandom, BattleActionResolution> Resolve { get; set; }
        public int Calls { get; private set; }

        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            Calls++;
            resolution = Resolve(context, command, random);
            return true;
        }
    }
}
