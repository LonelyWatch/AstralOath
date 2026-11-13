using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleKarinTests
{
    [Theory]
    [InlineData(true, 6, 0, true, true)]
    [InlineData(true, 2, 0, false, true)]
    [InlineData(true, 6, 20000, true, false)]
    [InlineData(false, 6, 0, false, false)]
    public void FlashSeparatesCriticalDefenseBreakAndAwakenedExtraTurn(bool critical, int awakenings, int resistance,
        bool extraTurn, bool defenseDown)
    {
        var match = Create(critical, awakenings, resistance);
        var result = Cast(match, "c1011.s2", false);
        Assert.Equal("c_karin_skill_02", result.Events.Single(item => item.Kind == BattleFlowEventKind.Action).PresentationId);
        Assert.Equal(extraTurn, result.Events.Any(item => item.Kind == BattleFlowEventKind.ExtraTurnGranted));
        Assert.Equal(defenseDown, result.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.DefenseDown) != null);
        if (extraTurn)
            Assert.Equal("karin", result.Snapshot.CurrentActorId);
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    public void CriticalBasicReducesExistingCooldownInAdditionToTheTurnTick(bool critical, int expectedCooldown)
    {
        var match = Create(critical, awakenings: 6, checkSecondAttack: true);
        var ultimate = Cast(match, "c1011.s3", false);
        Assert.Equal(6, ultimate.Snapshot.GetUnit("karin").GetCooldown("c1011.s3"));
        var basic = Cast(match, "c1011.s1", false);
        Assert.Equal(expectedCooldown, basic.Snapshot.GetUnit("karin").GetCooldown("c1011.s3"));
    }

    [Theory]
    [InlineData(true, false, 16000, 20000)]
    [InlineData(false, false, 16000, 15000)]
    [InlineData(true, true, 23000, 20000)]
    public void ThunderUsesThePublishedCriticalAndBurnCoefficients(bool critical, bool burn, int rate, int criticalDamage)
    {
        var match = Create(critical, awakenings: 6);
        var result = Cast(match, "c1011.s3", burn);
        var damage = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage);
        Assert.Equal(critical, damage.HitKind == BattleHitKind.Critical);
        Assert.Equal(EpicSevenCombatMath.Damage(1188, 300, 4855, rate, 8500, 0, 0, 0, damage.HitKind, criticalDamage),
            damage.Amount);
        Assert.Equal(burn ? 13 : 23, result.Snapshot.AllySoul);
    }

    private static BattleMatch Create(bool critical, int awakenings, int resistance = 0, bool checkSecondAttack = false)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, item => item.id == "c1011");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1011.s1", "c1011.s2", "c1011.s3" });
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Ice, row.heroClass, row.zodiac, form, preset, steps);
        var enemyForm = new BattleUnitForm("standard", "enemy", new[] { "enemy.s1" });
        var enemy = BattleUnit.CreateMonster("enemy", new MonsterDefinition("enemy", "enemy", BattleElement.Ice, enemyForm),
            new BattleSlot(BattleSide.Enemy, 1), 60, MonsterRank.Normal, new BattleStats(1, 30000, 300, 1,
                effectResistanceBp: resistance));
        // Equal seed and fixed geometry. Select one deterministically critical or noncritical opening.
        for (ulong seed = 0; seed < 100; seed++)
        {
            var match = Build(seed);
            if (checkSecondAttack)
                Cast(match, "c1011.s3", false);
            var hit = Assert.Single(Cast(match, checkSecondAttack ? "c1011.s1" : "c1011.s2", false).Events,
                item => item.Kind == BattleFlowEventKind.Damage);
            bool landed = resistance > 0 || !critical ||
                match.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.DefenseDown) != null;
            if ((hit.HitKind == BattleHitKind.Critical) == critical && (checkSecondAttack || landed))
                return Build(seed);
        }
        throw new InvalidOperationException("No deterministic fixture for the requested hit type.");

        BattleMatch Build(ulong seed) => new("karin", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[]
        {
            BattleUnit.CreateHero("karin", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings), enemy,
        }), new BattleSkillResolver(catalog, randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCommandResult Cast(BattleMatch match, string skill, bool burn)
    {
        var state = match.Snapshot;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, state.LastCommandSequence + 1,
            skill, "karin", skill, "enemy", burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
