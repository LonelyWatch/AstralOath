using AstralOath.Shared.Battle;
using Xunit;

public sealed class HeroStatCalculatorTests
{
    private static readonly HeroGrowthPreset Ras = new("ras", 3, 40200, 171800, 52800, 95625);
    private static readonly HeroGrowthPreset Mercedes = new("mercedes", 4, 63000, 141000, 53700, 101250);
    private static readonly HeroGrowthPreset Angelica = new("angelica", 4, 28200, 155000, 60000, 98750);

    [Theory]
    [InlineData(1, 53, 263, 68)]
    [InlineData(2, 61, 329, 75)]
    [InlineData(3, 69, 395, 83)]
    [InlineData(4, 77, 460, 91)]
    public void RasUnawakenedStatsMatchLevelSamples(int level, int attack, int health, int defense)
    {
        var stats = HeroStatCalculator.Calculate(Ras, level, 3, 0, Array.Empty<HeroAwakeningStep>());

        Assert.Equal((attack, health, defense, 95),
            (stats.Attack, stats.MaxHealth, stats.Defense, stats.Speed));
    }

    [Theory]
    [InlineData(1, 90, 230, 74)]
    [InlineData(2, 102, 287, 82)]
    [InlineData(3, 115, 345, 90)]
    public void MercedesDisplaysUnequalPerLevelIncreasesAfterRounding(int level, int attack, int health, int defense)
    {
        var stats = HeroStatCalculator.Calculate(Mercedes, level, 4, 0, Array.Empty<HeroAwakeningStep>());

        Assert.Equal((attack, health, defense, 101),
            (stats.Attack, stats.MaxHealth, stats.Defense, stats.Speed));
    }

    [Fact]
    public void PromotionChangesStatsWithoutChangingLevelOrSpeed()
    {
        var steps = Array.Empty<HeroAwakeningStep>();
        var original = HeroStatCalculator.Calculate(Ras, 30, 3, 0, steps);
        var promoted = HeroStatCalculator.Calculate(Ras, 30, 4, 0, steps);

        Assert.Equal((277, 2173, 288), (original.Attack, original.MaxHealth, original.Defense));
        Assert.Equal((295, 2315, 307), (promoted.Attack, promoted.MaxHealth, promoted.Defense));
        Assert.Equal(original.Speed, promoted.Speed);
    }

    [Fact]
    public void MercedesFloorBoundaryMatchesPublishedOperationGrouping()
    {
        var stats = HeroStatCalculator.Calculate(Mercedes, 47, 5, 0, Array.Empty<HeroAwakeningStep>());

        Assert.Equal(3054, stats.MaxHealth);
    }

    [Fact]
    public void PartialAwakeningAppliesOnlyTheUnlockedBonuses()
    {
        var steps = new HeroAwakeningStep[]
        {
            new(1, 20, 60, healthPercentBp: 300),
            new(2, 20, 60, defensePercentBp: 300),
            new(3, 20, 60),
            new(4, 30, 80, effectResistanceBp: 1200)
        };
        var stats = HeroStatCalculator.Calculate(Ras, 30, 4, 1, steps);

        Assert.Equal((315, 2444, 307, 95, 0),
            (stats.Attack, stats.MaxHealth, stats.Defense, stats.Speed, stats.EffectResistanceBp));
    }

    [Fact]
    public void MaxAwakeningMatchesTheThreePublishedPanels()
    {
        var ras = HeroStatCalculator.Calculate(Ras, 60, 6, 6, new HeroAwakeningStep[]
        {
            new(1, 20, 60, healthPercentBp: 300),
            new(2, 20, 60, defensePercentBp: 300),
            new(3, 20, 60),
            new(4, 30, 80, effectResistanceBp: 1200),
            new(5, 30, 80, healthPercentBp: 600),
            new(6, 30, 80, defensePercentBp: 600)
        });
        var mercedes = HeroStatCalculator.Calculate(Mercedes, 60, 6, 6, new HeroAwakeningStep[]
        {
            new(1, 20, 60, critChanceBp: 400),
            new(2, 20, 60, attackPercentBp: 300),
            new(3, 20, 60),
            new(4, 30, 80, critDamageBp: 1000),
            new(5, 30, 80, critChanceBp: 800),
            new(6, 30, 80, attackPercentBp: 600)
        });
        var angelica = HeroStatCalculator.Calculate(Angelica, 60, 6, 6, new HeroAwakeningStep[]
        {
            new(1, 20, 60, healthPercentBp: 300),
            new(2, 20, 60, healthPercentBp: 300),
            new(3, 20, 60),
            new(4, 30, 80, defensePercentBp: 600),
            new(5, 30, 80, healthPercentBp: 600),
            new(6, 30, 80, healthPercentBp: 600)
        });

        Assert.Equal((758, 5826, 672, 95, 1500, 15000, 0, 1200), Panel(ras));
        Assert.Equal((1187, 4491, 627, 101, 2700, 16000, 0, 0), Panel(mercedes));
        Assert.Equal((576, 5700, 743, 98, 1500, 15000, 0, 0), Panel(angelica));
    }

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(31, 3, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 7, 0)]
    [InlineData(1, 3, -1)]
    [InlineData(1, 3, 4)]
    public void RejectsInvalidHeroProgression(int level, int stars, int awakenings)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HeroStatCalculator.Calculate(Ras, level, stars, awakenings, Array.Empty<HeroAwakeningStep>()));
    }

    [Fact]
    public void FirstStarAllowsLevelFifteen()
    {
        var preset = new HeroGrowthPreset("test", 1, 40200, 171800, 52800, 95625);
        var steps = Array.Empty<HeroAwakeningStep>();

        Assert.Equal(140, HeroStatCalculator.Calculate(preset, 15, 1, 0, steps).Attack);
        Assert.Throws<ArgumentOutOfRangeException>(() => HeroStatCalculator.Calculate(preset, 16, 1, 0, steps));
    }

    [Fact]
    public void RejectsMissingOrUnorderedAwakeningSteps()
    {
        Assert.Throws<ArgumentException>(() =>
            HeroStatCalculator.Calculate(Ras, 30, 3, 2, new HeroAwakeningStep[] { new(1, 20, 60) }));
        Assert.Throws<ArgumentException>(() =>
            HeroStatCalculator.Calculate(Ras, 30, 3, 2, new HeroAwakeningStep[] { new(2, 20, 60), new(1, 20, 60) }));
    }

    private static (int, int, int, int, int, int, int, int) Panel(BattleStats stats) =>
        (stats.Attack, stats.MaxHealth, stats.Defense, stats.Speed, stats.CritChanceBp,
            stats.CritDamageBp, stats.EffectivenessBp, stats.EffectResistanceBp);
}
