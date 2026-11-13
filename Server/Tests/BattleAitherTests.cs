using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleAitherTests
{
    [Theory]
    [InlineData(2, false, 1900, 0, false)]
    [InlineData(3, false, 1900, 4, true)]
    [InlineData(3, true, 2900, 0, true)]
    [InlineData(3, true, 2900, 4, true)]
    public void GuardUsesRecipientsHealthAndOnlyAwakenedCasterGainsReadiness(int awakening, bool burn, int rate, int rank, bool ready)
    {
        var match = Fixture(awakening, rank, "none");
        var result = Cast(match, "aither", "c1018.s2", "ally", burn);
        var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "ally");
        Assert.Equal(rate * (rank == 4 ? 130 : 100) / 100, heal.Amount);
        Assert.Equal(ready, result.Events.Any(item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "aither" && item.Amount == 2000));
        Assert.Equal(burn ? 11 : 21, result.Snapshot.AllySoul);
        Assert.Equal(1, result.Snapshot.GetUnit("aither").GetCooldown("c1018.s2"));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 7)]
    public void SpiritCallCalculatesBarrierAndHealingPerRecipient(int awakening, int rank)
    {
        var match = Fixture(awakening, rank, "none");
        var result = Cast(match, "aither", "c1018.s3", null);
        foreach (string id in new[] { "aither", "ally" })
        {
            var target = result.Snapshot.GetUnit(id);
            var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == id);
            var barrier = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
                item.TargetId == id && item.StatusKind == BattleStatusKind.Barrier);
            int expected = (int)Math.Round(target.Stats.MaxHealth * (rank == 7 ? .21m : .15m), MidpointRounding.AwayFromZero);
            Assert.Equal(expected, heal.Amount);
            Assert.Equal((int)Math.Round(target.Stats.MaxHealth * .15m, MidpointRounding.AwayFromZero), barrier.Status.Strength);
            Assert.Equal(2, barrier.Status.RemainingTurns);
            Assert.True(heal.Sequence < barrier.Sequence);
        }
        Assert.Equal(2, result.Snapshot.GetUnit("aither").GetCooldown("c1018.s3"));
    }

    [Fact]
    public void GuardRemovesTwoStatusesAndLeavesThird()
    {
        var match = Fixture(3, 0, "debuffs");
        Cast(match, "enemy", "setup.debuff", "ally", true);
        var result = Cast(match, "aither", "c1018.s2", "ally");
        Assert.Equal(2, result.Events.Count(item => item.Kind == BattleFlowEventKind.StatusRemoved && item.TargetId == "ally"));
        Assert.Single(result.Snapshot.GetUnit("ally").Statuses);
        var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "ally");
        Assert.True(heal.Sequence > result.Events.Where(item => item.Kind == BattleFlowEventKind.StatusRemoved).Max(item => item.Sequence));
    }

    [Fact]
    public void SpiritCallStillShieldsUnhealableAllyWithoutHealingIt()
    {
        var match = Fixture(3, 0, "debuffs");
        Cast(match, "enemy", "setup.debuff", "ally", true);
        var result = Cast(match, "aither", "c1018.s3", null);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "ally");
        Assert.Contains(result.Events, item => item.EffectRejection == BattleEffectRejection.HealingPrevented && item.TargetId == "ally");
        Assert.Equal(1500, result.Snapshot.GetUnit("ally").Barrier);
    }

    private static BattleMatch Fixture(int awakening, int rank, string variant)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, item => item.id == "c1018");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var s2 = Assert.Single(catalog.skills, item => item.id == "c1018.s2");
        var s3 = Assert.Single(catalog.skills, item => item.id == "c1018.s3");
        var setup = Skill("setup.debuff", 1, "one_enemy", burn: 1, burnKind: "ignore_resistance");
        catalog.skills = catalog.skills.Append(setup).ToArray();
        catalog.effects = catalog.effects.Concat(new[]
        {
            Effect(setup.id, 1, "apply_status", status: "Unhealable", turns: 3),
            Effect(setup.id, 2, "apply_status", status: "AttackDown", turns: 3, value: 5000),
            Effect(setup.id, 3, "apply_status", status: "DefenseDown", turns: 3, value: 7000)
        }).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1018.s1", s2.id, s3.id });
        var definition = new HeroDefinition(row.id, row.nameKey, BattleElement.Ice, row.heroClass, row.zodiac, form, preset, steps);
        var actor = BattleUnit.CreateHero("aither", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        actor.ApplyDamage(3000);
        var ally = Monster("ally", BattleSide.Ally, 2, 10000, 10);
        ally.ApplyDamage(5000);
        var enemy = Monster("enemy", BattleSide.Enemy, 1, 20000, variant == "debuffs" ? 120 : 90, setup.id);
        return new BattleMatch("aither", BattlePreviewFactory.RulesVersion, 4,
            new BattleFormation(new[] { actor, ally, enemy }),
            new BattleSkillResolver(catalog, enhancementRank: (_, skill) => skill == s2.id && rank <= 4 || skill == s3.id ? rank : 0,
                randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20, initialEnemySoul: 20));
    }

    private static BattleCatalogData.SkillRow Skill(string id, int slot, string target, int cooldown = 0,
        int souls = 0, int burn = 0, string burnKind = "none") =>
        new() { id = id, heroId = "c1018", slot = slot, target = target, cooldownTurns = cooldown,
            soulGain = souls, soulBurnCost = burn, soulBurnKind = burnKind };

    private static BattleCatalogData.EffectRow Effect(string id, int order, string operation, string recipient = "targets",
        string status = "none", int turns = 0, int value = 0, string condition = "none") =>
        new() { skillId = id, order = order, phase = "impact", operation = operation, recipient = recipient,
            status = status, turns = turns, value = value, chanceBp = 10000, condition = condition };

    private static BattleUnit Monster(string id, BattleSide side, int slot, int health, int speed, params string[] skills) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Ice, new BattleUnitForm("standard", id, skills)),
            new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(25, health, 300, speed));

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong next = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, next,
            "aither-" + next, actor, skill, target!, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
