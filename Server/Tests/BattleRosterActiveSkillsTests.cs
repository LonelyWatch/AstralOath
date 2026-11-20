using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRosterActiveSkillsTests
{
    private const string Counter = "fixture.counter";
    private const string Buffs = "fixture.buffs";
    private static readonly string[] OpeningBuffs = { "DefenseUp", "Immunity", "SpeedUp", "CriticalResistanceUp",
        "CriticalDamageUp", "EffectivenessUp", "Invincible", "CriticalChanceUp", "ContinuousHealing", "Immortality", "Counter" };

    [Fact]
    public void LidicasNullifierPersistsAcrossTurnsAndStopsOneCounter()
    {
        var match = Match("c1046", 3, _ => new[] { Enemy(1, 100), Enemy(2, 100, passive: Counter) });
        var granted = Cast(match, "c1046.s3", "enemy1");
        Assert.NotNull(granted.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.SkillNullifier));
        BattleCommandResult Basic(string target)
        {
            var state = match.Snapshot;
            var sequence = state.LastCommandSequence + 1;
            Assert.Equal("hero", state.CurrentActorId);
            var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
                "basic-" + sequence, "hero", "c1046.s1", target));
            Assert.Equal(BattleCommandStatus.Accepted, result.Status);
            return result;
        }
        Assert.NotNull(Basic("enemy1").Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.SkillNullifier));
        var blocked = Basic("enemy2");
        Assert.Contains(blocked.Events, e => e.Kind == BattleFlowEventKind.StatusRemoved &&
            e.TargetId == "hero" && e.StatusKind == BattleStatusKind.SkillNullifier);
        Assert.Equal(0, Assert.Single(blocked.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "hero").Amount);
        Assert.True(Assert.Single(Basic("enemy2").Events,
            e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "hero").Amount > 0);
    }

    [Theory]
    [InlineData("c1044", 2)]
    [InlineData("c1043", 0)]
    public void SoulBurnReducesOnlyTheCooldownsNamedInTheOriginalSkill(string hero, int extraReduction)
    {
        int Remaining(bool burn)
        {
            var match = Match(hero, 3, _ => new[] { Enemy(1, 10) });
            Cast(match, hero + ".s3");
            var state = match.Snapshot;
            Assert.Equal("hero", state.CurrentActorId);
            var cast = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, 2, "second",
                "hero", hero + ".s2", null, burn));
            Assert.Equal(BattleCommandStatus.Accepted, cast.Status);
            return cast.Snapshot.GetUnit("hero").GetCooldown(hero + ".s3");
        }
        Assert.Equal(extraReduction, Remaining(false) - Remaining(true));
    }

    [Theory]
    [InlineData("c1044", "s3", 3000)]
    [InlineData("c1046", "s2", 2500)]
    [InlineData("c1048", "s2", 3000)]
    public void ReadinessUsesTheActualCriticalsTargetsOrSuccessfulDebuffs(string hero, string slot, int amount)
    {
        var seen = new HashSet<int>();
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var match = Match(hero, 3, _ => Enumerable.Range(1, 4)
                .Select(i => Enemy(i, 10, resistance: i == 4 ? 20000 : 0)).ToArray(), seed);
            var cast = Cast(match, hero + "." + slot);
            int count = cast.Events.Count(e => hero switch
            {
                "c1044" => e.Kind == BattleFlowEventKind.Damage && e.HitKind == BattleHitKind.Critical,
                "c1046" => e.Kind == BattleFlowEventKind.Damage,
                _ => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.Unhealable
            });
            seen.Add(count);
            var boosts = cast.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged && e.TargetId == "hero");
            if (count == 0) Assert.Empty(boosts);
            Assert.Equal(count * amount, boosts.LastOrDefault()?.Amount ?? 0);
        }
        if (hero == "c1046") Assert.Equal(4, Assert.Single(seen));
        else Assert.True(seen.Count > 1);
    }

    [Theory]
    [InlineData("c1034")]
    [InlineData("c1039")]
    public void UltimateSuppressesTheSameDeterministicCounterThatTriggersOnBasic(string hero)
    {
        BattleMatch Encounter() => Match(hero, 3, _ => new[] { Enemy(1, 50, passive: Counter) });
        var basic = Cast(Encounter(), hero + ".s1", "enemy1");
        Assert.Contains(basic.Events, e => e.Origin == BattleActionOrigin.Counter && e.Kind == BattleFlowEventKind.Action);
        var ultimate = Cast(Encounter(), hero + ".s3");
        Assert.Contains(ultimate.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
        Assert.DoesNotContain(ultimate.Events, e => e.Origin == BattleActionOrigin.Counter && e.Kind == BattleFlowEventKind.Action);
    }

    [Fact]
    public void ExistingUnboundedDispelStillRemovesAllElevenOnASuccessfulTargetRoll()
    {
        var counts = Enumerable.Range(1, 12).Select(seed =>
        {
            var match = Match("c1024", 3, _ => new[] { Enemy(1, 50, passive: Buffs) }, (ulong)seed);
            return Cast(match, "c1024.s3", "enemy1").Events.Count(e =>
                e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy1");
        }).ToArray();
        Assert.All(counts, count => Assert.True(count is 0 or 11));
        Assert.Contains(11, counts);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(20000)]
    public void StrazeFullyPiercesOnlyTheHighestMaximumHealthDespiteCurrentHpAndAttack(int victimAttack)
    {
        BattleCommandResult WithDefense(int defense)
        {
            var match = Match("c1034", 3, _ =>
            {
                var high = Enemy(2, victimAttack, defense, 60000);
                high.ApplyDamage(30000);
                return new[] { Enemy(1, victimAttack, defense, 50000), high };
            });
            return Cast(match, "c1034.s3");
        }
        var unarmored = WithDefense(0);
        var armored = WithDefense(900);
        int Damage(BattleCommandResult result, string target) => Assert.Single(result.Events,
            e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "hero" && e.TargetId == target).Amount;
        Assert.Equal(Damage(unarmored, "enemy2"), Damage(armored, "enemy2"));
        Assert.InRange(Damage(armored, "enemy1") * 4 - Damage(unarmored, "enemy1"), -3, 3);
    }

    [Fact]
    public void StrazeDispelIgnoresResistanceOnlyBelowHisAttackAndRemovesAtMostTenBuffs()
    {
        var match = Match("c1034", 3, hero => new[] { -1, 0, 1 }.Select((difference, index) =>
            Enemy(index + 1, hero.Stats.Attack + difference, passive: Buffs, resistance: 30000)).ToArray());
        foreach (string id in new[] { "enemy1", "enemy2", "enemy3" })
            Assert.Equal(11, match.Snapshot.GetUnit(id).Statuses.Count);
        var result = Cast(match, "c1034.s2");
        Assert.Equal(10, result.Events.Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy1"));
        Assert.Single(result.Snapshot.GetUnit("enemy1").Statuses);
        foreach (string id in new[] { "enemy2", "enemy3" })
        {
            Assert.Equal(11, result.Snapshot.GetUnit(id).Statuses.Count);
            Assert.Single(result.Events, e => e.Kind == BattleFlowEventKind.EffectRejected &&
                e.TargetId == id && e.EffectRejection == BattleEffectRejection.Resisted);
        }
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void SerilaRecoversThirtyPercentOfHealthActuallyLostOnlyAfterThirdAwakening(int awakening, bool burn)
    {
        var match = Match("c1040", awakening, _ => new[] { Enemy(1, 10, hp: 400) });
        var result = Cast(match, "c1040.s2", "enemy1", burn);
        Assert.Equal(0, result.Snapshot.GetUnit("enemy1").CurrentHealth);
        Assert.Contains(result.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
        var heals = result.Events.Where(e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "hero").ToArray();
        if (awakening < 3) Assert.Empty(heals);
        else Assert.Equal(120, Assert.Single(heals).Amount);
    }

    [Theory]
    [InlineData("c1039", 27, false)]
    [InlineData("c1039", 27, true)]
    [InlineData("c1040", 30, false)]
    [InlineData("c1040", 30, true)]
    public void LifestealUsesTheHitThatPrecededOptionalSpiritlordRevival(string hero, int expectedHealing, bool revive)
    {
        var match = Match(hero, 3, _ =>
        {
            var victim = Enemy(1, 10, hp: 10000, passive: revive ? "c1022.s3" : null);
            victim.ApplyDamage(9900);
            return new[] { victim };
        });
        var cast = Cast(match, hero + ".s2", "enemy1");
        var damage = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
        Assert.Equal(100, damage.Amount);
        var heal = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "hero");
        if (revive)
        {
            var revived = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.UnitRevived && e.TargetId == "enemy1");
            Assert.True(damage.Sequence < revived.Sequence && revived.Sequence < heal.Sequence);
            Assert.Equal(5000, cast.Snapshot.GetUnit("enemy1").CurrentHealth);
        }
        else Assert.Equal(0, cast.Snapshot.GetUnit("enemy1").CurrentHealth);
        Assert.Equal(expectedHealing, heal.Amount);
    }

    private static BattleUnit Enemy(int slot, int attack, int defense = 300, int hp = 50000,
        string? passive = null, int resistance = 0)
    {
        var skills = new List<string> { BattlePreviewFactory.SlimeSkillId };
        if (passive != null) skills.Add(passive);
        var definition = new MonsterDefinition("fixture", "fixture", BattleElement.Dark,
            new BattleUnitForm("normal", "fixture", skills));
        return BattleUnit.CreateMonster("enemy" + slot, definition, new BattleSlot(BattleSide.Enemy, slot),
            60, MonsterRank.Normal, new BattleStats(attack, hp, defense, 1, effectResistanceBp: resistance));
    }

    private static BattleMatch Match(string heroId, int awakening, Func<BattleUnit, BattleUnit[]> enemies, ulong seed = 17)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = catalog.heroes.Single(entry => entry.id == heroId);
        var growth = catalog.growthPresets.Single(entry => entry.id == row.growthPresetId);
        var form = new BattleUnitForm(row.variant, heroId, catalog.skills.Where(skill => skill.heroId == heroId)
            .OrderBy(skill => skill.slot).Select(skill => skill.id));
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(step => step.heroId == heroId).Select(step => new HeroAwakeningStep(step.rank,
            step.attackFlat, step.healthFlat, step.attackPercentBp, step.healthPercentBp, step.defensePercentBp,
            step.speedFlat, step.critChanceBp, step.critDamageBp, step.effectivenessBp, step.effectResistanceBp));
        var definition = new HeroDefinition(heroId, row.nameKey, Enum.Parse<BattleElement>(row.element, true),
            row.heroClass, row.zodiac, form, preset, steps);
        var hero = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        hero.ApplyDamage(1000);
        catalog.skills = catalog.skills.Concat(new[] { Counter, Buffs }.Select(id => new BattleCatalogData.SkillRow
            { id = id, heroId = "fixture", slot = 2, target = "passive" })).ToArray();
        catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow { id = Counter + ".1", skillId = Counter,
            phase = "on_attacked", operation = "counterattack", recipient = "self", chanceBp = 10000, order = 1 })
            .Concat(OpeningBuffs.Select((status, index) => new BattleCatalogData.EffectRow { id = Buffs + "." + (index + 1),
                skillId = Buffs, order = index + 1, phase = "battle_start", operation = "apply_status", status = status,
                recipient = "self", chanceBp = 10000, turns = 5 })).ToArray();
        return new BattleMatch("active", BattlePreviewFactory.RulesVersion, seed,
            new BattleFormation(new[] { hero }.Concat(enemies(hero))),
            new BattleSkillResolver(catalog, includePreviewSlime: true, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 20));
    }

    private static BattleCommandResult Cast(BattleMatch match, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, 1, "cast", "hero", skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
