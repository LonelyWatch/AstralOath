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

public sealed class ArenaDraftTests
{
    [MySqlFact]
    public async Task TwoSeatsDraftPrivatelyAndBattleRetainsCommandsAcrossRestart()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = Configure(source, connection);
        using var host = factory.CreateClient();
        using var guest = factory.CreateClient();
        using var spectator = factory.CreateClient();
        await Login(host, "测试主办");
        await Login(guest, "测试对手");
        await Login(spectator, "测试观众");

        var room = await Send(host, "rooms", new { name = "选禁集成测试", rule = "basic", allowChat = true, prebanCount = 2, firstPick = "host" });
        string roomPath = "rooms/" + room.GetProperty("room").GetProperty("id").GetString();
        string draftPath = roomPath + "/draft";
        string battlePath = roomPath + "/battle";
        var start = new { requestId = RequestId() };
        try
        {
            using (var notReady = await host.PostAsJsonAsync(Api(draftPath + "/start"), start, ct))
                Assert.Equal(HttpStatusCode.Conflict, notReady.StatusCode);

            await Send(guest, roomPath + "/join", new { });
            await Send(spectator, roomPath + "/join", new { });
            using (var notOwner = await guest.PostAsJsonAsync(Api(draftPath + "/start"), start, ct))
                Assert.Equal(HttpStatusCode.Forbidden, notOwner.StatusCode);

            var initial = await Send(host, draftPath + "/start", start);
            string draftId = initial.GetProperty("id").GetString()!;
            int version = Version(initial);
            Assert.Equal("preban", Phase(initial));
            Assert.Equal("guest", (await Read(guest, draftPath)).GetProperty("mySide").GetString());
            using (var spectatorBlocked = await spectator.GetAsync(Api(draftPath), ct))
                Assert.Equal(HttpStatusCode.Forbidden, spectatorBlocked.StatusCode);

            var eligible = initial.GetProperty("eligibleHeroIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var firstPreban = new { requestId = RequestId(), draftId, version, kind = "preban", heroIds = eligible[..2] };
            await Send(host, draftPath + "/actions", firstPreban);
            var observer = await Read(guest, draftPath);
            Assert.Empty(observer.GetProperty("host").GetProperty("prebans").EnumerateArray());
            Assert.True(observer.GetProperty("host").GetProperty("submitted").GetBoolean());
            await Action(guest, draftId, version, "preban", eligible[2], eligible[3]);
            var replay = await Send(host, draftPath + "/actions", firstPreban);
            Assert.Equal("pick", Phase(replay));

            var picks = eligible.Skip(4).Take(10).ToArray();
            HttpClient[] order = [host, guest, guest, host, host, guest, guest, host, host, guest];
            using (var wrongTurn = await guest.PostAsJsonAsync(Api(draftPath + "/actions"),
                       new { requestId = RequestId(), draftId, version = Version(replay), kind = "pick", heroIds = new[] { picks[0] } }, ct))
                Assert.Equal(HttpStatusCode.Conflict, wrongTurn.StatusCode);
            int pickVersion = Version(replay);
            string previewPath = draftPath + "/preview";
            var firstPreview = new { draftId, version = pickVersion, heroId = picks[0], sequence = 1 };
            using (var wrongPreviewSeat = await guest.PostAsJsonAsync(Api(previewPath), firstPreview, ct))
                Assert.Equal(HttpStatusCode.Conflict, wrongPreviewSeat.StatusCode);
            using (var spectatorPreview = await spectator.PostAsJsonAsync(Api(previewPath), firstPreview, ct))
                Assert.Equal(HttpStatusCode.Forbidden, spectatorPreview.StatusCode);
            using (var bannedPreview = await host.PostAsJsonAsync(Api(previewPath), firstPreview with { heroId = eligible[0] }, ct))
                Assert.Equal(HttpStatusCode.Conflict, bannedPreview.StatusCode);

            var deadline = replay.GetProperty("deadlineAt").GetDateTimeOffset();
            var preview = await Send(host, previewPath, firstPreview);
            Assert.Equal(pickVersion, Version(preview));
            Assert.Equal(deadline, preview.GetProperty("deadlineAt").GetDateTimeOffset());
            Assert.Equal(picks[0], preview.GetProperty("previewHeroId").GetString());
            Assert.Empty(preview.GetProperty("host").GetProperty("picks").EnumerateArray());
            preview = await Send(host, previewPath, firstPreview with { heroId = picks[1], sequence = 3 });
            Assert.Equal(picks[1], preview.GetProperty("previewHeroId").GetString());
            preview = await Send(host, previewPath, firstPreview with { sequence = 2 });
            Assert.Equal(3, preview.GetProperty("previewSequence").GetInt32());
            observer = await Read(guest, draftPath);
            Assert.Equal(3, observer.GetProperty("previewSequence").GetInt32());
            Assert.Equal(picks[1], observer.GetProperty("previewHeroId").GetString());

            preview = await Send(host, previewPath, new { draftId, version = pickVersion, sequence = 4 });
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("previewHeroId").ValueKind);
            Assert.Equal(4, preview.GetProperty("previewSequence").GetInt32());
            await Send(host, previewPath, firstPreview with { sequence = 5 });
            var requests = new[] { RequestId(), RequestId() }.Select(requestId => host.PostAsJsonAsync(
                Api(draftPath + "/actions"), new { requestId, draftId, version = pickVersion, kind = "pick", heroIds = new[] { picks[0] } }, ct));
            var replies = await Task.WhenAll(requests);
            try
            {
                Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.OK);
                Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.Conflict);
            }
            finally { foreach (var reply in replies) reply.Dispose(); }
            replay = await Read(host, draftPath);
            Assert.Equal(JsonValueKind.Null, replay.GetProperty("previewHeroId").ValueKind);
            Assert.Equal(0, replay.GetProperty("previewSequence").GetInt32());
            using (var latePreview = await host.PostAsJsonAsync(Api(previewPath), firstPreview with { sequence = 6 }, ct))
                Assert.Equal(HttpStatusCode.Conflict, latePreview.StatusCode);
            Assert.Equal(2, replay.GetProperty("picksRemainingInWindow").GetInt32());
            var pairDeadline = replay.GetProperty("deadlineAt").GetDateTimeOffset();
            await Send(guest, previewPath, firstPreview with { version = Version(replay), heroId = picks[1] });
            replay = await Action(guest, draftId, Version(replay), "pick", picks[1]);
            Assert.Equal(1, replay.GetProperty("picksRemainingInWindow").GetInt32());
            Assert.Equal(pairDeadline, replay.GetProperty("deadlineAt").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, replay.GetProperty("previewHeroId").ValueKind);
            Assert.Equal(0, replay.GetProperty("previewSequence").GetInt32());
            for (int i = 2; i < order.Length; i++)
                replay = await Action(order[i], draftId, Version(replay), "pick", picks[i]);
            Assert.Equal("ban", Phase(replay));
            Assert.Equal(picks[4], replay.GetProperty("host").GetProperty("protectedHeroId").GetString());
            Assert.Equal(picks[5], replay.GetProperty("guest").GetProperty("protectedHeroId").GetString());
            using (var protectedPick = await host.PostAsJsonAsync(Api(draftPath + "/actions"),
                new { requestId = RequestId(), draftId, version = Version(replay), kind = "ban", heroIds = new[] { picks[5] } }, ct))
                Assert.Equal(HttpStatusCode.BadRequest, protectedPick.StatusCode);

            version = Version(replay);
            await Action(host, draftId, version, "ban", picks[1]);
            observer = await Read(guest, draftPath);
            Assert.Equal(JsonValueKind.Null, observer.GetProperty("host").GetProperty("ban").ValueKind);
            var bothBans = await Action(guest, draftId, version, "ban", picks[0]);
            Assert.Equal("formation", Phase(bothBans));
            Assert.Equal(picks[1], bothBans.GetProperty("host").GetProperty("ban").GetString());

            version = Version(bothBans);
            var hostFormation = new[] { picks[8], picks[7], picks[4], picks[3] };
            var guestFormation = new[] { picks[9], picks[6], picks[5], picks[2] };
            await Action(host, draftId, version, "formation", hostFormation);
            observer = await Read(guest, draftPath);
            Assert.Empty(observer.GetProperty("host").GetProperty("formation").EnumerateArray());
            var ready = await Action(guest, draftId, version, "formation", guestFormation);
            Assert.Equal("battle", Phase(ready));

            var opening = (await Read(guest, battlePath)).GetProperty("snapshot");
            string activeActorId = opening.GetProperty("currentActorId").GetString()!;
            var activeActor = opening.GetProperty("units").EnumerateArray().Single(unit => unit.GetProperty("instanceId").GetString() == activeActorId);
            string skill = activeActor.GetProperty("form").GetProperty("skillIds")[0].GetString()!;
            bool hostTurn = activeActorId.StartsWith("ally-", StringComparison.Ordinal);
            var command = new
            {
                battleId = draftId, rulesVersion = opening.GetProperty("rulesVersion").GetInt32(), sequence = 1, commandId = RequestId(),
                actorId = activeActorId, skillId = skill, targetId = (hostTurn ? "enemy-" : "ally-") + 1,
                useSoulBurn = false, contentHash = ready.GetProperty("contentHash").GetString()
            };
            var presented = new { battleId = draftId, commandSequence = 0UL,
                eventSequence = opening.GetProperty("lastEventSequence").GetUInt64(), actorId = activeActorId };
            await Send(host, battlePath + "/ready", presented);
            await Send(guest, battlePath + "/ready", presented);
            using (var wrongSeat = await (hostTurn ? guest : host).PostAsJsonAsync(Api(battlePath + "/commands"), command, ct))
                Assert.Equal(HttpStatusCode.Forbidden, wrongSeat.StatusCode);
            var accepted = await Send(hostTurn ? host : guest, battlePath + "/commands", command);
            Assert.Equal(0, accepted.GetProperty("status").GetInt32());

            await using var restart = Configure(source, connection);
            using var resumed = restart.CreateClient();
            resumed.DefaultRequestHeaders.Authorization = (hostTurn ? host : guest).DefaultRequestHeaders.Authorization;
            var snapshot = (await Read(resumed, battlePath)).GetProperty("snapshot");
            Assert.Equal(1UL, snapshot.GetProperty("lastCommandSequence").GetUInt64());
            var duplicate = await Send(resumed, battlePath + "/commands", command);
            Assert.Equal(1, duplicate.GetProperty("status").GetInt32());

            using var guestLeaves = await guest.PostAsync(Api(roomPath + "/leave"), null, ct);
            Assert.Equal(HttpStatusCode.NoContent, guestLeaves.StatusCode);
            Assert.Equal("cancelled", Phase(await Read(host, draftPath)));
            var next = await Send(host, draftPath + "/start", new { requestId = RequestId() });
            Assert.NotEqual(draftId, next.GetProperty("id").GetString());
            Assert.Equal("preban", Phase(next));
            using (var oldAction = await host.PostAsJsonAsync(Api(draftPath + "/actions"), firstPreban, ct))
                Assert.Equal(HttpStatusCode.Conflict, oldAction.StatusCode);

            // Use a past persisted deadline to exercise server time without sleeping thirty seconds.
            await using var sql = new MySqlConnection(connection);
            await sql.OpenAsync(ct);
            using var expire = new MySqlCommand("""
                UPDATE arena_room_draft SET state = JSON_SET(state, '$.DeadlineAt', @deadline)
                WHERE draft_id = @draft;
                """, sql);
            expire.Parameters.AddWithValue("@deadline", DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"));
            expire.Parameters.AddWithValue("@draft", next.GetProperty("id").GetString());
            Assert.Equal(1, await expire.ExecuteNonQueryAsync(ct));
            var advanced = await Read(host, draftPath);
            Assert.Equal("pick", Phase(advanced));
            Assert.Equal(2, advanced.GetProperty("host").GetProperty("prebans").GetArrayLength());
            Assert.Equal(2, advanced.GetProperty("guest").GetProperty("prebans").GetArrayLength());
        }
        finally
        {
            foreach (var member in new[] { guest, host, spectator })
            {
                using var leave = await member.PostAsync(Api(roomPath + "/leave"), null, ct);
            }
        }

        async Task<JsonElement> Action(HttpClient actor, string draftId, int version, string kind, params string[] heroIds) =>
            await Send(actor, draftPath + "/actions", new { requestId = RequestId(), draftId, version, kind, heroIds });
        async Task Login(HttpClient client, string label)
        {
            var result = await Send(client, "sessions", new { displayName = label });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.GetProperty("token").GetString());
        }
        async Task<JsonElement> Send(HttpClient client, string route, object body)
        {
            using var response = await client.PostAsJsonAsync(Api(route), body, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        async Task<JsonElement> Read(HttpClient client, string route)
        {
            using var response = await client.GetAsync(Api(route), ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
    }

    [MySqlFact]
    public async Task HostChangesRoomDraftSettingsAtomicallyBeforeDraftStarts()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = Configure(source, connection);
        using var host = factory.CreateClient();
        using var guest = factory.CreateClient();
        using var outsider = factory.CreateClient();

        await Login(host, "设置房主");
        await Login(guest, "设置对手");
        await Login(outsider, "房外访客");
        var room = (await Send(host, "rooms", new { name = "房间选禁设置", rule = "basic" })).GetProperty("room");
        string roomPath = "rooms/" + room.GetProperty("id").GetString();
        string settings = roomPath + "/draft/settings";
        string? otherRoomPath = null;
        try
        {
            AssertSettings(room, 2, "host", true);
            await Send(guest, roomPath + "/join", new { });
            await ExpectFailure(guest, settings, new { prebanCount = 1 }, HttpStatusCode.Forbidden, "draft_host_only");
            await ExpectFailure(outsider, settings, new { banProtection = false }, HttpStatusCode.Forbidden, "not_in_room");
            await ExpectFailure(host, settings, new { }, HttpStatusCode.BadRequest, "invalid_draft_settings");
            await ExpectFailure(host, settings, new { prebanCount = 4 }, HttpStatusCode.BadRequest, "invalid_draft_settings");
            await ExpectFailure(host, settings, new { firstPick = "unknown" }, HttpStatusCode.BadRequest, "invalid_draft_settings");

            // Concurrent updates to different fields must preserve both settings.
            await Task.WhenAll(Send(host, settings, new { prebanCount = 3 }),
                Send(host, settings, new { firstPick = "guest" }));
            var updated = (await Send(host, settings, new { banProtection = false })).GetProperty("room");
            AssertSettings(updated, 3, "guest", false);
            var guestRoom = (await guest.GetFromJsonAsync<JsonElement>(Api(roomPath), ct)).GetProperty("room");
            AssertSettings(guestRoom, 3, "guest", false);

            var draft = await Send(host, roomPath + "/draft/start", new { requestId = RequestId() });
            Assert.Equal("preban", Phase(draft));
            AssertSettings(draft, 3, "guest", false);
            var eligible = draft.GetProperty("eligibleHeroIds").EnumerateArray().Select(row => row.GetString()!).ToArray();
            Assert.True(eligible.Length >= 16);
            var id = draft.GetProperty("id").GetString();
            int version = Version(draft);
            await Send(host, roomPath + "/draft/actions",
                new { requestId = RequestId(), draftId = id, version, kind = "preban", heroIds = eligible[..3] });
            var pick = await Send(guest, roomPath + "/draft/actions",
                new { requestId = RequestId(), draftId = id, version, kind = "preban", heroIds = eligible[3..6] });
            Assert.Equal("pick", Phase(pick));
            Assert.Equal("guest", pick.GetProperty("activeSide").GetString());
            await ExpectFailure(host, settings, new { prebanCount = 0 }, HttpStatusCode.Conflict, "draft_already_started");
            guestRoom = (await guest.GetFromJsonAsync<JsonElement>(Api(roomPath), ct)).GetProperty("room");
            AssertSettings(guestRoom, 3, "guest", false);

            var otherRoom = (await Send(outsider, "rooms", new { name = "其他规则设置", rule = "round" })).GetProperty("room");
            otherRoomPath = "rooms/" + otherRoom.GetProperty("id").GetString();
            await ExpectFailure(outsider, otherRoomPath + "/draft/settings", new { prebanCount = 1 },
                HttpStatusCode.Conflict, "draft_rule_unsupported");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await guest.PostAsJsonAsync(Api(roomPath + "/leave"), new { }, cleanup.Token);
            await host.PostAsJsonAsync(Api(roomPath + "/leave"), new { }, cleanup.Token);
            if (otherRoomPath != null)
                await outsider.PostAsJsonAsync(Api(otherRoomPath + "/leave"), new { }, cleanup.Token);
        }

        static void AssertSettings(JsonElement target, int count, string first, bool protection)
        {
            Assert.Equal(count, target.GetProperty("prebanCount").GetInt32());
            Assert.Equal(first, target.GetProperty("firstPick").GetString());
            Assert.Equal(protection, target.GetProperty("banProtection").GetBoolean());
        }

        async Task Login(HttpClient client, string name)
        {
            var session = await Send(client, "sessions", new { displayName = name });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        }

        async Task<JsonElement> Send(HttpClient client, string route, object body)
        {
            using var response = await client.PostAsJsonAsync(Api(route), body, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }

        async Task ExpectFailure(HttpClient client, string route, object body, HttpStatusCode status, string code)
        {
            using var response = await client.PostAsJsonAsync(Api(route), body, ct);
            Assert.Equal(status, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(code, error.GetProperty("code").GetString());
        }
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory, string connection) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection })));
    private static string Api(string route) => "/api/arena/" + route;
    private static int Version(JsonElement snapshot) => snapshot.GetProperty("version").GetInt32();
    private static string? Phase(JsonElement snapshot) => snapshot.GetProperty("phase").GetString();
    private static string RequestId() => Guid.NewGuid().ToString("N");
}
