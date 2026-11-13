using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleBaalTests
{
    [Fact]
    public void PoisonStacksTickSeparatelyBeforeActionAndExpireByInstance()
    {
        var battle = Fixture("poison");
        var opening = Cast(battle, "baal", "c1015.s1", "target");
        var stacks = opening.Snapshot.GetUnit("target").Statuses.Where(item => item.Kind == BattleStatusKind.Poison).ToArray();
        Assert.Equal(2, stacks.Length);
        Assert.All(stacks, stack => Assert.Equal("baal", stack.SourceId));
        Assert.Equal(2, stacks.Select(stack => stack.InstanceId).Distinct().Count());
        Assert.All(stacks, stack => Assert.NotEqual(0UL, stack.InstanceId));
        var ticks = opening.Events.Where(item => item.Kind == BattleFlowEventKind.Damage && item.StatusKind == BattleStatusKind.Poison).ToArray();
        Assert.Equal(new[] { 500, 500 }, ticks.Select(item => item.Amount));
        Assert.Equal(stacks.Select(item => item.InstanceId), ticks.Select(item => item.Status.InstanceId));
        var turn = Cast(battle, "target", "setup.pass", null);
        var expired = Assert.Single(turn.Events, item => item.Kind == BattleFlowEventKind.StatusRemoved &&
            item.StatusKind == BattleStatusKind.Poison && item.TargetId == "target");
        Assert.Equal(stacks[0].InstanceId, expired.Status.InstanceId);
        Assert.Equal(stacks[1].InstanceId, Assert.Single(turn.Snapshot.GetUnit("target").Statuses).InstanceId);
    }

    [Fact]
    public void CleanseRemovesExactlyOnePoisonInstance()
    {
        var battle = Fixture("poison");
        var opening = Cast(battle, "baal", "c1015.s1", "target");
        var stacks = opening.Snapshot.GetUnit("target").Statuses.ToArray();
        var cleansed = Cast(battle, "target", "setup.cleanse", null);
        var removed = cleansed.Events.Where(item => item.Kind == BattleFlowEventKind.StatusRemoved && item.TargetId == "target").ToArray();
        Assert.Single(removed);
        Assert.Equal(stacks[0].InstanceId, removed[0].Status.InstanceId);
        Assert.Equal(stacks[1].InstanceId, Assert.Single(cleansed.Snapshot.GetUnit("target").Statuses).InstanceId);
    }

    [Fact]
    public void PoisonDeathBeforeActorCanMoveIsStableOnRetryAndDoesNotCountAsAttackKill()
    {
        var battle = Fixture("poison", health: 510);
        var opening = battle.Snapshot;
        var command = new BattleSkillCommand(opening.BattleId, opening.RulesVersion, 1,
            "lethal-poison", "baal", "c1015.s1", "target", false);
        var result = battle.Submit(command);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(BattleOutcome.InProgress, result.Snapshot.Outcome);
        Assert.False(result.Snapshot.GetUnit("target").IsAlive);
        Assert.NotEqual("target", result.Snapshot.CurrentActorId);
        var defeat = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated);
        Assert.Equal(BattleStatusKind.Poison, defeat.StatusKind);
        Assert.Equal("baal", defeat.SourceId);
        Assert.NotEqual(0UL, defeat.Status.InstanceId);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.Action && item.SourceId == "target");
        var repeated = battle.Submit(command);
        Assert.Equal(BattleCommandStatus.Duplicate, repeated.Status);
        Assert.Same(result.Snapshot, repeated.Snapshot);
        Assert.Equal(result.Events.Select(item => item.Sequence), repeated.Events.Select(item => item.Sequence));
        var replay = Fixture("poison", health: 510).Submit(command);
        Assert.Equal(result.Events.Select(item => (item.Sequence, item.Status?.InstanceId)),
            replay.Events.Select(item => (item.Sequence, item.Status?.InstanceId)));
    }

    [Theory]
    [InlineData(false, 11000)]
    [InlineData(true, 13500)]
    public void DarkCloudScalesEachRecipientWithTheirOwnDebuffs(bool burn, int rate)
    {
        var battle = Fixture("damage");
        Cast(battle, "baal", "c1015.s3", "target");
        Cast(battle, "target", "setup.pass", null);
        var before = battle.Snapshot;
        var result = Cast(battle, "baal", "c1015.s2", null, burn);
        foreach (var recipient in new[] { "target", "second" })
        {
            var target = before.GetUnit(recipient);
            int count = target.Statuses.Count;
            if (recipient == "target") Assert.Equal(2, count);
            else Assert.Equal(0, count);
            var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.StatusKind == null && item.TargetId == recipient);
            int expected = EpicSevenCombatMath.Damage(before.GetUnit("baal").Stats.Attack, target.Stats.Defense,
                before.GetUnit("baal").Stats.MaxHealth, rate, 9000, 0, 0, 0, hit.HitKind, 15000,
                damageMultiplier: 1m + count * 0.15m);
            Assert.Equal(expected, hit.Amount);
        }
    }

    [Theory]
    [InlineData(800, true)]
    [InlineData(1500, false)]
    public void DarkCloudOnlyResetsNamedSkillWhenThisAttackKills(int initialTargetHealth, bool kill)
    {
        var battle = Fixture("reset", initialTargetHealth);
        Cast(battle, "baal", "c1015.s3", "target");
        Cast(battle, "target", "setup.pass", null);
        var result = Cast(battle, "baal", "c1015.s2", null);
        Assert.Equal(kill, result.Events.Any(item => item.Kind == BattleFlowEventKind.UnitDefeated && item.TargetId == "target"));
        Assert.Equal(kill, result.Events.Any(item => item.Kind == BattleFlowEventKind.CooldownChanged &&
            item.TargetId == "baal" && item.SkillId == "c1015.s3" && item.Amount == 0));
        Assert.Equal(kill ? 0 : 2, result.Snapshot.GetUnit("baal").GetCooldown("c1015.s3"));
        Assert.Equal(2, result.Snapshot.GetUnit("baal").GetCooldown("c1015.s2"));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(6, true)]
    public void LastRequiemUsesIndependentRecipientAndStatusChecksAndUnlocksDefenseAtAwakening(int awakenings, bool defense)
    {
        var catalog = Catalog();
        var skill = Assert.Single(catalog.skills, item => item.id == "c1015.s3");
        var debuffs = catalog.effects.Where(item => item.skillId == skill.id && item.operation == "apply_status").ToArray();
        Assert.Equal(3, debuffs.Length);
        Assert.All(debuffs, item => { Assert.Equal(6500, item.chanceBp); Assert.Equal(2, item.turns); });
        var observed = new List<BattleStatusKind[]>();
        for (ulong seed = 1; seed <= 48; seed++)
        {
            var result = Cast(RosterBattle(catalog, awakenings, seed), "baal", "c1015.s3", null);
            foreach (string id in new[] { "target", "second" })
                observed.Add(result.Events.Where(item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == id)
                    .Select(item => item.StatusKind!.Value).ToArray());
        }
        Assert.Equal(defense, observed.Any(items => items.Contains(BattleStatusKind.DefenseDown)));
        Assert.Contains(observed, items => items.Length == 1 && items[0] == BattleStatusKind.SpeedDown);
        Assert.Contains(observed, items => items.Length == 1 && items[0] == BattleStatusKind.Unhealable);
        Assert.Contains(observed.Chunk(2), pair => !pair[0].SequenceEqual(pair[1]));
    }

    [Fact]
    public void GhostHauntSelectsTwoDistinctEnemiesAndAppliesNativePoison()
    {
        var catalog = Catalog();
        var poison = Assert.Single(catalog.effects, item => item.skillId == "c1015.s1" && item.status == "Poison");
        Assert.Equal(3500, poison.chanceBp);
        Assert.Equal(2, poison.turns);
        Assert.Equal(500, poison.value);
        var observedTargets = new HashSet<string>();
        bool observedPoison = false;
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var result = Cast(RosterBattle(catalog, 6, seed), "baal", "c1015.s1", "target");
            var hits = result.Events.Where(item => item.Kind == BattleFlowEventKind.Damage && item.StatusKind == null).ToArray();
            Assert.Equal(2, hits.Select(item => item.TargetId).Distinct().Count());
            Assert.Contains(hits, item => item.TargetId == "target");
            observedTargets.UnionWith(hits.Select(item => item.TargetId));
            observedPoison |= result.Events.Any(item => item.Kind == BattleFlowEventKind.StatusApplied && item.StatusKind == BattleStatusKind.Poison);
        }
        Assert.Equal(3, observedTargets.Count);
        Assert.True(observedPoison);
    }

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private static BattleMatch RosterBattle(BattleCatalogData catalog, int awakenings, ulong seed)
    {
        var row = Assert.Single(catalog.heroes, item => item.id == "c1015");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1015.s1", "c1015.s2", "c1015.s3" });
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Fire, row.heroClass, row.zodiac, form, preset, steps);
        var baal = BattleUnit.CreateHero("baal", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings);
        return new BattleMatch("baal-roster", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[]
        {
            baal,
            Unit("target", BattleSide.Enemy, 1, 10000, 100, 1, "setup.pass"),
            Unit("second", BattleSide.Enemy, 2, 10000, 10, 1, "setup.pass"),
            Unit("third", BattleSide.Enemy, 3, 10000, 10, 1, "setup.pass"),
        }), new BattleSkillResolver(catalog, randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleMatch Fixture(string fixture, int health = 10000)
    {
        var skills = new[]
        {
            Skill("c1015.s1", 1, "one_enemy", 10000, 10000), Skill("c1015.s2", 2, "all_enemies", 11000, 9000),
            Skill("c1015.s3", 3, "one_enemy", 8000, 10000), Skill("setup.pass", 1, "self", 0, 0),
            Skill("setup.cleanse", 2, "self", 0, 0)
        };
        skills[1].cooldownTurns = 3; skills[1].soulBurnCost = 10; skills[1].soulBurnKind = "increase_damage";
        skills[1].burnAttackRateBp = 13500; skills[2].cooldownTurns = 4;
        var effects = new List<BattleCatalogData.EffectRow>
        {
            Effect("c1015.s1", 1, "impact", "damage"),
            Effect("c1015.s1", 2, "after_hit", "apply_status", "targets", "Poison", 1, 500),
            Effect("c1015.s1", 3, "after_hit", "apply_status", "targets", "Poison", 2, 500),
            Effect("c1015.s2", 1, "impact", "damage_bonus_per_debuff", "self", value: 1500),
            Effect("c1015.s2", 2, "impact", "damage"),
            Effect("c1015.s2", 3, "after_attack", "reset_cooldown", "self", value: 3, condition: "defeated_target"),
            Effect("c1015.s3", 1, "impact", "damage"),
            Effect("setup.pass", 1, "impact", "readiness", "self"),
            Effect("setup.cleanse", 1, "impact", "cleanse", "self", value: 1)
        };
        if (fixture == "damage")
        {
            effects.Add(Effect("c1015.s3", 2, "after_hit", "apply_status", "targets", "Poison", 2, 500));
            effects.Add(Effect("c1015.s3", 3, "after_hit", "apply_status", "targets", "Unhealable", 2));
        }
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2, skills = skills, effects = effects.ToArray(),
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
        const ulong seed = 4;
        return new BattleMatch("baal", 5, seed, new BattleFormation(new[]
        {
            Unit("baal", BattleSide.Ally, 1, 10000, 105, 500, "c1015.s1", "c1015.s2", "c1015.s3"),
            Unit("target", BattleSide.Enemy, 1, health, 100, 1, "setup.pass", "setup.cleanse"),
            Unit("second", BattleSide.Enemy, 2, 10000, 10, 1, "setup.pass", "setup.cleanse"),
        }), new BattleSkillResolver(catalog, randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCatalogData.SkillRow Skill(string id, int slot, string target, int rate, int power) =>
        new() { id = id, heroId = id.Split('.')[0], slot = slot, target = target, soulBurnKind = "none",
            attackRateBp = rate, powerBp = power };

    private static BattleCatalogData.EffectRow Effect(string skillId, int order, string phase, string operation,
        string recipient = "targets", string status = "none", int turns = 0, int value = 0, string condition = "none") =>
        new() { skillId = skillId, order = order, phase = phase, operation = operation, recipient = recipient,
            status = status, turns = turns, value = value, condition = condition, chanceBp = 10000 };

    private static BattleUnit Unit(string id, BattleSide side, int position, int health, int speed, int attack, params string[] skills) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Fire, new BattleUnitForm("standard", id, skills)),
            new BattleSlot(side, position), 60, MonsterRank.Normal, new BattleStats(attack, health, 300, speed));

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong next = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, next,
            "cast-" + next, actor, skill, target!, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
