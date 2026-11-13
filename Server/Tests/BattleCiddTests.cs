using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleCiddTests
{
    [Theory]
    [InlineData(false, 9000, 9500, "c_cedars_skill_01_new")]
    [InlineData(true, 15000, 9000, "c_cedars_skill_02_new")]
    public void BasicSnapshotsTheSpeedBuffBeforeDamageAndChoosesTheMatchingGraph(bool speedUp, int rate,
        int power, string presentation)
    {
        var match = Create(speedUp: speedUp);
        var actor = match.Snapshot.GetUnit("cidd");
        var enemy = match.Snapshot.GetUnit("enemy");
        var result = Cast(match, "c1014.s1");
        var action = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Action);
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage);
        Assert.Equal(presentation, action.PresentationId);
        Assert.Equal(BattleHitKind.Critical, hit.HitKind);
        Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, enemy.Stats.Defense, actor.Stats.MaxHealth,
            rate, power, 0, 0, 0, hit.HitKind, actor.Stats.CritDamageBp,
            damageMultiplier: 1m + actor.Stats.Speed * (speedUp ? 1.3m : 1m) * .00075m), hit.Amount);
        Assert.Equal(speedUp, result.Events.Any(item => item.StatusKind == BattleStatusKind.DefenseDown));
        var speed = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.StatusKind == BattleStatusKind.SpeedUp && item.TargetId == "cidd");
        Assert.True(hit.Sequence < speed.Sequence);
    }

    [Theory]
    [InlineData(false, 6, false, 16000, -1)]
    [InlineData(true, 2, false, 16000, -1)]
    [InlineData(true, 6, false, 16000, 1)]
    [InlineData(true, 6, true, 22000, 1)]
    public void UltimateUsesAnAwakenedSpeedBuffForAdvantageAndBurnUsesItsOwnAttackRate(bool speedUp,
        int awakenings, bool burn, int rate, int relation)
    {
        var match = Create(speedUp: speedUp, awakenings: awakenings, enemyElement: BattleElement.Fire);
        var actor = match.Snapshot.GetUnit("cidd");
        var enemy = match.Snapshot.GetUnit("enemy");
        var result = Cast(match, "c1014.s3", burn);
        Assert.Equal("c_cedars_skill_03_new",
            Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Action).PresentationId);
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage);
        if (relation > 0) Assert.Equal(BattleHitKind.Critical, hit.HitKind);
        Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, enemy.Stats.Defense, actor.Stats.MaxHealth,
            rate, 10000, 0, 0, relation, hit.HitKind, actor.Stats.CritDamageBp,
            damageMultiplier: 1m + actor.Stats.Speed * (speedUp ? 1.3m : 1m) * .0021m), hit.Amount);
        Assert.Equal(burn ? 12 : 22, result.Snapshot.AllySoul);
    }

    [Fact]
    public void PassiveCannotBeSelectedAndMaxEnhancementAddsOneBuffAfterCriticalDamage()
    {
        var match = Create(passiveRank: 5);
        var state = match.Snapshot;
        Assert.Equal(BattleCommandStatus.InvalidAction, match.Submit(new BattleSkillCommand(state.BattleId,
            state.RulesVersion, 1, "passive", "cidd", "c1014.s2", "enemy", false)).Status);
        var result = Cast(match, "c1014.s1");
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage);
        var buffs = result.Events.Where(item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == "cidd").ToArray();
        Assert.Equal(2, buffs.Length);
        Assert.All(buffs, item => Assert.True(hit.Sequence < item.Sequence));
        Assert.Equal(BattleStatusKind.SpeedUp, buffs[0].StatusKind);
        Assert.Contains(buffs[1].StatusKind?.ToString(), new[]
        {
            "Immunity", "EvasionUp", "CriticalDamageUp", "EffectivenessUp", "GreaterSpeedUp"
        });
    }

    [Fact]
    public void GreaterSpeedUsesTheSameAlternateGraphWithoutAddingNormalSpeed()
    {
        var match = Create(speedUp: true, greaterSpeedUp: true);
        var actor = match.Snapshot.GetUnit("cidd");
        var enemy = match.Snapshot.GetUnit("enemy");
        Assert.True(actor.HasSpeedBuff);
        var result = Cast(match, "c1014.s1");
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage);
        Assert.Equal("c_cedars_skill_02_new",
            Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Action).PresentationId);
        Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, enemy.Stats.Defense, actor.Stats.MaxHealth,
            15000, 9000, 0, 0, 0, hit.HitKind, actor.Stats.CritDamageBp,
            damageMultiplier: 1m + actor.Stats.Speed * 1.45m * .00075m), hit.Amount);
    }

    [Fact]
    public void NonCriticalMissDoesNotTriggerThePassive()
    {
        var match = Create(passiveRank: 5, enemyEvasion: true);
        var result = Cast(match, "c1014.s1");
        Assert.Equal(BattleHitKind.Miss, Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage).HitKind);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "cidd");
    }

    [Fact]
    public void RepeatedCriticalAttacksInTheSameActionCanRefreshSpeedButOnlyRollOneRandomBuff()
    {
        var match = Create(speedUp: true, passiveRank: 5, repeatBasic: true);
        var result = Cast(match, "c1014.s1");
        Assert.Equal(2, result.Events.Count(item => item.Kind == BattleFlowEventKind.Damage));
        var buffs = result.Events.Where(item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == "cidd").ToArray();
        Assert.Equal(2, buffs.Count(item => item.StatusKind == BattleStatusKind.SpeedUp));
        Assert.Single(buffs, item => item.StatusKind != BattleStatusKind.SpeedUp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void UltimateGrantsAnExtraTurnOnKillRegardlessOfAwakening(int awakenings)
    {
        var match = Create(awakenings: awakenings, enemyHealth: 1);
        var result = Cast(match, "c1014.s3");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted &&
            item.SourceId == "cidd");
    }

    private static BattleMatch Create(bool speedUp = false, int awakenings = 6, int passiveRank = 0,
        BattleElement enemyElement = BattleElement.Wind, int enemyHealth = 30000, bool greaterSpeedUp = false,
        bool enemyEvasion = false, bool repeatBasic = false)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, item => item.id == "c1014");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var skillIds = new List<string> { "c1014.s1", "c1014.s2", "c1014.s3" };
        if (speedUp)
            AddOpeningBuff(skillIds, "fixture.speed", "SpeedUp", 3000);
        if (greaterSpeedUp)
            AddOpeningBuff(skillIds, "fixture.greaterspeed", "GreaterSpeedUp", 4500);
        if (repeatBasic)
            catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow
            {
                id = "fixture.repeat", skillId = "c1014.s1", order = 3, phase = "after_attack",
                operation = "repeat_if_status", recipient = "self", status = "SpeedUp", chanceBp = 10000
            }).ToArray();

        // Retain the roster growth and awakened-skill gate, while making damage branches reproducible.
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp + (item.rank == 1 ? 8500 : 0),
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var hero = new HeroDefinition(row.id, row.nameKey, BattleElement.Wind, row.heroClass, row.zodiac,
            new BattleUnitForm(row.variant, row.id, skillIds), preset, steps);
        var enemySkills = new List<string> { "enemy.s1" };
        if (enemyEvasion)
            AddOpeningBuff(enemySkills, "fixture.evasion", "EvasionUp", 10000);
        var enemyForm = new BattleUnitForm("standard", "enemy", enemySkills);
        var enemyDefinition = new MonsterDefinition("enemy", "enemy", enemyElement, enemyForm);
        var enemy = BattleUnit.CreateMonster("enemy", enemyDefinition, new BattleSlot(BattleSide.Enemy, 1), 60,
            MonsterRank.Normal, new BattleStats(1, enemyHealth, 300, 1));
        // Leave a living enemy after the kill to exercise the real turn scheduler.
        var backup = BattleUnit.CreateMonster("backup", enemyDefinition, new BattleSlot(BattleSide.Enemy, 2), 60,
            MonsterRank.Normal, new BattleStats(1, 30000, 300, 1));
        return new BattleMatch("cidd", BattlePreviewFactory.RulesVersion, 7, new BattleFormation(new[]
        {
            BattleUnit.CreateHero("cidd", hero, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings), enemy, backup
        }), new BattleSkillResolver(catalog, enhancementRank: (_, id) => id == "c1014.s2" ? passiveRank : 0,
            randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 20));

        void AddOpeningBuff(List<string> skills, string id, string status, int strength)
        {
            skills.Add(id);
            catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow { id = id, target = "passive" }).ToArray();
            catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow
            {
                id = id, skillId = id, order = 1, phase = "battle_start", operation = "apply_status",
                recipient = "self", status = status, chanceBp = 10000, turns = 1, value = strength
            }).ToArray();
        }
    }

    private static BattleCommandResult Cast(BattleMatch match, string skill, bool burn = false)
    {
        var state = match.Snapshot;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, state.LastCommandSequence + 1,
            skill, "cidd", skill, "enemy", burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
