using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleAchatesTests
{
    [Theory]
    [InlineData(2, BattleStatusKind.Barrier)]
    [InlineData(3, BattleStatusKind.Invincible)]
    public void RapidCureAwakeningChoosesProtectionAndSelfReducesCurrentCooldown(int awakening, BattleStatusKind kind)
    {
        var match = Create(awakening);
        var before = match.Snapshot.GetUnit("achates");
        var result = Cast(match, "achates", "c1017.s2", "achates");
        var protection = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == "achates");
        Assert.Equal(kind, protection.StatusKind);
        Assert.Equal(1, protection.Status.RemainingTurns);
        var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "achates");
        Assert.True(heal.Sequence < protection.Sequence);
        Assert.Equal((before.Stats.MaxHealth * 1500 + 5000) / 10000, heal.Amount);
        var cooldown = result.Events.Where(item => item.Kind == BattleFlowEventKind.CooldownChanged &&
            item.TargetId == "achates" && item.SkillId == "c1017.s2").Select(item => item.Amount);
        Assert.Equal(new[] { 3, 2, 1 }, cooldown);
        Assert.Equal(1, result.Snapshot.GetUnit("achates").GetCooldown("c1017.s2"));
        Assert.Equal(22, result.Snapshot.AllySoul);
    }

    [Fact]
    public void RapidCureDoesNotReduceCastersCooldownWhenTargetingAnotherAlly()
    {
        var match = Create(6, allyCooldown: true);
        Cast(match, "ally", "setup.cooldown", null);
        Assert.Equal(3, match.Snapshot.GetUnit("ally").GetCooldown("setup.cooldown"));
        var result = Cast(match, "achates", "c1017.s2", "ally");
        Assert.Equal(2, result.Snapshot.GetUnit("achates").GetCooldown("c1017.s2"));
        Assert.Equal(2, result.Snapshot.GetUnit("ally").GetCooldown("setup.cooldown"));
        Assert.Equal(1, result.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.Invincible)!.RemainingTurns);
        Assert.Null(result.Snapshot.GetUnit("achates").GetStatus(BattleStatusKind.Invincible));
    }

    [Theory]
    [InlineData(false, 3000, 0)]
    [InlineData(true, 5000, 0)]
    [InlineData(true, 5000, 5)]
    public void CuringPrayerRemovesAllDebuffsBeforeHealingAllAllies(bool burn, int rate, int rank)
    {
        var match = Create(6, withDebuffs: true, healingRank: rank);
        Cast(match, "enemy", "setup.debuff", null);
        var caster = match.Snapshot.GetUnit("achates");
        var result = Cast(match, "achates", "c1017.s3", null, burn);
        int multiplier = rank == 5 ? 13000 : 10000;
        int expected = (int)Math.Round(caster.Stats.MaxHealth * rate * (decimal)multiplier / 100000000m,
            MidpointRounding.AwayFromZero);
        foreach (string ally in new[] { "achates", "ally" })
        {
            var removed = result.Events.Where(item => item.Kind == BattleFlowEventKind.StatusRemoved &&
                item.TargetId == ally).ToArray();
            Assert.Equal(3, removed.Length);
            Assert.Equal(2, removed.Count(item => item.StatusKind == BattleStatusKind.Poison));
            Assert.Empty(result.Snapshot.GetUnit(ally).Statuses);
            var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == ally);
            Assert.True(heal.Sequence > removed.Max(item => item.Sequence));
            Assert.Equal(expected, heal.Amount);
        }
        Assert.Equal(burn ? 12 : 22, result.Snapshot.AllySoul);
        Assert.Equal(rank == 5 ? 2 : 3, result.Snapshot.GetUnit("achates").GetCooldown("c1017.s3"));
    }

    [Fact]
    public void InvincibleProtectsHealthAndBarrierWhileHostileStatusStillLands()
    {
        var match = Create(6, allyBarrier: true);
        Cast(match, "achates", "c1017.s2", "ally");
        var before = match.Snapshot.GetUnit("ally");
        Assert.True(before.Barrier > 0);
        var result = Cast(match, "enemy", "setup.attack", "ally");
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "ally");
        Assert.Equal(0, hit.Amount);
        Assert.NotEqual(BattleHitKind.None, hit.HitKind);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.BarrierAbsorbed && item.TargetId == "ally");
        var after = result.Snapshot.GetUnit("ally");
        Assert.Equal(before.CurrentHealth, after.CurrentHealth);
        Assert.Equal(before.Barrier, after.Barrier);
        Assert.NotNull(after.GetStatus(BattleStatusKind.AttackDown));
    }

    [Fact]
    public void SoulWeaverAiSelectsCuringPrayerForFullCleanse()
    {
        var match = Create(6, withDebuffs: true);
        Cast(match, "enemy", "setup.debuff", null);
        Assert.Equal("c1017.s3", new BattleAiPolicy(Catalog()).ChooseAction(match.Snapshot).SkillId);
    }

    private static BattleMatch Create(int awakening, bool withDebuffs = false, bool allyBarrier = false,
        int healingRank = 0, bool allyCooldown = false)
    {
        var catalog = Catalog();
        var row = Assert.Single(catalog.heroes, item => item.id == "c1017");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1017.s1", "c1017.s2", "c1017.s3" });
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Fire, row.heroClass, row.zodiac, form, preset, steps);
        var actor = BattleUnit.CreateHero("achates", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        actor.ApplyDamage(withDebuffs ? 3800 : 1000);

        var enemyForm = new BattleUnitForm("standard", "enemy", new[] { "setup.attack", "setup.debuff" });
        var enemy = BattleUnit.CreateMonster("enemy", new MonsterDefinition("enemy", "enemy", BattleElement.Fire, enemyForm),
            new BattleSlot(BattleSide.Enemy, 1), 60, MonsterRank.Normal,
            new BattleStats(100, 30000, 300, withDebuffs ? 110 : 102, effectivenessBp: 10000));
        string[] allySkills = allyBarrier ? new[] { "setup.barrier" } : allyCooldown ? new[] { "setup.cooldown" } : Array.Empty<string>();
        var allyForm = new BattleUnitForm("standard", "ally", allySkills);
        var ally = BattleUnit.CreateMonster("ally", new MonsterDefinition("ally", "ally", BattleElement.Fire, allyForm),
            new BattleSlot(BattleSide.Ally, 2), 60, MonsterRank.Normal, new BattleStats(25, 10000, 300, allyCooldown ? 120 : 10));
        ally.ApplyDamage(5000);
        catalog.skills = catalog.skills.Concat(new[]
        {
            Skill("setup.attack", "one_enemy", 10000, 10000),
            Skill("setup.debuff", "all_enemies", 10000, 10000, soulBurn: true),
            Skill("setup.barrier", "passive"), Skill("setup.cooldown", "self")
        }).ToArray();
        catalog.skills.Single(item => item.id == "setup.barrier").casterHealthBarrierBp = 1500;
        catalog.skills.Single(item => item.id == "setup.cooldown").cooldownTurns = 4;
        catalog.effects = catalog.effects.Concat(new[]
        {
            Effect("setup.attack", 1, "impact", "damage", "targets"),
            Effect("setup.attack", 2, "after_hit", "apply_status", "targets", "AttackDown", 2, 5000),
            Effect("setup.debuff", 1, "impact", "apply_status", "targets", "Unhealable", 3),
            Effect("setup.debuff", 2, "impact", "apply_status", "targets", "Poison", 3, 500),
            Effect("setup.debuff", 3, "impact", "apply_status", "targets", "Poison", 3, 500),
            Effect("setup.barrier", 1, "battle_start", "apply_status", "self", "Barrier", 3),
            Effect("setup.cooldown", 1, "impact", "apply_status", "self", "AttackUp", 2, 5000)
        }).ToArray();
        return new BattleMatch("achates", BattlePreviewFactory.RulesVersion, 4,
            new BattleFormation(new[] { actor, ally, enemy }), new BattleSkillResolver(catalog,
                enhancementRank: (_, skill) => skill == "c1017.s3" ? healingRank : 0, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 20, initialEnemySoul: 20));
    }

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private static BattleCatalogData.SkillRow Skill(string id, string target, int rate = 0, int power = 0, bool soulBurn = false) =>
        new() { id = id, heroId = "setup", slot = 1, target = target,
            soulBurnKind = soulBurn ? "ignore_resistance" : "none", soulBurnCost = soulBurn ? 1 : 0,
            attackRateBp = rate, powerBp = power };

    private static BattleCatalogData.EffectRow Effect(string id, int order, string phase, string operation,
        string recipient, string status = "none", int turns = 0, int value = 0) =>
        new() { skillId = id, order = order, phase = phase, operation = operation, recipient = recipient,
            status = status, turns = turns, value = value, chanceBp = 10000 };

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "achates-" + sequence, actor, skill, target!, burn || skill == "setup.debuff"));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
