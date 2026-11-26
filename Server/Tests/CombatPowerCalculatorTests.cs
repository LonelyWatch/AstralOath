using AstralOath.Shared.Battle;
using Xunit;

public sealed class CombatPowerCalculatorTests
{
    [Fact]
    public void ReplaysTheThreePublishedHistoricalExamplesWithTheirExplicitProgress()
    {
        Assert.Equal(2290, Calculate(new(173, 459, 116, 106), 0, 0, 0m, 0m, 0m));
        Assert.Equal(2443, Calculate(new(187, 480, 116, 106), 3, 0, 0m, 0m, 0m));
        Assert.Equal(15712, Calculate(new(932, 4370, 652, 108), 0, 0, .079m, 0m, 0m));
    }

    [Fact]
    public void ConvertsAllPercentageInputsFromBasisPointsBeforeApplyingTheHistoricalFormula()
    {
        // Attack 640 + health 1000 + defense 930 = 2570; E/R multiplier is 1.1875.
        Assert.Equal(3051, Calculate(new(100, 1000, 100, 95, 5000, 20000, 2500, 5000), 0, 0, 0m, 0m, 0m));
    }

    [Theory]
    [InlineData(0, 0, 10000)]
    [InlineData(3, 0, 10300)]
    [InlineData(3, 2, 10300)]
    [InlineData(3, 3, 10330)]
    [InlineData(3, 6, 10360)]
    [InlineData(3, 9, 10390)]
    [InlineData(3, 12, 10420)]
    [InlineData(3, 15, 10450)]
    [InlineData(3, 18, 10480)]
    [InlineData(3, 21, 10510)]
    [InlineData(3, 24, 10540)]
    [InlineData(3, 27, 10570)]
    [InlineData(3, 30, 10600)]
    [InlineData(4, 0, 10450)]
    [InlineData(4, 3, 10495)]
    [InlineData(4, 6, 10540)]
    [InlineData(4, 9, 10585)]
    [InlineData(4, 12, 10630)]
    [InlineData(4, 15, 10675)]
    [InlineData(4, 18, 10720)]
    [InlineData(4, 21, 10765)]
    [InlineData(4, 24, 10810)]
    [InlineData(4, 27, 10855)]
    [InlineData(4, 30, 10900)]
    [InlineData(5, 0, 10600)]
    [InlineData(5, 3, 10660)]
    [InlineData(5, 6, 10720)]
    [InlineData(5, 9, 10780)]
    [InlineData(5, 12, 10840)]
    [InlineData(5, 15, 10900)]
    [InlineData(5, 18, 10960)]
    [InlineData(5, 21, 11020)]
    [InlineData(5, 24, 11080)]
    [InlineData(5, 27, 11140)]
    [InlineData(5, 30, 11200)]
    public void UsesSourceArtifactCoefficientsAndMapsEnhancementLevelToSkillRank(int stars, int level, int expected)
    {
        // A health-only panel makes the source's decimal coefficient observable without truncation noise.
        Assert.Equal(expected, Calculate(new(0, 10000, 0, 45, 0, 0), stars, level, 0m, 0m, 0m));
    }

    [Fact]
    public void ExplicitProgressCoefficientsAreAddedOnceAndTheFinalProductIsFloored()
    {
        Assert.Equal(1110, Calculate(new(0, 1001, 0, 45, 0, 0), 0, 0, .079m, .02m, .01m));
    }

    [Fact]
    public void ProjectHistoricalPolicyDoesNotInventCriticalChanceOrDamageCaps()
    {
        Assert.Equal(1928, Calculate(new(100, 1000, 0, 45, 12000, 40000), 0, 0, 0m, 0m, 0m));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(6, 0)]
    [InlineData(0, 1)]
    [InlineData(3, -1)]
    [InlineData(3, 31)]
    public void InvalidArtifactIdentityOrLevelCannotMasqueradeAsNoArtifact(int stars, int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(new(0, 10000, 0, 45), stars, level, 0m, 0m, 0m));
    }

    [Fact]
    public void MissingPanelNegativeProgressAndOverflowAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Calculate(null!, 0, 0, 0m, 0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(new(0, 1000, 0, 45), 0, 0, -.01m, 0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(new(0, 1000, 0, 45), 0, 0, 0m, -.01m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(new(0, 1000, 0, 45), 0, 0, 0m, 0m, -.01m));
        Assert.Throws<OverflowException>(() => Calculate(new(0, int.MaxValue, int.MaxValue, 45), 0, 0, 0m, 0m, 0m));
    }

    private static int Calculate(BattleStats panel, int stars, int level, decimal skill, decimal exclusive, decimal specialty)
        => CombatPowerCalculator.Calculate(panel, stars, level, skill, exclusive, specialty);
}
