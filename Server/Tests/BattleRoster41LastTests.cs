using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster41LastTests
{
    [Theory]
    [InlineData("MagicNail")]
    [InlineData("Sleep")]
    [InlineData("Restrict")]
    [InlineData("Bind")]
    [InlineData("SkillNullifier")]
    [InlineData("Vigor")]
    public void NewStatusesHaveStableWireValuesAfterThePublishedRoster(string name)
    {
        Assert.True(Enum.TryParse<BattleStatusKind>(name, out var status));
        Assert.True((int)status > (int)BattleStatusKind.VampiricTouch);
    }

    [Fact]
    public void AUnitThatStartsItsTurnAsleepSkipsAndExpiresSleep()
    {
        var skills = new[] {
            new BattleCatalogData.SkillRow { id="hit", heroId="fixture", slot=1, target="one_enemy", attackRateBp=10000, powerBp=10000 },
            new BattleCatalogData.SkillRow { id="sleep", heroId="fixture", slot=2, target="passive" }
        };
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2,
            skills = skills,
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>(),
            effects = new[] {
                new BattleCatalogData.EffectRow { id="hit.1", skillId="hit", phase="impact", operation="damage", order=1, recipient="targets", chanceBp=10000 },
                new BattleCatalogData.EffectRow { id="sleep.1", skillId="sleep", phase="battle_start", operation="apply_status",
                    order=1, recipient="self", status="Sleep", turns=1, chanceBp=10000 }
            }
        };
        BattleUnit Unit(string id, BattleSide side, int speed, params string[] abilities) => BattleUnit.CreateMonster(
            id, new MonsterDefinition(id, id, BattleElement.Fire, new BattleUnitForm("normal", id, abilities)),
            new BattleSlot(side, 1), 60, MonsterRank.Normal, new BattleStats(1000, 10000, 300, speed));
        var match = new BattleMatch("sleep", 1, 12, new BattleFormation(new[] {
            Unit("sleeper",BattleSide.Ally,110,"hit","sleep"),Unit("other",BattleSide.Enemy,100,"hit")}),
            new BattleSkillResolver(catalog), new BattleFlowRules(openingReadinessMaxBp: 0));
        Assert.Contains(match.OpeningEvents, e => e.Kind == BattleFlowEventKind.TurnSkipped && e.SourceId == "sleeper" &&
            e.StatusKind == BattleStatusKind.Sleep);
        Assert.Null(match.Snapshot.GetUnit("sleeper").GetStatus(BattleStatusKind.Sleep));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ChloeBuffsBeforeImpactAndOnlyAwakenedNailBypassesResistance(int awakening, bool marked)
    {
        var match = Match("c1049", awakening, resistance: 30000);
        var plant = Cast(match, "c1049.s2", "enemy1");
        var buff = Assert.Single(plant.Events, e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.AttackUp);
        var hit = Assert.Single(plant.Events, e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "hero");
        Assert.True(buff.Sequence < hit.Sequence);
        Assert.Equal(marked, plant.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.MagicNail) != null);
        var finish = Cast(match, "c1049.s3", "enemy1");
        Assert.Equal(marked, finish.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Stun) != null);
        Assert.Equal(marked ? 2 : 1, finish.Events.Count(e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1"));
        if (marked)
        {
            decimal attack = plant.Snapshot.GetUnit("hero").Stats.Attack * 1.5m;
            decimal defense = plant.Snapshot.GetUnit("enemy1").Stats.Defense * 1.6m;
            decimal nail = Math.Round(attack * .8m, MidpointRounding.AwayFromZero);
            int expected = (int)Math.Round(nail * 1.871m / (1m + defense * .3m / 300m), MidpointRounding.AwayFromZero);
            Assert.Equal(expected, Assert.Single(finish.Events, e => e.Kind == BattleFlowEventKind.Damage &&
                e.TargetId == "enemy1" && e.HitKind == BattleHitKind.None).Amount);
        }
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void TenebriaGainsReadinessPerSuccessfulSleepAndNeverStartsADual(int awakening, bool gains)
    {
        var counts = new HashSet<int>();
        for (ulong seed = 1; seed <= 28; seed++)
        {
            var result = Cast(Match("c1050", awakening, seed: seed, enemyCount: 2, dualChance: 10000), "c1050.s1", "enemy1");
            int sleeps = result.Events.Count(e => e.Kind == BattleFlowEventKind.StatusApplied && e.StatusKind == BattleStatusKind.Sleep);
            counts.Add(sleeps);
            Assert.Equal(gains ? 2500 * sleeps : 0, result.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged &&
                e.SourceId == "hero" && e.TargetId == "hero").Sum(e => e.Amount));
            Assert.Equal(2, result.Events.Count(e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "hero"));
            Assert.DoesNotContain(result.Events, e => e.Kind == BattleFlowEventKind.Action && e.Origin == BattleActionOrigin.DualAttack);
        }
        Assert.Equal(new[] { 0, 1, 2 }, counts.OrderBy(x => x));
    }

    [Fact]
    public void DamageToSleepingEnemyCriticallyHitsAndWakesItBeforeTheFollowingDebuffs()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var match = Match("c1050", 3, seed: seed);
            var first = Cast(match, "c1050.s1", "enemy1");
            if (first.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Sleep) == null) continue;
            var cast = Cast(match, "c1050.s2");
            var damage = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
            var wake = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.StatusRemoved && e.StatusKind == BattleStatusKind.Sleep);
            Assert.Equal(BattleHitKind.Critical, damage.HitKind);
            Assert.True(damage.Sequence < wake.Sequence);
            Assert.Null(cast.Snapshot.GetUnit("enemy1").GetStatus(BattleStatusKind.Sleep));
            return;
        }
        Assert.Fail("No seeded sleep reached the follow-up attack.");
    }

    [Theory]
    [InlineData(2, false, 1, 3)]
    [InlineData(3, false, 2, 3)]
    [InlineData(3, true, 2, 4)]
    public void RinHealsAndDrawsDistinctOriginalBuffsForEachAlly(int awakening, bool burn, int count, int turns)
    {
        var match = Match("c1054", awakening);
        var cast = Cast(match, "c1054.s3", burn: burn);
        foreach (string target in new[] { "hero", "ally" })
        {
            var heal = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == target);
            var buffs = cast.Events.Where(e => e.Kind == BattleFlowEventKind.StatusApplied && e.TargetId == target).ToArray();
            Assert.Equal(count, buffs.Select(e => e.StatusKind).Distinct().Count());
            Assert.All(buffs, e => { Assert.True(e.Sequence > heal.Sequence); Assert.Equal(turns, e.Status!.RemainingTurns); });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BasarAwakenedSandstormBurnBypassesResistancePerRecipient(bool burn)
    {
        var match = Match("c1053", 3, resistance: 30000, enemyCount: 2);
        var cast = Cast(match, "c1053.s3", burn: burn);
        foreach (string target in new[] { "enemy1", "enemy2" })
        {
            Assert.Equal(burn, cast.Snapshot.GetUnit(target).GetStatus(BattleStatusKind.Unbuffable) != null);
            Assert.Equal(!burn, cast.Snapshot.GetUnit(target).GetStatus(BattleStatusKind.DefenseUp) != null);
        }
    }

    [Fact]
    public void BasarCanAddARandomDebuffOnlyAfterTransferringHisOwn()
    {
        var noTransfer = Cast(Match("c1053", 3), "c1053.s1", "enemy1");
        Assert.DoesNotContain(noTransfer.Events, e => e.Kind == BattleFlowEventKind.StatusApplied && e.TargetId == "enemy1");
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var match = Match("c1053", 3, seed: seed, enemySpeed: 125, enemyDebuffs: true);
            var enemy = Cast(match, Basic, "hero");
            if (enemy.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.HitChanceDown) == null) continue;
            var cast = Cast(match, "c1053.s1", "enemy1");
            var applied = cast.Events.Where(e => e.Kind == BattleFlowEventKind.StatusApplied && e.TargetId == "enemy1").ToArray();
            if (applied.Length < 2) continue;
            Assert.Equal(BattleStatusKind.HitChanceDown, applied[0].StatusKind);
            Assert.Contains(applied[1].StatusKind!.Value, new[] { BattleStatusKind.AttackDown,BattleStatusKind.DefenseDown,
                BattleStatusKind.Unhealable,BattleStatusKind.Silence });
            Assert.Null(cast.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.HitChanceDown));
            return;
        }
        Assert.Fail("No seeded transfer applied its follow-up random debuff.");
    }

    [Fact]
    public void RinCanFollowHerSecondSkillWithOneBasicAttackAndItsSoulGain()
    {
        bool sawFollow = false, sawStandalone = false;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var cast = Cast(Match("c1054", 3, seed: seed), "c1054.s2", "enemy1");
            int echoes = cast.Events.Count(e => e.Kind == BattleFlowEventKind.Action && e.Origin == BattleActionOrigin.ExtraAttack && e.SkillId == "c1054.s1");
            Assert.InRange(echoes, 0, 1);
            Assert.Equal(1 + echoes, cast.Events.Count(e => e.Kind == BattleFlowEventKind.Damage && e.SourceId == "hero"));
            Assert.Equal(32 + echoes, cast.Snapshot.AllySoul);
            sawFollow |= echoes == 1; sawStandalone |= echoes == 0;
        }
        Assert.True(sawFollow && sawStandalone);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void JenuaSplashOccursWithoutAKillAndDoesNotEnterTheAwakenedLifestealBase(int awakening, bool heals)
    {
        var cast = Cast(Match("c1055", awakening, enemyCount: 2), "c1055.s3", "enemy1");
        var primary = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1");
        var splash = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy2");
        Assert.True(primary.Sequence < splash.Sequence && primary.HitKind != BattleHitKind.None && splash.HitKind == BattleHitKind.None);
        var healing = cast.Events.Where(e => e.Kind == BattleFlowEventKind.Healing && e.TargetId == "hero").ToArray();
        if (heals) Assert.Equal((int)Math.Round(primary.Amount * .25m, MidpointRounding.AwayFromZero), Assert.Single(healing).Amount);
        else Assert.Empty(healing);
        Assert.DoesNotContain(cast.Events, e => e.Kind == BattleFlowEventKind.UnitExtinct);
    }

    [Fact]
    public void JenuaMissDoesNotPlantTheAdditionalDamageOnOtherEnemies()
    {
        var cast = Cast(Match("c1055", 3, enemyCount: 2, enemyEvades: true), "c1055.s3", "enemy1");
        Assert.Equal(BattleHitKind.Miss, Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy1").HitKind);
        Assert.DoesNotContain(cast.Events, e => e.Kind == BattleFlowEventKind.Damage && e.TargetId == "enemy2");
    }

    [Fact]
    public void JenuaOwnTurnBasicCanTriggerOnlyOnceWhenItComesFromAnotherSkill()
    {
        var cast = Cast(Match("c1055", 3, additionalEnragedBasics: true), "c1055.s3", "enemy1");
        Assert.Equal(2, cast.Events.Count(e => e.Kind == BattleFlowEventKind.Action && e.SkillId == "c1055.s1"));
        var extra = Assert.Single(cast.Events, e => e.Kind == BattleFlowEventKind.Action && e.SkillId == "c1055.s3" && e.Origin == BattleActionOrigin.ExtraAttack);
        Assert.Equal("c_jenua_skill_02", extra.PresentationId);
    }

    [Fact]
    public void JenuaSurvivesLethalDamageOnlyOnceAndPlanAHasItsOwnCooldown()
    {
        var match = Match("c1055", 3, enemySpeed: 185, enemyAttack: 80000);
        var first = Cast(match, Basic, "hero");
        var hero = first.Snapshot.GetUnit("hero");
        Assert.Equal("c_jenua_skill_03", Assert.Single(first.Events, e => e.Kind == BattleFlowEventKind.Action && e.SkillId == "c1055.s2").PresentationId);
        Assert.True(hero.LethalProtectionConsumed);
        Assert.InRange(hero.CurrentHealth, 1, hero.MaximumHealth / 100 + 1);
        Assert.NotNull(hero.GetStatus(BattleStatusKind.Immortality));
        Assert.NotNull(hero.GetStatus(BattleStatusKind.Rage));
        Assert.Equal(5, hero.GetCooldown("c1055.s2"));
        var reply = Cast(match, "c1055.s1", "enemy1");
        Assert.Equal("c_jenua_skill_02", Assert.Single(reply.Events, e => e.Kind == BattleFlowEventKind.Action && e.SkillId == "c1055.s3" && e.Origin == BattleActionOrigin.ExtraAttack).PresentationId);
        var second = Cast(match, Basic, "hero");
        Assert.False(second.Snapshot.GetUnit("hero").IsAlive);
        Assert.DoesNotContain(second.Events, e => e.Kind == BattleFlowEventKind.LethalProtectionConsumed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LidicaBindingPreventsWoundedJenuaFromCastingPlanAOrSpendingItsCooldown(bool applyBind)
    {
        var match = Match("c1055", 3, enemySpeed: 185, lidicaAgainstWoundedHero: true);
        var incoming = applyBind ? Cast(match, "c1046.s2", burn: true) : Cast(match, Basic, "hero");
        var hero = incoming.Snapshot.GetUnit("hero");
        Assert.True(hero.IsAlive && hero.CurrentHealth * 2 <= hero.MaximumHealth);
        Assert.Equal(applyBind, hero.GetStatus(BattleStatusKind.Bind) != null);
        Assert.Equal(!applyBind, incoming.Events.Any(e => e.Kind == BattleFlowEventKind.Action &&
            e.SourceId == "hero" && e.SkillId == "c1055.s2" && e.PresentationId == "c_jenua_skill_03"));
        Assert.Equal(applyBind ? 0 : 5, hero.GetCooldown("c1055.s2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RomannRestrictAllowsNaturalTurnsAndPushbackButBlocksSkillReadinessGain(bool restrict)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var form = new BattleUnitForm("standard", "c1043", new[] { "c1043.s1", "c1043.s2", "c1043.s3" });
        var definition = new MonsterDefinition("c1043", "c1043", BattleElement.Ice, form);
        BattleUnit Unit(string id, BattleSide side, int speed) => BattleUnit.CreateMonster(id, definition,
            new BattleSlot(side, 1), 60, MonsterRank.Normal, new BattleStats(500, 50000, 300, speed));
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var match = new BattleMatch("restrict", BattlePreviewFactory.RulesVersion, seed,
                new BattleFormation(new[] { Unit("first", BattleSide.Ally, 100), Unit("second", BattleSide.Enemy, 85) }),
                new BattleSkillResolver(catalog, randomDualChanceBp: 0), new BattleFlowRules(openingReadinessMaxBp: 0));
            var open = restrict ? Cast(match, "c1043.s3") : Cast(match, "c1043.s1", "second");
            if (restrict && open.Snapshot.GetUnit("second").GetStatus(BattleStatusKind.Restrict) == null) continue;
            Assert.Equal("second", open.Snapshot.CurrentActorId);
            Assert.DoesNotContain(open.Events, e => e.Kind == BattleFlowEventKind.ExtraTurnGranted && e.TargetId == "second");
            var selfPush = Cast(match, "c1043.s1", "first");
            Assert.Equal(restrict ? 0 : 800, selfPush.Events.Where(e => e.Kind == BattleFlowEventKind.ReadinessChanged &&
                e.SourceId == "second" && e.TargetId == "second").Sum(e => e.Amount));
            Assert.Equal("first", selfPush.Snapshot.CurrentActorId);
            Assert.Equal(restrict, selfPush.Snapshot.GetUnit("second").GetStatus(BattleStatusKind.Restrict) != null);
            var enemyPush = Cast(match, "c1043.s1", "second");
            if (enemyPush.Events.Any(e => e.Kind == BattleFlowEventKind.EffectRejected && e.TargetId == "second")) continue;
            var reduced = Assert.Single(enemyPush.Events, e => e.Kind == BattleFlowEventKind.ReadinessChanged &&
                e.SourceId == "first" && e.TargetId == "second");
            Assert.Equal(selfPush.Snapshot.GetUnit("second").ReadinessBp - 800, reduced.Amount);
            return;
        }
        Assert.Fail("No seeded battle landed both Restrict and the follow-up pushback.");
    }

    private const string Basic = "test.enemy.s1";

    private static BattleMatch Match(string id, int awakening, ulong seed = 17, int resistance = 0,
        int enemyCount = 1, int enemySpeed = 1, int enemyAttack = 50, bool enemyDebuffs = false,
        int dualChance = 0, bool enemyEvades = false, bool additionalEnragedBasics = false, bool lidicaAgainstWoundedHero = false)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var hero = catalog.heroes.Single(row => row.id == id);
        var growth = catalog.growthPresets.Single(row => row.id == hero.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(row => row.heroId == id).Select(step => new HeroAwakeningStep(step.rank,
            step.attackFlat, step.healthFlat, step.attackPercentBp, step.healthPercentBp, step.defensePercentBp,
            step.speedFlat, step.critChanceBp, step.critDamageBp, step.effectivenessBp, step.effectResistanceBp));
        var form = new BattleUnitForm(hero.variant, id, catalog.skills.Where(skill => skill.heroId == id).OrderBy(s => s.slot).Select(s => s.id));
        var definition = new HeroDefinition(id, hero.nameKey, Enum.Parse<BattleElement>(hero.element, true), hero.heroClass, hero.zodiac, form, preset, steps);
        var actor = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakening);
        actor.ApplyDamage(lidicaAgainstWoundedHero ? actor.CurrentHealth / 2 : 1000);
        const string buff = "test.enemy.buffs";
        catalog.skills = catalog.skills.Concat(new[] { new BattleCatalogData.SkillRow {
            id=Basic, slot=1, heroId="enemy", target="one_enemy", attackRateBp=10000, powerBp=10000 },
            new BattleCatalogData.SkillRow { id=buff, slot=2, heroId="enemy", target="passive" }}).ToArray();
        catalog.effects = catalog.effects.Concat(new[] { new BattleCatalogData.EffectRow { id=Basic+".1", skillId=Basic,
            order=1, phase="impact", operation="damage", recipient="targets", chanceBp=10000 },
            new BattleCatalogData.EffectRow { id=buff+".1", skillId=buff, order=1, phase="battle_start", operation="apply_status",
                recipient="self", status="DefenseUp", turns=5, value=6000, chanceBp=10000 } }).ToArray();
        if (enemyDebuffs)
        {
            catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow
            {
                id = Basic + ".2",
                skillId = Basic,
                order = 2,
                phase = "after_hit",
                operation = "apply_status",
                recipient = "targets",
                status = "HitChanceDown",
                value = 5000,
                turns = 4,
                chanceBp = 10000
            }).ToArray();
        }
        if (enemyEvades)
        {
            catalog.effects = catalog.effects.Append(new BattleCatalogData.EffectRow {
                id=buff+".2", skillId=buff, order=2, phase="battle_start", operation="apply_status",
                recipient="self", status="EvasionUp", value=10000, turns=4, chanceBp=10000 }).ToArray();
        }
        if (additionalEnragedBasics)
        {
            string skill = id + ".s3";
            int order = catalog.effects.Count(row => row.skillId == skill);
            catalog.effects = catalog.effects.Concat(new[] {
                new BattleCatalogData.EffectRow { id=skill+".rage", skillId=skill, order=++order, phase="battle_start",
                    operation="apply_status", recipient="self", status="Rage", value=2000, turns=3, chanceBp=10000 },
                new BattleCatalogData.EffectRow { id=skill+".extra1", skillId=skill, order=++order, phase="after_attack",
                    operation="trigger_skill", recipient="self", value=1, chanceBp=10000 },
                new BattleCatalogData.EffectRow { id=skill+".extra2", skillId=skill, order=++order, phase="after_attack",
                    operation="trigger_skill", recipient="self", value=1, chanceBp=10000 }
            }).ToArray();
        }
        var monster = new MonsterDefinition("enemy", "enemy", BattleElement.Dark, new BattleUnitForm("normal", "enemy",
            lidicaAgainstWoundedHero ? new[] { Basic, "c1046.s2" } : new[] { Basic, buff }));
        var ally = BattleUnit.CreateMonster("ally", new MonsterDefinition("ally", "ally", BattleElement.Dark,
            new BattleUnitForm("normal", "ally", new[] { Basic })), new BattleSlot(BattleSide.Ally, 2), 60, MonsterRank.Normal,
            new BattleStats(50, 50000, 300, 1));
        ally.ApplyDamage(15000);
        var enemies = Enumerable.Range(1, enemyCount).Select(slot => BattleUnit.CreateMonster("enemy" + slot, monster,
            new BattleSlot(BattleSide.Enemy, slot), 60, MonsterRank.Normal,
            new BattleStats(enemyAttack, 50000, 300, enemySpeed, effectResistanceBp: resistance)));
        return new BattleMatch("authority", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[] { actor, ally }.Concat(enemies)),
            new BattleSkillResolver(catalog, randomDualChanceBp: dualChance),
            new BattleFlowRules(initialAllySoul: 30, initialEnemySoul: 30, openingReadinessMaxBp: 0));
    }

    private static BattleCommandResult Cast(BattleMatch match, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence, "step" + sequence,
            state.CurrentActorId, skill, target, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
