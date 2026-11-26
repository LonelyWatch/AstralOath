using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using AstralOath.Server.Mail;
using MySqlConnector;
using Xunit;

public sealed class MailReviewRegressionTests
{
    private static string Database => Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;

    [MySqlFact]
    public async Task LateCommitOfLowerOutboxIdReachesEveryHostWithoutReplayOrGlobalDeletion()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(Database, ct);
        await using var app = new MailApiTests.Factory(Database);
        using var a = app.CreateClient();
        using var b = app.CreateClient();
        await MailTransactionTests.Login(a, ct);
        await MailTransactionTests.Login(b, ct);
        string pa = (await a.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("playerId").GetString()!;
        string pb = (await b.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("playerId").GetString()!;
        var firstHost = new MailStore(Database, MailCatalog.Load());
        var secondHost = new MailStore(Database, MailCatalog.Load());
        var firstHub = new MailEventHub();
        var secondHub = new MailEventHub();
        var aFirst = firstHub.Subscribe(pa);
        var bFirst = firstHub.Subscribe(pb);
        var aSecond = secondHub.Subscribe(pa);
        var bSecond = secondHub.Subscribe(pb);
        long firstPosition = await firstHost.DispatchOutbox(0, firstHub, ct);
        long secondPosition = await secondHost.DispatchOutbox(0, secondHub, ct);
        foreach (var subscription in new[] { aFirst, bFirst, aSecond, bSecond })
            while (subscription.Channel.Reader.TryRead(out _)) { }

        await using var adb = new MySqlConnection(Database);
        await using var bdb = new MySqlConnection(Database);
        await adb.OpenAsync(ct);
        await bdb.OpenAsync(ct);
        await using var atx = await adb.BeginTransactionAsync(ct);
        await using var btx = await bdb.BeginTransactionAsync(ct);
        long smallerId = await Add(adb, atx, pa, 1001, ct);
        long largerId = await Add(bdb, btx, pb, 1002, ct);
        Assert.True(smallerId < largerId);
        // Explicit barrier: n+1 is committed and dispatched while n is still uncommitted.
        await btx.CommitAsync(ct);
        firstPosition = await firstHost.DispatchOutbox(firstPosition, firstHub, ct);
        secondPosition = await secondHost.DispatchOutbox(secondPosition, secondHub, ct);
        Assert.True(bFirst.Channel.Reader.TryRead(out long firstB));
        Assert.True(bSecond.Channel.Reader.TryRead(out long secondB));
        Assert.Equal(1002, firstB);
        Assert.Equal(1002, secondB);
        Assert.False(aFirst.Channel.Reader.TryRead(out _));
        Assert.False(aSecond.Channel.Reader.TryRead(out _));
        await atx.CommitAsync(ct);
        firstPosition = await firstHost.DispatchOutbox(firstPosition, firstHub, ct);
        secondPosition = await secondHost.DispatchOutbox(secondPosition, secondHub, ct);
        Assert.True(aFirst.Channel.Reader.TryRead(out long firstA), "Late lower ID must be delivered on first Host.");
        Assert.True(aSecond.Channel.Reader.TryRead(out long secondA), "Late lower ID must be delivered on every Host.");
        Assert.Equal(1001, firstA);
        Assert.Equal(1001, secondA);
        await firstHost.DispatchOutbox(firstPosition, firstHub, ct);
        await secondHost.DispatchOutbox(secondPosition, secondHub, ct);
        Assert.False(aFirst.Channel.Reader.TryRead(out _));
        Assert.False(bFirst.Channel.Reader.TryRead(out _));
        using var persisted = new MySqlCommand("SELECT COUNT(*) FROM mail_outbox WHERE id IN (@a,@b);", adb);
        persisted.Parameters.AddWithValue("@a", smallerId);
        persisted.Parameters.AddWithValue("@b", largerId);
        Assert.Equal(2, Convert.ToInt32(await persisted.ExecuteScalarAsync(ct)));
        // A restarted third Host has independent acknowledgement and must see both records too.
        var restarted = new MailStore(Database, MailCatalog.Load());
        var restartedHub = new MailEventHub();
        var restartedA = restartedHub.Subscribe(pa);
        var restartedB = restartedHub.Subscribe(pb);
        await restarted.DispatchOutbox(0, restartedHub, ct);
        Assert.True(restartedA.Channel.Reader.TryRead(out _));
        Assert.True(restartedB.Channel.Reader.TryRead(out _));
    }

    private static async Task<long> Add(MySqlConnection db, MySqlTransaction tx, string player, long version, CancellationToken ct)
    {
        using var command = new MySqlCommand("UPDATE mailbox SET version=@version WHERE realm_id='undying-memory' AND player_id=@player; INSERT INTO mail_outbox(realm_id,player_id,version) VALUES('undying-memory',@player,@version); SELECT LAST_INSERT_ID();", db, tx);
        command.Parameters.AddWithValue("@player", player);
        command.Parameters.AddWithValue("@version", version);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    [MySqlFact]
    public async Task VersionDispatchBoundsEachBatchAndVisitsEveryActiveOwner()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        var store = new MailStore(Database, MailCatalog.Load());
        var hub = new MailEventHub();
        var subscriptions = new List<(Guid Id, System.Threading.Channels.Channel<long> Channel)>();
        await using var db = new MySqlConnection(Database);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        for (int i = 0; i < 205; i++)
        {
            string player = Guid.NewGuid().ToString("N");
            using var insert = new MySqlCommand("INSERT INTO arena_player(id,token_hash,display_name,avatar_id,created_at) VALUES(@player,@token,'MailDispatchQA','c1001',UTC_TIMESTAMP(6)); INSERT INTO mailbox(realm_id,player_id,version) VALUES('undying-memory',@player,1); INSERT INTO mail_outbox(realm_id,player_id,version) VALUES('undying-memory',@player,1);", db, tx);
            insert.Parameters.AddWithValue("@player", player);
            insert.Parameters.AddWithValue("@token", System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await insert.ExecuteNonQueryAsync();
            subscriptions.Add(hub.Subscribe(player));
        }
        await tx.CommitAsync();
        long position = await store.DispatchOutbox(0, hub, default);
        int received = subscriptions.Count(s => s.Channel.Reader.TryRead(out _));
        Assert.Equal(100, received);
        position = await store.DispatchOutbox(position, hub, default);
        received += subscriptions.Count(s => s.Channel.Reader.TryRead(out _));
        Assert.Equal(200, received);
        await store.DispatchOutbox(position, hub, default);
        received += subscriptions.Count(s => s.Channel.Reader.TryRead(out _));
        Assert.Equal(205, received);
    }

    [MySqlFact]
    public async Task UppercaseDeliveryGuidPersistsReadAtAcrossRequests()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        await MailTransactionTests.Login(http, default);
        string id = await MailTransactionTests.Send(http, "gm", default);
        var opened = await http.GetFromJsonAsync<JsonElement>("/api/mail/" + id.ToUpperInvariant());
        Assert.Equal(id, opened.GetProperty("deliveryId").GetString());
        await using var db = new MySqlConnection(Database);
        await db.OpenAsync();
        using var read = new MySqlCommand("SELECT read_at FROM mail_delivery WHERE delivery_id=@id;", db);
        read.Parameters.AddWithValue("@id", id);
        Assert.IsType<DateTime>(await read.ExecuteScalarAsync());
        var again = await http.GetFromJsonAsync<JsonElement>("/api/mail/" + id);
        Assert.Equal(opened.GetProperty("readAt").GetString(), again.GetProperty("readAt").GetString());
    }

    [MySqlFact]
    public async Task GmResourceWritesAdvanceIndependentRevisionWithSameMailboxVersionAndExposeTrueBalances()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        await MailTransactionTests.Login(http, default);
        var before = await http.GetFromJsonAsync<JsonElement>("/api/mail/summary");
        foreach (var change in new[] { ("energy", 401), ("gold", 123456), ("skystones", 789) })
        {
            var request = new { requestId = Guid.NewGuid().ToString("N"), action = "set", key = change.Item1, value = change.Item2 };
            using var reply = await http.PostAsJsonAsync("/api/development/gm/actions", request);
            reply.EnsureSuccessStatusCode();
        }
        var after = await http.GetFromJsonAsync<JsonElement>("/api/mail/summary");
        Assert.Equal(before.GetProperty("version").GetInt64(), after.GetProperty("version").GetInt64());
        Assert.Equal(401, after.GetProperty("energy").GetProperty("balance").GetInt32());
        Assert.True(after.GetProperty("energy").GetProperty("revision").GetInt64() > before.GetProperty("energy").GetProperty("revision").GetInt64());
        var balances = after.GetProperty("balances");
        Assert.Equal(123456, balances.GetProperty("gold").GetInt64());
        Assert.Equal(789, balances.GetProperty("crystals").GetInt64());
        Assert.Equal(after.GetProperty("energy").GetProperty("revision").GetInt64(), balances.GetProperty("revision").GetInt64());
        Assert.False(balances.TryGetProperty("pvpFlags", out _));
        var sameValueRequest = new { requestId = Guid.NewGuid().ToString("N"), action = "set", key = "energy", value = 401 };
        (await http.PostAsJsonAsync("/api/development/gm/actions", sameValueRequest)).EnsureSuccessStatusCode();
        var same = await http.GetFromJsonAsync<JsonElement>("/api/mail/summary");
        Assert.Equal(balances.GetProperty("revision").GetInt64(), same.GetProperty("balances").GetProperty("revision").GetInt64());
        string delivery = await MailTransactionTests.Send(http, "standard", default, new[] { new { kind = "energy", key = "energy", quantity = 2 } });
        (await http.PostAsJsonAsync("/api/mail/claim", new { requestId = Guid.NewGuid().ToString("N"), deliveryId = delivery })).EnsureSuccessStatusCode();
        var claimed = await http.GetFromJsonAsync<JsonElement>("/api/mail/summary");
        Assert.Equal(403, claimed.GetProperty("energy").GetProperty("balance").GetInt32());
        Assert.True(claimed.GetProperty("balances").GetProperty("revision").GetInt64() > balances.GetProperty("revision").GetInt64());
    }
}
