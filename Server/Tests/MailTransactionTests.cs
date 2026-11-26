using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using MySqlConnector;
using Xunit;

public sealed class MailTransactionTests
{
    [MySqlFact]
    public async Task FrozenMixedMailIsAtomicAndRetryableAndBulkExcludesGm()
    {
        var db = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(db, ct);
        await using var app = new MailApiTests.Factory(db);
        using var a = app.CreateClient();
        using var b = app.CreateClient();
        await Login(a, ct);
        await Login(b, ct);
        var before = await a.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct);
        var first = await Send(a, "standard", ct, new[] { new { kind = "energy", key = "energy", quantity = 400 }, new { kind = "energy", key = "energy", quantity = 5 } });
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("energy").GetProperty("balance").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/mail/" + first, ct)).StatusCode);
        string rid = Guid.NewGuid().ToString("N");
        var claim = new
        {
            requestId = rid,
            deliveryId = first
        };
        using var reply = await a.PostAsJsonAsync("/api/mail/claim", claim, ct);
        reply.EnsureSuccessStatusCode();
        string original = await reply.Content.ReadAsStringAsync(ct);
        using var retry = await a.PostAsJsonAsync("/api/mail/claim", claim, ct);
        Assert.Equal(original, await retry.Content.ReadAsStringAsync(ct));
        Assert.Equal(405, (await a.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("energy").GetProperty("balance").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/mail/claim", new { requestId = rid, deliveryId = Guid.NewGuid().ToString("N") }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/mail/claim", claim with { requestId = Guid.NewGuid().ToString("N") }, ct)).StatusCode);
        var ids = new List<string>();
        for (int i = 0; i < 5; i++)
            ids.Add(await Send(a, i < 3 ? "standard" : "gm", ct));
        using var bulk = await a.PostAsJsonAsync("/api/mail/claim-all", new { requestId = Guid.NewGuid().ToString("N"), deliveryIds = ids }, ct);
        bulk.EnsureSuccessStatusCode();
        Assert.Equal(3, (await bulk.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("claimed").GetArrayLength());
        Assert.Equal(2, (await a.GetFromJsonAsync<JsonElement>("/api/mail/summary", ct)).GetProperty("effectiveCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/mail/claim-all", new { requestId = Guid.NewGuid().ToString("N"), deliveryIds = ids.Skip(3) }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/development/gm/mail/preview", new { templateId = "maintenance", playerId = "someone" }, ct)).StatusCode);
    }

    internal static async Task Login(HttpClient http, CancellationToken ct)
    {
        using var reply = await http.PostAsJsonAsync("/api/arena/sessions", new { displayName = "邮件QA" + Guid.NewGuid().ToString("N")[..6] }, ct);
        reply.EnsureSuccessStatusCode();
        var session = await reply.Content.ReadFromJsonAsync<JsonElement>(ct);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
    }

    internal static async Task<string> Send(HttpClient http, string type, CancellationToken ct, object? attachments = null)
    {
        using var preview = await http.PostAsJsonAsync("/api/development/gm/mail/preview", new { templateId = "maintenance", letterType = type, attachments = attachments ?? new[] { new { kind = "currency", key = "bookmarks", quantity = 5 } } }, ct);
        preview.EnsureSuccessStatusCode();
        string id = (await preview.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("previewId").GetString()!;
        using var send = await http.PostAsJsonAsync("/api/development/gm/mail/send", new { requestId = Guid.NewGuid().ToString("N"), previewId = id }, ct);
        send.EnsureSuccessStatusCode();
        return (await send.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("deliveryId").GetString()!;
    }
}
