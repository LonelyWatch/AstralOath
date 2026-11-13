using AstralOath.Shared.Battle;
using Xunit;
using static AstralOath.Shared.Battle.BattleElement;
using static AstralOath.Shared.Battle.BattleSide;

public sealed class BattleAiPolicyTests
{
    [Fact]
    public void OffensiveAutoUsesHighestLegalSkillAndFavorsElementBeforeHealth()
    {
        var catalog = Catalog();
        var actor = Unit("actor", "hero", Ally, 1, Fire, speed: 10000);
        var match = Match(catalog, actor, Unit("weak", "target", Enemy, 1, Ice, health: 1),
            Unit("higher-slot", "target", Enemy, 3, Wind, health: 80),
            Unit("lower-slot", "target", Enemy, 2, Wind, health: 80),
            Unit("healthier", "target", Enemy, 4, Wind, health: 100));
        var policy = new BattleAiPolicy(catalog);

        var choice = policy.ChooseAction(match.Snapshot);
        Assert.Equal(("hero.s3", "lower-slot", false), (choice.SkillId, choice.TargetId, choice.UseSoulBurn));
        var result = Submit(match, choice);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);

        Assert.False(policy.IsSkillAvailable(result.Snapshot, "hero.s3"));
        Assert.Equal("hero.s2", policy.ChooseAction(result.Snapshot).SkillId);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, policy.ChooseAction(result.Snapshot)).Status);
        Assert.Equal("hero.s1", policy.ChooseAction(match.Snapshot).SkillId);
    }

    [Fact]
    public void WithoutAdvantageTargetsUseHealthRatioAndExcludeDefeatedUnits()
    {
        var catalog = Catalog();
        var snapshot = Match(catalog, Unit("actor", "hero", Ally, 1, Fire, speed: 10000),
            Unit("neutral", "target", Enemy, 1, Dark, health: 35),
            Unit("disadvantaged", "target", Enemy, 2, Ice, health: 50, maximum: 200),
            Unit("dead", "target", Enemy, 3, Wind, health: 0)).Snapshot;
        Assert.Equal("disadvantaged", new BattleAiPolicy(catalog).ChooseAction(snapshot).TargetId);
    }

    [Theory]
    [InlineData(49, "hero.s3", "wounded")]
    [InlineData(50, "hero.s1", "enemy")]
    public void SoulWeaverSupportUsesStrictlyBelowHalfHealth(int health, string skill, string target)
    {
        var catalog = Catalog("soul_weaver", "one_ally", "all_allies");
        var snapshot = Match(catalog, Unit("actor", "hero", Ally, 1, Fire, speed: 10000),
            Unit("wounded", "target", Ally, 2, Ice, health),
            Unit("enemy", "target", Enemy, 1, Wind)).Snapshot;

        var policy = new BattleAiPolicy(catalog);
        var automatic = policy.ChooseAction(snapshot);
        Assert.Equal((skill, target), (automatic.SkillId, automatic.TargetId));
        var explicitHeal = policy.ChooseTarget(snapshot, "hero.s2");
        Assert.Equal(("hero.s2", "wounded"), (explicitHeal.SkillId, explicitHeal.TargetId));
    }

    [Fact]
    public void RelevantCleansePrecedesHealAndCanTargetAHealthyAlly()
    {
        var catalog = Catalog("soul_weaver", "one_ally", "all_allies", cleanse: true);
        var match = new BattleMatch("ai", 1, 0, new BattleFormation(new[]
        {
            Unit("actor", "hero", Ally, 1, Fire, speed: 100),
            Unit("wounded", "target", Ally, 2, Wind, health: 40),
            Unit("afflicted", "target", Ally, 3, Fire),
            Unit("enemy", "target", Enemy, 1, Wind, speed: 101)
        }), new ApplyDebuffThenResolve(new BattleSkillResolver(catalog)));

        Assert.Equal(BattleCommandStatus.Accepted,
            match.Submit(new BattleSkillCommand("ai", 1, 1, "debuff", "enemy", "target.s1", "afflicted")).Status);
        var policy = new BattleAiPolicy(catalog);
        var choice = policy.ChooseAction(match.Snapshot);
        Assert.Equal(("hero.s2", "afflicted"), (choice.SkillId, choice.TargetId));
        var result = Submit(match, choice);
        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Null(result.Snapshot.GetUnit("afflicted").GetStatus(BattleStatusKind.DefenseDown));
    }

    [Fact]
    public void SelectedBurnHonorsSoulAndSkillCostAndAutoNeverSpendsSoul()
    {
        var catalog = Catalog();
        var skill = catalog.skills.Single(row => row.slot == 2);
        skill.soulBurnKind = "increase_damage";
        skill.soulBurnCost = 10;
        catalog.skills.Single(row => row.slot == 3).resourceCost = 100;
        var actor = Unit("actor", "hero", Ally, 1, Fire, speed: 10000);
        var foe = Unit("foe", "target", Enemy, 1, Wind);
        var policy = new BattleAiPolicy(catalog);
        var empty = Match(catalog, actor, foe).Snapshot;
        Assert.False(policy.IsSkillAvailable(empty, "hero.s3"));
        Assert.Null(policy.ChooseTarget(empty, "hero.s2", useSoulBurn: true));
        Assert.Null(policy.ChooseTarget(empty, "target.s1"));
        Assert.False(policy.ChooseAction(empty).UseSoulBurn);

        var match = new BattleMatch("ai", 1, 0, new BattleFormation(new[] { actor, foe }),
            new BattleSkillResolver(catalog), new BattleFlowRules(initialAllySoul: 10));
        Assert.True(policy.IsSkillAvailable(match.Snapshot, "hero.s2", useSoulBurn: true));
        var burn = policy.ChooseTarget(match.Snapshot, "hero.s2", useSoulBurn: true);
        Assert.True(burn.UseSoulBurn);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(match, burn).Status);
        Assert.Equal(0, match.Snapshot.AllySoul);
    }

    [Fact]
    public void PreviewSlimeAndPassiveOrSilencedActionsHaveUsableFallbacks()
    {
        var catalog = Catalog();
        var passive = catalog.skills.Single(row => row.slot == 2);
        passive.target = "passive";
        catalog.effects = catalog.effects.Where(row => row.skillId != passive.id).ToArray();
        var actor = Unit("actor", "hero", Ally, 1, Fire, speed: 100);
        var enemy = Unit("enemy", "target", Enemy, 1, Ice, speed: 101);
        var match = new BattleMatch("ai", 1, 0, new BattleFormation(new[] { actor, enemy }),
            new ApplyDebuffThenResolve(new BattleSkillResolver(catalog), BattleStatusKind.Silence));

        Assert.Equal(BattleCommandStatus.Accepted,
            match.Submit(new BattleSkillCommand("ai", 1, 1, "debuff", "enemy", "target.s1", "actor")).Status);
        var policy = new BattleAiPolicy(catalog);
        Assert.False(policy.IsSkillAvailable(match.Snapshot, "hero.s2"));
        Assert.False(policy.IsSkillAvailable(match.Snapshot, "hero.s3"));
        Assert.Equal("hero.s1", policy.ChooseAction(match.Snapshot).SkillId);

        var slime = Unit("slime", "preview.slime", Enemy, 1, Ice, speed: 1000,
            skills: new[] { BattlePreviewFactory.SlimeSkillId });
        var preview = new BattleMatch("ai", 1, 0, new BattleFormation(new[] { actor, slime }),
            new BattleSkillResolver(catalog, includePreviewSlime: true));
        Assert.Equal(BattlePreviewFactory.SlimeSkillId, policy.ChooseAction(preview.Snapshot).SkillId);
        Assert.Equal(BattleCommandStatus.Accepted, Submit(preview, policy.ChooseAction(preview.Snapshot)).Status);
    }

    private static BattleMatch Match(BattleCatalogData catalog, params BattleUnit[] units) =>
        new("ai", 1, 0, new BattleFormation(units), new BattleSkillResolver(catalog, randomDualChanceBp: 0));

    private static BattleUnit Unit(string id, string hero, BattleSide side, int position, BattleElement element,
        int health = 100, int maximum = 100, int speed = 1, string[]? skills = null)
    {
        skills ??= Enumerable.Range(1, 3).Select(slot => $"{hero}.s{slot}").ToArray();
        var definition = new MonsterDefinition(hero, hero, element, new BattleUnitForm("standard", hero, skills));
        var unit = BattleUnit.CreateMonster(id, definition, new BattleSlot(side, position), 60,
            MonsterRank.Normal, new BattleStats(10, maximum, 500, speed));
        unit.ApplyDamage(maximum - health);
        return unit;
    }

    private static BattleCatalogData Catalog(string heroClass = "knight", string s2 = "one_enemy",
        string s3 = "all_enemies", bool cleanse = false)
    {
        var skills = new[] { "one_enemy", s2, s3 }.Select((target, index) => new BattleCatalogData.SkillRow
        {
            id = $"hero.s{index + 1}", heroId = "hero", target = target, slot = index + 1,
            cooldownTurns = index == 0 ? 0 : 3, attackRateBp = 10000, powerBp = 10000, casterHealthHealBp = 1500
        }).ToArray();
        return new BattleCatalogData
        {
            schemaVersion = 2,
            heroes = new[] { new BattleCatalogData.HeroRow { id = "hero", heroClass = heroClass } },
            skills = skills,
            effects = skills.Select(row => new BattleCatalogData.EffectRow
            {
                skillId = row.id, order = 1, phase = "impact", recipient = "targets", chanceBp = 10000,
                operation = row.slot == 2 && cleanse ? "cleanse" : row.target.EndsWith("ally") ||
                    row.target == "all_allies" ? "heal" : "damage",
                status = "none", condition = "none", value = cleanse ? 1 : 0
            }).ToArray(),
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
    }

    private static BattleCommandResult Submit(BattleMatch match, BattleAiChoice choice)
    {
        var snapshot = match.Snapshot;
        var sequence = snapshot.LastCommandSequence + 1;
        return match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence,
            $"action-{sequence}", snapshot.CurrentActorId, choice.SkillId, choice.TargetId, choice.UseSoulBurn));
    }

    private sealed class ApplyDebuffThenResolve : IBattleActionResolver
    {
        private readonly BattleSkillResolver resolver;
        private readonly BattleStatusKind debuff;
        internal ApplyDebuffThenResolve(BattleSkillResolver resolver, BattleStatusKind debuff = BattleStatusKind.DefenseDown)
        {
            this.resolver = resolver;
            this.debuff = debuff;
        }

        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            if (command.Sequence != 1) return resolver.TryResolve(context, command, random, out resolution);
            resolution = new BattleActionResolution(new[] { BattleChange.ApplyStatus(command.ActorId, command.TargetId, debuff, 2) });
            return true;
        }
    }
}
