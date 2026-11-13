using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleYufineTests
{
    [Fact]
    public void DragonRoarBurnBypassesResistanceButRetainsMissImmunityAndEffectProbability()
    {
        bool applied = false, chanceFailed = false, missed = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var normal = Cast(Create(seed, startImmunity: true), "yufine", "c1016.s2", null);
            Assert.DoesNotContain(normal.Events, item => item.Kind == BattleFlowEventKind.StatusApplied);
            Assert.DoesNotContain(normal.Events, item => item.Kind == BattleFlowEventKind.StatusRemoved);
            var burned = Cast(Create(seed, startImmunity: true), "yufine", "c1016.s2", null, true);
            Assert.Equal(2, burned.Snapshot.AllySoul);
            Assert.DoesNotContain(burned.Events, item => item.EffectRejection == BattleEffectRejection.Resisted);
            var fireHit = Assert.Single(burned.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "target");
            var immuneRemoved = burned.Events.SingleOrDefault(item => item.Kind == BattleFlowEventKind.StatusRemoved &&
                item.StatusKind == BattleStatusKind.Immunity && item.TargetId == "target");
            var debuffs = burned.Events.Where(item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == "target").ToArray();
            if (fireHit.HitKind == BattleHitKind.Miss)
            {
                missed = true;
                Assert.Null(immuneRemoved);
                Assert.Empty(debuffs);
            }
            else
            {
                Assert.NotNull(immuneRemoved);
                Assert.All(debuffs, item => Assert.True(immuneRemoved!.Sequence < item.Sequence));
                applied |= debuffs.Length > 0;
                chanceFailed |= debuffs.Length < 2;
            }
            Assert.Contains(burned.Events, item => item.TargetId == "second" &&
                item.Kind == BattleFlowEventKind.EffectRejected && item.EffectRejection == BattleEffectRejection.Blocked);
            Assert.DoesNotContain(burned.Events, item => item.Kind == BattleFlowEventKind.StatusApplied && item.TargetId == "second");
        }
        Assert.True(applied && chanceFailed && missed);
    }

    [Theory]
    [InlineData(2, false, 1)]
    [InlineData(2, true, 1)]
    [InlineData(6, false, 1)]
    [InlineData(6, true, 1.5)]
    public void DragonChargeBuffsBeforeDamageAndNeedsAwakeningForDebuffedBonus(int awakening, bool debuffed, double bonus)
    {
        var battle = Create(4, awakenings: awakening, allySpeed: debuffed ? 120 : 1);
        if (debuffed) Cast(battle, "ally", "setup.prepare", null);
        var before = battle.Snapshot.GetUnit("yufine");
        var result = Cast(battle, "yufine", "c1016.s3", "second");
        var buff = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "yufine" && item.StatusKind == BattleStatusKind.AttackUp);
        var damage = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.Damage && item.TargetId == "second");
        Assert.True(buff.Sequence < damage.Sequence);
        Assert.Equal(2, result.Snapshot.GetUnit("yufine").GetStatus(BattleStatusKind.AttackUp)!.RemainingTurns);
        int expected = EpicSevenCombatMath.Damage(before.Stats.Attack * 1.5m, 300, before.Stats.MaxHealth,
            20000, 9500, 0, 0, 0, damage.HitKind, before.Stats.CritDamageBp, damageMultiplier: (decimal)bonus);
        Assert.Equal(expected, damage.Amount);
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted);
    }

    [Fact]
    public void DragonChargeKillGrantsExtraTurnBeforeThirdAwakening()
    {
        var battle = Create(4, awakenings: 2, secondHealth: 30);
        var result = Cast(battle, "yufine", "c1016.s3", "second");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.UnitDefeated && item.TargetId == "second");
        Assert.Contains(result.Events, item => item.Kind == BattleFlowEventKind.ExtraTurnGranted && item.TargetId == "yufine");
        Assert.Equal("yufine", result.Snapshot.CurrentActorId);
    }

    [Theory]
    [InlineData("setup.support", true, 1)]
    [InlineData("setup.support", false, 0)]
    [InlineData("setup.attack", true, 1)]
    public void OtherAllyPrimarySkillTriggersAtMostOneReadinessIncrease(string skill, bool debuffed, int expected)
    {
        var battle = Create(4, allySpeed: debuffed ? 230 : 120);
        if (debuffed) Cast(battle, "ally", "setup.prepare", null);
        int before = battle.Snapshot.GetUnit("yufine").ReadinessBp;
        var result = Cast(battle, "ally", skill, null);
        var pushes = result.Events.Where(item => item.Kind == BattleFlowEventKind.ReadinessChanged && item.TargetId == "yufine").ToArray();
        Assert.Equal(expected, pushes.Length);
        Assert.All(pushes, item => Assert.Equal(before + 2000, item.Amount));
    }

    [Fact]
    public void AllySkillCanCreateTheFirstDebuffBeforeReadinessIsGranted()
    {
        var battle = Create(4, allySpeed: 120, enemyResistance: 0);
        Assert.Empty(battle.Snapshot.GetUnit("second").Statuses);
        var result = Cast(battle, "ally", "setup.debuff", "second");
        var debuff = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.StatusApplied &&
            item.TargetId == "second" && item.StatusKind == BattleStatusKind.Unhealable);
        var readiness = Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged &&
            item.TargetId == "yufine");
        Assert.True(debuff.Sequence < readiness.Sequence);
    }

    [Fact]
    public void OwnAndEnemySkillsDoNotTriggerAndCooldownBlocksOtherAlly()
    {
        var battle = Create(4, allySpeed: 100, enemySpeed: 105);
        var own = Cast(battle, "yufine", "c1016.s3", "second");
        Assert.DoesNotContain(own.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged);
        Assert.True(own.Snapshot.GetUnit("yufine").GetCooldown("c1016.s3") > 0);
        var enemy = Cast(battle, "target", "setup.support", null);
        Assert.DoesNotContain(enemy.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged);
        var blocked = Cast(battle, "ally", "setup.prepare", null);
        Assert.NotNull(blocked.Snapshot.GetUnit("second").GetStatus(BattleStatusKind.Unhealable));
        Assert.DoesNotContain(blocked.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged);
    }

    private static BattleMatch Create(ulong seed, int awakenings = 6, bool startImmunity = false,
        int allySpeed = 1, int enemySpeed = 10, int secondHealth = 30000, int enemyResistance = 20000)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var row = Assert.Single(catalog.heroes, item => item.id == "c1016");
        var growth = Assert.Single(catalog.growthPresets, item => item.id == row.growthPresetId);
        var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
            growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
        var steps = catalog.awakeningSteps.Where(item => item.heroId == row.id).OrderBy(item => item.rank)
            .Select(item => new HeroAwakeningStep(item.rank, item.attackFlat, item.healthFlat, item.attackPercentBp,
                item.healthPercentBp, item.defensePercentBp, item.speedFlat, item.critChanceBp,
                item.critDamageBp, item.effectivenessBp, item.effectResistanceBp)).ToArray();
        var form = new BattleUnitForm(row.variant, row.id, new[] { "c1016.s1", "c1016.s2", "c1016.s3" });
        var yufine = new HeroDefinition(row.id, row.nameKey, BattleElement.Wind, row.heroClass, row.zodiac, form, preset, steps);
        var testSkills = new[]
        {
            Skill("setup.support", "self"), Skill("setup.attack", "all_enemies"),
            Skill("setup.immunity", "passive"), Skill("setup.second-immunity", "passive"), Skill("setup.prepare", "self"), Skill("setup.debuff", "one_enemy")
        };
        testSkills[1].attackRateBp = 10000; testSkills[1].powerBp = 10000;
        catalog.skills = catalog.skills.Concat(testSkills).ToArray();
        catalog.effects = catalog.effects.Concat(new[]
        {
            Effect("setup.support", 1, "impact", "apply_status", "self", "DefenseUp", 2, 6000),
            Effect("setup.attack", 1, "impact", "damage", "targets"),
            Effect("setup.immunity", 1, "battle_start", "apply_status", "self", "Immunity", 3),
            Effect("setup.second-immunity", 1, "battle_start", "apply_status", "self", "AttackUp", 3, 5000),
            Effect("setup.second-immunity", 2, "battle_start", "apply_status", "self", "Immunity", 3),
            Effect("setup.prepare", 1, "impact", "apply_status", "self", "DefenseUp", 2, 6000),
            Effect("setup.debuff", 1, "impact", "apply_status", "targets", "Unhealable", 3),
        }).ToArray();
        var first = startImmunity ? new[] { "setup.support", "setup.immunity" } : new[] { "setup.support" };
        var second = startImmunity ? new[] { "setup.support", "setup.second-immunity" } :
            new[] { "setup.support" };
        return new BattleMatch("yufine", BattlePreviewFactory.RulesVersion, seed, new BattleFormation(new[]
        {
            BattleUnit.CreateHero("yufine", yufine, new BattleSlot(BattleSide.Ally, 1), 60, 6, awakenings),
            Monster("ally", BattleSide.Ally, 2, BattleElement.Wind, allySpeed, 30000, 0, "setup.support", "setup.attack", "setup.prepare", "setup.debuff"),
            Monster("target", BattleSide.Enemy, 1, BattleElement.Fire, enemySpeed, 30000, 20000, first),
            Monster("second", BattleSide.Enemy, 2, BattleElement.Wind, 1, secondHealth, enemyResistance, second),
        }), startImmunity ? new BattleSkillResolver(catalog, randomDualChanceBp: 0) :
            new SetupResolver(new BattleSkillResolver(catalog, randomDualChanceBp: 0)),
            new BattleFlowRules(initialAllySoul: 20));
    }

    private sealed class SetupResolver(BattleSkillResolver inner) : IBattleActionResolver
    {
        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            if (command.SkillId == "setup.prepare")
            {
                resolution = new BattleActionResolution(new[]
                {
                    BattleChange.ApplyStatus("ally", "second", BattleStatusKind.Unhealable, 3)
                });
                return true;
            }
            return inner.TryResolve(context, command, random, out resolution);
        }
    }

    private static BattleUnit Monster(string id, BattleSide side, int slot, BattleElement element,
        int speed, int health, int resist, params string[] skills) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, element, new BattleUnitForm("standard", id, skills)),
            new BattleSlot(side, slot), 60, MonsterRank.Normal, new BattleStats(25, health, 300, speed, effectResistanceBp: resist));

    private static BattleCatalogData.SkillRow Skill(string id, string target) =>
        new() { id = id, heroId = "setup", slot = 1, target = target, soulBurnKind = "none" };

    private static BattleCatalogData.EffectRow Effect(string id, int order, string phase, string operation,
        string recipient, string status = "none", int turns = 0, int value = 0) =>
        new() { skillId = id, order = order, phase = phase, operation = operation, recipient = recipient,
            status = status, turns = turns, value = value, chanceBp = 10000 };

    private static BattleCommandResult Cast(BattleMatch match, string actor, string skill, string? target, bool burn = false)
    {
        var state = match.Snapshot;
        ulong sequence = state.LastCommandSequence + 1;
        var result = match.Submit(new BattleSkillCommand(state.BattleId, state.RulesVersion, sequence,
            "yufine-" + sequence, actor, skill, target!, burn));
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        return result;
    }
}
