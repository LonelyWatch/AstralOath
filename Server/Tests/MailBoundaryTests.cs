using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Mail;
using MySqlConnector;
using Xunit;

public sealed class MailBoundaryTests
{
    private static string Database => Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static async Task Exec(string text, params (string Key, object Value)[] args)
    {
        await using var db = new MySqlConnection(Database);
        await db.OpenAsync();
        using var sql = new MySqlCommand(text, db);
        foreach (var p in args)
            sql.Parameters.AddWithValue(p.Key, p.Value);
        await sql.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(string text, string player)
    {
        await using var db = new MySqlConnection(Database);
        await db.OpenAsync();
        using var sql = new MySqlCommand(text, db);
        sql.Parameters.AddWithValue("@player", player);
        return Convert.ToInt64(await sql.ExecuteScalarAsync());
    }

    private static async Task<string> Owner(HttpClient http)
    {
        await MailTransactionTests.Login(http, default);
        return (await http.GetFromJsonAsync<JsonElement>("/api/mail/summary")).GetProperty("playerId").GetString()!;
    }

    private static MailDraft Draft(params MailAttachmentInput[] attachments) => new(TemplateId: "maintenance", Attachments: attachments);
    [Theory]
    [InlineData("Production", "127.0.0.1")]
    [InlineData("Development", "0.0.0.0")]
    public async Task SelfGmIsAbsentOutsideDevelopmentLoopback(string environment, string bind)
    {
        await using var app = new MailApiTests.Factory(null, environment, bind);
        using var http = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/api/development/gm/mail/preview", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync("/api/development/gm/mail/send", new { })).StatusCode);
    }

    [MySqlFact]
    public async Task FullHeroMailboxRollsBackWholeMixedRewardAndBulkSkipsOnlyCapacity()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        string player = await Owner(http);
        var store = new MailStore(Database, MailCatalog.Load());
        var mixed = await store.PreviewSelf(player, Draft(new("material", "gold", 777), new("hero", "c1001", 1)), default);
        var first = await store.SendSelf(player, new(NewId(), mixed.PreviewId), default);
        (await http.GetAsync("/api/inventory/heroes")).EnsureSuccessStatusCode();
        await Exec("UPDATE inventory_capacity SET capacity=3 WHERE player_id=@player;", ("@player", player));
        var error = await Assert.ThrowsAsync<EntryProblem>(() => store.Claim(player, new(NewId(), first.DeliveryId), default));
        Assert.Equal("hero_inventory_full", error.Code);
        Assert.Equal(0, await Scalar("SELECT COALESCE(SUM(balance),0) FROM inventory_artifact_material WHERE player_id=@player AND material_id='gold';", player));
        var energy = await store.PreviewSelf(player, Draft(new MailAttachmentInput("energy", "energy", 50)), default);
        var second = await store.SendSelf(player, new(NewId(), energy.PreviewId), default);
        var result = await store.ClaimAll(player, new(NewId(), [first.DeliveryId, second.DeliveryId]), default);
        Assert.Equal([second.DeliveryId], result.Claimed);
        Assert.Equal("hero_inventory_full", Assert.Single(result.Skipped).Reason);
        Assert.Equal(50, result.Summary.Energy.Balance);
        await Exec("UPDATE inventory_capacity SET capacity=1000 WHERE player_id=@player;", ("@player", player));
        await store.Claim(player, new(NewId(), first.DeliveryId), default);
        Assert.Equal(777, await Scalar("SELECT balance FROM inventory_artifact_material WHERE player_id=@player AND material_id='gold';", player));
    }

