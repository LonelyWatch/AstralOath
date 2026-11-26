using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Mail;
using MySqlConnector;
using Xunit;

public sealed class MailCapacityTests
{
    private static string Database => Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;

    private static string Id() => Guid.NewGuid().ToString("N");
    private static async Task Exec(string sql, params (string Key, object Value)[] args)
    {
        await using var db = new MySqlConnection(Database);
        await db.OpenAsync();
        using var q = new MySqlCommand(sql, db);
        foreach (var p in args)
            q.Parameters.AddWithValue(p.Key, p.Value);
        await q.ExecuteNonQueryAsync();
    }

    private static async Task<(string Player, MailStore Store)> Owner(HttpClient http)
    {
        await MailTransactionTests.Login(http, default);
        var player = (await http.GetFromJsonAsync<JsonElement>("/api/mail/summary")).GetProperty("playerId").GetString()!;
        return (player, new(Database, MailCatalog.Load()));
    }

    [MySqlFact]
    public async Task NinetyNineConcurrentDeliveryCannotExceedCapacityAndOriginalBatchRetries()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        var(player, store) = await Owner(http);
        string first = await MailTransactionTests.Send(http, "standard", default);
        for (int i = 0; i < 98; i++)
            await Exec("INSERT INTO mail_delivery(delivery_id,realm_id,player_id,batch_id,delivered_at,expires_at,frozen) SELECT @id,realm_id,player_id,@batch,delivered_at,expires_at,frozen FROM mail_delivery WHERE delivery_id=@source;", ("@id", Id()), ("@batch", Id()), ("@source", first));
        await Exec("INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered) VALUES('undying-memory',@player,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),TRUE);", ("@player", player));
        var a = await store.PreviewOperator(new("undying-memory", "qa-capacity", "ids", [player], [], new(TemplateId: "maintenance")), default);
        var b = await store.PreviewOperator(new("undying-memory", "qa-capacity", "ids", [player], [], new(TemplateId: "maintenance")), default);
        var results = await Task.WhenAll(store.ConfirmOperator(a.PreviewId, "qa-capacity", default), store.ConfirmOperator(b.PreviewId, "qa-capacity", default));
        Assert.Equal(1, results.Sum(x => x.DeliveredCount));
        Assert.Equal(1, results.Sum(x => x.FailedCount));
        Assert.Equal(100, (await store.ReadSummary(player, default)).EffectiveCount);
        var failed = results.Single(x => x.FailedCount == 1);
        Assert.Equal("mailbox_full", Assert.Single(failed.Failures).Reason);
        await store.Claim(player, new(Id(), first), default);
        var retried = await store.RetryBatch(failed.BatchId, "qa-capacity", default);
        Assert.Equal(1, retried.DeliveredCount);
        Assert.Equal(failed.ExpiresAt, retried.ExpiresAt);
        Assert.Equal(100, (await store.ReadSummary(player, default)).EffectiveCount);
    }

    [MySqlFact]
    public async Task NewMailIsOutsideBulkSnapshotAndFrozenRewardSurvivesCatalogRemoval()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        var(player, store) = await Owner(http);
        string first = await MailTransactionTests.Send(http, "standard", default, new[] { new { kind = "energy", key = "energy", quantity = 15 } });
        var source = MailCatalogTests.Source();
        source["resources"] = new JsonArray();
        source["bundles"] = new JsonArray();
        source["templates"] = new JsonArray();
        var removed = new MailStore(Database, MailCatalogTests.Catalog(source));
        string second = await MailTransactionTests.Send(http, "standard", default);
        var claim = new MailClaimAllInput(Id(), [first]);
        var result = await removed.ClaimAll(player, claim, default);
        Assert.Equal(15, result.Summary.Energy.Balance);
        Assert.Equal(1, result.Summary.EffectiveCount);
        Assert.Equal([second], (await store.ReadPage(player, false, null, 100, default)).Items.Select(x => x.DeliveryId));
        await Exec("UPDATE mail_delivery SET expires_at=UTC_TIMESTAMP(6)-INTERVAL 1 DAY WHERE delivery_id=@id;", ("@id", first));
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await removed.ClaimAll(player, claim, default)));
    }

    [MySqlFact]
    public async Task AbsoluteEnergyOverflowRollsBackCurrencyAndHistoryIsCappedAt100()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        var(player, store) = await Owner(http);
        (await http.PostAsJsonAsync("/api/development/gm/actions", new { requestId = Id(), action = "set", key = "energy", value = 1_000_000_000 })).EnsureSuccessStatusCode();
        string first = await MailTransactionTests.Send(http, "standard", default, new[] { new { kind = "energy", key = "energy", quantity = 1 }, new { kind = "material", key = "gold", quantity = 10 } });
        var error = await Assert.ThrowsAsync<EntryProblem>(() => store.Claim(player, new(Id(), first), default));
        Assert.Equal("resource_overflow", error.Code);
        Assert.Equal(1, (await store.ReadSummary(player, default)).EffectiveCount);
        for (int i = 0; i < 105; i++)
            await Exec("INSERT INTO mail_delivery(delivery_id,realm_id,player_id,batch_id,delivered_at,expires_at,history_at,outcome,frozen) SELECT @id,realm_id,player_id,@batch,delivered_at,expires_at,UTC_TIMESTAMP(6),'claimed',frozen FROM mail_delivery WHERE delivery_id=@source;", ("@id", Id()), ("@batch", Id()), ("@source", first));
        var page = await store.ReadPage(player, true, null, 60, default);
        Assert.Equal(60, page.Items.Length);
        Assert.NotNull(page.NextCursor);
        var next = await store.ReadPage(player, true, page.NextCursor, 60, default);
        Assert.Equal(40, next.Items.Length);
        Assert.Null(next.NextCursor);
        Assert.Empty(page.Items.Select(x => x.DeliveryId).Intersect(next.Items.Select(x => x.DeliveryId)));
    }
}
