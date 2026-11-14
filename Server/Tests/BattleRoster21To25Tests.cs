using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRoster21To25Tests
{
    [Theory]
    [InlineData("c1020", 1068, 5650, 536, 110)]
    [InlineData("c1021", 957, 5057, 592, 112)]
    [InlineData("c1022", 621, 5474, 802, 102)]
    [InlineData("c1023", 1119, 5340, 483, 129)]
    [InlineData("c1024", 1158, 6002, 553, 112)]
    public void FullAwakeningMatchesBareHeroStats(string heroId, int attack, int health, int defense, int speed)
    {
        var unit = Hero(Catalog(), heroId);
        Assert.Equal((attack, health, defense, speed),
            (unit.Stats.Attack, unit.Stats.MaxHealth, unit.Stats.Defense, unit.Stats.Speed));
    }

    [Fact]
    public void SchuriCriticalFillsFocusAndSwitchesSourceGraphAndResistanceRule()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1020");
        var target = Monster("enemy", BattleSide.Enemy, 1, resistance: 10000);
        var ally = Monster("ally", BattleSide.Ally, 2);
        var secondTarget = Monster("other", BattleSide.Enemy, 2, resistance: 10000);
        var basic = catalog.skills.Single(skill => skill.id == "c1020.s1");
        basic.alwaysCritical = true;
        catalog.skills.Single(skill => skill.id == "c1020.s3").alwaysCritical = true;
        var match = Match(catalog, new[] { actor, target, secondTarget, ally });
        Assert.Equal(3, match.Snapshot.GetUnit("hero").Resource.Amount);

        var unchargedMatch = Match(catalog, new[] { actor, target, secondTarget, ally });
        var uncharged = Cast(unchargedMatch, "hero", "c1020.s3");
        Assert.Equal("c_schuri_skill_02", Action(uncharged, "c1020.s3").PresentationId);
        Assert.Contains(uncharged.Events, item => item.TargetId == "enemy" &&
            item.EffectRejection == BattleEffectRejection.Resisted);
        Assert.Equal(3, uncharged.Snapshot.GetUnit("hero").Resource.Amount);

        int allyOpeningReadiness = match.Snapshot.GetUnit("ally").ReadinessBp;
        var critical = Cast(match, "hero", "c1020.s1", "enemy");
        Assert.Equal(5, critical.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.Contains(critical.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.SourceId == "hero" && item.TargetId == "ally" && item.Amount == 1500 + allyOpeningReadiness);

        int beforeChargedReadiness = match.Snapshot.GetUnit("ally").ReadinessBp;
        var charged = Cast(match, "hero", "c1020.s3", burn: true);
        Assert.Equal("c_schuri_skill_03", Action(charged, "c1020.s3").PresentationId);
        Assert.Equal(0, charged.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.Contains(charged.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.SourceId == "hero" && item.TargetId == "ally" && item.Amount == beforeChargedReadiness + 3000);
        Assert.DoesNotContain(charged.Events, item => item.TargetId == "enemy" &&
            item.EffectRejection == BattleEffectRejection.Resisted);
        Assert.Contains(charged.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted);
    }

    [Fact]
    public void DingoFeastAwakeningGrantsTurnAndEnemyDefeatReducesItsCooldown()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1021");
        var victim = Monster("victim", BattleSide.Enemy, 1, health: 1);
        var other = Monster("other", BattleSide.Enemy, 2);
        var ally = Monster("ally", BattleSide.Ally, 2);
        var match = Match(catalog, new[] { actor, victim, other, ally });

        var feast = Cast(match, "hero", "c1021.s3");
        Assert.Contains(feast.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted);
        Assert.NotNull(feast.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.CriticalChanceUp));
        var attack = Cast(match, "hero", "c1021.s1", "victim");
        var defeat = Assert.Single(attack.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated &&
            item.TargetId == "victim");
        Assert.Contains(attack.Events, item => item.Sequence > defeat.Sequence &&
            item.Kind == BattleFlowEventKind.CooldownChanged && item.TargetId == "hero" && item.SkillId == "c1021.s3");
    }

    [Fact]
    public void DingoFollowUpBurnsOnlyBleedingTargets()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1021");
        var enemy = Monster("enemy", BattleSide.Enemy, 1);
        var other = Monster("other", BattleSide.Enemy, 2);
        var result = Enumerable.Range(1, 256).Select(seed =>
        {
            var match = Match(catalog, new[] { actor, enemy, other }, (ulong)seed);
            return Cast(match, "hero", "c1021.s1", "enemy");
        }).First(item => item.Events.Any(evt => evt.Kind == BattleFlowEventKind.StatusApplied &&
            evt.TargetId == "enemy" && evt.StatusKind == BattleStatusKind.Burn));

        var bleed = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "enemy" && item.StatusKind == BattleStatusKind.Bleed);
        var followUp = Action(result, "c1021.s2");
        Assert.Equal(BattleActionOrigin.ExtraAttack, followUp.Origin);
        Assert.True(bleed.Sequence < followUp.Sequence);
        Assert.Null(result.Snapshot.GetUnit("other").GetStatus(BattleStatusKind.Burn));
    }

    [Fact]
    public void RueleRevivesFallenAllyBeforeInvincibility()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1022");
        var ally = Monster("ally", BattleSide.Ally, 2);
        ally.ApplyDamage(ally.CurrentHealth);
        var enemy = Monster("enemy", BattleSide.Enemy, 1);
        var match = Match(catalog, new[] { actor, ally, enemy });

        Assert.NotNull(match.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.SpiritlordProtection));
        Assert.Equal("ally", new BattleAiPolicy(catalog).ChooseTarget(match.Snapshot, "c1022.s3").TargetId);
        var result = Cast(match, "hero", "c1022.s3", "ally");
        var revive = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.UnitRevived && item.TargetId == "ally");
        var buff = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "ally" && item.StatusKind == BattleStatusKind.Invincible);
        Assert.True(revive.Sequence < buff.Sequence);
        Assert.Equal(ally.Stats.MaxHealth, result.Snapshot.GetUnit("ally").CurrentHealth);
    }

    [Fact]
    public void RueleSelfReviveConsumesProtectionAndRestoresHalfHealth()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1022");
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 500, attack: 100000);
        var match = Match(catalog, new[] { actor, enemy });

        var result = Cast(match, "enemy", BattlePreviewFactory.SlimeSkillId, "hero");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.UnitRevived && item.TargetId == "hero");
        var hero = result.Snapshot.GetUnit("hero");
        Assert.Equal((int)Math.Round(actor.Stats.MaxHealth * .5m, MidpointRounding.AwayFromZero), hero.CurrentHealth);
        Assert.Null(hero.GetStatus(BattleStatusKind.SpiritlordProtection));
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "hero" && item.Amount == 5000);
    }

    [Fact]
    public void RueleSelfRevivalFromTurnStartDamageReturnsToReadinessOrder()
    {
        const string poisonId = "setup.poison";
        var catalog = Catalog();
        AddEnemySkill(catalog, poisonId, new[] { Effect(poisonId, 1, "apply_status", "Poison", 2, 500) });
        var actor = Hero(catalog, "c1022");
        actor.ApplyDamage(actor.CurrentHealth - 100);
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 110, skill: poisonId);
        var other = Monster("other", BattleSide.Enemy, 2, speed: 85);
        var result = Enumerable.Range(1, 32).Select(seed =>
            Cast(Match(catalog, new[] { actor, enemy, other }, (ulong)seed), "enemy", poisonId, "hero"))
            .First(item => item.Events.Any(evt => evt.Kind == BattleFlowEventKind.UnitRevived && evt.TargetId == "hero"));

        Assert.Equal("other", result.Snapshot.CurrentActorId);
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "hero" && item.Amount == 5000);
    }

    [Theory]
    [InlineData(false, 500, "c_ruell_skill_01")]
    [InlineData(true, 3500, "c_ruell_skill_01_2")]
    public void RueleBasicHealsEveryAllyAndUsesTheBurnGraph(bool burn, int rate, string graph)
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1022");
        var ally = Monster("ally", BattleSide.Ally, 2);
        var enemy = Monster("enemy", BattleSide.Enemy, 1);
        actor.ApplyDamage(3000);
        ally.ApplyDamage(3000);
        var match = Match(catalog, new[] { actor, ally, enemy });

        var result = Cast(match, "hero", "c1022.s1", "enemy", burn);
        Assert.Equal(graph, Action(result, "c1022.s1").PresentationId);
        var healing = result.Events.Where(item => item.Kind == BattleFlowEventKind.Healing).ToArray();
        Assert.Equal(new[] { "hero", "ally" }, healing.Select(item => item.TargetId));
        int expected = (int)Math.Round(actor.Stats.MaxHealth * rate / 10000m, MidpointRounding.AwayFromZero);
        Assert.All(healing, item => Assert.Equal(expected, item.Amount));
    }

    [Fact]
    public void RuelePillarLimitsDamageToTargetMaximumWhileGrantingCasterHealthBarrier()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1022");
        var ally = Monster("ally", BattleSide.Ally, 2);
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 100, attack: 100000);
        var match = Match(catalog, new[] { actor, ally, enemy });

        var pillar = Cast(match, "hero", "c1022.s2", "ally");
        var protectedAlly = pillar.Snapshot.GetUnit("ally");
        Assert.Equal((int)Math.Round(actor.Stats.MaxHealth * .2m, MidpointRounding.AwayFromZero), protectedAlly.Barrier);
        Assert.Equal(3500, protectedAlly.GetStatus(BattleStatusKind.DamageLimit).Strength);

        var attack = Cast(match, "enemy", BattlePreviewFactory.SlimeSkillId, "ally");
        var hit = Assert.Single(attack.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "ally");
        Assert.InRange(hit.Amount, 1, ally.Stats.MaxHealth * 35 / 100);
        Assert.True(attack.Snapshot.GetUnit("ally").IsAlive);
    }

    [Fact]
    public void DamageLimitAppliesOncePerAttackEvenWithMultipleDamageRows()
    {
        const string attackId = "setup.multi_hit";
        var catalog = Catalog();
        AddEnemySkill(catalog, attackId, new[]
        {
            Effect(attackId, 1, "damage"),
            Effect(attackId, 2, "damage")
        });
        var ruele = Hero(catalog, "c1022");
        var ally = Monster("ally", BattleSide.Ally, 2);
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 100, attack: 100000, skill: attackId);
        var match = Match(catalog, new[] { ruele, ally, enemy });
        var pillar = Cast(match, "hero", "c1022.s2", "ally");
        Assert.True(pillar.Snapshot.GetUnit("ally").Barrier > 0);

        var result = Cast(match, "enemy", attackId, "ally");
        int incoming = result.Events.Where(item => item.TargetId == "ally" &&
            (item.Kind == BattleFlowEventKind.Damage || item.Kind == BattleFlowEventKind.BarrierAbsorbed))
            .Sum(item => item.Amount);
        Assert.Equal(ally.Stats.MaxHealth * 35 / 100, incoming);
    }

    [Fact]
    public void KayronLethalProtectionTriggersOnTurnStartPoison()
    {
        const string poisonId = "setup.poison";
        var catalog = Catalog();
        AddEnemySkill(catalog, poisonId, new[] { Effect(poisonId, 1, "apply_status", "Poison", 2, 500) });
        var actor = Hero(catalog, "c1023");
        actor.ApplyDamage(actor.CurrentHealth - 100);
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 130, skill: poisonId);
        var result = Enumerable.Range(1, 32).Select(seed =>
        {
            var match = Match(catalog, new[] { actor, enemy }, (ulong)seed);
            return Cast(match, "enemy", poisonId, "hero");
        }).First(item => item.Events.Any(evt => evt.Kind == BattleFlowEventKind.StatusApplied &&
            evt.TargetId == "hero" && evt.StatusKind == BattleStatusKind.Poison));

        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.Damage &&
            item.TargetId == "hero" && item.StatusKind == BattleStatusKind.Poison);
        var kayron = result.Snapshot.GetUnit("hero");
        Assert.True(kayron.IsAlive);
        Assert.NotNull(kayron.GetStatus(BattleStatusKind.Immortality));
        Assert.Equal(9, kayron.GetCooldown("c1023.s2"));
    }

    [Fact]
    public void KayronSurvivesLethalDamageAndBuffedBasicUsesAreaGraph()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1023");
        var enemy = Monster("enemy", BattleSide.Enemy, 1, speed: 130, attack: 100000);
        var other = Monster("other", BattleSide.Enemy, 2);
        var match = Match(catalog, new[] { actor, enemy, other });
        var hit = Cast(match, "enemy", BattlePreviewFactory.SlimeSkillId, "hero");

        var protectedUnit = hit.Snapshot.GetUnit("hero");
        Assert.InRange(protectedUnit.CurrentHealth, 1, 54);
        Assert.NotNull(protectedUnit.GetStatus(BattleStatusKind.Immortality));
        Assert.NotNull(protectedUnit.GetStatus(BattleStatusKind.EvasionUp));
        Assert.Equal(9, protectedUnit.GetCooldown("c1023.s2"));
        Assert.DoesNotContain(hit.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated && item.TargetId == "hero");

        var slash = Cast(match, "hero", "c1023.s1", burn: true);
        Assert.Equal("c_kylon_skill_02", Action(slash, "c1023.s1").PresentationId);
        Assert.Equal(2, slash.Events.Count(item => item.Kind == BattleFlowEventKind.Damage && item.SourceId == "hero"));
        Assert.DoesNotContain(slash.Events, item => item.Origin == BattleActionOrigin.DualAttack);
    }

    [Fact]
    public void KayronApocalypseExtendsAwakenedAttackBuffOnlyOnKill()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1023");
        var victim = Monster("victim", BattleSide.Enemy, 1, health: 1);
        var other = Monster("other", BattleSide.Enemy, 2);
        var match = Match(catalog, new[] { actor, victim, other });

        var result = Cast(match, "hero", "c1023.s3", "victim");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated && item.TargetId == "victim");
        Assert.Equal(3, result.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.AttackUp).RemainingTurns);

        var noKill = Match(catalog, new[] { actor, other });
        var normal = Cast(noKill, "hero", "c1023.s3", "other");
        Assert.Equal(2, normal.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.AttackUp).RemainingTurns);
    }

    [Fact]
    public void IseriaCannotSelectSelfAndAwakeningGrantsExtraTurn()
    {
        var catalog = Catalog();
        var actor = Hero(catalog, "c1024");
        var ally = Monster("ally", BattleSide.Ally, 2);
        var enemy = Monster("enemy", BattleSide.Enemy, 1);
        var match = Match(catalog, new[] { actor, ally, enemy });
        Assert.False(new BattleAiPolicy(catalog).CanSelectTarget(match.Snapshot, "c1024.s2", "hero"));
        Assert.Equal(BattleCommandStatus.InvalidAction, Submit(match, "hero", "c1024.s2", "hero").Status);

        var oath = Cast(match, "hero", "c1024.s2", "ally");
        Assert.NotNull(oath.Snapshot.GetUnit("ally").GetStatus(BattleStatusKind.GreaterAttackUp));
        Assert.Null(oath.Snapshot.GetUnit("hero").GetStatus(BattleStatusKind.GreaterAttackUp));
        Assert.Contains(oath.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted);
        var blossom = Cast(match, "hero", "c1024.s1", "enemy", burn: true);
        Assert.Equal("c_iseria_skill_01_2", Action(blossom, "c1024.s1").PresentationId);
    }

    private static BattleCatalogData Catalog()
    {
        using var source = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(source, new JsonSerializerOptions { IncludeFields = true })!;
    }

    private static BattleUnit Hero(BattleCatalogData catalog, string id)
    {
        var row = catalog.heroes.Single(hero => hero.id == id);
        var growth = catalog.growthPresets.Single(item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { id + ".s1", id + ".s2", id + ".s3" });
        var definition = new HeroDefinition(row.id, row.nameKey, Enum.Parse<BattleElement>(row.element, true),
            row.heroClass, row.zodiac, form, preset, steps, row.GetResourceDefinition());
        return BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, 6);
    }

    private static void AddEnemySkill(BattleCatalogData catalog, string id, BattleCatalogData.EffectRow[] effects)
    {
        catalog.skills = catalog.skills.Append(new BattleCatalogData.SkillRow
        {
            id = id, heroId = "setup", slot = 1, target = "one_enemy", soulBurnKind = "none",
            attackRateBp = 10000, powerBp = 10000
        }).ToArray();
        catalog.effects = catalog.effects.Concat(effects).ToArray();
    }

    private static BattleCatalogData.EffectRow Effect(string id, int order, string operation, string status = "none",
        int turns = 0, int value = 0) => new()
    {
        id = id + "." + order, skillId = id, order = order, phase = "impact", operation = operation,
        recipient = "targets", status = status, chanceBp = 10000, turns = turns, value = value
    };

    private static BattleUnit Monster(string id, BattleSide side, int slot, int health = 20000, int speed = 1,
        int attack = 25, int resistance = 0, string skill = BattlePreviewFactory.SlimeSkillId) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Dark,
            new BattleUnitForm("standard", id, new[] { skill })), new BattleSlot(side, slot),
            60, MonsterRank.Normal, new BattleStats(attack, health, 300, speed, effectResistanceBp: resistance));

    private static BattleMatch Match(BattleCatalogData catalog, BattleUnit[] units, ulong seed = 4) =>
        new("roster-early", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(units),
            new BattleSkillResolver(catalog, includePreviewSlime: true, randomDualChanceBp: 0),
            new BattleFlowRules(initialAllySoul: 20));

    private static BattleFlowEvent Action(BattleCommandResult result, string id) =>
        Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Action && item.SkillId == id);

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target = null, bool burn = false)
    {
        var result = Submit(match, actor, skill, target, burn);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }

    private static BattleCommandResult Submit(BattleMatch match, string actor, string skill, string? target = null, bool burn = false)
    {
        var state = match.Snapshot;
        ulong seq = state.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, seq,
            "roster-early-" + seq, actor, skill, target!, burn));
    }
}
