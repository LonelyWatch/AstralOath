using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleTargetingTests
{
    [Fact]
    public void SingleTargetAiAvoidsStealthAndAuthorityRejectsManualCommand()
    {
        var catalog = Catalog();
        var match = StartWithStatus(catalog, BattleStatusKind.Stealth, "hidden");
        var policy = new BattleAiPolicy(catalog);

        Assert.Equal("visible", policy.ChooseTarget(match.Snapshot, "actor.s1")?.TargetId);
        Assert.False(policy.CanSelectTarget(match.Snapshot, "actor.s1", "hidden"));
        Assert.True(policy.CanSelectTarget(match.Snapshot, "actor.s1", "visible"));
        Assert.True(policy.CanSelectTarget(match.Snapshot, "actor.s3", "hidden"));
        Assert.False(policy.CanSelectTarget(match.Snapshot, "actor.s3", "missing"));
        var before = match.Snapshot;
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "actor.s1", "hidden").Status);
        Assert.Same(before, match.Snapshot);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, "actor.s1", "visible").Status);
    }

    [Fact]
    public void AoeMitigatesStealthAndRevealsOnlyWhenHealthIsLost()
    {
        var catalog = Catalog();
        var match = StartWithStatus(catalog, BattleStatusKind.Stealth, "hidden");
        var result = Submit(match, "actor.s3", "hidden");
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        int visibleDamage = Assert.Single(result.Events, item =>
            item.Kind == BattleFlowEventKind.Damage && item.TargetId == "visible").Amount;
        int hiddenDamage = Assert.Single(result.Events, item =>
            item.Kind == BattleFlowEventKind.Damage && item.TargetId == "hidden").Amount;
        Assert.InRange(hiddenDamage * 2, visibleDamage - 1, visibleDamage + 1);
        Assert.Null(result.Snapshot.GetUnit("hidden")?.GetStatus(BattleStatusKind.Stealth));

        var shielded = StartWithStatus(catalog, BattleStatusKind.Stealth, "hidden", barrier: 1000);
        var blocked = Submit(shielded, "actor.s3", "hidden");
        Assert.Equal(BattleCommandStatus.Accepted, blocked.Status);
        Assert.NotNull(blocked.Snapshot.GetUnit("hidden")?.GetStatus(BattleStatusKind.Stealth));
        Assert.Contains(blocked.Events, item => item.Kind == BattleFlowEventKind.BarrierAbsorbed && item.TargetId == "hidden");
    }

    [Fact]
    public void CanSelectAnOpponentWhenEverySurvivorIsStealthed()
    {
        var catalog = Catalog();
        var match = StartWithStatus(catalog, BattleStatusKind.Stealth, "hidden", allHidden: true);
        var policy = new BattleAiPolicy(catalog);
        Assert.Equal("hidden", policy.ChooseTarget(match.Snapshot, "actor.s1")?.TargetId);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, "actor.s1", "hidden").Status);
    }

    [Fact]
    public void ProvokeForcesItsLivingSourceAndBlocksOtherSkillsAndBurn()
    {
        const BattleStatusKind kind = BattleStatusKind.Provoke;
        var catalog = Catalog();
        var match = StartWithStatus(catalog, kind, "actor");
        var policy = new BattleAiPolicy(catalog);
        Assert.True(policy.IsForcedAction(match.Snapshot));
        Assert.Equal("hidden", match.Snapshot.GetUnit("actor").GetStatus(kind)?.SourceId);
        var choice = policy.ChooseAction(match.Snapshot);
        Assert.NotNull(choice);
        Assert.Equal(("actor.s1", "hidden", false), (choice.SkillId, choice.TargetId, choice.UseSoulBurn));
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "actor.s3", "hidden").Status);
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "actor.s1", "visible").Status);
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "actor.s1", "hidden", burn: true).Status);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, choice.SkillId, choice.TargetId).Status);
    }

    [Fact]
    public void DefeatedProvokerDoesNotLeaveTheNextActorWithoutAnAction()
    {
        var catalog = Catalog();
        var match = StartWithStatus(catalog, BattleStatusKind.Provoke, "actor", sourceDies: true);
        var policy = new BattleAiPolicy(catalog);
        Assert.False(policy.IsForcedAction(match.Snapshot));
        var choice = policy.ChooseAction(match.Snapshot);
        Assert.NotNull(choice);
        Assert.Equal(("actor.s3", "visible"), (choice.SkillId, choice.TargetId));
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, choice.SkillId, choice.TargetId).Status);
    }

    [Fact]
    public void BuffConvertedAreaAttackUsesSameLegalTargetsInAiAndResolution()
    {
        var catalog = Catalog();
        catalog.skills.Single(skill => skill.id == "actor.s1").aoeWhenBuffed = true;
        var match = StartWithStatus(catalog, BattleStatusKind.Stealth, "hidden", buffActor: true);
        var policy = new BattleAiPolicy(catalog);

        Assert.True(policy.CanSelectTarget(match.Snapshot, "actor.s1", "hidden"));
        Assert.True(policy.CanSelectTarget(match.Snapshot, "actor.s1", null));
        var result = Submit(match, "actor.s1", "hidden");
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(new[] { "hidden", "visible" },
            result.Events.Where(item => item.Kind == BattleFlowEventKind.Damage).Select(item => item.TargetId));
    }

    private static BattleMatch StartWithStatus(BattleCatalogData catalog, BattleStatusKind kind, string target,
        bool allHidden = false, int barrier = 0, bool sourceDies = false, bool buffActor = false)
    {
        var match = new BattleMatch("targeting", 1, 7, new BattleFormation(new[]
        {
            Unit("actor", "actor", BattleSide.Ally, 1, 990),
            Unit("hidden", "target", BattleSide.Enemy, 1, 1000),
            Unit("visible", "target", BattleSide.Enemy, 2, 1)
        }), new GrantStatusThenResolve(new BattleSkillResolver(catalog, randomDualChanceBp: 0), kind,
            target, allHidden, barrier, sourceDies, buffActor), new BattleFlowRules(initialAllySoul: 20));
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, "target.s1", "actor").Status);
        return match;
    }

    private static BattleUnit Unit(string instance, string id, BattleSide side, int slot, int speed)
    {
        var skillIds = id == "actor" ? new[] { "actor.s1", "actor.s3" } : new[] { "target.s1" };
        var definition = new MonsterDefinition(id, id, BattleElement.Fire, new BattleUnitForm("standard", id, skillIds));
        return BattleUnit.CreateMonster(instance, definition, new BattleSlot(side, slot), 60, MonsterRank.Normal,
            new BattleStats(100, 2000, 300, speed, critChanceBp: 0));
    }

    private static BattleCatalogData Catalog()
    {
        var skills = new[] { Skill("actor.s1", "actor", 1, "one_enemy"), Skill("actor.s3", "actor", 3, "all_enemies"),
            Skill("target.s1", "target", 1, "one_enemy") };
        skills[0].soulBurnCost = 10;
        skills[0].soulBurnKind = "increase_damage";
        return new BattleCatalogData
        {
            schemaVersion = 2, skills = skills,
            effects = skills.Select(skill => new BattleCatalogData.EffectRow
            {
                skillId = skill.id, order = 1, phase = "impact", operation = "damage", recipient = "targets",
                chanceBp = 10000, status = "none", condition = "none"
            }).ToArray(),
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
    }

    private static BattleCatalogData.SkillRow Skill(string id, string hero, int slot, string target) => new()
    {
        id = id, heroId = hero, slot = slot, target = target, attackRateBp = 10000, powerBp = 10000,
        soulBurnKind = "none"
    };

    private static BattleCommandResult Submit(BattleMatch match, string skillId, string target, bool burn = false)
    {
        var current = match.Snapshot;
        return match.Submit(new BattleSkillCommand(current.BattleId, current.RulesVersion,
            current.LastCommandSequence + 1, Guid.NewGuid().ToString("N"), current.CurrentActorId,
            skillId, target, burn));
    }

    private sealed class GrantStatusThenResolve(BattleSkillResolver resolver, BattleStatusKind kind, string target,
        bool allHidden, int barrier, bool sourceDies, bool buffActor) : IBattleActionResolver
    {
        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            if (command.Sequence != 1) return resolver.TryResolve(context, command, random, out resolution);
            var changes = new List<BattleChange> { BattleChange.ApplyStatus(command.ActorId, target, kind, 2) };
            if (buffActor) changes.Add(BattleChange.ApplyStatus(command.ActorId, "actor", BattleStatusKind.AttackUp, 2, 5000));
            if (allHidden) changes.Add(BattleChange.ApplyStatus(command.ActorId, "visible", kind, 2));
            if (barrier != 0) changes.Add(BattleChange.ApplyStatus(command.ActorId, target, BattleStatusKind.Barrier, 2, barrier));
            if (sourceDies) changes.Add(BattleChange.Damage(command.ActorId, command.ActorId, 2000));
            resolution = new BattleActionResolution(changes);
            return true;
        }
    }
}
