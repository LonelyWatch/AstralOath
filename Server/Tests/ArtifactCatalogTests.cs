using AstralOath.Shared.Battle;
using Xunit;

namespace AstralOath.Server.Tests;

public sealed class ArtifactCatalogTests
{
    [Fact]
    public void ProgressionRequiresBreaksAndAdvancesSkillAtMultiplesOfThree()
    {
        var track = Enumerable.Range(0, 31).Select(level => level * 100).ToArray();
        var catalog = new ArtifactCatalog(new ArtifactCatalogData
        {
            experienceByLevel = track, goldPerExperience = 6, templates = [Template()]
        });
        var artifact = catalog.Get("ef317");
        Assert.Equal(15, catalog.LevelForExperience(2900, 0));
        Assert.Equal(18, catalog.LevelForExperience(2900, 1));
        Assert.Equal(29, catalog.LevelForExperience(2900, 5));
        Assert.Equal(1500, catalog.ExperienceForLevel(15));
        Assert.Equal(600, catalog.EnhancementCost(100));
        Assert.Equal(150, artifact.EffectAt(2));
        Assert.Equal(165, artifact.EffectAt(3));
        Assert.Equal(300, artifact.EffectAt(30));
        Assert.Equal(208, artifact.AttackAt(30));
        Assert.False(artifact.CanUseBottle);
    }

    [Fact]
    public void RejectsInvalidConfigurationRatherThanApplyingApproximateData()
    {
        var data = new ArtifactCatalogData
        {
            experienceByLevel = new int[31], goldPerExperience = 6, templates = [Template()]
        };
        Assert.Throws<ArgumentException>(() => new ArtifactCatalog(data));
    }

    private static ArtifactCatalogData.Row Template() => new()
    {
        id = "ef317", name = "白日梦小丑", stars = 3, canSummon = true, baseAttack = 16, baseHealth = 14,
        effect = "target_max_hp_damage", effectBase = 150, effectMax = 300,
        icon = "art0017", description = "依据目标生命上限追加伤害", sellGold = 5000, sellPowder = 1
    };
}
