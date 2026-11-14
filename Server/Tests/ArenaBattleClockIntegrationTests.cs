using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class ArenaBattleClockIntegrationTests
{
    [MySqlFact]
    public async Task ReadyDeadlineTimeoutRaceAndRestartShareOnePersistedAuthority()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = Configure(source, connection);
        using var host = factory.CreateClient();
        using var guest = factory.CreateClient();
        foreach (var client in new[] { host, guest })
        {
            var identity = await Send(client, "sessions", new { displayName = "回合计时测试" });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.GetProperty("token").GetString());
        }
        var created = await Send(host, "rooms", new { name = "计时事务测试", rule = "basic", prebanCount = 0 });
        string room = "rooms/" + created.GetProperty("room").GetProperty("id").GetString();
        string battle = room + "/battle";
        try
        {
            await Send(guest, room + "/join", new { });
            var draft = await Send(host, room + "/draft/start", new { requestId = Key() });
            var heroes = draft.GetProperty("eligibleHeroIds").EnumerateArray().Take(10).Select(row => row.GetString()!).ToArray();
            for (int index = 0; index < 10; index++)
                draft = await Action(draft.GetProperty("activeSide").GetString() == "host" ? host : guest, draft, "pick", [heroes[index]]);
            string[] Picks(string side) => draft.GetProperty(side).GetProperty("picks").EnumerateArray().Select(row => row.GetString()!).ToArray();
            var allies = Picks("host"); var enemies = Picks("guest");
            draft = await Action(host, draft, "ban", [enemies[0]]);
            draft = await Action(guest, draft, "ban", [allies[0]]);
            draft = await Action(host, draft, "formation", allies[1..]);
            draft = await Action(guest, draft, "formation", enemies[1..]);
            string id = draft.GetProperty("battleId").GetString()!;
            await using (var initialSql = new MySqlConnection(connection))
            {
                await initialSql.OpenAsync(ct);
                using var initial = new MySqlCommand("SELECT state FROM arena_room_draft WHERE draft_id = @id", initialSql);
                initial.Parameters.AddWithValue("@id", id);
                using var persisted = JsonDocument.Parse((string)(await initial.ExecuteScalarAsync(ct))!);
                Assert.Equal(JsonValueKind.Object, persisted.RootElement.GetProperty("BattleClock").ValueKind);
            }
            var opening = await Read(host, battle);
            Assert.Equal(JsonValueKind.Null, opening.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").ValueKind);
            var snapshot = opening.GetProperty("snapshot");
            var command = Command(snapshot, draft.GetProperty("contentHash").GetString()!);
            bool hostTurn = snapshot.GetProperty("currentActorId").GetString()!.StartsWith("ally-", StringComparison.Ordinal);
            using (var early = await (hostTurn ? host : guest).PostAsJsonAsync(Api(battle + "/commands"), command, ct))
                Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
            var firstReady = await Send(host, battle + "/ready", Ready(snapshot));
            Assert.Equal(JsonValueKind.Null, firstReady.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").ValueKind);
            var readyRace = await Task.WhenAll(Send(guest, battle + "/ready", Ready(snapshot)),
                Send(host, battle + "/ready", Ready(snapshot)));
            var bothReady = readyRace[0];
            long expires = bothReady.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").GetInt64();
            Assert.Equal(30000L, expires - bothReady.GetProperty("clock").GetProperty("serverNowUnixMilliseconds").GetInt64());
            Assert.Equal(expires, (await Send(host, battle + "/ready", Ready(snapshot))).GetProperty("clock").GetProperty("deadlineUnixMilliseconds").GetInt64());

            await using var restarted = Configure(source, connection);
            using var resumed = restarted.CreateClient();
            resumed.DefaultRequestHeaders.Authorization = host.DefaultRequestHeaders.Authorization;
            Assert.Equal(expires, (await Read(resumed, battle)).GetProperty("clock").GetProperty("deadlineUnixMilliseconds").GetInt64());

            await using var sql = new MySqlConnection(connection);
            await sql.OpenAsync(ct);
            using (var expire = new MySqlCommand("UPDATE arena_room_draft SET state = JSON_SET(state, '$.BattleClock.DeadlineUnixMilliseconds', @time) WHERE draft_id = @id", sql))
            {
                expire.Parameters.AddWithValue("@time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1);
                expire.Parameters.AddWithValue("@id", id);
                Assert.Equal(1, await expire.ExecuteNonQueryAsync(ct));
            }
            // Late manual input and the other side's poll contend for the same SQL room lock.
            var late = Send(hostTurn ? host : guest, battle + "/commands", command);
            var poll = Read(hostTurn ? guest : host, battle);
            await Task.WhenAll(late, poll);
            Assert.NotEqual(0, late.Result.GetProperty("status").GetInt32());
            using (var count = new MySqlCommand("SELECT COUNT(*) FROM arena_battle_command WHERE draft_id = @id", sql))
            {
                count.Parameters.AddWithValue("@id", id);
                Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync(ct)));
            }
            var advanced = await Read(resumed, battle);
            Assert.Equal((await Read(host, battle)).GetProperty("snapshot").GetRawText(),
                advanced.GetProperty("snapshot").GetRawText());
            Assert.True(advanced.GetProperty("snapshot").GetProperty("lastCommandSequence").GetUInt64() >= 1);
            Assert.Equal(JsonValueKind.Null, advanced.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").ValueKind);
            // A stale ready from before timeout cannot start this new window.
            var stale = await Send(guest, battle + "/ready", Ready(snapshot));
            Assert.Equal(JsonValueKind.Null, stale.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").ValueKind);
            snapshot = advanced.GetProperty("snapshot");
            await Send(host, battle + "/ready", Ready(snapshot));
            await Send(guest, battle + "/ready", Ready(snapshot));
            command = Command(snapshot, draft.GetProperty("contentHash").GetString()!);
            hostTurn = snapshot.GetProperty("currentActorId").GetString()!.StartsWith("ally-", StringComparison.Ordinal);
            var accepted = await Send(hostTurn ? host : guest, battle + "/commands", command);
            Assert.Equal(0, accepted.GetProperty("status").GetInt32());
            var duplicate = await Send(hostTurn ? host : guest, battle + "/commands", command);
            Assert.Equal(1, duplicate.GetProperty("status").GetInt32());
            Assert.Equal(accepted.GetProperty("snapshot").GetRawText(), duplicate.GetProperty("snapshot").GetRawText());
            // Retrying an earlier accepted command after the following clock has started
            // must preserve that following deadline, even though the receipt is historical.
            snapshot = accepted.GetProperty("snapshot");
            await Send(host, battle + "/ready", Ready(snapshot));
            var nextReady = await Send(guest, battle + "/ready", Ready(snapshot));
            long nextDeadline = nextReady.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").GetInt64();
            duplicate = await Send(hostTurn ? host : guest, battle + "/commands", command);
            Assert.Equal(1, duplicate.GetProperty("status").GetInt32());
            Assert.Equal(nextDeadline, duplicate.GetProperty("clock").GetProperty("deadlineUnixMilliseconds").GetInt64());
        }
        finally
        {
            foreach (var client in new[] { guest, host })
                using (await client.PostAsync(Api(room + "/leave"), null, ct)) { }
        }

        async Task<JsonElement> Action(HttpClient client, JsonElement state, string kind, string[] ids) =>
            await Send(client, room + "/draft/actions", new { requestId = Key(), draftId = state.GetProperty("id").GetString(),
                version = state.GetProperty("version").GetInt32(), kind, heroIds = ids });
        async Task<JsonElement> Send(HttpClient client, string path, object body)
        {
            using var response = await client.PostAsJsonAsync(Api(path), body, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        async Task<JsonElement> Read(HttpClient client, string path)
        {
            using var response = await client.GetAsync(Api(path), ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
    }

    private static object Ready(JsonElement state) => new { battleId = state.GetProperty("battleId").GetString(),
        commandSequence = state.GetProperty("lastCommandSequence").GetUInt64(), eventSequence = state.GetProperty("lastEventSequence").GetUInt64(),
        actorId = state.GetProperty("currentActorId").GetString() };
    private static object Command(JsonElement state, string hash)
    {
        string actor = state.GetProperty("currentActorId").GetString()!;
        var unit = state.GetProperty("units").EnumerateArray().Single(row => row.GetProperty("instanceId").GetString() == actor);
        string enemyPrefix = actor.StartsWith("ally-", StringComparison.Ordinal) ? "enemy-" : "ally-";
        string target = state.GetProperty("units").EnumerateArray().First(row => row.GetProperty("instanceId").GetString()!.StartsWith(enemyPrefix, StringComparison.Ordinal) && row.GetProperty("currentHealth").GetInt32() > 0).GetProperty("instanceId").GetString()!;
        return new { battleId = state.GetProperty("battleId").GetString(), rulesVersion = state.GetProperty("rulesVersion").GetUInt32(),
            sequence = state.GetProperty("lastCommandSequence").GetUInt64() + 1, commandId = Key(), actorId = actor,
            skillId = unit.GetProperty("form").GetProperty("skillIds")[0].GetString(), targetId = target, useSoulBurn = false, contentHash = hash };
    }
    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> source, string connection) =>
        source.WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection })));
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Api(string route) => "/api/arena/" + route;
}
