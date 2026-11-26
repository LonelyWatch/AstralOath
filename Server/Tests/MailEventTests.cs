using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Server.Foundation;
using AstralOath.Server.Mail;
using MySqlConnector;
using Xunit;

public sealed class MailEventTests
{
    [MySqlFact]
    public async Task VersionOutboxReachesStreamsAndRotationClosesOldSessionAndReconnectSyncs()
    {
        string database = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(database, ct);
        await using var app = new MailApiTests.Factory(database);
        using var http = app.CreateClient();
        await MailTransactionTests.Login(http, ct);
        var summary = await http.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct);
        string player = summary.GetProperty("playerId").GetString()!;
        using var streamReply = await http.GetAsync("/api/mail/events", HttpCompletionOption.ResponseHeadersRead, ct);
        streamReply.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await streamReply.Content.ReadAsStreamAsync(ct));
        Assert.Equal("sync", JsonDocument.Parse((await reader.ReadLineAsync(ct))!).RootElement.GetProperty("kind").GetString());
        await MailTransactionTests.Send(http, "standard", ct);
        var notification = JsonDocument.Parse((await reader.ReadLineAsync(ct))!).RootElement;
        Assert.Equal("mail", notification.GetProperty("kind").GetString());
        Assert.True(notification.GetProperty("version").GetInt64() > summary.GetProperty("version").GetInt64());
        byte[] token = RandomNumberGenerator.GetBytes(32);
        string bearer = Convert.ToHexStringLower(token);
        await using (var db = new MySqlConnection(database))
        {
            await db.OpenAsync(ct);
            using var sql = new MySqlCommand("UPDATE arena_player SET token_hash=@hash WHERE id=@id;", db);
            sql.Parameters.AddWithValue("@hash", SHA256.HashData(token));
            sql.Parameters.AddWithValue("@id", player);
            await sql.ExecuteNonQueryAsync(ct);
        }

        // A committed outbox event must never be sent to an invalidated bearer.
        var store = new MailStore(database, MailCatalog.Load());
        var preview = await store.PreviewSelf(player, new(TemplateId: "maintenance"), ct);
        await store.SendSelf(player, new(Guid.NewGuid().ToString("N"), preview.PreviewId), ct);
        Assert.Null(await reader.ReadLineAsync(ct));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/mail/summary", ct)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var reconnect = await http.GetAsync("/api/mail/events", HttpCompletionOption.ResponseHeadersRead, ct);
        reconnect.EnsureSuccessStatusCode();
        using var reReader = new StreamReader(await reconnect.Content.ReadAsStreamAsync(ct));
        var sync = JsonDocument.Parse((await reReader.ReadLineAsync(ct))!).RootElement;
        Assert.Equal("sync", sync.GetProperty("kind").GetString());
        Assert.True(sync.GetProperty("version").GetInt64() > notification.GetProperty("version").GetInt64());
    }

    [MySqlFact]
    public async Task RotationWhileInitialSummaryWaitsNeverLeaksSync()
    {
        string database = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(database, ct);
        await using var app = new MailApiTests.Factory(database);
        using var http = app.CreateClient();
        await MailTransactionTests.Login(http, ct);
        string player = (await http.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("playerId").GetString()!;
        await using var db = new MySqlConnection(database);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        using (var owner = new MySqlCommand("SELECT id FROM arena_player WHERE id=@id FOR UPDATE;", db, tx))
        {
            owner.Parameters.AddWithValue("@id", player);
            await owner.ExecuteScalarAsync(ct);
        }

        var request = http.GetAsync("/api/mail/events", HttpCompletionOption.ResponseHeadersRead, ct);
        await Task.Delay(200, ct);
        Assert.False(request.IsCompleted);
        using (var rotate = new MySqlCommand("UPDATE arena_player SET token_hash=@hash WHERE id=@id;", db, tx))
        {
            rotate.Parameters.AddWithValue("@hash", RandomNumberGenerator.GetBytes(32));
            rotate.Parameters.AddWithValue("@id", player);
            await rotate.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        using var reply = await request;
        Assert.Equal(HttpStatusCode.Unauthorized, reply.StatusCode);
    }
}
