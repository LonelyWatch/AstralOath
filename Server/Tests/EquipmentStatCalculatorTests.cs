using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Shared.Battle;
using Xunit;

namespace AstralOath.Server.Tests;

public sealed class EquipmentStatCalculatorTests
{
    private static EquipmentPiece Piece(string setId, string kind, int value, params EquipmentStat[] substats) =>
        new(setId, new EquipmentStat(kind, value), substats);

    [Fact]
    public void AppliesCompleteSetsAndFlatStatsAfterSummingPercentages()
    {
        var bare = new BattleStats(101, 301, 53, 100);
        EquipmentPiece[] pieces = [Piece("speed", "attack", 31, new EquipmentStat("attackPct", 500)),
            Piece("speed", "health", 80, new EquipmentStat("healthPct", 1000)), Piece("speed", "defense", 40, new EquipmentStat("defensePct", 1500)),
            Piece("speed", "critDamageBp", 500), Piece("critical", "attackPct", 500, new EquipmentStat("effectivenessBp", 320)),
            Piece("critical", "speed", 40, new EquipmentStat("resistanceBp", 520))];
        var stats = EquipmentStatCalculator.Calculate(bare, pieces);
        Assert.Equal((142, 411, 100, 165), (stats.Attack, stats.MaxHealth, stats.Defense, stats.Speed));
        Assert.Equal((2700, 15500, 320, 520), (stats.CritChanceBp, stats.CritDamageBp, stats.EffectivenessBp, stats.EffectResistanceBp));
        Assert.Equal(1, EquipmentStatCalculator.ActiveSets(pieces)["speed"]);
        var partial = EquipmentStatCalculator.Calculate(bare, pieces[1..]);
        Assert.Equal(140, partial.Speed);
        Assert.Equal(106, partial.Attack);
        Assert.False(EquipmentStatCalculator.ActiveSets(pieces[1..]).ContainsKey("speed"));
        var sixCritical = Enumerable.Repeat(Piece("critical", "critChanceBp", 150), 6).ToArray();
        Assert.Equal(6000, EquipmentStatCalculator.Calculate(bare, sixCritical).CritChanceBp);
    }

    [Fact]
    public void OwnedBattleCopiesEquipmentPerHeroAndAddsArtifactAfterPercentages()
    {
        using var source = typeof(ArtifactCatalogReader).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var data = JsonSerializer.Deserialize<BattleCatalogData>(source, new JsonSerializerOptions { IncludeFields = true })!;
        var book = ArtifactCatalogReader.Load().Get("efm03");
        var pieces = new[] { Piece("critical", "attackPct", 10000), Piece("critical", "healthPct", 5000) };
        var hero = new BattleOwnedHero("c1005", 1, 4, 0, book, 30, pieces);
        pieces[0] = Piece("speed", "speed", 100);
        var match = BattlePreviewFactory.CreateOwned("wearer", 4, data, [hero, new("c1005", 1, 4, 0)]);
        var stats = match.Snapshot.GetUnit("ally-1").Stats;
        var bare = match.Snapshot.GetUnit("ally-2").Stats;
        Assert.Equal(2 * bare.Attack + book.AttackAt(30), stats.Attack);
        Assert.Equal(bare.MaxHealth + bare.MaxHealth / 2 + book.HealthAt(30), stats.MaxHealth);
        Assert.Equal(bare.CritChanceBp + 1200, stats.CritChanceBp);
        Assert.Equal(bare.Speed, stats.Speed);
    }
}
