using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Shared.Battle;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class BattlePreviewRoomTests
{
    [Fact]
    public void RoomResolvesForcedTurnAndReplaysTheSameAggregateAfterRetry()
    {
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream("AstralOath.BattleCatalog")!;
        var catalog = JsonSerializer.Deserialize<BattleCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })!;
        var match = new BattleMatch("forced", BattlePreviewFactory.RulesVersion, 23,
            new BattleFormation(new[] { Unit("ally", BattleSide.Ally, 120), Unit("enemy", BattleSide.Enemy, 100) }),
            new FirstHitProvokes(new BattleSkillResolver(catalog, randomDualChanceBp: 0)));
        var roomType = assembly.GetType("AstralOath.Server.Battle.BattlePreviewRoom", throwOnError: true)!;
        var commandType = assembly.GetType("AstralOath.Server.Battle.BattlePreviewCommand", throwOnError: true)!;
        var room = Activator.CreateInstance(roomType, match, "ally-token", "enemy-token", DateTimeOffset.UtcNow.AddHours(1),
            "content", new BattleAiPolicy(catalog))!;
        var submit = roomType.GetMethod("Submit")!;
        var read = roomType.GetMethod("Read")!;
        var command = Activator.CreateInstance(commandType, BattlePreviewFactory.RulesVersion, 1UL, "open", "ally",
            "c1001.s1", "enemy", false, "content");
        ulong cursor = match.Snapshot.LastEventSequence;
        Assert.Null(submit.Invoke(room, new[] { (object)BattleSide.Enemy, command }));
        var accepted = AsJson(submit.Invoke(room, new[] { (object)BattleSide.Ally, command }));
        Assert.Equal((int)BattleCommandStatus.Accepted, accepted.GetProperty("status").GetInt32());
        Assert.Equal(2UL, accepted.GetProperty("snapshot").GetProperty("lastCommandSequence").GetUInt64());
        var actions = accepted.GetProperty("events").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetInt32() == (int)BattleFlowEventKind.Action).ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Equal("enemy", actions[1].GetProperty("sourceId").GetString());
        Assert.Equal("ally", actions[1].GetProperty("targetId").GetString());
        Assert.Equal("c1001.s1", actions[1].GetProperty("skillId").GetString());

        var replay = AsJson(submit.Invoke(room, new[] { (object)BattleSide.Ally, command }));
        Assert.Equal((int)BattleCommandStatus.Duplicate, replay.GetProperty("status").GetInt32());
        Assert.Equal(accepted.GetProperty("snapshot").GetRawText(), replay.GetProperty("snapshot").GetRawText());
        Assert.Equal(accepted.GetProperty("events").GetRawText(), replay.GetProperty("events").GetRawText());
        var resumed = AsJson(read.Invoke(room, new object[] { cursor }));
        Assert.Equal(accepted.GetProperty("events").GetRawText(), resumed.GetProperty("events").GetRawText());

        static JsonElement AsJson(object? value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        static BattleUnit Unit(string id, BattleSide side, int speed)
        {
            var form = new BattleUnitForm("standard", "ras", new[] { "c1001.s1" });
            var definition = new MonsterDefinition("c1001", "ras", BattleElement.Fire, form);
            return BattleUnit.CreateMonster(id, definition, new BattleSlot(side, 1), 60,
                MonsterRank.Normal, new BattleStats(100, 5000, 300, speed));
        }
    }

    private sealed class FirstHitProvokes(BattleSkillResolver inner) : IBattleActionResolver
    {
        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            if (command.Sequence != 1) return inner.TryResolve(context, command, random, out resolution);
            resolution = new BattleActionResolution(new[]
            {
                BattleChange.ApplyStatus(command.ActorId, command.TargetId, BattleStatusKind.Provoke, 1)
            }, "fixture");
            return true;
        }
    }


    [Fact]
    public async Task TwoClientsCanPlayAndReconnectButCannotActForTheOtherSeat()
    {
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = source.WithWebHostBuilder(host => host.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BattlePreview:Enabled"] = "true",
                ["Server:BindAddress"] = "127.0.0.1"
            })));
        using var organizer = factory.CreateClient();
        using var creation = await organizer.PostAsync("/api/battle-preview/rooms", null);
        creation.EnsureSuccessStatusCode();
        var invite = await creation.Content.ReadFromJsonAsync<JsonElement>();
        var opening = invite.GetProperty("opening").GetProperty("snapshot");
        string path = "/api/battle-preview/rooms/" + invite.GetProperty("roomId").GetString();
        using var ally = Seat("allyToken");
        using var enemy = Seat("enemyToken");

        string actor = opening.GetProperty("currentActorId").GetString()!;
        bool allyActs = actor.StartsWith("ally-", StringComparison.Ordinal);
        var actorUnit = opening.GetProperty("units").EnumerateArray().Single(unit => unit.GetProperty("instanceId").GetString() == actor);
        var command = new
        {
            rulesVersion = opening.GetProperty("rulesVersion").GetUInt32(),
            sequence = 1,
            commandId = "first-action",
            actorId = actor,
            skillId = actorUnit.GetProperty("form").GetProperty("skillIds")[0].GetString(),
            targetId = allyActs ? "enemy-1" : "ally-1",
            contentHash = invite.GetProperty("contentHash").GetString()
        };
        var caller = allyActs ? ally : enemy;
        var opponent = allyActs ? enemy : ally;
        using var missing = await organizer.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var foreign = await opponent.PostAsJsonAsync(path + "/commands", command);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var first = await caller.PostAsJsonAsync(path + "/commands", command);
        first.EnsureSuccessStatusCode();
        var accepted = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)BattleCommandStatus.Accepted, accepted.GetProperty("status").GetInt32());
        using var duplicate = await caller.PostAsJsonAsync(path + "/commands", command);
        duplicate.EnsureSuccessStatusCode();
        var retry = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)BattleCommandStatus.Duplicate, retry.GetProperty("status").GetInt32());

        ulong cursor = opening.GetProperty("lastEventSequence").GetUInt64();
        var update = await opponent.GetFromJsonAsync<JsonElement>(path + $"?after={cursor}");
        Assert.False(update.GetProperty("snapshotReset").GetBoolean());
        Assert.Equal(1UL, update.GetProperty("snapshot").GetProperty("lastCommandSequence").GetUInt64());
        Assert.All(update.GetProperty("events").EnumerateArray(), item =>
            Assert.True(item.GetProperty("sequence").GetUInt64() > cursor));
        Assert.Equal(accepted.GetProperty("events").GetArrayLength(), update.GetProperty("events").GetArrayLength());
        var reset = await opponent.GetFromJsonAsync<JsonElement>(path + "?after=999999");
        Assert.True(reset.GetProperty("snapshotReset").GetBoolean());
        Assert.Equal(0, reset.GetProperty("events").GetArrayLength());

        HttpClient Seat(string key)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invite.GetProperty(key).GetString());
            return client;
        }
    }
}
