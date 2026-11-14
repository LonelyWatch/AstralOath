using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleRosterExtensionsTests
{
    [Fact]
    public void CriticalStrikeCanGrantResourceAndAdvanceOtherAllies()
    {
        var catalog = new BattleCatalogData
        {
            schemaVersion = 2,
            skills = new[]
            {
                new BattleCatalogData.SkillRow { id = "test.s1", slot = 1, target = "one_enemy",
                    attackRateBp = 10000, powerBp = 10000, resourceGain = 1, alwaysCritical = true },
                new BattleCatalogData.SkillRow { id = "test.s2", slot = 2, target = "passive" }
            },
            effects = new[]
            {
                Effect("test.s1", 1, "impact", "damage", "targets", 0),
                Effect("test.s1", 2, "after_attack", "resource_gain", "self", 1, "attack_critical"),
                Effect("test.s2", 1, "after_caster_critical", "readiness", "other_allies", 1500)
            },
            enhancements = Array.Empty<BattleCatalogData.EnhancementRow>()
        };
        var form = new BattleUnitForm("standard", "test", new[] { "test.s1", "test.s2" });
        var definition = new HeroDefinition("test", "test", BattleElement.Fire, "ranger", "leo", form,
            new HeroGrowthPreset("test", 4, 55800, 166200, 45900, 160000), Array.Empty<HeroAwakeningStep>(),
            new BattleResourceDefinition(BattleResourceKind.Focus, 5, openingGain: 3));
        var hero = BattleUnit.CreateHero("hero", definition, new BattleSlot(BattleSide.Ally, 1), 60, 6, 0);
        var ally = Monster("ally", BattleSide.Ally, 2);
        var enemy = Monster("enemy", BattleSide.Enemy, 1);
        var match = new BattleMatch("test", 1, 4, new BattleFormation(new[] { hero, ally, enemy }),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0));

        var result = match.Submit(new BattleSkillCommand("test", 1, 1, "test-1", "hero", "test.s1", "enemy"));

        Assert.Equal(BattleCommandStatus.Accepted, result.Status);
        Assert.Equal(5, result.Snapshot.GetUnit("hero").Resource.Amount);
        Assert.Single(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged && item.TargetId == "ally");
        Assert.DoesNotContain(result.Events, item => item.Kind == BattleFlowEventKind.ReadinessChanged && item.TargetId == "hero");
    }

    private static BattleUnit Monster(string id, BattleSide side, int slot) =>
        BattleUnit.CreateMonster(id, new MonsterDefinition(id, id, BattleElement.Fire,
            new BattleUnitForm("standard", id, new[] { "test.s1" })), new BattleSlot(side, slot), 60,
            MonsterRank.Normal, new BattleStats(10, 100000, 400, 10));

    private static BattleCatalogData.EffectRow Effect(string skill, int order, string phase, string operation,
        string recipient, int value, string condition = "none") =>
        new() { id = $"{skill}.{order}", skillId = skill, order = order, phase = phase, operation = operation,
            recipient = recipient, status = "none", chanceBp = 10000, value = value, condition = condition };
}
