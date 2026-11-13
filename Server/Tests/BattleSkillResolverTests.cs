using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleSkillResolverTests
{
    [Fact]
    public void ElementHitAndResistanceMatchRuleExamples()
    {
        Assert.Equal(1, EpicSevenCombatMath.ElementRelation(BattleElement.Fire, BattleElement.Wind));
        Assert.Equal(-1, EpicSevenCombatMath.ElementRelation(BattleElement.Fire, BattleElement.Ice));
        Assert.Equal(1, EpicSevenCombatMath.ElementRelation(BattleElement.Light, BattleElement.Dark));
        Assert.Equal(1, EpicSevenCombatMath.ElementRelation(BattleElement.Dark, BattleElement.Light));
        Assert.Equal(0, EpicSevenCombatMath.ElementRelation(BattleElement.Dark, BattleElement.Fire));
        Assert.Equal(8500, EpicSevenCombatMath.EffectSuccessBp(4000, 0));
        Assert.Equal(5000, EpicSevenCombatMath.EffectSuccessBp(1000, 6000));
        Assert.Equal(0, EpicSevenCombatMath.EffectSuccessBp(0, 15000));
        Assert.Equal(BattleHitKind.Miss, EpicSevenCombatMath.RollHit(new BattleRandom(0), -1, 10000, evasion: 5000));
        Assert.Equal(936, EpicSevenCombatMath.Damage(1000, 300, 1, 10000, 10000, 0, 0, 0, BattleHitKind.Normal, 15000));
        Assert.Equal(1544, EpicSevenCombatMath.Damage(1000, 300, 1, 10000, 10000, 0, 0, 1, BattleHitKind.Critical, 15000));
    }

    [Theory]
    [InlineData("c1001", 1, "enemy")]
    [InlineData("c1001", 2, "enemy")]
    [InlineData("c1001", 3, "enemy")]
    [InlineData("c1005", 1, "enemy")]
    [InlineData("c1005", 2, "enemy")]
    [InlineData("c1005", 3, "enemy")]
    [InlineData("c1062", 1, "enemy")]
    [InlineData("c1062", 2, "friend")]
    [InlineData("c1062", 3, "friend")]
    public void EveryGeneratedSkillProducesAuthoritativeEvents(string hero, int slot, string target)
    {
        var match = Match(hero);
        var response = Cast(match, $"actor", $"{hero}.s{slot}", target);
        Assert.Equal(BattleCommandStatus.Accepted, response.Status);
        Assert.Contains(response.Events, item => item.Kind == BattleFlowEventKind.Action &&
            item.SkillId == $"{hero}.s{slot}" && !string.IsNullOrWhiteSpace(item.PresentationId));
        Assert.Contains(response.Events, item => item.Kind == BattleFlowEventKind.Damage || item.Kind == BattleFlowEventKind.Healing);
        Assert.Equal(response.Events.Select(item => item.Sequence).OrderBy(value => value), response.Events.Select(item => item.Sequence));
    }

    [Fact]
    public void RasBurnChecksSoulAndTriggersHighestAttackAllyBeforeTurnEnd()
    {
        var empty = Match("c1001", soul: 0);
        var before = empty.Snapshot;
        Assert.Equal(BattleCommandStatus.InvalidAction, Cast(empty, "actor", "c1001.s2", "enemy", burn: true).Status);
        Assert.Same(before, empty.Snapshot);

        var match = Match("c1001", soul: 20);
        var result = Cast(match, "actor", "c1001.s2", "enemy", burn: true);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        var actions = result.Events.Where(item => item.Kind == BattleFlowEventKind.Action).ToArray();
        Assert.Equal(BattleActionOrigin.Primary, actions[0].Origin);
        Assert.True(actions[0].UseSoulBurn);
        Assert.Equal(("strong", BattleActionOrigin.DualAttack), (actions[1].SourceId, actions[1].Origin));
        Assert.Equal(12, result.Snapshot.AllySoul);
        Assert.Equal(0, result.Snapshot.GetUnit("actor").GetCooldown("c1001.s2"));
    }

    [Fact]
    public void MercedesAttackUpRepeatsS2ExactlyOnceWithoutExtraSoulOrTurn()
    {
        var match = Match("c1005", soul: 0);
        Assert.Equal(BattleCommandStatus.Accepted, Cast(match, "actor", "c1005.s3", null).Status);
        int soul = match.Snapshot.AllySoul;
        var result = Cast(match, "actor", "c1005.s2", "enemy");
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Action && item.Origin == BattleActionOrigin.ExtraAttack);
        Assert.Equal(2, result.Events.Count(item => item.Kind == BattleFlowEventKind.Damage));
        Assert.Equal(soul + 1, result.Snapshot.AllySoul);
        Assert.Equal(3, result.Snapshot.TurnNumber);
    }

    [Fact]
    public void AngelicaBurnExtendsGrantedEffectsAndHealingUsesLowestHealthPercent()
    {
        var heal = Match("c1062");
        var before = heal.Snapshot;
        Assert.True(before.GetUnit("strong").CurrentHealth < before.GetUnit("friend").CurrentHealth);
        var result = Cast(heal, "actor", "c1062.s2", "strong");
        Assert.Equal(new[] { "strong", "friend" }, result.Events.Where(item => item.Kind == BattleFlowEventKind.Healing)
            .Select(item => item.TargetId));

        var match = Match("c1062", soul: 20);
        var shield = Cast(match, "actor", "c1062.s3", null, burn: true).Snapshot;
        foreach (var ally in shield.Units.Where(unit => unit.Slot.Side == BattleSide.Ally))
        {
            Assert.Equal(4, ally.GetStatus(BattleStatusKind.Immunity).RemainingTurns);
            Assert.Equal(4, ally.GetStatus(BattleStatusKind.Barrier).RemainingTurns);
            Assert.Equal(600, ally.Barrier);
        }
    }

    [Theory]
    [InlineData("c1002", 1, "enemy")]
    [InlineData("c1002", 2, "enemy")]
    [InlineData("c1002", 3, "enemy")]
    [InlineData("c1003", 1, "enemy")]
    [InlineData("c1003", 2, "friend")]
    [InlineData("c1003", 3, "friend")]
    [InlineData("c1004", 1, "enemy")]
    [InlineData("c1004", 3, "enemy")]
    [InlineData("c1006", 1, "enemy")]
    [InlineData("c1006", 2, "enemy")]
    [InlineData("c1006", 3, "enemy")]
    [InlineData("c1007", 1, "enemy")]
    [InlineData("c1007", 3, "enemy")]
    [InlineData("c1008", 1, "enemy")]
    [InlineData("c1008", 3, "friend")]
    [InlineData("c1009", 1, "enemy")]
    [InlineData("c1009", 3, "enemy")]
    public void ExpandedRosterActiveSkillsProduceAuthoritativeResults(string hero, int slot, string target)
    {
        var match = Match(hero);
        string id = $"{hero}.s{slot}";
        var response = Cast(match, "actor", id, target);
        Assert.Equal(BattleCommandStatus.Accepted, response.Status);
        var action = Assert.Single(response.Events, item => item.Kind == BattleFlowEventKind.Action);
        Assert.Equal(id, action.SkillId);
        Assert.False(string.IsNullOrWhiteSpace(action.PresentationId));
        Assert.Contains(response.Events, item => item.Kind == BattleFlowEventKind.Damage ||
            item.Kind == BattleFlowEventKind.Healing || item.Kind == BattleFlowEventKind.StatusApplied ||
            item.Kind == BattleFlowEventKind.ReadinessChanged);
    }

    [Fact]
    public void RoseBurnExtendsOnlyGrantedBuffsAndDefenseBarrierUsesCasterStat()
    {
        var match = Match("c1003");
        var buff = Cast(match, "actor", "c1003.s3", "friend", burn: true);
        Assert.Equal(BattleCommandStatus.Accepted, buff.Status);
        Assert.Contains(buff.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged && item.TargetId == "friend");
        Assert.Equal(3, buff.Snapshot.GetUnit("friend").GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
        var shield = Cast(match, "actor", "c1003.s2", "friend");
        Assert.Equal(BattleCommandStatus.Accepted, shield.Status);
        Assert.Equal(480, shield.Snapshot.GetUnit("friend").Barrier);
        Assert.Equal(2, shield.Snapshot.GetUnit("friend").GetStatus(BattleStatusKind.Barrier).RemainingTurns);
    }

    [Fact]
    public void PublishedRosterRunsFromOpeningToResultWithDeterministicReplay()
    {
        var catalog = ReadCatalog();
        var first = Run(catalog);
        var second = Run(catalog);
        Assert.Equal(first, second);
        Assert.NotEqual(BattleOutcome.InProgress, first.Outcome);

        static (BattleOutcome Outcome, ulong Turns, ulong Events) Run(BattleCatalogData data)
        {
            var match = BattlePreviewFactory.Create("playtest", 271828UL, data);
            for (int i = 0; i < 200 && match.Snapshot.Outcome == BattleOutcome.InProgress; i++)
            {
                var actor = match.Snapshot.GetUnit(match.Snapshot.CurrentActorId);
                var target = match.Snapshot.Units.First(unit => unit.IsAlive && unit.Slot.Side != actor.Slot.Side);
                var result = Cast(match, actor.InstanceId, actor.Form.SkillIds[0], target.InstanceId);
                Assert.Equal(BattleCommandStatus.Accepted, result.Status);
            }
            return (match.Snapshot.Outcome, match.Snapshot.LastCommandSequence, match.Snapshot.LastEventSequence);
        }
    }

    [Fact]
    public void PreviewRosterUsesRequestedSlotsAndMirrorsOnlyInDuels()
    {
        var catalog = ReadCatalog();
        string[] roster = { "c1005", "c1001", "c1001", "c1062" };
        var local = BattlePreviewFactory.Create("custom", 123, catalog, allyHeroIds: roster).Snapshot;
        var duel = BattlePreviewFactory.Create("duel", 123, catalog, duel: true, allyHeroIds: roster).Snapshot;
        for (int slot = 1; slot <= roster.Length; slot++)
        {
            Assert.Equal(roster[slot - 1], local.GetUnit($"ally-{slot}").Form.PresentationId);
            Assert.Equal("slime", local.GetUnit($"enemy-{slot}").Form.PresentationId);
            Assert.Equal(roster[slot - 1], duel.GetUnit($"enemy-{slot}").Form.PresentationId);
        }
        Assert.Throws<ArgumentException>(() => BattlePreviewFactory.Create("bad", 123, catalog, allyHeroIds: roster[..3]));
        roster[0] = "missing";
        Assert.Throws<ArgumentException>(() => BattlePreviewFactory.Create("bad", 123, catalog, allyHeroIds: roster));
    }

    [Fact]
    public void PassiveSlotCannotBeSubmittedAndDoesNotConsumeTurn()
    {
        var catalog = ReadCatalog();
        var row = Assert.Single(catalog.skills, item => item.id == "c1001.s2");
        row.target = "passive";
        catalog.effects = catalog.effects.Where(item => item.skillId != row.id).ToArray();
        var actor = Unit("actor", "c1001", BattleSide.Ally, 1, 1000, 4000, 10000);
        var enemy = Unit("enemy", "c1001", BattleSide.Enemy, 1, 100, 500000, 10);
        var match = new BattleMatch("test", 1, 123, new BattleFormation(new[] { actor, enemy }),
            new BattleSkillResolver(catalog), new BattleFlowRules(initialAllySoul: 20));
        var before = match.Snapshot;
        Assert.Equal(BattleCommandStatus.InvalidAction, Cast(match, "actor", row.id, "enemy").Status);
        Assert.Same(before, match.Snapshot);
    }

    private static BattleMatch Match(string hero, int soul = 20)
    {
        var catalog = ReadCatalog();
        var actor = Unit("actor", hero, BattleSide.Ally, 1, 1000, 4000, 10000);
        var friend = Unit("friend", "c1001", BattleSide.Ally, 2, 150, 30000, 10);
        friend.ApplyDamage(28000);
        var strong = Unit("strong", "c1001", BattleSide.Ally, 3, 1000, 2000, 10);
        strong.ApplyDamage(1300);
        var enemy = Unit("enemy", "c1001", BattleSide.Enemy, 1, 100, 500000, 10);
        return new BattleMatch("test", 1, 123, new BattleFormation(new[] { actor, friend, strong, enemy }),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: soul));
    }

    private static BattleUnit Unit(string id, string hero, BattleSide side, int position, int attack, int health, int speed)
    {
        var form = new BattleUnitForm("standard", hero, new[] { $"{hero}.s1", $"{hero}.s2", $"{hero}.s3" });
        var definition = new MonsterDefinition(hero, hero, BattleElement.Fire, form);
        return BattleUnit.CreateMonster(id, definition, new BattleSlot(side, position), 60,
            MonsterRank.Normal, new BattleStats(attack, health, 300, speed, critChanceBp: 0));
    }

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var snapshot = match.Snapshot;
        ulong sequence = snapshot.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence, $"action-{sequence}",
            actor, skill, target!, burn));
    }

    private static BattleCatalogData ReadCatalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }
}
