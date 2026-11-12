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
