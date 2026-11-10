using AstralOath.Shared.Battle;
using Xunit;

public sealed class BattleTests
{
    [Fact]
    public void SeedZeroHasStableSequence()
    {
        var random = new BattleRandom(0);
        Assert.Equal(0xe220a8397b1dcdafUL, random.NextUInt64());
        Assert.Equal(0x6e789e6aa1b965f4UL, random.NextUInt64());
    }
}
