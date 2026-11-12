using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleUnitTests
{
    private static BattleUnitForm SlimeForm() => new("standard", "slime", new[] { "slime.bite" });

    private static BattleUnit Monster(string instanceId, BattleSide side, int slot, MonsterRank rank = MonsterRank.Normal)
    {
        var definition = new MonsterDefinition("slime", "monster.slime.name", BattleElement.Wind, SlimeForm());
        return BattleUnit.CreateMonster(instanceId, definition, new BattleSlot(side, slot), 75, rank,
            new BattleStats(350, 1200, 200, 90));
    }

    [Fact]
    public void FormationSupportsFourPerSideAndKeepsSidesSeparate()
    {
        var units = Enumerable.Range(1, 4).SelectMany(index => new[]
        {
            Monster($"enemy-{index}", BattleSide.Enemy, index),
            Monster($"ally-{index}", BattleSide.Ally, index)
        }).ToArray();
        var formation = new BattleFormation(units);
        Assert.Equal(8, formation.Units.Count);
        Assert.Equal("ally-1", formation.Units[0].InstanceId);
        Assert.Equal("enemy-4", formation.Units[7].InstanceId);
        Assert.Same(units[0], formation.GetUnitOrDefault(new BattleSlot(BattleSide.Enemy, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleSlot(BattleSide.Ally, 5));
        Assert.Throws<ArgumentException>(() => formation.GetUnitOrDefault(default));
    }

    [Fact]
    public void FormationRejectsDuplicateSlotsOrInstanceIdsAndAllowsEmptyPositions()
    {
        var unit = Monster("m1", BattleSide.Ally, 1);
        var formation = new BattleFormation(new[] { unit });
        Assert.Null(formation.GetUnitOrDefault(new BattleSlot(BattleSide.Enemy, 1)));
        Assert.Throws<ArgumentException>(() =>
            new BattleFormation(new[] { unit, Monster("m2", BattleSide.Ally, 1) }));
        Assert.Throws<ArgumentException>(() =>
            new BattleFormation(new[] { unit, Monster("m1", BattleSide.Enemy, 1) }));
    }

    [Fact]
    public void EnemyHeroUsesGrowthAndAwakeningWithoutSharingHealthWithAnotherInstance()
    {
        var skills = new[] { "c1001.s1", "c1001.s2", "c1001.s3" };
        var form = new BattleUnitForm("standard", "ras", skills);
        var growth = new HeroGrowthPreset("knight_libra_3", 3, 40200, 171800, 52800, 95625);
        var hero = new HeroDefinition("c1001", "character.ras.name", BattleElement.Fire, "knight", "libra",
            form, growth, new[] { new HeroAwakeningStep(1, 20, 60, healthPercentBp: 300) });
        skills[0] = "replaced";
        var ally = BattleUnit.CreateHero("ras-ally", hero, new BattleSlot(BattleSide.Ally, 1), 1, 3, 1);
        var enemy = BattleUnit.CreateHero("ras-enemy", hero, new BattleSlot(BattleSide.Enemy, 1), 1, 3, 1);
        Assert.Equal(330, enemy.CurrentHealth);
        Assert.Equal(73, enemy.Stats.Attack);
        Assert.Equal("c1001.s1", enemy.Form.SkillIds[0]);
        Assert.Null(enemy.MonsterRank);
        Assert.Equal(100, enemy.ApplyDamage(100));
        Assert.Equal(230, enemy.CurrentHealth);
        Assert.Equal(330, ally.CurrentHealth);
    }

    [Fact]
    public void MonsterRankUsesEncounterStatsAndHealthChangesAreBounded()
    {
        var normal = Monster("normal", BattleSide.Enemy, 1);
        var boss = Monster("boss", BattleSide.Enemy, 2, MonsterRank.Boss);
        Assert.Equal(75, boss.Level);
        Assert.Equal(MonsterRank.Boss, boss.MonsterRank);
        Assert.Equal(normal.Stats.MaxHealth, boss.Stats.MaxHealth);
        Assert.Equal(1200, boss.CurrentHealth);
        Assert.Equal(1200, boss.ApplyDamage(int.MaxValue));
        Assert.False(boss.IsAlive);
        Assert.Equal(0, boss.ApplyHealing(100));
        Assert.Equal(0, boss.CurrentHealth);
        Assert.Equal(20, normal.ApplyDamage(20));
        Assert.Equal(20, normal.ApplyHealing(int.MaxValue));
        Assert.Equal(1200, normal.CurrentHealth);
        Assert.Throws<ArgumentOutOfRangeException>(() => normal.ApplyDamage(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => normal.ApplyHealing(-1));
    }
}
