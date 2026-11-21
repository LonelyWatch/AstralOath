using AstralOath.Shared.Battle;
using Xunit;

namespace AstralOath.Server.Tests;

public sealed class EquipmentScoreCalculatorTests
{
    public static TheoryData<EquipmentStat, EquipmentStat[], int> RecordedGear => new()
    {
        // 11/21 01:34, the left and middle comparison columns.
        { new("healthPct", 1300), [new("attackPct", 800), new("defensePct", 800), new("speed", 4), new("effectivenessBp", 800)], 37 },
        { new("resistanceBp", 6000), [new("defensePct", 800), new("attackPct", 600), new("defense", 68), new("critChanceBp", 2100)], 80 },
        { new("attackPct", 6500), [new("healthPct", 900), new("speed", 15), new("critChanceBp", 1100), new("effectivenessBp", 1800)], 100 },
        // 11/17 00:45.850 shoes, 11/21 03:48 weapon, 04:36 armor.
        { new("speed", 45), [new("attack", 84), new("critChanceBp", 1200), new("attackPct", 2300), new("critDamageBp", 1600)], 95 },
        { new("attack", 100), [new("resistanceBp", 600), new("speed", 4), new("attackPct", 600), new("healthPct", 500)], 29 },
        { new("defense", 60), [new("speed", 2), new("critDamageBp", 700), new("critChanceBp", 300), new("effectivenessBp", 600)], 27 }
    };

    [Theory]
    [MemberData(nameof(RecordedGear))]
    public void ReproducesDisplayedScores(EquipmentStat main, EquipmentStat[] substats, int expected)
    {
        Assert.Equal(expected, EquipmentScoreCalculator.Calculate(main, substats));
    }

    [Fact]
    public void IncludesFractionalContributionsBeforeTruncationAndHandlesEmptySubstats()
    {
        var speedWeapon = new EquipmentPiece("speed", new("attack", 20), [new("speed", 2)]);
        var criticalWeapon = new EquipmentPiece("critical", new("attack", 30), [new("health", 20)]);
        Assert.Equal(4, EquipmentScoreCalculator.Calculate(speedWeapon));
        Assert.Equal(1, EquipmentScoreCalculator.Calculate(criticalWeapon));
        Assert.Equal(0, EquipmentScoreCalculator.Calculate(new EquipmentStat("health", 80), []));
        Assert.Equal(2, EquipmentScoreCalculator.Calculate(new EquipmentStat("critDamageBp", 600), []));
        Assert.Equal(16, EquipmentScoreCalculator.Calculate(new EquipmentStat("critChanceBp", 3800), []));
        Assert.Equal(1, EquipmentScoreCalculator.Calculate(new EquipmentStat("health", 80), [new("resistanceBp", 26)]));
    }
}
