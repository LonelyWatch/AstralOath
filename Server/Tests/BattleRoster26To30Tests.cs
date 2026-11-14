using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster26To30Tests
{
    private const string EnemySkill = "roster-test.enemy.s1";

    [Fact]
    public void CharlesAwakenedFaithfulStrikeMakesCounterAndSmashGuaranteed()
    {
        var match = Create("c1027", seed: 13, enemySpeed: 108);
        var opening = Cast(match, "hero", "c1027.s3", burn: true);
        Assert.Equal(22, opening.Snapshot.AllySoul);
        Assert.Equal(2, opening.Snapshot.GetUnit("hero").GetCooldown("c1027.s3"));
        Assert.Contains(opening.Events, evt => Status(evt, "ally", BattleStatusKind.AttackUp, 2));
        Assert.Contains(opening.Events, evt => Status(evt, "ally", BattleStatusKind.DefenseUp, 2));
        Assert.Contains(opening.Events, evt => Status(evt, "hero", BattleStatusKind.Counter, 2));

        var retaliation = Cast(match, "enemy", EnemySkill, "hero");
        var counter = Assert.Single(retaliation.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
            evt.SkillId == "c1027.s1" && evt.Origin == BattleActionOrigin.Counter);
        var smash = Assert.Single(retaliation.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
            evt.SkillId == "c1027.s2" && evt.Origin == BattleActionOrigin.ExtraAttack);
        Assert.True(counter.Sequence < smash.Sequence);
        Assert.Contains(retaliation.Events, evt => evt.Kind == BattleFlowEventKind.Healing && evt.TargetId == "hero");
        Assert.Contains(retaliation.Events, evt => evt.Kind == BattleFlowEventKind.ReadinessChanged && evt.TargetId == "ally");
        Assert.Equal(0, retaliation.Snapshot.GetUnit("hero").GetCooldown("c1027.s2"));
    }

    [Theory]
    [InlineData(1, 1.801)]
    [InlineData(2, 1.534)]
    [InlineData(3, 1.267)]
    [InlineData(4, 1.0)]
    public void CharlesFaithfulStrikeUsesStartingEnemyCount(int enemyCount, double multiplier)
    {
        var match = Create("c1027", seed: 11, enemyCount: enemyCount);
        var actor = match.Snapshot.GetUnit("hero");
        var enemy = match.Snapshot.GetUnit("enemy");
        var result = Cast(match, "hero", "c1027.s3");
        var hits = result.Events.Where(evt => evt.Kind == BattleFlowEventKind.Damage && evt.SourceId == "hero").ToArray();
        Assert.Equal(enemyCount, hits.Length);
        var primary = Assert.Single(hits, evt => evt.TargetId == "enemy");
        int expected = EpicSevenCombatMath.Damage(actor.Stats.Attack, enemy.Stats.Defense, actor.MaximumHealth,
            12000, 10000, 0, 0, 0, primary.HitKind, actor.Stats.CritDamageBp, damageMultiplier: (decimal)multiplier);
        Assert.Equal(expected, primary.Amount);
    }

    [Theory]
    [InlineData("c1028.s1", "enemy")]
    [InlineData("c1028.s3", null)]
    public void ClarissaKillingWithBasicOrUltimateGrantsRageAndOneFollowUp(string skill, string? target)
    {
        var match = Create("c1028", seed: 8, enemyHealth: 1, enemyCount: 2);
        var result = Cast(match, "hero", skill, target);
        var defeated = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.UnitDefeated && evt.TargetId == "enemy");
        var rage = Assert.Single(result.Events, evt => Status(evt, "hero", BattleStatusKind.Rage, 3));
        var followUp = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
            evt.SkillId == "c1028.s2" && evt.Origin == BattleActionOrigin.Passive);
        Assert.True(defeated.Sequence < rage.Sequence && defeated.Sequence < followUp.Sequence);
        Assert.Contains(result.Events, evt => evt.Kind == BattleFlowEventKind.Damage &&
            evt.TargetId == "enemy-2" && evt.Sequence > followUp.Sequence && evt.Sequence > rage.Sequence);
        Assert.Equal(skill.EndsWith(".s1", StringComparison.Ordinal) ? 32 : 33, result.Snapshot.AllySoul);
        Assert.Equal(0, result.Snapshot.GetUnit("hero").GetCooldown("c1028.s3"));
    }

    [Fact]
    public void ClarissaAwakenedUltimateCanApplyTwoIndependentBleeds()
    {
        var outcomes = Enumerable.Range(1, 32).Select(seed =>
            Cast(Create("c1028", (ulong)seed), "hero", "c1028.s3"));
        var result = Assert.Single(outcomes.Where(outcome =>
            outcome.Snapshot.GetUnit("enemy").Statuses.Count(status => status.Kind == BattleStatusKind.Bleed) == 2).Take(1));
        var bleeds = result.Snapshot.GetUnit("enemy").Statuses.Where(status => status.Kind == BattleStatusKind.Bleed).ToArray();
        Assert.All(bleeds, status => Assert.Equal(2, status.RemainingTurns));
        Assert.NotEqual(bleeds[0].InstanceId, bleeds[1].InstanceId);
    }

    [Fact]
    public void LeoSoulBurnGuaranteesOneSparrowDiveWithoutConsumingAnotherTurn()
    {
        foreach (int seed in Enumerable.Range(1, 8))
        {
            var match = Create("c1029", (ulong)seed);
            var result = Cast(match, "hero", "c1029.s2", "enemy", burn: true);
            Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.Action &&
                evt.SkillId == "c1029.s1" && evt.Origin == BattleActionOrigin.ExtraAttack);
            Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.TurnEnded && evt.SourceId == "hero");
            Assert.Equal(1, result.Snapshot.GetUnit("hero").GetCooldown("c1029.s2"));
        }
        var ordinary = Enumerable.Range(1, 16).Select(seed =>
            Cast(Create("c1029", (ulong)seed), "hero", "c1029.s2", "enemy")).ToArray();
        bool UsesSparrowDive(BattleCommandResult result) => result.Events.Any(evt =>
            evt.Kind == BattleFlowEventKind.Action && evt.SkillId == "c1029.s1" && evt.Origin == BattleActionOrigin.ExtraAttack);
        Assert.Contains(ordinary, UsesSparrowDive);
        Assert.Contains(ordinary, result => !UsesSparrowDive(result));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(6, true)]
    public void LeoUltimateAwakeningAddsSlowToItsGroupStun(int awakening, bool appliesSlow)
    {
        var outcomes = Enumerable.Range(1, 24).Select(seed =>
            Cast(Create("c1029", (ulong)seed, awakening: awakening, enemyCount: 2), "hero", "c1029.s3")).ToArray();
        Assert.All(outcomes, result => Assert.Equal(2, result.Events.Count(evt =>
            evt.Kind == BattleFlowEventKind.Damage && evt.SourceId == "hero")));
        Assert.Contains(outcomes, result => result.Events.Any(evt => Status(evt, "enemy", BattleStatusKind.Stun, 1)));
        Assert.Equal(appliesSlow, outcomes.Any(result => result.Events.Any(evt =>
            Status(evt, "enemy", BattleStatusKind.SpeedDown, 2))));
    }


    [Fact]
    public void YunaUsesFocusAndAnExtraTurnBeforeSpendingFocusOnMeteorCannon()
    {
        var match = Create("c1030", seed: 17, enemyCount: 2, rank: 2);
        var policy = new BattleAiPolicy(Catalog());
        Assert.False(policy.IsSkillAvailable(match.Snapshot, "c1030.s3"));
        var upgrade = Cast(match, "hero", "c1030.s2");
        Assert.Equal("hero", upgrade.Snapshot.CurrentActorId);
        Assert.Equal(3, upgrade.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.Equal(33, upgrade.Snapshot.AllySoul);
        Assert.Contains(upgrade.Events, evt => Status(evt, "ally", BattleStatusKind.AttackUp, 3));
        Assert.Contains(upgrade.Events, evt => Status(evt, "ally", BattleStatusKind.SpeedUp, 3));

        var before = upgrade.Snapshot.GetUnit("ally").ReadinessBp;
        var laser = Cast(match, "hero", "c1030.s1");
        Assert.Equal(4, laser.Snapshot.GetUnit("hero").Resource.Amount);
        var selfPush = Assert.Single(laser.Events, evt => evt.Kind == BattleFlowEventKind.ReadinessChanged &&
            evt.SourceId == "hero" && evt.TargetId == "hero");
        Assert.Equal(1500, selfPush.Amount);
        var allyPush = Assert.Single(laser.Events, evt => evt.Kind == BattleFlowEventKind.ReadinessChanged &&
            evt.SourceId == "hero" && evt.TargetId == "ally");
        Assert.Equal(before + 500, allyPush.Amount);
        Cast(match, "hero", "c1030.s1");
        Assert.True(policy.IsSkillAvailable(match.Snapshot, "c1030.s3"));
        var meteor = Cast(match, "hero", "c1030.s3");
        Assert.Equal(0, meteor.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.Equal(2, meteor.Events.Count(evt => evt.Kind == BattleFlowEventKind.Damage && evt.SourceId == "hero"));
    }

    [Fact]
    public void LotsHolyLightCanApplyOneTurnAttackDown()
    {
        var outcomes = Enumerable.Range(1, 20).Select(seed =>
            Cast(Create("c1031", (ulong)seed), "hero", "c1031.s1", "enemy")).ToArray();
        Assert.All(outcomes, result => Assert.Single(result.Events, evt =>
            evt.Kind == BattleFlowEventKind.Damage && evt.SourceId == "hero" && evt.TargetId == "enemy"));
        Assert.Contains(outcomes, result => result.Events.Any(evt => Status(evt, "enemy", BattleStatusKind.AttackDown, 1)));
        Assert.Contains(outcomes, result => !result.Events.Any(evt => Status(evt, "enemy", BattleStatusKind.AttackDown, 1)));
    }


    [Fact]
    public void LotsEnhancedRegenerateIncreasesOnlyTheTargetPushAndHealsFromTargetMaximum()
    {
        var match = Create("c1031", seed: 7, rank: 5);
        var ally = match.Snapshot.GetUnit("ally");
        var result = Cast(match, "hero", "c1031.s2", "ally");
        var targetPush = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.ReadinessChanged &&
            evt.SourceId == "hero" && evt.TargetId == "ally");
        var selfPush = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.ReadinessChanged &&
            evt.SourceId == "hero" && evt.TargetId == "hero");
        var heal = Assert.Single(result.Events, evt => evt.Kind == BattleFlowEventKind.Healing && evt.TargetId == "ally");
        Assert.Equal(ally.ReadinessBp + 10000, targetPush.Amount);
        Assert.Equal(3000, selfPush.Amount);
        Assert.Equal(ScaleHealth(ally.MaximumHealth, 1500, 2000), heal.Amount);
        Assert.Equal("ally", result.Snapshot.CurrentActorId);
    }

    [Theory]
    [InlineData(2, false, 2)]
    [InlineData(6, false, 3)]
    [InlineData(6, true, 5)]
    public void LotsPeaceHealsImmediatelyAndOnTurnStartWithTheCorrectDuration(int awakening, bool burn, int turns)
    {
        var match = Create("c1031", seed: 6, awakening: awakening, rank: 5);
        var before = match.Snapshot.GetUnit("ally");
        var cast = Cast(match, "hero", "c1031.s3", burn: burn);
        Assert.Contains(cast.Events, evt => Status(evt, "ally", BattleStatusKind.ContinuousHealing, turns));
        var immediate = Assert.Single(cast.Events, evt => evt.Kind == BattleFlowEventKind.Healing &&
            evt.TargetId == "ally" && evt.StatusKind == null);
        Assert.Equal(ScaleHealth(before.MaximumHealth, 1000), immediate.Amount);
        Assert.Equal("hero", cast.Snapshot.CurrentActorId);
        var next = Cast(match, "hero", "c1031.s2", "ally");
        Assert.Equal("ally", next.Snapshot.CurrentActorId);
        var turn = Assert.Single(next.Events, evt => evt.Kind == BattleFlowEventKind.TurnStarted && evt.SourceId == "ally");
        var heal = Assert.Single(next.Events, evt => evt.Kind == BattleFlowEventKind.Healing &&
            evt.TargetId == "ally" && evt.StatusKind == BattleStatusKind.ContinuousHealing);
        Assert.True(turn.Sequence < heal.Sequence);
        Assert.Equal(ScaleHealth(before.MaximumHealth, 1500), heal.Amount);
    }

    private static int ScaleHealth(int health, int rate, int enhancement = 0) =>
        (int)Math.Round(health * rate / 10000m * (1 + enhancement / 10000m), MidpointRounding.AwayFromZero);

    private static bool Status(BattleFlowEvent evt, string target, BattleStatusKind status, int turns) =>
        evt.Kind == BattleFlowEventKind.StatusApplied && evt.TargetId == target && evt.StatusKind == status &&
        evt.Status.RemainingTurns == turns;

    private static BattleCatalogData Catalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private static BattleMatch Create(string heroId, ulong seed, int awakening = 6, int enemySpeed = 1,
        int enemyHealth = 50000, int enemyCount = 1, int rank = 0)
    {
        var catalog = Catalog();
        var row = Assert.Single(catalog.heroes, item => item.id == heroId);
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == heroId).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, heroId, catalog.skills.Where(item => item.heroId == heroId)
            .OrderBy(item => item.slot).Select(item => item.id));
        var definition = new HeroDefinition(heroId, row.nameKey, Enum.Parse<BattleElement>(row.element, ignoreCase: true),
            row.heroClass, row.zodiac, form, preset, steps, row.GetResourceDefinition());
        var hero = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        hero.ApplyDamage(1000);
        var ally = Unit("ally", BattleSide.Ally, 2, 12000, 1);
        ally.ApplyDamage(9000);
        var units = new List<BattleUnit> { hero, ally, Unit("enemy", BattleSide.Enemy, 1, enemyHealth, enemySpeed) };
        for (int slot = 2; slot <= enemyCount; slot++)
            units.Add(Unit("enemy-" + slot, BattleSide.Enemy, slot, 50000, 1));
        catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow
        {
            id = EnemySkill, heroId = "roster-test", slot = 1, target = "one_enemy", soulBurnKind = "none",
            attackRateBp = 10000, powerBp = 10000
        }).ToArray();
        catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow
        {
            id = EnemySkill + ".1", skillId = EnemySkill, phase = "impact", operation = "damage",
            recipient = "targets", status = "none", order = 1, chanceBp = 10000
        }).ToArray();
        var resolver = new BattleSkillResolver(catalog, randomDualChanceBp: 0,
            enhancementRank: (_, skill) => skill == heroId + ".s2" ? rank : 0);
        return new BattleMatch("roster-test", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(units),
            resolver, new BattleFlowRules(initialAllySoul: 30));
    }

    private static BattleUnit Unit(string id, BattleSide side, int slot, int maxHealth, int speed)
    {
        var form = new BattleUnitForm("standard", id, side == BattleSide.Enemy ? new[] { EnemySkill } : Array.Empty<string>());
        var unit = new MonsterDefinition(id, id, BattleElement.Light, form);
        return BattleUnit.CreateMonster(id, unit, new BattleSlot(side, slot), 60, MonsterRank.Normal,
            new BattleStats(40, maxHealth, 300, speed));
    }

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence, "turn-" + sequence,
            actor, skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