    [MySqlFact]
    public async Task ExpiredClaimCommitsHistoryAndNeverGrantsAndHistoryHasBothLimits()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        string player = await Owner(http);
        var store = new MailStore(Database, MailCatalog.Load());
        var p = await store.PreviewSelf(player, Draft(new MailAttachmentInput("energy", "energy", 50)), default);
        var sent = await store.SendSelf(player, new(NewId(), p.PreviewId), default);
        await Exec("UPDATE mail_delivery SET expires_at=UTC_TIMESTAMP(6) WHERE delivery_id=@id;", ("@id", sent.DeliveryId));
        var error = await Assert.ThrowsAsync<EntryProblem>(() => store.Claim(player, new(NewId(), sent.DeliveryId), default));
        Assert.Equal("mail_expired", error.Code);
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM mail_delivery WHERE player_id=@player AND outcome='expired';", player));
        var history = await store.ReadPage(player, true, null, 100, default);
        Assert.Single(history.Items);
        Assert.Equal(0, history.Summary.Energy.Balance);
        await Exec("UPDATE mail_delivery SET history_at=UTC_TIMESTAMP(6)-INTERVAL 91 DAY WHERE delivery_id=@id;", ("@id", sent.DeliveryId));
        Assert.Empty((await store.ReadPage(player, true, null, 100, default)).Items);
    }

    [MySqlFact]
    public async Task MixedInstancesAndMaterialsUseFrozenSpecsAndUniqueSourceIndexes()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var http = app.CreateClient();
        string player = await Owner(http);
        var store = new MailStore(Database, MailCatalog.Load());
        var p = await store.PreviewSelf(player, Draft(new("material", "gold", 3), new("material", "gold", 4), new("hero", "c1001", 2), new("artifact", "ef301", 2), new("equipment", "training_speed_weapon", 2), new("equipment", "training_speed_helmet", 1), new("bundle", "gm_gift", 1)), default);
        var sent = await store.SendSelf(player, new(NewId(), p.PreviewId), default);
        var request = new MailClaimInput(NewId(), sent.DeliveryId);
        var results = await Task.WhenAll(store.Claim(player, request, default), store.Claim(player, request, default));
        Assert.Equal(results[0].Claimed, results[1].Claimed);
        Assert.Equal(7, await Scalar("SELECT balance FROM inventory_artifact_material WHERE player_id=@player AND material_id='gold';", player));
        Assert.Equal(3, await Scalar("SELECT COUNT(DISTINCT source_index) FROM inventory_equipment WHERE player_id=@player AND source_ref LIKE 'mail-%';", player));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM inventory_artifact WHERE player_id=@player AND source LIKE 'mail-%';", player));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM summon_collection WHERE player_id=@player;", player));
        Assert.Equal("gmGift", results[0].Presentation);
    }

    [MySqlFact]
    public async Task OperatorFreezeDeduplicatesExcludesAndConfirmsOnce()
    {
        await DatabaseMigrator.ApplyAsync(Database, default);
        await using var app = new MailApiTests.Factory(Database);
        using var a = app.CreateClient();
        using var b = app.CreateClient();
        string pa = await Owner(a), pb = await Owner(b);
        var store = new MailStore(Database, MailCatalog.Load());
        foreach (var player in new[]
        {
            pa,
            pb
        }

        )
            await Exec("INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered) VALUES('undying-memory',@player,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),TRUE);", ("@player", player));
        var p = await store.PreviewOperator(new("undying-memory", "qa-operator", "whitelist", [pa, pb, pb], [pb], Draft(new MailAttachmentInput("energy", "energy", 21))), default);
        Assert.Equal(1, p.RecipientCount);
        Assert.Equal(1, p.DuplicateCount);
        Assert.Equal(1, p.ExcludedCount);
        var confirmed = await Task.WhenAll(store.ConfirmOperator(p.PreviewId, "qa-operator", default), store.ConfirmOperator(p.PreviewId, "qa-operator", default));
        Assert.Equal(confirmed[0].BatchId, confirmed[1].BatchId);
        Assert.Equal(1, confirmed[0].DeliveredCount);
        Assert.Equal(1, (await store.ReadSummary(pa, default)).EffectiveCount);
        Assert.Equal(0, (await store.ReadSummary(pb, default)).EffectiveCount);
        await Assert.ThrowsAsync<EntryProblem>(() => store.PreviewOperator(new("undying-memory", "qa-operator", "whitelist", [], [pb], Draft()), default));
        var all = await store.PreviewOperator(new("undying-memory", "qa-operator", "allInRealm", [], [], Draft()), default);
        using var c = app.CreateClient();
        string pc = await Owner(c);
        await Exec("INSERT INTO player_realm_membership(realm_id,player_id,first_entered_at,last_entered_at,registered) VALUES('undying-memory',@player,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),TRUE);", ("@player", pc));
        var batch = await store.ConfirmOperator(all.PreviewId, "qa-operator", default);
        Assert.Equal(all.RecipientCount, batch.TargetCount);
        Assert.Equal(0, (await store.ReadSummary(pc, default)).EffectiveCount);
    }
}
