using System.Text.Json;
using AstralOath.Shared.Battle;
using Xunit;

public sealed class ArenaTimeoutIdentityTests
{
    [Fact]
    public void APlayerCannotReserveTheIdOfAFutureServerTimeout()
    {
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var match = new BattleMatch("timeout-identity", BattlePreviewFactory.RulesVersion, 23,
            new BattleFormation(new[] { Unit("ally", BattleSide.Ally, 120), Unit("enemy", BattleSide.Enemy, 100) }),
            new BattleSkillResolver(catalog, randomDualChanceBp: 0));
        var roomType = assembly.GetType("AstralOath.Server.Battle.BattlePreviewRoom", true)!;
        var commandType = assembly.GetType("AstralOath.Server.Battle.BattlePreviewCommand", true)!;
        var room = Activator.CreateInstance(roomType, match, "a", "b", DateTimeOffset.MaxValue, "hash", new BattleAiPolicy(catalog))!;
        var submit = roomType.GetMethod("Submit")!;
        Assert.Equal(BattleCommandStatus.Accepted, Send(false, BattleSide.Ally, 1, "timeout:3", "ally", "enemy"));
        Assert.Equal(BattleCommandStatus.Accepted, Send(false, BattleSide.Enemy, 2, "normal", "enemy", "ally"));
        Assert.Equal("ally", match.Snapshot.CurrentActorId);
        Assert.Equal(BattleCommandStatus.Accepted, Send(true, BattleSide.Ally, 3, "timeout:3", "ally", "enemy"));

        BattleCommandStatus Send(bool timeout, BattleSide side, ulong sequence, string id, string actor, string target)
        {
            var command = Activator.CreateInstance(commandType, BattlePreviewFactory.RulesVersion, sequence, id,
                actor, "c1001.s1", target, false, "hash")!;
            var method = timeout ? roomType.GetMethod("SubmitTimeout") ?? submit : submit;
            var result = method.Invoke(room, new[] { (object)side, command })!;
            return (BattleCommandStatus)result.GetType().GetProperty("Status")!.GetValue(result)!;
        }
    }

    private static BattleUnit Unit(string id, BattleSide side, int speed) => BattleUnit.CreateMonster(id,
        new MonsterDefinition("c1001", "ras", BattleElement.Fire, new BattleUnitForm("standard", "ras", new[] { "c1001.s1" })),
        new BattleSlot(side, 1), 60, MonsterRank.Normal, new BattleStats(10, 100000, 1000, speed));
}
