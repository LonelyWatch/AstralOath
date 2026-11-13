using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleCartujaTests
{
    [Theory]
    [InlineData(0, 0, 1000)]
    [InlineData(0, 3, 1500)]
    [InlineData(3600, 0, 2000)]
    [InlineData(3600, 3, 2500)]
    public void PassiveRaisesReadinessOncePerAttackWithSeparateLowHealthBonus(int priorDamage, int rank, int gain)
    {
        var match = Create(priorDamage, passiveRank: rank, enemySpeed: 200);
        var before = match.Snapshot.GetUnit("cartuja");
        Assert.Equal("enemy", match.Snapshot.CurrentActorId);
        Assert.Equal(priorDamage > 0, 2L * before.CurrentHealth <= before.Stats.MaxHealth);
        var result = Submit(match, "enemy", "enemy.s1", "cartuja");
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(2, result.Events.Count(item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "cartuja"));
        var readiness = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "cartuja");
        Assert.Equal(before.ReadinessBp + gain, readiness.Amount);
    }

    [Fact]
    public void DangerZoneCannotBeSelectedAsAnActiveSkill()
    {
        var match = Create(0);
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "cartuja", "c1013.s2", "enemy").Status);
        Assert.Equal(0UL, match.Snapshot.LastCommandSequence);
    }

    [Theory]
    [InlineData(0, false, 6000, 500)]
    [InlineData(0, true, 8000, 800)]
    [InlineData(3400, false, 10000, 625)]
    [InlineData(3400, true, 12000, 1000)]
    public void PredatoryRoarScalesAttackAndHealthIndependently(int priorDamage, bool burn, int attackRate, int healthRate)
    {
        var match = Create(priorDamage);
        var caster = match.Snapshot.GetUnit("cartuja");
        var enemy = match.Snapshot.GetUnit("enemy");
        var result = Submit(match, "cartuja", "c1013.s3", null, burn);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "enemy");
        int damage = EpicSevenCombatMath.Damage(caster.Stats.Attack, enemy.Stats.Defense, caster.Stats.MaxHealth,
            attackRate, 10000, healthRate, 0, 0, hit.HitKind, caster.Stats.CritDamageBp);
        Assert.Equal(damage, hit.Amount);
        var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing);
        var defense = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.StatusKind == BattleStatusKind.DefenseUp);
        Assert.True(heal.Sequence < defense.Sequence);
        Assert.Equal(Math.Min(priorDamage, (caster.Stats.MaxHealth + 1) / 2), heal.Amount);
        Assert.Equal(burn ? 13 : 23, result.Snapshot.AllySoul);
    }

    [Fact]
    public void PredatoryRoarBeforeThirdAwakeningStillGrantsDefenseWithoutHealing()
    {
        var match = Create(2500, awakenings: 2);
        var result = Submit(match, "cartuja", "c1013.s3", null);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.Healing);
        Assert.Equal(2, result.Snapshot.GetUnit("cartuja").GetStatus(BattleStatusKind.DefenseUp)!.RemainingTurns);
    }

    private static BattleMatch Create(int priorDamage, int passiveRank = 0, int enemySpeed = 1, int awakenings = 6)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, hero => hero.id == "c1013");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1013.s1", "c1013.s2", "c1013.s3" });
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Wind, row.heroClass, row.zodiac, form, preset, steps);
        var actor = BattleUnit.CreateHero("cartuja", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings);
        actor.ApplyDamage(priorDamage);
        var enemyForm = new BattleUnitForm("standard", "enemy", new[] { "enemy.s1" });
        var enemy = BattleUnit.CreateMonster("enemy", new MonsterDefinition("enemy", "enemy", BattleElement.Wind, enemyForm),
            new BattleSlot(BattleSide.Enemy, 1), 60, MonsterRank.Normal, new BattleStats(20, 30000, 300, enemySpeed));
        catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow
        {
            id = "enemy.s1", heroId = "enemy", slot = 1, target = "one_enemy", soulBurnKind = "none",
            attackRateBp = 10000, powerBp = 10000
        }).ToArray();
        catalog.effects = catalog.effects.Concat(Enumerable.Range(1, 2).Select(order => new BattleCatalogData.EffectRow
        {
            skillId = "enemy.s1", order = order, phase = "impact", operation = "damage",
            recipient = "targets", status = "none", chanceBp = 10000
        })).ToArray();
        return new BattleMatch("cartuja", BattlePreviewFactory.RulesVersion, 4,
            new BattleFormation(new[] { actor, enemy }), new BattleSkillResolver(catalog,
                enhancementRank: (_, skill) => skill == "c1013.s2" ? passiveRank : 0, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCommandResult Submit(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "cartuja-" + sequence, actor, skill, target!, burn));
    }
}
