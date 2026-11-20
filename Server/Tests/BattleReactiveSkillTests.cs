using AstralOath.Shared.Battle;
using System.Text.Json;
using Xunit;

public sealed class BattleReactiveSkillTests
{
    [Fact]
    public void GuardShieldsTheAttackedAllyBelowHalfAndStartsItsOwnCooldown()
    {
        const string id = "guard";
        var basic = new BattleCatalogData.SkillRow { id = "basic", slot = 1, target = "one_enemy", attackRateBp = 10000, powerBp = 10000 };
        var passive = new BattleCatalogData.SkillRow { id = id, slot = 2, target = "passive", cooldownTurns = 2 };
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2, skills = new[] { basic, passive },
            effects = new[]
            {
                new BattleCatalogData.EffectRow { id = "basic.1", skillId = "basic", order = 1, phase = "impact", operation = "damage", recipient = "targets", chanceBp = 10000 },
                new BattleCatalogData.EffectRow { id = "guard.1", skillId = id, order = 1, phase = "after_ally_damaged", operation = "trigger", recipient = "targets", condition = "recipient_health_at_most_half", chanceBp = 10000 },
                new BattleCatalogData.EffectRow { id = "guard.2", skillId = id, order = 2, phase = "after_ally_damaged", operation = "target_health_barrier", recipient = "targets", value = 1500, turns = 2, chanceBp = 10000 }
            },
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
        var guardian = Unit("guardian", BattleSide.Ally, 1, 1, "basic", id);
        var ward = Unit("ward", BattleSide.Ally, 4, 1, "basic");
        ward.ApplyDamage(5000);
        var enemy = Unit("enemy", BattleSide.Enemy, 1, 200, "basic");
        var match = new BattleMatch("guard-test", BattlePreviewFactory.RulesVersion, 1, new BattleFormation(new[] { guardian, ward, enemy }),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0));
        var result = match.Submit(new BattleSkillCommand("guard-test", BattlePreviewFactory.RulesVersion, 1, "attack", "enemy", "basic", "ward"));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(1500, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Barrier)?.Strength);
        Assert.Null(result.Snapshot.GetUnit("guardian").GetStatus(BattleStatusKind.Barrier));
        Assert.Equal(2, result.Snapshot.GetUnit("guardian").GetCooldown(id));
    }

    private static BattleUnit Unit(string id, BattleSide side, int position, int speed, params string[] skills) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Dark,
            new BattleUnitForm("standard", id, skills)), new BattleSlot(side, position), 60, MonsterRank.Normal,
            new BattleStats(200, 10000, 300, speed));

    [Fact]
    public void PurrgisCounterUsesItsAuthoredAreaAttackAndGrantsOneSoul()
    {
        var catalog = ShippedCatalog();
        catalog.effects.Single(e => e.skillId == "c1035.s2" && e.operation == "counterattack").chanceBp = 10000;
        var result = Attack(catalog, "c1035", "guardian");
        var counter = Assert.Single(result.Events, e => e.Origin == BattleActionOrigin.Counter && e.Kind == BattleFlowEventKind.Action);
        Assert.Equal("c1035.s2", counter.SkillId);
        Assert.Equal("c_fuzzies_skill_03_2", counter.PresentationId);
        Assert.Equal(2, result.Events.Count(e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "guardian"));
        Assert.Equal(1, result.Snapshot.GetSoul(BattleSide.Ally));
    }

    [Fact]
    public void DominielReactsOnceToCriticalDamageAndAwakeningAddsTeamReflect()
    {
        var catalog = ShippedCatalog();
        catalog.effects.Single(e => e.skillId == "c1037.s2" && e.operation == "trigger").chanceBp = 10000;
        var result = Attack(catalog, "c1037", "ward", awakenings: 3);
        Assert.Equal(5, result.Snapshot.GetUnit("guardian").GetCooldown("c1037.s2"));
        Assert.Equal(2500, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Barrier)?.Strength);
        Assert.Equal(3000, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Reflect)?.Strength);
        Assert.NotNull(result.Snapshot.GetUnit("guardian").GetStatus(BattleStatusKind.Reflect));
    }

    [Theory]
    [InlineData("c1037", true)]
    [InlineData("c1036", false)]
    public void LethalCriticalCanTriggerTeamSupportButCannotShieldTheDefeatedAlly(string hero, bool teamReacts)
    {
        var catalog = ShippedCatalog();
        var passive = hero + ".s2";
        catalog.effects.Single(e => e.skillId == passive && e.operation == "trigger").chanceBp = 10000;
        var result = Attack(catalog, hero, "ward", awakenings: 3, attackRate: 10000000);
        var defeated = result.Snapshot.GetUnit("ward");
        var survivor = result.Snapshot.GetUnit("guardian");
        Assert.Equal(0, defeated.CurrentHealth);
        Assert.Null(defeated.GetStatus(BattleStatusKind.Barrier));
        Assert.Equal(teamReacts, survivor.GetStatus(BattleStatusKind.Reflect) != null);
        Assert.Equal(teamReacts, survivor.GetStatus(BattleStatusKind.Barrier) != null);
        Assert.Equal(teamReacts, survivor.GetCooldown(passive) > 0);
    }

    [Theory]
    [InlineData(0, 2, 1500)]
    [InlineData(3, 1, 1950)]
    public void CrozetUsesTargetsMaxHealthAndAwakenedCooldown(int awakening, int cooldown, int barrier)
    {
        var result = Attack(ShippedCatalog(), "c1036", "ward", awakenings: awakening, rank: awakening == 3 ? 4 : 0);
        Assert.Equal(cooldown, result.Snapshot.GetUnit("guardian").GetCooldown("c1036.s2"));
        Assert.Equal(barrier, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Barrier)?.Strength);
        Assert.Equal(6000, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.DefenseUp)?.Strength);
    }

    [Theory]
    [InlineData(0, 7000)]
    [InlineData(5, 5100)]
    public void SezSkillEnhancementsReduceTheDamageCap(int rank, int expectedLimitBp)
    {
        var result = Attack(ShippedCatalog(), "c1038", "guardian", rank: rank, attackRate: 10000000);
        var target = result.Snapshot.GetUnit("guardian");
        int limit = (int)Math.Round(target.Stats.MaxHealth * expectedLimitBp / 10000m, MidpointRounding.AwayFromZero);
        Assert.Equal(target.Stats.MaxHealth - limit, target.CurrentHealth);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SezEncroachesOnlyAfterAnotherAllyAttacksTheWoundedTarget(bool anotherAlly, bool activates)
    {
        const string hero = "c1038";
        var catalog = ShippedCatalog();
        string attacker = anotherAlly ? "ally" : "sez";
        var sez = Unit("sez", BattleSide.Ally, 1, anotherAlly ? 1 : 200, hero + ".s1", hero + ".s2", hero + ".s3");
        var ally = Unit("ally", BattleSide.Ally, 2, anotherAlly ? 200 : 1, "c1042.s1");
        var enemy = Unit("enemy", BattleSide.Enemy, 1, 1, "c1042.s1");
        enemy.ApplyDamage(7000);
        var match = new BattleMatch("encroach", BattlePreviewFactory.RulesVersion, 19,
            new BattleFormation(new[] { sez, ally, enemy, Unit("other", BattleSide.Enemy, 2, 1, "c1042.s1") }),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0));
        var result = match.Submit(new BattleSkillCommand("encroach", BattlePreviewFactory.RulesVersion, 1, "attack",
            attacker, anotherAlly ? "c1042.s1" : hero + ".s1", "enemy"));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(activates, result.Events.Any(e => e.Kind == BattleFlowEventKind.Action && e.SkillId == hero + ".s2"));
        if (activates)
        {
            Assert.Equal(3, result.Snapshot.GetUnit("sez").GetCooldown(hero + ".s2"));
            Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "sez" && e.TargetId == "other");
            Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId == "sez");
        }
    }

    [Fact]
    public void ReflectReturnsThirtyPercentToAttackerWithoutAnotherAttack()
    {
        var catalog = ShippedCatalog();
        var gift = catalog.skills.Single(s => s.id == "c1037.s2");
        var reflect = catalog.effects.Single(e => e.skillId == gift.id && e.status == "Reflect");
        // Prepare the exact shipped Reflect strength at battle start, independently of its random team trigger.
        catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow { id = gift.id + ".fixture",
            order = catalog.effects.Count(e => e.skillId == gift.id) + 1, skillId = gift.id, phase = "battle_start",
            operation = "apply_status", recipient = "self", status = reflect.status, chanceBp = 10000,
            value = reflect.value, turns = reflect.turns }).ToArray();
        var result = Attack(catalog, "c1037", "guardian");
        var direct = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "enemy");
        var returned = Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "guardian");
        Assert.Equal("enemy", returned.TargetId);
        Assert.Equal((int)Math.Round(direct.Amount * .3m, MidpointRounding.AwayFromZero), returned.Amount);
    }

    [Theory]
    [InlineData("ward", 4, true)]
    [InlineData("ward", 2, false)]
    [InlineData("guardian", 4, false)]
    public void TywinCleansesOnlyTheBackRowAndBlocksOwnStun(string target, int wardPosition, bool reacts)
    {
        var catalog = ShippedCatalog();
        // Use a seeded actual attack and force Stun through resistance for the ward fixture.
        var result = Enumerable.Range(1, 64).Select(seed => Attack(catalog, "c1042", target, awakenings: 3,
            wardPosition: wardPosition, addStun: true, seed: (ulong)seed))
            .First(r => target == "guardian" || r.Events.Any(e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.Stun));
        Assert.Null(result.Snapshot.GetUnit("guardian").GetStatus(BattleStatusKind.Stun));
        Assert.Equal(reacts, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Immunity) != null);
        Assert.Equal(reacts, result.Snapshot.GetUnit("ward").GetStatus(BattleStatusKind.Barrier) != null);
    }

    private static BattleCatalogData ShippedCatalog()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private static BattleCommandResult Attack(BattleCatalogData catalog, string hero, string target, int awakenings = 0,
        int rank = 0, int attackRate = 10000, int wardPosition = 4, bool addStun = false, ulong seed = 7)
    {
        const string probe = "probe";
        if (catalog.skills.All(s => s.id != probe))
        {
            catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow { id = probe, slot = 1,
                target = "one_enemy", powerBp = 10000, attackRateBp = attackRate, alwaysCritical = true }).ToArray();
            catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow { id = probe+".1", skillId = probe,
                order = 1, phase = "impact", operation = "damage", recipient = "targets", chanceBp = 10000 }).ToArray();
            if (addStun) catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow { id = probe+".2", skillId = probe,
                order = 2, phase = "after_hit", operation = "apply_status", recipient = "targets", status = "Stun", chanceBp = 10000, turns = 1 }).ToArray();
        }
        var form = new BattleUnitForm("standard", hero, new[] { hero + ".s1", hero + ".s2", hero + ".s3" });
        var preset = new HeroGrowthPreset("test", 5, 55000, 202600, 56000, 101000);
        var nodes = Enumerable.Range(1, 6).Select(i => new HeroAwakeningStep(i, 20, 60)).ToArray();
        var guardian = BattleUnit.CreateHero("guardian", new HeroDefinition(hero, hero, BattleElement.Dark, "warrior", "leo", form, preset, nodes),
            new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings);
        var ward = Unit("ward", BattleSide.Ally, wardPosition, 1, probe);
        ward.ApplyDamage(5000);
        var party = new[] { guardian, ward, Unit("enemy", BattleSide.Enemy, 1, 200, probe), Unit("other", BattleSide.Enemy, 2, 1, probe) };
        var match = new BattleMatch("test", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(party),
            new BattleSkillResolver(catalog, enhancementRank: (_, skill) => skill == hero+".s2" ? rank : 0, randomDualChanceBp: 0));
        var result = match.Submit(new BattleSkillCommand("test", BattlePreviewFactory.RulesVersion, 1, "attack", "enemy", probe, target));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
