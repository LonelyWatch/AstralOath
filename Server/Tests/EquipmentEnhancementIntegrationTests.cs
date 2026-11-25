using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class EquipmentEnhancementIntegrationTests
{
    [MySqlFact]
    public async Task EnhancementPersistsExperienceBalancesAndOriginalReceiptWhileRejectingStaleOrForeignWrites()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        using var factory = new WebApplicationFactory<Program>();
        using var app = factory.WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection,
                ["Entry:AllowLegacyArenaGuests"] = "true", ["Inventory:DevelopmentEquipmentGrant"] = "false" })));
        using var client = app.CreateClient();
        using var stranger = app.CreateClient();
        string owner = await InventoryTests.OpenGuest(client, "enh_" + Id()[..12], ct);
        string other = await InventoryTests.OpenGuest(stranger, "enh_" + Id()[..12], ct);
        try
        {
            await Set(client, "gold", 1_000_000, ct);
            await Set(client, "ma_est1e", 100, ct);
            await Set(client, "ma_est2e", 100, ct);
            using var forged = await client.PostAsJsonAsync("/api/development/gm/actions", new
            {
                requestId = Id(), action = "forgeEquipment", key = "weapon", value = 1,
                forge = new { itemLevel = 85, enhanceLevel = 0, grade = "epic", setId = "speed", mainKind = "attack",
                    rulesVersion = "equipment.enhancement.source-r4262.recorded-economy-v1",
                    substats = new[] { new {kind="healthPct",value=400,rolls=1}, new {kind="speed",value=2,rolls=1},
                        new {kind="critChanceBp",value=300,rolls=1}, new {kind="critDamageBp",value=400,rolls=1} } }
            }, ct);
            Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
            var before = await Read(client, ct);
            var item = Assert.Single(before.GetProperty("items").EnumerateArray());
            Assert.Equal(100, item.GetProperty("mainStat").GetProperty("value").GetInt32());
            Assert.True(item.GetProperty("enhancementSupported").GetBoolean());
            string instance = item.GetProperty("instanceId").GetString()!;
            var request = new { requestId = Id(), expectedVersion = before.GetProperty("version").GetInt64(),
                materials = new[] {new {kind="ma_est1e",quantity=2},new {kind="ma_est2e",quantity=1}} };
            string endpoint = "/api/inventory/equipment/" + instance + "/enhance";
            using var first = await client.PostAsJsonAsync(endpoint, request, ct);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var receipt = await first.Content.ReadFromJsonAsync<JsonElement>(ct);
            var result = receipt.GetProperty("result");
            Assert.Equal(17600, result.GetProperty("consumedGold").GetInt64());
            Assert.Contains(result.GetProperty("grantedXp").GetInt64(), new long[] {1700,2550,3400});
            var after = receipt.GetProperty("inventory");
            Assert.Equal(request.expectedVersion + 1, after.GetProperty("version").GetInt64());
            Assert.Equal(1_000_000 - 17600, after.GetProperty("materials").GetProperty("gold").GetInt64());
            Assert.Equal(98, Balance(after, "ma_est1e"));
            Assert.Equal(99, Balance(after, "ma_est2e"));
            Assert.Equal(result.GetProperty("afterXp").GetInt64(), after.GetProperty("items")[0].GetProperty("xp").GetInt64());
            using var replay = await client.PostAsJsonAsync(endpoint, request, ct);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(receipt.ToString(), (await replay.Content.ReadFromJsonAsync<JsonElement>(ct)).ToString());
            using var conflict = await client.PostAsJsonAsync(endpoint, new {request.requestId, request.expectedVersion,
                materials = new[]{new {kind="ma_est2e",quantity=2}}}, ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var stale = await client.PostAsJsonAsync(endpoint, new {requestId=Id(),request.expectedVersion,request.materials}, ct);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var foreign = await stranger.PostAsJsonAsync(endpoint,
                new {requestId=Id(),expectedVersion=0,request.materials}, ct);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            await Set(client, "gold", 0, ct);
            using var insufficient = await client.PostAsJsonAsync(endpoint, new {requestId=Id(),
                expectedVersion=after.GetProperty("version").GetInt64(),request.materials}, ct);
            Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
            var final = await Read(client, ct);
            Assert.Equal(after.GetProperty("version").GetInt64(), final.GetProperty("version").GetInt64());
            Assert.Equal(98, Balance(final, "ma_est1e"));
            Assert.Equal(99, Balance(final, "ma_est2e"));
            await using var db = new MySqlConnection(connection);
            await db.OpenAsync(ct);
            using var audit = new MySqlCommand("SELECT COUNT(*) FROM game_operation_audit WHERE actor_id=@id AND operation='inventory.equipment.enhance'", db);
            audit.Parameters.AddWithValue("@id", owner);
            Assert.Equal(1, Convert.ToInt32(await audit.ExecuteScalarAsync(ct)));
            await Set(client, "gold", 1_000_000, ct);
            await Set(client, "ma_est1e", 1, ct);
            var profile = AstralOath.Server.Inventory.EquipmentEnhancementCatalog.Rules.Profiles.Single(p =>
                p.ItemLevel == 85 && p.Grade == "epic" && p.Slot == "weapon" && p.MainKind == "attack");
            using (var nearCap = new MySqlCommand("UPDATE inventory_equipment SET enhance_level=14,main_value=@main,experience=@xp WHERE instance_id=@id", db))
            {
                nearCap.Parameters.AddWithValue("@main", profile.MainValues[14]);
                nearCap.Parameters.AddWithValue("@xp", profile.XpThresholds[15] - 50);
                nearCap.Parameters.AddWithValue("@id", instance);
                await nearCap.ExecuteNonQueryAsync(ct);
            }
            var overflowRequest = new { requestId = Id(), expectedVersion = final.GetProperty("version").GetInt64(),
                materials = new[] {new {kind="ma_est1e",quantity=10}} };
            // The refund must never let an account spend material it does not own.
            using var missingGross = await client.PostAsJsonAsync(endpoint, overflowRequest, ct);
            Assert.Equal(HttpStatusCode.Conflict, missingGross.StatusCode);
            Assert.Equal(1, Balance(await Read(client, ct), "ma_est1e"));
            await Set(client, "ma_est1e", 20, ct);
            using var capped = await client.PostAsJsonAsync(endpoint, overflowRequest, ct);
            Assert.Equal(HttpStatusCode.OK, capped.StatusCode);
            var capReceipt = await capped.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(9, capReceipt.GetProperty("result").GetProperty("returnedPowderQuantity").GetInt32());
            Assert.Equal(19, Balance(capReceipt.GetProperty("inventory"), "ma_est1e"));
            using var capReplay = await client.PostAsJsonAsync(endpoint, overflowRequest, ct);
            Assert.Equal(capReceipt.ToString(), (await capReplay.Content.ReadFromJsonAsync<JsonElement>(ct)).ToString());
            using var capReject = await client.PostAsJsonAsync(endpoint, new {requestId=Id(),
                expectedVersion=capReceipt.GetProperty("inventory").GetProperty("version").GetInt64(),overflowRequest.materials}, ct);
            Assert.Equal(HttpStatusCode.Conflict, capReject.StatusCode);
            using var netEntry = new MySqlCommand("SELECT COUNT(*),SUM(delta) FROM inventory_artifact_material_entry WHERE player_id=@owner AND request_id=@request AND material_id='ma_est1e'", db);
            netEntry.Parameters.AddWithValue("@owner", owner); netEntry.Parameters.AddWithValue("@request", overflowRequest.requestId);
            await using var netReader = await netEntry.ExecuteReaderAsync(ct);
            Assert.True(await netReader.ReadAsync(ct));
            Assert.Equal(1, netReader.GetInt32(0));
            Assert.Equal(-1, netReader.GetInt32(1));
        }
        finally
        {
            await using var db = new MySqlConnection(connection);
            await db.OpenAsync(CancellationToken.None);
            using var clean = new MySqlCommand("DELETE FROM arena_player WHERE id IN (@a,@b)", db);
            clean.Parameters.AddWithValue("@a", owner); clean.Parameters.AddWithValue("@b", other);
            await clean.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static string Id() => Guid.NewGuid().ToString("N");
    private static long Balance(JsonElement snapshot, string kind) => snapshot.GetProperty("materials").GetProperty("items")
        .EnumerateArray().Single(m => m.GetProperty("kind").GetString() == kind).GetProperty("quantity").GetInt64();
    private static async Task Set(HttpClient client, string kind, int value, CancellationToken ct)
    {
        using var result = await client.PostAsJsonAsync("/api/development/gm/actions", new {requestId=Id(),action="set",key=kind,value}, ct);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
    private static async Task<JsonElement> Read(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync("/api/inventory/equipment", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
