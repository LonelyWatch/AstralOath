using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class ArenaRoomTests
{
    private const string Arena = "/api/arena";

    [Fact]
    public async Task EndpointsRejectMissingIdentityAndInvalidRoomData()
    {
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, "");
        using var client = factory.CreateClient();
        using var unauthenticated = await client.GetAsync(Arena + "/rooms");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var invalid = await client.PostAsJsonAsync(Arena + "/sessions", new { displayName = "\u0001", avatarId = "c1001" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [MySqlFact]
    public async Task AuditRecordsCommittedChangesAndDeduplicatesAuthenticatedClientEvents()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await DatabaseMigrator.ApplyAsync(connection, deadline.Token);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, connection);
        using var host = factory.CreateClient();
        using var guest = factory.CreateClient();
        string refreshId = Guid.NewGuid().ToString("N");
        var refresh = new { eventId = refreshId, name = "directory_refresh" };
        using var unauthorized = await guest.PostAsJsonAsync(Arena + "/telemetry", refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await Authorize(host, "留痕房主");
        await Authorize(guest, "留痕访客");
        using var accepted = await guest.PostAsJsonAsync(Arena + "/telemetry", refresh);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        using var duplicate = await guest.PostAsJsonAsync(Arena + "/telemetry", refresh);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        using var spoof = await guest.PostAsJsonAsync(Arena + "/telemetry", new { eventId = Guid.NewGuid().ToString("N"), name = "arena.room.create" });
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var arbitrary = await guest.PostAsJsonAsync(Arena + "/telemetry", new { eventId = Guid.NewGuid().ToString("N"), name = "directory_search", query = "私密搜索" });
        Assert.Equal(HttpStatusCode.BadRequest, arbitrary.StatusCode);

        var created = await Post(host, "/rooms", new { name = "留痕房间", password = "0042", allowChat = true, rule = "basic" });
        string roomId = created.GetProperty("room").GetProperty("id").GetString()!;
        using var wrong = await guest.PostAsJsonAsync(Arena + $"/rooms/{roomId}/join", new { password = "0000" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var joined = await Post(guest, $"/rooms/{roomId}/join", new { password = "0042" });
        await Post(guest, $"/rooms/{roomId}/join", new { password = "0042" });
        string hostId = created.GetProperty("room").GetProperty("host").GetProperty("id").GetString()!;
        string guestId = joined.GetProperty("members")[1].GetProperty("id").GetString()!;
        await Post(guest, $"/rooms/{roomId}/messages", new { text = "聊天私密正文" });
        foreach (var player in new[] { guest, guest, host })
        {
            using var leave = await player.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        }

        await using var mysql = new MySqlConnection(connection);
        await mysql.OpenAsync(deadline.Token);
        using var read = new MySqlCommand("""
            SELECT actor_id, operation, subject_type, subject_id, client_event_id
            FROM game_operation_audit
            WHERE subject_id = @room OR client_event_id = @event ORDER BY id;
            """, mysql);
        read.Parameters.AddWithValue("@room", roomId);
        read.Parameters.AddWithValue("@event", refreshId);
        var actual = new List<(string, string, string?, string?, string?)>();
        await using var rows = await read.ExecuteReaderAsync(deadline.Token);
        while (await rows.ReadAsync(deadline.Token))
            actual.Add((rows.GetString(0), rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2),
                rows.IsDBNull(3) ? null : rows.GetString(3), rows.IsDBNull(4) ? null : rows.GetString(4)));
        Assert.Equal(new[]
        {
            (guestId, "arena.directory.refresh", (string?)null, (string?)null, refreshId),
            (hostId, "arena.room.create", "arena_room", roomId, (string?)null),
            (guestId, "arena.room.join", "arena_room", roomId, (string?)null),
            (guestId, "arena.chat.send", "arena_room", roomId, (string?)null),
            (guestId, "arena.room.leave", "arena_room", roomId, (string?)null),
            (hostId, "arena.room.leave", "arena_room", roomId, (string?)null),
        }, actual);
    }

    [MySqlFact]
    public async Task TwoSessionsCanDiscoverJoinAndChatAcrossRestart()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await DatabaseMigrator.ApplyAsync(connection, deadline.Token);
        await using var source = new WebApplicationFactory<Program>();
        string roomName = "测试房" + Guid.NewGuid().ToString("N")[..8];
        string roomId;
        string guestToken;

        await using (var factory = WithDatabase(source, connection))
        {
            using var owner = factory.CreateClient();
            using var guest = factory.CreateClient();
            string ownerToken = await Authorize(owner, "房主");
            guestToken = await Authorize(guest, "访客");
            Assert.NotEqual(ownerToken, guestToken);

            var created = await Post(owner, "/rooms", new { name = roomName, password = "0042", allowChat = true, rule = "basic" });
            var room = created.GetProperty("room");
            roomId = room.GetProperty("id").GetString()!;
            Assert.True(room.GetProperty("requiresPassword").GetBoolean());
            Assert.False(room.TryGetProperty("password", out _));

            var directory = await Get(guest, "/rooms?visibility=private&sort=players&query=" + Uri.EscapeDataString(roomName));
            Assert.Equal(roomId, Assert.Single(directory.GetProperty("rooms").EnumerateArray()).GetProperty("id").GetString());
            Assert.Empty((await Get(guest, "/rooms?visibility=public&query=" + Uri.EscapeDataString(roomName)))
                .GetProperty("rooms").EnumerateArray());
            using var invalidDirection = await guest.GetAsync(Arena + "/rooms?direction=sideways");
            Assert.Equal(HttpStatusCode.BadRequest, invalidDirection.StatusCode);

            using var wrong = await guest.PostAsJsonAsync(Arena + $"/rooms/{roomId}/join", new { password = "1234" });
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            using var forbidden = await guest.GetAsync(Arena + $"/rooms/{roomId}/messages");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var joined = await Post(guest, $"/rooms/{roomId}/join", new { password = "0042" });
            Assert.Equal(2, joined.GetProperty("members").GetArrayLength());
            var repeated = await Post(guest, $"/rooms/{roomId}/join", new { password = "0042" });
            Assert.Equal(2, repeated.GetProperty("room").GetProperty("memberCount").GetInt32());

            var sent = await Post(guest, $"/rooms/{roomId}/messages", new { text = "你好，竞技场" });
            long id = sent.GetProperty("id").GetInt64();
            var inbox = await Get(owner, $"/rooms/{roomId}/messages?after={id - 1}");
            Assert.Equal("你好，竞技场", Assert.Single(inbox.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());
            Assert.Empty((await Get(owner, $"/rooms/{roomId}/messages?after={id}")).GetProperty("messages").EnumerateArray());

            using var leave = await owner.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        }

        await using var restarted = WithDatabase(source, connection);
        using var restored = restarted.CreateClient();
        restored.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", guestToken);
        var persisted = await Get(restored, "/rooms/current");
        Assert.Equal(roomId, persisted.GetProperty("room").GetProperty("id").GetString());
        Assert.Single(persisted.GetProperty("members").EnumerateArray());
        Assert.Equal("访客", persisted.GetProperty("room").GetProperty("host").GetProperty("displayName").GetString());
        Assert.Single((await Get(restored, $"/rooms/{roomId}/messages")).GetProperty("messages").EnumerateArray());
        using var finalLeave = await restored.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, finalLeave.StatusCode);
        using var cleared = await restored.GetAsync(Arena + "/rooms/current");
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Empty((await Get(restored, "/rooms?query=" + Uri.EscapeDataString(roomName))).GetProperty("rooms").EnumerateArray());
    }

    [MySqlFact]
    public async Task DirectorySortsBothKeysInEitherDirectionBeforeReturningRooms()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, CancellationToken.None);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, connection);
        using var firstHost = factory.CreateClient();
        using var secondHost = factory.CreateClient();
        using var guest = factory.CreateClient();
        await Authorize(firstHost, "较早房主");
        await Authorize(secondHost, "较晚房主");
        await Authorize(guest, "排序访客");
        string prefix = "排序" + Guid.NewGuid().ToString("N")[..8];
        var first = await Post(firstHost, "/rooms", new { name = prefix + "甲", allowChat = true, rule = "basic" });
        var second = await Post(secondHost, "/rooms", new { name = prefix + "乙", allowChat = true, rule = "basic" });
        string firstId = first.GetProperty("room").GetProperty("id").GetString()!;
        string secondId = second.GetProperty("room").GetProperty("id").GetString()!;
        try
        {
            await Post(guest, $"/rooms/{firstId}/join", new { });
            Assert.Equal(new[] { firstId, secondId }, await Sort("players", ""));
            Assert.Equal(new[] { firstId, secondId }, await Sort("players", "&direction=desc"));
            Assert.Equal(new[] { secondId, firstId }, await Sort("players", "&direction=asc"));
            Assert.Equal(new[] { secondId, firstId }, await Sort("recent", "&direction=desc"));
            Assert.Equal(new[] { firstId, secondId }, await Sort("recent", "&direction=asc"));
        }
        finally
        {
            using var guestLeave = await guest.PostAsync(Arena + $"/rooms/{firstId}/leave", null);
            using var firstLeave = await firstHost.PostAsync(Arena + $"/rooms/{firstId}/leave", null);
            using var secondLeave = await secondHost.PostAsync(Arena + $"/rooms/{secondId}/leave", null);
        }

        async Task<string?[]> Sort(string key, string direction) =>
            (await Get(guest, $"/rooms?query={Uri.EscapeDataString(prefix)}&sort={key}{direction}"))
            .GetProperty("rooms").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
    }

    [MySqlFact]
    public async Task NativeStickerAndLegacyTextShareAuthenticatedMessageHistory()
    {
        const string sticker = "c1001_goodjob_001";
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, CancellationToken.None);
        await using var source = new WebApplicationFactory<Program>();
        string roomId;
        string hostToken;
        long stickerId;
        await using (var factory = WithDatabase(source, connection))
        {
            using var host = factory.CreateClient();
            using var outsider = factory.CreateClient();
            hostToken = await Authorize(host, "表情房主");
            await Authorize(outsider, "房间外");
            var room = await Post(host, "/rooms", new { name = "表情测试", allowChat = true, rule = "basic" });
            roomId = room.GetProperty("room").GetProperty("id").GetString()!;
            string route = $"/rooms/{roomId}/messages";
            using var forbidden = await outsider.PostAsJsonAsync(Arena + route, new { kind = "sticker", stickerId = sticker });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var unknown = await host.PostAsJsonAsync(Arena + route, new { kind = "sticker", stickerId = "https://example.invalid/image.png" });
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using var mixed = await host.PostAsJsonAsync(Arena + route, new { kind = "sticker", stickerId = sticker, text = "混合参数" });
            Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
            var sent = await Post(host, route, new { kind = "sticker", stickerId = sticker, text = "" });
            Assert.Equal("sticker", sent.GetProperty("kind").GetString());
            Assert.Equal(sticker, sent.GetProperty("stickerId").GetString());
            Assert.Equal("", sent.GetProperty("text").GetString());
            stickerId = sent.GetProperty("id").GetInt64();
            using var tooSoon = await host.PostAsJsonAsync(Arena + route, new { text = "限流" });
            Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
            using var secondMember = factory.CreateClient();
            await Authorize(secondMember, "旧版访客");
            await Post(secondMember, $"/rooms/{roomId}/join", new { });
            var text = await Post(secondMember, route, new { text = "旧文本" });
            Assert.Equal("text", text.GetProperty("kind").GetString());
            Assert.Equal("旧文本", text.GetProperty("text").GetString());
            using var leave = await secondMember.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        }

        await using var restarted = WithDatabase(source, connection);
        using var restored = restarted.CreateClient();
        restored.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        var history = (await Get(restored, $"/rooms/{roomId}/messages")).GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(new[] { "sticker", "text" }, history.Select(row => row.GetProperty("kind").GetString()).ToArray());
        Assert.Equal(stickerId, history[0].GetProperty("id").GetInt64());
        Assert.Equal(sticker, history[0].GetProperty("stickerId").GetString());
        Assert.False(history[1].TryGetProperty("stickerId", out _));
        var delta = (await Get(restored, $"/rooms/{roomId}/messages?after={stickerId}")).GetProperty("messages");
        Assert.Equal("旧文本", Assert.Single(delta.EnumerateArray()).GetProperty("text").GetString());
        using var final = await restored.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, final.StatusCode);
    }

    [MySqlFact]
    public async Task ChatPermissionAndValidationAreEnforcedOnTheServer()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, CancellationToken.None);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, connection);
        using var owner = factory.CreateClient();
        await Authorize(owner, "房主");

        using var invalid = await owner.PostAsJsonAsync(Arena + "/rooms",
            new { name = new string('房', 29), password = "111", allowChat = false, rule = "no_duplicate_class" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Post(owner, "/rooms", new { name = "禁聊", allowChat = false, rule = "no_duplicate_class" });
        var roomId = created.GetProperty("room").GetProperty("id").GetString();
        using var denied = await owner.PostAsJsonAsync(Arena + $"/rooms/{roomId}/messages", new { text = "拒绝" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedSticker = await owner.PostAsJsonAsync(Arena + $"/rooms/{roomId}/messages",
            new { kind = "sticker", stickerId = "c1001_goodjob_001" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedSticker.StatusCode);
        var rejection = await deniedSticker.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("chat_disabled", rejection.GetProperty("code").GetString());
        using var leave = await owner.PostAsync(Arena + $"/rooms/{roomId}/leave", null);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [MySqlFact]
    public async Task ConcurrentJoinsAllowOnlyOneRoomForTheSameGuest()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, CancellationToken.None);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, connection);
        using var firstHost = factory.CreateClient();
        using var secondHost = factory.CreateClient();
        using var guest = factory.CreateClient();
        await Authorize(firstHost, "房主一");
        await Authorize(secondHost, "房主二");
        await Authorize(guest, "访客");
        var first = await Post(firstHost, "/rooms", new { name = "房间一", allowChat = true, rule = "basic" });
        var second = await Post(secondHost, "/rooms", new { name = "房间二", allowChat = true, rule = "draft" });
        string firstId = first.GetProperty("room").GetProperty("id").GetString()!;
        string secondId = second.GetProperty("room").GetProperty("id").GetString()!;
        try
        {
            var joinAttempts = await Task.WhenAll(PostJoin(firstId), PostJoin(secondId));
            using var firstJoin = joinAttempts[0];
            using var secondJoin = joinAttempts[1];
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                new[] { firstJoin.StatusCode, secondJoin.StatusCode }.Order().ToArray());
            var memberCounts = await Task.WhenAll(MemberCount(firstHost, firstId), MemberCount(secondHost, secondId));
            Assert.Equal(new[] { 1, 2 }, memberCounts.Order().ToArray());
        }
        finally
        {
            using var guestLeaveFirst = await guest.PostAsync(Arena + $"/rooms/{firstId}/leave", null);
            using var guestLeaveSecond = await guest.PostAsync(Arena + $"/rooms/{secondId}/leave", null);
            using var hostLeaveFirst = await firstHost.PostAsync(Arena + $"/rooms/{firstId}/leave", null);
            using var hostLeaveSecond = await secondHost.PostAsync(Arena + $"/rooms/{secondId}/leave", null);
        }

        Task<HttpResponseMessage> PostJoin(string id) =>
            guest.PostAsJsonAsync(Arena + $"/rooms/{id}/join", new { });
        static async Task<int> MemberCount(HttpClient host, string id) =>
            (await Get(host, $"/rooms/{id}")).GetProperty("room").GetProperty("memberCount").GetInt32();
    }

    [MySqlFact]
    public async Task ConcurrentJoinersCannotOverfillTheLastSeat()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, CancellationToken.None);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = WithDatabase(source, connection);
        using var host = factory.CreateClient();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        await Authorize(host, "容量测试房主");
        await Authorize(first, "访客甲");
        await Authorize(second, "访客乙");
        var created = await Post(host, "/rooms", new { name = "容量测试", allowChat = true, rule = "basic" });
        string roomId = created.GetProperty("room").GetProperty("id").GetString()!;
        var seededPlayers = Enumerable.Range(0, 48).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
        await using var mysql = new MySqlConnection(connection);
        await mysql.OpenAsync();
        try
        {
            // Seed only the already occupied seats. The two competing requests
            // below enter through HTTP and must serialize on the same room row.
            await using (var transaction = await mysql.BeginTransactionAsync())
            {
                foreach (string id in seededPlayers)
                {
                    using var seed = new MySqlCommand("""
                        INSERT INTO arena_player (id, token_hash, display_name, avatar_id, created_at)
                        VALUES (@id, @hash, '测试席位', 'c1001', UTC_TIMESTAMP(6));
                        INSERT INTO arena_room_member (player_id, room_id, joined_at)
                        VALUES (@id, @room, UTC_TIMESTAMP(6));
                        """, mysql, transaction);
                    seed.Parameters.AddWithValue("@id", id);
                    seed.Parameters.AddWithValue("@hash", RandomNumberGenerator.GetBytes(32));
                    seed.Parameters.AddWithValue("@room", roomId);
                    await seed.ExecuteNonQueryAsync();
                }
                await transaction.CommitAsync();
            }
            var replies = await Task.WhenAll(first.PostAsJsonAsync(Arena + $"/rooms/{roomId}/join", new { }),
                second.PostAsJsonAsync(Arena + $"/rooms/{roomId}/join", new { }));
            using var firstReply = replies[0];
            using var secondReply = replies[1];
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
                replies.Select(reply => reply.StatusCode).Order().ToArray());
            using var rejection = replies.Single(reply => reply.StatusCode == HttpStatusCode.Conflict);
            var rejected = await rejection.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("room_full", rejected.GetProperty("code").GetString());
            Assert.Equal(50, (await Get(host, $"/rooms/{roomId}")).GetProperty("members").GetArrayLength());
        }
        finally
        {
            using var removeRoom = new MySqlCommand("DELETE FROM arena_room WHERE id = @id;", mysql);
            removeRoom.Parameters.AddWithValue("@id", roomId);
            await removeRoom.ExecuteNonQueryAsync();
            foreach (string id in seededPlayers)
            {
                using var remove = new MySqlCommand("DELETE FROM arena_player WHERE id = @id;", mysql);
                remove.Parameters.AddWithValue("@id", id);
                await remove.ExecuteNonQueryAsync();
            }
        }
    }

    private static WebApplicationFactory<Program> WithDatabase(WebApplicationFactory<Program> source, string connection) =>
        source.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection })));

    private static async Task<string> Authorize(HttpClient client, string name)
    {
        var value = await Post(client, "/sessions", new { displayName = name, avatarId = "c1001" });
        string token = value.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private static async Task<JsonElement> Post(HttpClient client, string route, object body)
    {
        using var response = await client.PostAsJsonAsync(Arena + route, body);
        return await Json(response);
    }

    private static async Task<JsonElement> Get(HttpClient client, string route)
    {
        using var response = await client.GetAsync(Arena + route);
        return await Json(response);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
