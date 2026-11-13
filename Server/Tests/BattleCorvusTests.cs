using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleCorvusTests
{
    [Fact]
    public void FightingSpiritStartsAtFiftyAndGainOccursOncePerAttack()
    {
        var battle = Create();
        Assert.Equal(50, battle.Snapshot.GetUnit("corvus").Resource!.Amount);
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(battle, "corvus", "c1012.s3", "corvus").Status);
        Assert.Equal(60, Accepted(battle, "corvus", "c1012.s1", "enemy").Snapshot.GetUnit("corvus").Resource!.Amount);
        var attack = Accepted(battle, "enemy", "setup.attack", "corvus");
        Assert.Equal(70, attack.Snapshot.GetUnit("corvus").Resource!.Amount);
        Assert.Single(attack.Events, item => item.Kind == BattleFlowEventKind.ResourceChanged && item.TargetId == "corvus");
    }

    [Fact]
    public void UltimateRequiresSelfThenSpendsSpiritAndGrantsAnExtraTurn()
    {
        var battle = Create();
        ReadyUltimate(battle);
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(battle, "corvus", "c1012.s3", "enemy").Status);
        var result = Accepted(battle, "corvus", "c1012.s3", null, true);
        Assert.Equal(0, result.Snapshot.GetUnit("corvus").Resource!.Amount);
        Assert.Equal(3, result.Snapshot.GetUnit("corvus").GetStatus(BattleStatusKind.Indomitable)!.RemainingTurns);
        Assert.Equal("corvus", result.Snapshot.CurrentActorId);
        var healing = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "corvus");
        Assert.True(healing.Amount > 0);
        Assert.Equal(result.Snapshot.GetUnit("corvus").Stats.MaxHealth, result.Snapshot.GetUnit("corvus").CurrentHealth);
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted && item.TargetId == "corvus");
        Assert.Equal(5, result.Snapshot.AllySoul);
    }

    [Theory]
    [InlineData(4UL, 20, true)]
    [InlineData(3UL, 20, false)]
    [InlineData(4UL, 50, false)]
    public void LowerReadinessStunsOnlyWhenDrainedAndNotResisted(ulong seed, int speed, bool stun)
    {
        var battle = Create(seed: seed, enemySpeed: speed);
        int before = battle.Snapshot.GetUnit("enemy").ReadinessBp;
        var result = Accepted(battle, "corvus", "c1012.s2", "enemy");
        var readiness = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "enemy");
        Assert.Equal(Math.Max(0, before - 2000), readiness.Amount);
        Assert.Equal(stun, result.Events.Any(item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "enemy" && item.StatusKind == BattleStatusKind.Stun));
        if (seed == 3) Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.EffectRejected &&
            item.StatusKind == BattleStatusKind.Stun && item.EffectRejection == BattleEffectRejection.Resisted);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20000, false)]
    public void TransferOnlyRemovesCasterDebuffAfterAcceptance(int resistance, bool transfers)
    {
        var battle = Create(resistance: resistance, editCatalog: catalog =>
        {
            catalog.effects = catalog.effects.Append(EnemyEffect("apply_status", "AttackDown", 5000, 3)).ToArray();
            Assert.Single(catalog.effects, item => item.skillId == "c1012.s1" && item.operation == "transfer_debuff")
                .chanceBp = 10000;
        });
        ReadyUltimate(battle);
        Assert.NotNull(battle.Snapshot.GetUnit("corvus").GetStatus(BattleStatusKind.AttackDown));
        Accepted(battle, "corvus", "c1012.s3", "corvus");
        var basic = Accepted(battle, "corvus", "c1012.s1", "enemy");
        Assert.Equal(transfers, basic.Events.Any(item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "enemy" && item.StatusKind == BattleStatusKind.AttackDown));
        Assert.Equal(!transfers, basic.Snapshot.GetUnit("corvus").GetStatus(BattleStatusKind.AttackDown) != null);
    }

    [Fact]
    public void IndomitableCannotBeDispelledAndDoublesEnhancedReadinessReduction()
    {
        var battle = Create(seed: 4, enhancement: (actor, skill) => skill == "c1012.s2" ? 5 : 0,
            editCatalog: catalog => catalog.effects = catalog.effects.Append(EnemyEffect("dispel", "none", 1)).ToArray());
        Accepted(battle, "corvus", "c1012.s1", "enemy");
        Accepted(battle, "enemy", "setup.attack", "corvus");
        Accepted(battle, "corvus", "c1012.s1", "enemy");
        Accepted(battle, "enemy", "setup.attack", "corvus");
        Accepted(battle, "corvus", "c1012.s1", "enemy");
        if (battle.Snapshot.CurrentActorId == "enemy") Accepted(battle, "enemy", "setup.attack", "corvus");
        Accepted(battle, "corvus", "c1012.s3", "corvus");
        int before = battle.Snapshot.GetUnit("enemy").ReadinessBp;
        var sweep = Accepted(battle, "corvus", "c1012.s2", "enemy");
        Assert.Contains(sweep.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "enemy" && item.Amount == Math.Max(0, before - 6000));
        for (int turn = 0; turn < 2 && battle.Snapshot.CurrentActorId == "corvus"; turn++)
            Accepted(battle, "corvus", "c1012.s1", "enemy");
        Assert.NotNull(battle.Snapshot.GetUnit("corvus").GetStatus(BattleStatusKind.Indomitable));
        var attack = Accepted(battle, "enemy", "setup.attack", "corvus");
        Assert.DoesNotContain(attack.Events, item => item.Kind == BattleFlowEventKind.StatusRemoved &&
            item.TargetId == "corvus" && item.StatusKind == BattleStatusKind.Indomitable);
    }

    [Theory]
    [InlineData(2, false, 0)]
    [InlineData(2, true, 0)]
    [InlineData(6, false, 5000)]
    [InlineData(6, true, 10000)]
    public void UltimateAwakeningAndSoulBurnDetermineHealing(int awakenings, bool burn, int rate)
    {
        var battle = Create(awakenings: awakenings, enemyAttack: 1650);
        ReadyUltimate(battle);
        var actor = battle.Snapshot.GetUnit("corvus");
        var result = Accepted(battle, "corvus", "c1012.s3", "corvus", burn);
        if (rate == 0)
        {
            Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.Healing);
            return;
        }
        int missing = actor.Stats.MaxHealth - actor.CurrentHealth;
        Assert.True(missing > actor.Stats.MaxHealth / 2);
        int expected = Math.Min(missing, (actor.Stats.MaxHealth * rate + 5000) / 10000);
        Assert.Equal(expected, Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing).Amount);
    }

    private static BattleCatalogData.EffectRow EnemyEffect(string operation, string status, int value, int turns = 0) =>
        new()
        {
            skillId = "setup.attack", order = 3, phase = "after_hit", operation = operation,
            recipient = "targets", status = status, chanceBp = 10000, value = value, turns = turns
        };

    private static void ReadyUltimate(BattleMatch battle)
    {
        Accepted(battle, "corvus", "c1012.s1", "enemy");
        Accepted(battle, "enemy", "setup.attack", "corvus");
        Accepted(battle, "corvus", "c1012.s2", "enemy");
        if (battle.Snapshot.CurrentActorId == "enemy") Accepted(battle, "enemy", "setup.attack", "corvus");
        Assert.Equal(100, battle.Snapshot.GetUnit("corvus").Resource!.Amount);
    }

    private static BattleMatch Create(ulong seed = 3, int enemySpeed = 102, int enemyAttack = 500,
        int awakenings = 6, int resistance = 0, Func<string, string, int>? enhancement = null,
        Action<BattleCatalogData>? editCatalog = null)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, item => item.id == "c1012");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1012.s1", "c1012.s2", "c1012.s3" });
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Fire, row.heroClass, row.zodiac, form, preset, steps,
            row.GetResourceDefinition());
        var enemyForm = new BattleUnitForm("standard", "enemy", new[] { "setup.attack" });
        var enemy = BattleUnit.CreateMonster("enemy", new MonsterDefinition("enemy", "enemy", BattleElement.Fire, enemyForm),
            new BattleSlot(BattleSide.Enemy, 1), 60, MonsterRank.Normal, new BattleStats(enemyAttack, 30000, 300, enemySpeed, effectResistanceBp: resistance));
        catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow
        {
            id = "setup.attack", heroId = "enemy", slot = 1, target = "one_enemy", soulBurnKind = "none",
            attackRateBp = 10000, powerBp = 10000
        }).ToArray();
        catalog.effects = catalog.effects.Concat(Enumerable.Range(1, 2).Select(order => new BattleCatalogData.EffectRow
        {
            skillId = "setup.attack", order = order, phase = "impact", operation = "damage",
            recipient = "targets", status = "none", chanceBp = 10000
        })).ToArray();
        editCatalog?.Invoke(catalog);
        return new BattleMatch("corvus", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[]
        {
            BattleUnit.CreateHero("corvus", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings), enemy,
        }), new BattleSkillResolver(catalog, enhancementRank: enhancement, randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCommandResult Accepted(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var result = Submit(match, actor, skill, target, burn);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }

    private static BattleCommandResult Submit(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "turn-" + sequence, actor, skill, target!, burn));
    }
}
