using System;
using System.Linq;
using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleSpecialAttackTests
{
    [Fact]
    public void TeamHealingUsesActualHealthLostAndHealsEachLivingAlly()
    {
        var match = Match("team_lifesteal", 2700, 3000, hurtAlly: true);
        var cast = Attack(match);
        var damage = cast.Events.Single(e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy");
        var healed = cast.Events.Single(e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "ally");
        Assert.Equal((int)Math.Round(damage.Amount * .27m, MidpointRounding.AwayFromZero), healed.Amount);
    }

    [Fact]
    public void ExtinctionAndAftermathRequireKillingTheOriginalTarget()
    {
        var cast = Attack(Match("extinction", 1, 1));
        Assert.Contains(cast.Events, e => e.Kind == BattleFlowEventKind.UnitExtinct && e.TargetId == "enemy");
        Assert.Contains(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "other");
        var survived = Attack(Match("extinction", 1, 20000));
        Assert.DoesNotContain(survived.Events, e => e.Kind == BattleFlowEventKind.UnitExtinct || e.Kind == BattleFlowEventKind.Damage && e.TargetId == "other");
    }

    [Fact]
    public void ColiFireTargetGrantsTwoPushesAndBladeArtDetonatesBothSourceStacks()
    {
        var match = HeroMatch("c1033", 17);
        var planted = Cast(match, "c1033.s3", "enemy");
        Assert.Equal(2, planted.Events.Count(e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId == "hero"));
        Assert.NotNull(planted.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.Bomb));
        Assert.NotNull(planted.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.Bleed));
        var detonated = Cast(match, "c1033.s2", "enemy");
        foreach (var kind in new[] { BattleStatusKind.Bomb, BattleStatusKind.Bleed })
        {
            Assert.Contains(detonated.Events, e => e.Kind == BattleFlowEventKind.Damage && e.StatusKind == kind);
            Assert.Null(detonated.Snapshot.GetUnit("enemy").GetStatus(kind));
        }
        Assert.NotNull(detonated.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.Stealth));
        Assert.NotNull(detonated.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.Stun));
    }

    [Fact]
    public void HasteAwakenedScytheHealsAlliesAndMarkHealsTheNextAttacker()
    {
        var match = HeroMatch("c1039", 4);
        var siphon = Cast(match, "c1039.s2", "enemy");
        var damage = Assert.Single(siphon.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy");
        var allyHeal = Assert.Single(siphon.Events, e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "ally");
        Assert.Equal((int)Math.Round(damage.Amount * .27m, MidpointRounding.AwayFromZero), allyHeal.Amount);
        Assert.NotNull(siphon.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.VampiricTouch));
        Assert.NotNull(siphon.Snapshot.GetUnit("enemy").GetStatus(BattleStatusKind.Unhealable));
        var follow = Cast(match, "c1039.s1", "enemy");
        Assert.Contains(follow.Events, e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "hero" && e.Amount > 0);
    }

    [Fact]
    public void SezExtinctionKillsTheSelectedEnemyAndDamagesOnlyTheOtherEnemy()
    {
        var match = HeroMatch("c1038", 13, enemyHp: 1);
        var execution = Cast(match, "c1038.s3", "enemy");
        Assert.True(execution.Snapshot.GetUnit("enemy").IsExtinct);
        Assert.Single(execution.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "other");
        Assert.DoesNotContain(execution.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "ally");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ColiStealsTheOriginalBuffsWithTheirRemainingDurations(bool burn, int count)
    {
        // The unresistable fire exception belongs to her S3. Sample S1 with its own resistance roll.
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var match = HeroMatch("c1033", seed, openingBuffs: true);
            var action = Cast(match, "c1033.s1", "enemy", burn);
            if (action.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.DefenseUp) == null) continue;
            Assert.Equal(count, action.Events.Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy"));
            Assert.Equal(3, action.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.DefenseUp).RemainingTurns);
            Assert.Equal(burn, action.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.AttackUp) != null);
            return;
        }
        Assert.Fail("No seed produced a successful S1 steal.");
    }

    private static BattleMatch HeroMatch(string hero, ulong seed, int enemyHp = 50000, bool openingBuffs = false)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream,
            new JsonSerializerOptions { IncludeFields = true })!;
        var row = catalog.heroes.Single(item => item.id == hero);
        var growth = catalog.growthPresets.Single(item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var form = new BattleUnitForm(row.variant, hero, catalog.skills.Where(skill => skill.heroId == hero)
            .OrderBy(skill => skill.slot).Select(skill => skill.id));
        var definition = new HeroDefinition(hero, row.nameKey, Enum.Parse<BattleElement>(row.element, true),
            row.heroClass, row.zodiac, form, preset, catalog.awakeningSteps.Where(s => s.heroId == hero)
                .Select(s => new HeroAwakeningStep(s.rank, s.attackFlat, s.healthFlat, s.attackPercentBp, s.healthPercentBp,
                    s.defensePercentBp, s.speedFlat, s.critChanceBp, s.critDamageBp, s.effectivenessBp, s.effectResistanceBp)));
        var caster = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, 6);
        caster.ApplyDamage(caster.CurrentHealth - 100);
        const string fixtureBuff = "fixture.buffs";
        if (openingBuffs)
        {
            catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow {
                id = fixtureBuff, heroId = "fixture", slot = 2, target = "passive"
            }).ToArray();
            catalog.effects = catalog.effects.Concat(new[] { "DefenseUp", "AttackUp" }.Select((kind, index) =>
                new BattleCatalogData.EffectRow { id = fixtureBuff + "." + (index + 1), skillId = fixtureBuff, order = index + 1,
                    phase = "battle_start", operation = "apply_status", recipient = "self", chanceBp = 10000,
                    status = kind, turns = 3, value = 3000 })).ToArray();
        }
        var monster = new MonsterDefinition("fixture", "fixture", BattleElement.Fire, new BattleUnitForm("normal", "fixture",
            openingBuffs ? new[] { BattlePreviewFactory.SlimeSkillId, fixtureBuff } : new[] { BattlePreviewFactory.SlimeSkillId }));
        BattleUnit Unit(string id, BattleSide side, int slot, int hp) => BattleUnit.CreateMonster(id, monster,
            new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(10, hp, 300, 1));
        var ally = Unit("ally", BattleSide.Ally, 2, 10000);
        ally.ApplyDamage(9500);
        return new BattleMatch("special", BattlePreviewFactory.RulesVersion, seed,
            new BattleFormation(new[] { caster, ally, Unit("enemy", BattleSide.Enemy, 1, enemyHp),
                Unit("other", BattleSide.Enemy, 2, 50000) }),
            new BattleSkillResolver(catalog, includePreviewSlime: true, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCommandResult Cast(BattleMatch match, string skill, string target, bool burn = false)
    {
        var snapshot = match.Snapshot;
        ulong sequence = snapshot.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion,
            sequence, "cmd" + sequence, "hero", skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }

    private static BattleCommandResult Attack(BattleMatch match)
    {
        var result = match.Submit(new BattleSkillCommand("test", 1, 1, "cmd", "hero", "skill", "enemy"));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }

    private static BattleMatch Match(string operation, int value, int enemyHp, bool hurtAlly = false)
    {
        var skill = new BattleCatalogData.SkillRow
        {
            id = "skill", heroId = "fixture", slot = 1, target = "one_enemy",
            powerBp = 10000, attackRateBp = 10000, soulBurnKind = "none"
        };
        BattleCatalogData.EffectRow Row(int order, string kind, int coefficient) => new()
        {
            id = "skill." + order, skillId = "skill", order = order, phase = "impact", operation = kind,
            status = "none", recipient = "targets", chanceBp = 10000, value = coefficient, condition = "none"
        };
        var rows = new[] { Row(1, "damage", 0), Row(2, operation, value) };
        if (operation == "extinction") rows = rows.Append(Row(3, "kill_splash", 15000)).ToArray();
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2, skills = new[] { skill }, effects = rows,
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
        var form = new BattleUnitForm("normal", "fixture", new[] { "skill" });
        var monster = new MonsterDefinition("fixture", "fixture", BattleElement.Light, form);
        BattleUnit Unit(string id, BattleSide side, int slot, int health, int speed) =>
            BattleUnit.CreateMonster(id, monster, new BattleSlot(side, slot), 60, MonsterRank.Normal,
                new BattleStats(1000, health, 300, speed));
        var ally = Unit("ally", BattleSide.Ally, 2, 10000, 1);
        if (hurtAlly) ally.ApplyDamage(6000);
        var party = new[] { Unit("hero", BattleSide.Ally, 1, 10000, 200), ally,
            Unit("enemy", BattleSide.Enemy, 1, enemyHp, 1), Unit("other", BattleSide.Enemy, 2, 10000, 1) };
        return new BattleMatch("test", 1, 3, new BattleFormation(party),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0));
    }
}
