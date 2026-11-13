using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRaviTests
{
    [Theory]
    [InlineData(false, 10000, false)]
    [InlineData(true, 25000, true)]
    public void OpeningHealthBonusAndSlaughterLeechUseExactHealthLoss(bool burn, int rate, bool noDual)
    {
        var match = Fixture(6, support: true);
        var before = match.Snapshot.GetUnit("ravi");
        Assert.Equal(60, before.Resource.Amount);
        int bonus = (int)Math.Round(before.Stats.MaxHealth * .08m, MidpointRounding.AwayFromZero);
        Assert.Equal(bonus, before.OpeningAttackBonus);
        var result = Cast(match, "ravi", "ravi.s1", "enemy", burn);
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.SourceId == "ravi");
        Assert.Equal(BattleHitKind.Critical, hit.HitKind);
        Assert.Equal(EpicSevenCombatMath.Damage(before.Stats.Attack + bonus, result.Snapshot.GetUnit("enemy").Stats.Defense, before.MaximumHealth, rate,
            10000, 0, 0, 0, hit.HitKind, before.Stats.CritDamageBp), hit.Amount);
        var heal = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Healing && item.TargetId == "ravi");
        Assert.Equal(Math.Min(before.MaximumHealth - before.CurrentHealth,
            (int)Math.Round(hit.Amount * .3m, MidpointRounding.AwayFromZero)), heal.Amount);
        Assert.Equal(noDual, !result.Events.Any(item => item.Kind == BattleFlowEventKind.Action && item.Origin == BattleActionOrigin.DualAttack));
        Assert.Equal(90, result.Snapshot.GetUnit("ravi").Resource.Amount);
    }

    [Theory]
    [InlineData(2, 1000)]
    [InlineData(6, 2000)]
    public void InjuryUsesHeroOnlyBaseCapAndCarriesOrderedEffectiveMaximum(int awakening, int capBp)
    {
        var match = Fixture(awakening, fastRavi: true);
        var before = match.Snapshot.GetUnit("enemy");
        Cast(match, "ravi", "ravi.s1", "enemy");
        Assert.Equal(90, match.Snapshot.GetUnit("ravi").Resource.Amount);
        var result = Cast(match, "ravi", "ravi.s3", null);
        var hit = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "enemy");
        var injury = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.InjuryApplied && item.TargetId == "enemy");
        Assert.True(hit.Sequence < injury.Sequence);
        Assert.Equal(before.MaximumHealth, hit.MaximumHealth);
        Assert.Equal(Math.Min(hit.Amount, before.Stats.MaxHealth * capBp / 10000), injury.Amount);
        Assert.Equal(before.MaximumHealth - injury.Amount, injury.MaximumHealth);
        Assert.Equal(injury.MaximumHealth, result.Snapshot.GetUnit("enemy").MaximumHealth);
        Assert.Equal(before.Stats.MaxHealth, result.Snapshot.GetUnit("enemy").Stats.MaxHealth);
        Assert.Equal(injury.Amount, result.Snapshot.GetUnit("enemy").InjuryAmount);
        Assert.Equal(0, result.Snapshot.GetUnit("ravi").Resource.Amount);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.InjuryApplied && item.TargetId == "monster");
    }

    [Fact]
    public void RepeatedInjuriesStopAtFiftyPercentOfUnmodifiedMaximum()
    {
        var match = Fixture(6, fastRavi: true, repeated: true);
        var enemy = match.Snapshot.GetUnit("enemy");
        int originalMaximum = enemy.MaximumHealth;
        for (int i = 0; i < 4; i++)
        {
            var result = Cast(match, "ravi", "ravi.s3", null);
            int expected = Math.Min(originalMaximum / 2, originalMaximum / 5 * (i + 1));
            var current = result.Snapshot.GetUnit("enemy");
            Assert.Equal(expected, current.InjuryAmount);
            Assert.Equal(originalMaximum - expected, current.MaximumHealth);
            Assert.True(current.CurrentHealth <= current.MaximumHealth);
        }
    }

    [Fact]
    public void EachGlobalTurnGrantsOnlyOneCriticalCleanseAndKeepsOtherDebuffs()
    {
        var match = Fixture(6, attackedFirst: true);
        var first = Cast(match, "enemy", "enemy.s1", "ravi");
        Assert.Equal(1, first.Events.Count(item => item.Kind == BattleFlowEventKind.StatusRemoved && item.TargetId == "ravi"));
        Assert.Contains(first.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged && item.TargetId == "ravi");
        Assert.NotNull(first.Snapshot.GetUnit("ravi").GetStatus(BattleStatusKind.SpeedDown));
        Assert.Equal(60, first.Snapshot.GetUnit("ravi").Resource.Amount);
    }

    [Fact]
    public void EmbeddedRaviKitAndAiAgreeOnStrengthenedFightingSpiritCost()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, hero => hero.id == "c1019");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1019.s1", "c1019.s2", "c1019.s3" });
        var definition = new HeroDefinition(row.id, row.nameKey, BattleElement.Fire, row.heroClass, row.zodiac, form, preset,
            steps, new BattleResourceDefinition(BattleResourceKind.FightingSpirit, row.resourceMaximum, openingGain: row.resourceOpeningGain));
        var ravi = BattleUnit.CreateHero("ravi", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, 6);
        var enemy = Hero("enemy", BattleSide.Enemy, 1, 6, 1, new[] { BattlePreviewFactory.SlimeSkillId });
        int EnhancedRank(string actor, string skill) => actor == "ravi" && skill == "c1019.s3" ? 3 : 0;
        var match = new BattleMatch("ravi-live", BattlePreviewFactory.RulesVersion, 4,
            new BattleFormation(new[] { ravi, enemy }), new BattleSkillResolver(catalog, enhancementRank: EnhancedRank, randomDualChanceBp: 0));
        var enhancedAi = new BattleAiPolicy(catalog, EnhancedRank);
        Assert.False(enhancedAi.IsSkillAvailable(match.Snapshot, "c1019.s3"));
        Cast(match, "ravi", "c1019.s1", "enemy");
        Assert.Equal(90, match.Snapshot.GetUnit("ravi").Resource.Amount);
        Assert.False(new BattleAiPolicy(catalog).IsSkillAvailable(match.Snapshot, "c1019.s3"));
        Assert.True(enhancedAi.IsSkillAvailable(match.Snapshot, "c1019.s3"));
        var result = Cast(match, "ravi", "c1019.s3", null);
        Assert.Equal(0, result.Snapshot.GetUnit("ravi").Resource.Amount);
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.InjuryApplied && item.TargetId == "enemy");
    }

    [Fact]
    public void EarlyCriticalHitTriggersCleanseWhenLastHitIsNormal()
    {
        var result = Enumerable.Range(1, 256).Select(seed =>
        {
            var match = Fixture(6, attackedFirst: true, mixedHits: true, seed: (ulong)seed);
            return Cast(match, "enemy", "enemy.s1", "ravi");
        }).First(item => item.Events.Where(hit => hit.Kind == BattleFlowEventKind.Damage && hit.TargetId == "ravi")
            .Select(hit => hit.HitKind).SequenceEqual(new[] { BattleHitKind.Critical, BattleHitKind.Normal }));
        Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusRemoved && item.TargetId == "ravi");
    }

    private static BattleMatch Fixture(int awakening, bool fastRavi = false, bool support = false, bool attackedFirst = false,
        bool repeated = false, bool mixedHits = false, ulong seed = 4)
    {
        var r1 = Skill("ravi.s1", 1, "one_enemy", 10000, 10000);
        r1.alwaysCritical = true; r1.soulBurnCost = 20; r1.soulBurnKind = "increase_damage_no_dual";
        r1.burnAttackRateBp = 25000; r1.resourceGain = 30; r1.soulGain = 1;
        var r2 = Skill("ravi.s2", 2, "passive", null, null);
        var r3 = Skill("ravi.s3", 3, "all_enemies", repeated ? 8000 : 12000, 9500);
        r3.alwaysCritical = true; r3.resourceCost = repeated ? 10 : 100; r3.soulGain = 3;
        var e1 = Skill("enemy.s1", 1, "one_enemy", 5000, 10000);
        e1.alwaysCritical = !mixedHits;
        var supportSkill = Skill("support.s1", 1, "one_enemy", 10000, 10000);
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2, skills = new[] { r1, r2, r3, e1, supportSkill },
            effects = new[]
            {
                Effect(r1.id, 1, "impact", "damage"),
                Effect(r1.id, 2, "after_attack", "lifesteal", "self", value: 3000),
                Effect(r2.id, 1, "battle_start", "opening_attack_from_health", "self", value: 800),
                Effect(r2.id, 2, "on_attacked", "readiness", "self", value: 800),
                Effect(r2.id, 3, "on_attacked", "cleanse_once_on_critical", "self", value: 1),
                Effect(r3.id, 1, "impact", "damage"),
                Effect(r3.id, 2, "after_hit", "injury", value: 1000, condition: "caster_skill_not_awakened"),
                Effect(r3.id, 3, "after_hit", "injury", value: 2000, condition: "caster_skill_awakened"),
                Effect(e1.id, 1, "impact", "damage"),
                Effect(e1.id, 2, "impact", "damage"),
                Effect(e1.id, 3, "after_hit", "apply_status", status: "AttackDown", turns: 2, value: 5000),
                Effect(e1.id, 4, "after_hit", "apply_status", status: "SpeedDown", turns: 2, value: 3000),
                Effect(supportSkill.id, 1, "impact", "damage"),
            },
            enhancements = new[] { new BattleCatalogData.EnhancementRow { skillId = r3.id, step = 1, kind = "resource_cost", value = 10 } }
        };
        var ally = Hero("ravi", BattleSide.Ally, 1, awakening, fastRavi ? 120000 : 102000,
            new[] { r1.id, r2.id, r3.id }, new BattleResourceDefinition(BattleResourceKind.FightingSpirit, 100, openingGain: 60));
        ally.ApplyDamage(2500);
        var units = new List<BattleUnit> { ally,
            Hero("enemy", BattleSide.Enemy, 1, 6, attackedFirst ? 150000 : fastRavi ? 1 : 101000, new[] { e1.id },
                defense: repeated ? 20000 : 56000) };
        if (fastRavi)
            units.Add(Monster("monster", BattleSide.Enemy, 2, 1, e1.id));
        if (support)
            units.Add(Monster("support", BattleSide.Ally, 2, 1, supportSkill.id));
        return new BattleMatch("ravi", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(units),
            new BattleSkillResolver(catalog, enhancementRank: (_, skill) => skill == r3.id ? 1 : 0, randomDualChanceBp: support ? 10000 : 0),
            new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleUnit Hero(string id, BattleSide side, int slot, int awakenings, int speedMilli,
        string[] skills, BattleResourceDefinition? resource = null, int defense = 56000)
    {
        var form = new BattleUnitForm("standard", id, skills);
        var preset = new HeroGrowthPreset(id + "_preset", 5, 55000, 202600, defense, speedMilli);
        var steps = Enumerable.Range(1, 6).Select(i => new HeroAwakeningStep(i, 20, 60)).ToArray();
        return BattleUnit.CreateHero(id, new HeroDefinition(id, id, BattleElement.Fire, "warrior", "capricorn", form, preset, steps,
            resource), new BattleSlot(side, slot), 60, 6, awakenings);
    }

    private static BattleUnit Monster(string id, BattleSide side, int slot, int speed, string skill) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Fire, new BattleUnitForm("standard", id, new[] { skill })),
            new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(25, 10000, 300, speed));

    private static BattleCatalogData.SkillRow Skill(string id, int slot, string target, int? attack, int? power) =>
        new() { id = id, heroId = id.Split('.')[0], slot = slot, target = target, soulBurnKind = "none", attackRateBp = attack, powerBp = power };

    private static BattleCatalogData.EffectRow Effect(string id, int order, string phase, string operation,
        string recipient = "targets", string status = "none", int turns = 0, int value = 0, string condition = "none") =>
        new() { id = id + "." + order, skillId = id, order = order, phase = phase, operation = operation, recipient = recipient,
            status = status, turns = turns, value = value, chanceBp = 10000, condition = condition };

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong seq = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, seq, "turn-" + seq,
            actor, skill, target!, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
