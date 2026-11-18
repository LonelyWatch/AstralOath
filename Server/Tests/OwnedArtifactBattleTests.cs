using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Shared.Battle;
using Xunit;

namespace AstralOath.Server.Tests;

public sealed class OwnedArtifactBattleTests
{
    [Fact]
    public void OwnedTrialAppliesTheEquippedArtifactStatsAndOpeningSoul()
    {
        var data = BattleData();
        var book = ArtifactCatalogReader.Load().Get("efm03");
        var roster = new[] { new BattleOwnedHero("c1005", 1, 4, 0, book, 30) };
        var baseline = BattlePreviewFactory.CreateOwned("bare", 4, data,
            [new BattleOwnedHero("c1005", 1, 4, 0)]);
        var equipped = BattlePreviewFactory.CreateOwned("book", 4, data, roster);
        var stats = baseline.Snapshot.GetUnit("ally-1").Stats;
        var withBook = equipped.Snapshot.GetUnit("ally-1");
        Assert.Equal(stats.Attack + book.AttackAt(30), withBook.Stats.Attack);
        Assert.Equal(stats.MaxHealth + book.HealthAt(30), withBook.CurrentHealth);
        Assert.Equal(0, baseline.Snapshot.GetSoul(BattleSide.Ally));
        Assert.Equal(book.EffectAt(30), equipped.Snapshot.GetSoul(BattleSide.Ally));
        Assert.Single(equipped.Snapshot.Units, unit => unit.Slot.Side == BattleSide.Enemy);
    }

    [Fact]
    public void OwnedTrialRejectsEquipmentFromADifferentProfession()
    {
        var book = ArtifactCatalogReader.Load().Get("efm03");
        Assert.Throws<ArgumentException>(() => BattlePreviewFactory.CreateOwned("wrong", 1, BattleData(),
            [new BattleOwnedHero("c1001", 1, 3, 0, book, 0)]));
    }

    [Fact]
    public void EveryInitialArtifactRunsInsideAnAuthoritativeBattle()
    {
        var data = BattleData();
        foreach (var artifact in ArtifactCatalogReader.Load().Templates)
        {
            var hero = data.heroes.First(row => string.IsNullOrEmpty(artifact.HeroClass) || row.heroClass == artifact.HeroClass);
            var battle = BattlePreviewFactory.CreateOwned(artifact.Id, 113, data,
                [new BattleOwnedHero(hero.id, 45, 5, 0, artifact, 30), new BattleOwnedHero("c1001", 45, 5, 0)]);
            for (ulong sequence = 1; sequence <= 24 && battle.Snapshot.Outcome == BattleOutcome.InProgress; sequence++)
            {
                var actor = battle.Snapshot.GetUnit(battle.Snapshot.CurrentActorId);
                var target = battle.Snapshot.Units.First(unit => unit.Slot.Side != actor.Slot.Side && unit.IsAlive);
                var command = new BattleSkillCommand(battle.Snapshot.BattleId, battle.Snapshot.RulesVersion,
                    sequence, "test-" + sequence, actor.InstanceId, actor.Form.SkillIds[0], target.InstanceId, false);
                var result = battle.Submit(command);
                Assert.True(result.Status == BattleCommandStatus.Accepted, artifact.Id + " " + result.Status);
            }
        }
    }

    private static BattleCatalogData BattleData()
    {
        using var stream = typeof(ArtifactCatalogReader).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        return JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
    }
}
