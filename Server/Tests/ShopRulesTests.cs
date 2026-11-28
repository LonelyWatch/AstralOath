using AstralOath.Server.Shop;
using Xunit;

namespace AstralOath.Server.Tests;

public sealed class ShopRulesTests
{
    [Fact]
    public void ArenaCatalogKeepsRegularAndSeasonGoodsDistinct()
    {
        var goods = ShopCatalog.Arena;
        Assert.Equal(6, goods.Count(x => x.Price > 0));
        Assert.Equal(150, goods.Single(x => x.Id == "molagora").Price);
        Assert.Equal(40, goods.Single(x => x.Id == "energy").Quantity);
        Assert.Equal(3, goods.Single(x => x.Id == "energy").Limit);
        var directories = goods.Where(x => x.Period == "directory").ToArray();
        Assert.Equal(11, directories.Length);
        Assert.All(directories, item =>
        {
            Assert.Equal(0, item.Price);
            Assert.Equal(0, item.Quantity);
            var entries = Assert.IsType<string[]>(item.Entries);
            Assert.Equal(6, entries.Distinct().Count());
            Assert.All(entries, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        });
        Assert.Equal("盟约破坏者长矛", directories.Single(x => x.Name == "盟约赛季装备").Entries![0]);
        Assert.Equal("追击毁灭者靴子", directories.Single(x => x.Name == "追击赛季装备").Entries![5]);
    }

    [Fact]
    public void WeeklyQuotaTurnsOverMondayAtRealmMidnight()
    {
        Assert.Equal("2026-11-23", ShopCatalog.PeriodKey("week", DateTimeOffset.Parse("2026-11-29T15:59:59Z")));
        Assert.Equal("2026-11-30", ShopCatalog.PeriodKey("week", DateTimeOffset.Parse("2026-11-29T16:00:00Z")));
    }
}
