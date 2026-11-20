using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster68AuditTests
{
    [Theory]
    [InlineData("c1086", BattleStatusKind.Bleed, 4000, false, false)]
    [InlineData("c1086", BattleStatusKind.Bleed, 4000, true, false)]
    [InlineData("c1086", BattleStatusKind.Bleed, 4000, false, true)]
    [InlineData("c1086", BattleStatusKind.Bleed, 4000, true, true)]
    [InlineData("c1087", BattleStatusKind.Burn, 7000, false, false)]
    [InlineData("c1087", BattleStatusKind.Burn, 7000, true, false)]
    [InlineData("c1087", BattleStatusKind.Burn, 7000, false, true)]
    [InlineData("c1087", BattleStatusKind.Burn, 7000, true, true)]
    public void PrimaryAndStatusDamageUseTheTargetsLiveDefense(string hero, BattleStatusKind kind, int attackRate,
        bool aurius, bool resistPenetration)
    {
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var match = Encounter(hero, seed, rank: 7, aurius: aurius, resistPenetration: resistPenetration);
            var actor = match.Snapshot.GetUnit("hero");
            var victim = match.Snapshot.GetUnit("enemy1");
            var cast = Use(match, hero + ".s1", "enemy1");
            var ticks = cast.Events.Where(e => e.Kind == BattleFlowEventKind.Damage && e.StatusKind == kind).ToArray();
            if (ticks.Length == 0 || (hero == "c1086" && ticks.Length != 2)) continue;

            decimal defense = victim.Stats.Defense * (aurius ? 1.1m : 1m);
            var direct = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.HitKind != BattleHitKind.None);
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, defense, actor.MaximumHealth, 10000, 10000,
                0, 3000, 0, direct.HitKind, actor.Stats.CritDamageBp), direct.Amount);
            if (hero == "c1086")
            {
                var additional = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage &&
                    e.HitKind == BattleHitKind.None && e.StatusKind == null);
                Assert.Equal(AttackDamage(actor.Stats.Attack, 6000, defense, .7m), additional.Amount);
            }

            var applied = cast.Events.Where(e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == kind);
            Assert.Equal(applied.Select(e => e.Status.InstanceId), ticks.Select(e => e.Status.InstanceId));
            foreach (var tick in ticks)
            {
                Assert.Equal(2, tick.Status.RemainingTurns);
                Assert.Equal(AttackDamage(actor.Stats.Attack, attackRate, defense, resistPenetration ? .35m : .7m), tick.Amount);
            }
            Assert.Equal("enemy1", cast.Snapshot.CurrentActorId);
            var remaining = cast.Snapshot.GetUnit("enemy1").Statuses.Where(s => s.Kind == kind).ToArray();
            Assert.Equal(ticks.Select(tick => tick.Status.InstanceId), remaining.Select(status => status.InstanceId));
            Assert.All(remaining, status => Assert.Equal(1, status.RemainingTurns));
            return;
        }
        Assert.Fail("Expected the source status stacks to land within the configured seeds.");
    }

    [Theory]
    [InlineData("c1086", BattleStatusKind.Bleed, 0, 3500, 2)]
    [InlineData("c1086", BattleStatusKind.Bleed, 5, 5000, 2)]
    [InlineData("c1087", BattleStatusKind.Burn, 0, 2000, 1)]
    [InlineData("c1087", BattleStatusKind.Burn, 7, 3500, 1)]
    public void EachDamageOverTimeRowChecksItsOwnChanceAndResistance(string hero, BattleStatusKind kind, int rank,
        int chance, int rows)
    {
        var observed = new HashSet<int>();
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var match = Encounter(hero, seed, rank);
            var cast = Use(match, hero + ".s1", "enemy1");
            var hit = cast.Events.Single(e => e.Kind == BattleFlowEventKind.Damage && e.HitKind != BattleHitKind.None);
            // First command stream: a neutral hit consumes the critical roll, then the crushing roll on non-criticals.
            var random = new BattleRandom(seed ^ 0x9E3779B97F4A7C15UL);
            random.NextUInt64();
            if (hit.HitKind != BattleHitKind.Critical) random.NextUInt64();
            int applied = 0;
            for (int i = 0; i < rows; i++)
                if (random.NextUInt64() % 10000 < (ulong)chance && random.NextUInt64() % 10000 < 8500) applied++;
            Assert.Equal(applied, cast.Events.Count(e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == kind));
            observed.Add(applied);
        }
        Assert.Equal(Enumerable.Range(0, rows + 1), observed.OrderBy(count => count));
    }

    private static int AttackDamage(int attack, int rate, decimal defense, decimal penetration)
    {
        decimal strength = Math.Round(attack * rate / 10000m, MidpointRounding.AwayFromZero);
        return (int)Math.Round(strength * 1.871m / (1m + defense * (1m - penetration) / 300m), MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void AuriusAndDefenseUpAddTheirBonusesToBaseDefense()
    {
        var match = Encounter("c1087", 7, aurius: true, defenseUp: true);
        var actor = match.Snapshot.GetUnit("hero");
        var victim = match.Snapshot.GetUnit("enemy1");
        var cast = Use(match, "c1087.s1", "enemy1");
        var hit = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.HitKind != BattleHitKind.None);
        Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, victim.Stats.Defense * 1.7m, actor.MaximumHealth,
            10000, 10000, 0, 0, 0, hit.HitKind, actor.Stats.CritDamageBp), hit.Amount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    public void KhawanaHitsWithHerSpeedAndAppliesEachTeamEffectToTheCorrectElement(int rank, bool burn)
    {
        var match = Encounter("c1086", 12, rank, enemyHealth: new[] { 500000, 500000 });
        var actor = match.Snapshot.GetUnit("hero");
        var field = Use(match, "c1086.s2", burn: burn);
        var hits = field.Events.Where(e => e.Kind == BattleFlowEventKind.Damage).ToArray();
        Assert.Equal(new[] { "enemy1", "enemy2" }, hits.Select(e => e.TargetId));
        foreach (var hit in hits)
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, 1000, actor.MaximumHealth, 9000, 10000, 0,
                rank == 0 ? 0 : 3000, 0, hit.HitKind, actor.Stats.CritDamageBp,
                damageMultiplier: 1m + actor.Stats.Speed * .00075m), hit.Amount);
        foreach (var id in new[] { "hero", "fire", "dark" })
        {
            var unit = field.Snapshot.GetUnit(id);
            Assert.Equal((5000, 2), (unit.GetStatus(BattleStatusKind.AttackUp).Strength, unit.GetStatus(BattleStatusKind.AttackUp).RemainingTurns));
            Assert.Equal(id != "dark", unit.GetStatus(BattleStatusKind.SkillNullifier) != null);
        }
        Assert.Equal(burn, field.Events.Any(e => e.Kind == BattleFlowEventKind.ExtraTurnGranted));
        Assert.Equal(burn ? 32 : 52, field.Snapshot.AllySoul);
        Assert.Equal(3, field.Snapshot.GetUnit("hero").GetCooldown("c1086.s2"));

        for (ulong seed = 1; seed <= 32; seed++)
        {
            match = Encounter("c1086", seed, rank, defenseUp: true, resistPenetration: true);
            int fireReadiness = match.Snapshot.GetUnit("fire").ReadinessBp;
            var cut = Use(match, "c1086.s3", "enemy1");
            var events = cut.Events.ToArray();
            var stun = Array.FindIndex(events, e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.Stun);
            if (stun < 0 || events.Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy1" &&
                (e.StatusKind == BattleStatusKind.DefenseUp || e.StatusKind == BattleStatusKind.PenetrationResistanceUp)) != 2) continue;
            Assert.Equal(2, events.Take(stun).Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy1"));
            Assert.Equal(1, events[stun].Status.RemainingTurns);
            var hit = Assert.Single(events, e => e.Kind == BattleFlowEventKind.Damage);
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, 1600, actor.MaximumHealth, 15000, 10000, 0,
                rank == 0 ? 0 : 3000, 0, hit.HitKind, actor.Stats.CritDamageBp,
                damageMultiplier: 1m + actor.Stats.Speed * .001125m), hit.Amount);
            var shifts = events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged).ToArray();
            Assert.Equal(new[] { "hero", "fire" }, shifts.Select(e => e.TargetId));
            Assert.Equal(new[] { 2000, fireReadiness + 2000 }, shifts.Select(e => e.Amount));
            Assert.Equal(rank == 0 ? 4 : 3, cut.Snapshot.GetUnit("hero").GetCooldown("c1086.s3"));
            return;
        }
        Assert.Fail("No seed removed both opening buffs and applied Stun.");
    }

    [Fact]
    public void KhawanaChecksResistanceOnceWhenSheDispelsTwoBuffsBeforeStun()
    {
        var counts = new HashSet<int>();
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var cut = Use(Encounter("c1086", seed, defenseUp: true, resistPenetration: true), "c1086.s3", "enemy1");
            int removed = cut.Events.Count(e => e.Kind == BattleFlowEventKind.StatusRemoved && e.TargetId == "enemy1" &&
                (e.StatusKind == BattleStatusKind.DefenseUp || e.StatusKind == BattleStatusKind.PenetrationResistanceUp));
            counts.Add(removed);
        }
        Assert.Equal(new[] { 0, 2 }, counts.OrderBy(value => value));
    }

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(3, 7, true)]
    public void FuriousGrantsTwoTurnsOfCritAndUsesTheSoulBurnRateOnlyOnS3(int awakening, int rank, bool burn)
    {
        var match = Encounter("c1087", 12, rank, awakening: awakening);
        var formation = Use(match, "c1087.s2");
        foreach (var id in new[] { "hero", "fire", "dark" })
        {
            var buff = formation.Snapshot.GetUnit(id).GetStatus(BattleStatusKind.CriticalChanceUp);
            Assert.Equal((5000, 2), (buff.Strength, buff.RemainingTurns));
        }
        Assert.Equal(awakening >= 3, formation.Events.Any(e => e.Kind == BattleFlowEventKind.ExtraTurnGranted));
        Assert.Equal(rank == 0 ? 3 : 2, formation.Snapshot.GetUnit("hero").GetCooldown("c1087.s2"));
        bool landed = false, resisted = false, chanceFailed = false;
        for (ulong seed = 1; seed <= 48; seed++)
        {
            match = Encounter("c1087", seed, rank, awakening: awakening);
            var actor = match.Snapshot.GetUnit("hero");
            var arrow = Use(match, "c1087.s3", "enemy1", burn);
            var hit = Assert.Single(arrow.Events, e => e.Kind == BattleFlowEventKind.Damage);
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, 1000, actor.MaximumHealth,
                burn ? 19500 : 16500, 10000, 0, rank == 0 ? 0 : 3000, 0, hit.HitKind, actor.Stats.CritDamageBp), hit.Amount);
            Assert.Equal(burn ? 42 : 52, arrow.Snapshot.AllySoul);
            Assert.Equal(rank == 0 ? 3 : 2, arrow.Snapshot.GetUnit("hero").GetCooldown("c1087.s3"));
            bool rejected = arrow.Events.Any(e => e.Kind == BattleFlowEventKind.EffectRejected && e.EffectRejection == BattleEffectRejection.Resisted);
            resisted |= rejected;
            chanceFailed |= !rejected && !arrow.Events.Any(e => e.Kind == BattleFlowEventKind.StatusApplied);
            foreach (var applied in arrow.Events.Where(e => e.Kind == BattleFlowEventKind.StatusApplied))
            {
                Assert.Equal(BattleStatusKind.DefenseDown, applied.StatusKind);
                Assert.Equal((7000, 2), (applied.Status.Strength, applied.Status.RemainingTurns));
                landed = true;
            }
        }
        Assert.True(landed);
        Assert.True(resisted);
        Assert.Equal(rank == 0, chanceFailed);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 7)]
    public void VivianGrantsHerOwnCritBonusAndSeparatesTheTwoAttackBuffs(int awakening, int rank)
    {
        bool testedCrit = false, testedNormal = false;
        for (ulong seed = 1; seed <= 40 && !(testedCrit && testedNormal); seed++)
        {
            var match = Encounter("c1088", seed, rank, awakening: awakening);
            var actor = match.Snapshot.GetUnit("hero");
            var cast = Use(match, "c1088.s1", "enemy1");
            var hit = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage);
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, 1000, actor.MaximumHealth, 12000, 10000,
                0, rank == 0 ? 0 : 3000, 0, hit.HitKind, actor.Stats.CritDamageBp), hit.Amount);
            bool critical = hit.HitKind == BattleHitKind.Critical;
            testedCrit |= critical;
            testedNormal |= !critical;
            var changes = cast.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged).ToArray();
            int normal = rank == 0 ? 1000 : 1500;
            Assert.Equal(awakening >= 3 && critical ? new[] { normal, normal + 1000 } : new[] { normal },
                changes.Select(e => e.Amount));
        }
        Assert.True(testedNormal && testedCrit);
        var spell = Use(Encounter("c1088", 15, rank, awakening: awakening), "c1088.s3");
        foreach (var id in new[] { "hero", "fire", "dark" })
        {
            var unit = spell.Snapshot.GetUnit(id);
            Assert.Equal(3, unit.GetStatus(BattleStatusKind.Immunity).RemainingTurns);
            var buff = Assert.Single(unit.Statuses, s => s.Kind == BattleStatusKind.AttackUp || s.Kind == BattleStatusKind.GreaterAttackUp);
            Assert.Equal(id == "hero" ? BattleStatusKind.GreaterAttackUp : BattleStatusKind.AttackUp, buff.Kind);
            Assert.Equal((id == "hero" ? 7500 : 5000, 3), (buff.Strength, buff.RemainingTurns));
        }
        Assert.DoesNotContain(spell.Events, e => e.Kind == BattleFlowEventKind.ReadinessChanged);
        Assert.Equal(rank == 0 ? 5 : 4, spell.Snapshot.GetUnit("hero").GetCooldown("c1088.s3"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(7, false)]
    [InlineData(7, true)]
    public void VivianInheritsS2EnhancementOnBothReducedStormsAndChargesCooldownOnce(int rank, bool burn)
    {
        var intact = Encounter("c1088", 13, rank, enemyHealth: new[] { 500000, 500000, 500000 });
        var firstOnly = Use(intact, "c1088.s2", burn: burn);
        Assert.DoesNotContain(firstOnly.Events, e => e.Origin == BattleActionOrigin.ExtraAttack);
        int secondHealth = 1 + firstOnly.Events.Single(e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy2").Amount;
        var match = Encounter("c1088", 13, rank, enemyHealth: new[] { 1, secondHealth, 500000 });
        var actor = match.Snapshot.GetUnit("hero");
        var cast = Use(match, "c1088.s2", burn: burn);
        var actions = cast.Events.Where(e => e.Kind == BattleFlowEventKind.Action).ToArray();
        Assert.Equal(new[] { BattleActionOrigin.Primary, BattleActionOrigin.ExtraAttack, BattleActionOrigin.ExtraAttack },
            actions.Select(e => e.Origin));
        Assert.All(actions, e => Assert.Equal("c_vivian_skill_02", e.PresentationId));
        var hits = cast.Events.Where(e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy3").ToArray();
        var rates = new[] { burn ? 13000 : 10500, 5500, 3000 };
        Assert.Equal(3, hits.Length);
        for (int stage = 0; stage < hits.Length; stage++)
            Assert.Equal(EpicSevenCombatMath.Damage(actor.Stats.Attack, 1000, actor.MaximumHealth, rates[stage],
                9000, 0, rank == 0 ? 0 : 4000, 0, hits[stage].HitKind, actor.Stats.CritDamageBp), hits[stage].Amount);
        Assert.Equal(burn ? 46 : 56, cast.Snapshot.AllySoul);
        Assert.Equal(new[] { 2, 1 }, cast.Events.Where(e => e.Kind == BattleFlowEventKind.CooldownChanged).Select(e => e.Amount));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    private static readonly BattleCatalogData Catalog = Read<BattleCatalogData>("AstralOath.BattleCatalog");
    private static readonly ArtifactTemplate Aurius = new ArtifactCatalog(Read<ArtifactCatalogData>("AstralOath.ArtifactCatalog"))
        .Templates.Single(a => a.Effect == "aurius");

    private static T Read<T>(string resource)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)!;
    }

    private static BattleMatch Encounter(string hero, ulong seed, int rank = 0, bool aurius = false,
        bool resistPenetration = false, int[]? enemyHealth = null, int awakening = 3, bool defenseUp = false)
    {
        const string probe = "audit.probe";
        const string setup = "audit.setup";
        var rows = new List<BattleCatalogData.EffectRow>
        {
            new() { id = probe + ".hit", skillId = probe, order = 1, phase = "impact", operation = "damage",
                recipient = "targets", chanceBp = 10000 }
        };
        if (resistPenetration)
            rows.Add(new() { id = setup + ".153", skillId = setup, order = 1, phase = "battle_start", operation = "apply_status",
                recipient = "self", status = "PenetrationResistanceUp", value = 5000, turns = 3, chanceBp = 10000 });
        if (defenseUp)
            rows.Add(new() { id = setup + ".defense", skillId = setup, order = rows.Count, phase = "battle_start", operation = "apply_status",
                recipient = "self", status = "DefenseUp", value = 6000, turns = 3, chanceBp = 10000 });
        var data = new BattleCatalogData
        {
            schemaVersion = Catalog.schemaVersion,
            skills = Catalog.skills.Concat(new[] {
                new BattleCatalogData.SkillRow { id = probe, slot = 1, target = "one_enemy", attackRateBp = 5000, powerBp = 10000 },
                new BattleCatalogData.SkillRow { id = setup, slot = 2, target = "passive" }
            }).ToArray(),
            effects = Catalog.effects.Concat(rows).ToArray(),
            enhancements = Catalog.enhancements
        };
        var skills = data.skills.Where(s => s.heroId == hero).OrderBy(s => s.slot).Select(s => s.id).ToArray();
        var steps = Enumerable.Range(1, 6).Select(n => new HeroAwakeningStep(n, 0, 0)).ToArray();
        var source = Catalog.heroes.Single(h => h.id == hero);
        var actor = new HeroDefinition(hero, hero, Enum.Parse<BattleElement>(source.element, true), source.heroClass, source.zodiac,
            new BattleUnitForm("normal", hero, skills),
            new HeroGrowthPreset("attacker", 5, 120000, 300000, 60000, 120000), steps);
        var units = new List<BattleUnit> { BattleUnit.CreateHero("hero", actor, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening) };
        BattleUnit Monster(string id, BattleSide side, int slot, BattleElement element, int health, int speed)
        {
            var form = new BattleUnitForm("normal", id, id == "enemy1" && (resistPenetration || defenseUp) ? new[] { probe, setup } : new[] { probe });
            return BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, element, form),
                new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(100, health, 1000, speed));
        }
        units.Add(Monster("fire", BattleSide.Ally, 2, BattleElement.Fire, 500000, 1));
        units.Add(Monster("dark", BattleSide.Ally, 3, BattleElement.Dark, 500000, 1));
        var health = enemyHealth ?? new[] { 500000 };
        for (int i = 0; i < health.Length; i++)
        {
            if (i == 0 && aurius)
            {
                // Equipping the victim excludes damage sharing while exercising the real team defense aura.
                var holder = new HeroDefinition("guard", "guard", BattleElement.Dark, Aurius.HeroClass, "lion",
                    new BattleUnitForm("normal", "guard", resistPenetration || defenseUp ? new[] { probe, setup } : new[] { probe }),
                    new HeroGrowthPreset("guard", 5, 60000, 900000, 100000, 115000), steps);
                units.Add(BattleUnit.CreateHero("enemy1", holder, new BattleSlot(BattleSide.Enemy, 1), 60, 6, 6, Aurius));
            }
            else units.Add(Monster("enemy" + (i + 1), BattleSide.Enemy, i + 1, BattleElement.Dark, health[i], i == 0 ? 115 : 1));
        }
        return new BattleMatch("audit", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(units),
            new BattleSkillResolver(data, enhancementRank: (_, skill) => Math.Min(rank, data.enhancements.Count(e => e.skillId == skill)),
                randomDualChanceBp: 0), new BattleFlowRules(initialAllySoul: 50, openingReadinessMaxBp: 0));
    }

    private static BattleCommandResult Use(BattleMatch match, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        var seq = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, seq, "audit" + seq,
            state.CurrentActorId, skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
