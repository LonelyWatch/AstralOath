using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class EquipmentInventoryTests
{
    private const string Equipment = "/api/inventory/equipment";

    [MySqlFact]
    public async Task SixSlotsReplaceMoveUnequipAndReplayStayScopedToOwnerAndHeroInstance()
    {
        string database = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(database, ct);
        using var factory = new WebApplicationFactory<Program>();
        using var app = Configure(factory, database, true);
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        using var stranger = app.CreateClient();
        using (var anonymous = await first.GetAsync(Equipment, ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var probe = new HttpRequestMessage(HttpMethod.Head, Equipment))
        using (var response = await first.SendAsync(probe, ct))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string player = await InventoryTests.OpenGuest(first, "eq_" + Id()[..12], ct);
        string other = await InventoryTests.OpenGuest(stranger, "eq_" + Id()[..12], ct);
        second.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
        await using var db = new MySqlConnection(database);
        await db.OpenAsync(ct);
        try
        {
            await CheckVersion(first, null, 0, HttpStatusCode.OK, ct);
            string hero = InventoryTests.AssertStarters(await InventoryTests.Read(first, ct))[0];
            string foreignHero = InventoryTests.AssertStarters(await InventoryTests.Read(stranger, ct))[0];
            string twin = Id();
            using (var insert = new MySqlCommand("""
                INSERT INTO inventory_hero(instance_id,player_id,realm_id,template_id,level,stars)
                SELECT @twin,player_id,realm_id,template_id,level,stars FROM inventory_hero WHERE instance_id=@hero;
                """, db))
            {
                insert.Parameters.AddWithValue("@hero", hero);
                insert.Parameters.AddWithValue("@twin", twin);
                Assert.Equal(1, await insert.ExecuteNonQueryAsync(ct));
            }
            var reads = await Task.WhenAll(Read(first, ct), Read(second, ct));
            Assert.Equal(1, Version(reads[0]));
            Assert.Equal(reads[0].ToString(), reads[1].ToString());
            var items = reads[0].GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(7, items.Length);
            Assert.Equal(7, reads[0].GetProperty("used").GetInt32());
            Assert.All(items, i => Assert.Equal(JsonValueKind.Null, i.GetProperty("equippedHeroId").ValueKind));
            JsonElement[] slots = items.GroupBy(i => i.GetProperty("slot").GetString()).Select(g => g.First()).ToArray();
            Assert.Equal(6, slots.Length);
            var weapon = items.First(i => i.GetProperty("slot").GetString() == "weapon");
            var spare = items.Last(i => i.GetProperty("slot").GetString() == "weapon");
            var badSlot = Action(weapon, hero, 1) with { Slot = "boots" };
            Assert.Equal("equipment_slot_mismatch", (await Send(first, badSlot, HttpStatusCode.BadRequest, ct)).GetProperty("code").GetString());
            await Send(first, Action(weapon, foreignHero, 1), HttpStatusCode.NotFound, ct);
            await Read(stranger, ct);
            await Send(stranger, Action(weapon, foreignHero, 1), HttpStatusCode.NotFound, ct);
            var request = Action(weapon, hero, 1);
            var competing = Action(weapon, twin, 1);
            var race = await Task.WhenAll(first.PostAsJsonAsync(Equipment + "/actions", request, ct),
                second.PostAsJsonAsync(Equipment + "/actions", competing, ct));
            try
            {
                Assert.Single(race, r => r.StatusCode == HttpStatusCode.OK);
                var conflict = Assert.Single(race, r => r.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal("equipment_version_conflict",
                    (await conflict.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());
                if (!race[0].IsSuccessStatusCode)
                    (hero, twin, request) = (twin, hero, competing);
            }
            finally { foreach (var response in race) response.Dispose(); }
            var original = await Send(first, request, HttpStatusCode.OK, ct);
            Assert.Equal(2, Version(original));
            Assert.Equal(hero, Owner(original, weapon));
            var current = original;
            await CheckVersion(second, "\"1\"", 2, HttpStatusCode.OK, ct);
            await CheckVersion(second, "W/\"2\"", 2, HttpStatusCode.NotModified, ct);
            await CheckVersion(second, "*", 2, HttpStatusCode.NotModified, ct);
            await CheckVersion(stranger, "\"2\"", 1, HttpStatusCode.OK, ct);
            foreach (var item in slots.Where(i => i.GetProperty("slot").GetString() != "weapon"))
                current = await Send(first, Action(item, hero, Version(current)), HttpStatusCode.OK, ct);
            Assert.Equal(6, current.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("equippedHeroId").GetString() == hero));
            current = await Send(second, Action(spare, hero, Version(current)), HttpStatusCode.OK, ct);
            Assert.Null(Owner(current, weapon));
            Assert.Equal(hero, Owner(current, spare));
            current = await Send(second, Action(spare, twin, Version(current)), HttpStatusCode.OK, ct);
            Assert.Equal(twin, Owner(current, spare));
            await Send(first, Action(spare, hero, Version(current)) with { Action = "unequip" }, HttpStatusCode.Conflict, ct);
            current = await Send(first, Action(spare, twin, Version(current)) with { Action = "unequip" }, HttpStatusCode.OK, ct);
            Assert.Null(Owner(current, spare));
            var replay = await Send(second, request, HttpStatusCode.OK, ct);
            Assert.Equal(original.ToString(), replay.ToString());
            Assert.Equal("request_conflict", (await Send(second, request with { HeroInstanceId = twin }, HttpStatusCode.Conflict, ct)).GetProperty("code").GetString());
            Assert.Equal(Version(current), Version(await Read(first, ct)));
            using var audit = new MySqlCommand("""
                SELECT COUNT(*) FROM game_operation_audit
                WHERE actor_id = @player AND operation IN ('inventory.equipment.development_grant',
                    'inventory.equipment.equip','inventory.equipment.unequip');
                """, db);
            audit.Parameters.AddWithValue("@player", player);
            Assert.Equal(Version(current), Convert.ToInt64(await audit.ExecuteScalarAsync(ct)));
        }
        finally
        {
            using var clean = new MySqlCommand("DELETE FROM arena_player WHERE id IN (@a,@b);", db);
            clean.Parameters.AddWithValue("@a", player);
            clean.Parameters.AddWithValue("@b", other);
            await clean.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [MySqlFact]
    public async Task DevelopmentGrantRequiresExplicitOptIn()
    {
        string database = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await DatabaseMigrator.ApplyAsync(database, ct);
        using var factory = new WebApplicationFactory<Program>();
        using var app = Configure(factory, database, false);
        using var client = app.CreateClient();
        string player = await InventoryTests.OpenGuest(client, "eq_" + Id()[..12], ct);
        try
        {
            var snapshot = await Read(client, ct);
            Assert.Equal(0, Version(snapshot));
            Assert.Empty(snapshot.GetProperty("items").EnumerateArray());
            var releaseStore = new InventoryStore(database, development: false, grantTrainingEquipment: true);
            var releaseSnapshot = await releaseStore.ReadEquipment(player, ct);
            Assert.Empty(releaseSnapshot.Items);
            Assert.Equal(0, releaseSnapshot.Version);
        }
        finally
        {
            await using var db = new MySqlConnection(database);
            await db.OpenAsync(CancellationToken.None);
            using var clean = new MySqlCommand("DELETE FROM arena_player WHERE id = @id", db);
            clean.Parameters.AddWithValue("@id", player);
            await clean.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory, string database, bool grant) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, settings) =>
            settings.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = database,
                ["Entry:AllowLegacyArenaGuests"] = "true", ["Inventory:DevelopmentEquipmentGrant"] = grant.ToString() })));
    private sealed record Mutation(string RequestId, string Action, string InstanceId, string HeroInstanceId, string Slot, long ExpectedVersion);
    private static string Id() => Guid.NewGuid().ToString("N");
    private static long Version(JsonElement snapshot) => snapshot.GetProperty("version").GetInt64();
    private static Mutation Action(JsonElement item, string hero, long version) => new(Id(), "equip",
        item.GetProperty("instanceId").GetString()!, hero, item.GetProperty("slot").GetString()!, version);
    private static string? Owner(JsonElement snapshot, JsonElement item) => snapshot.GetProperty("items").EnumerateArray()
        .Single(i => i.GetProperty("instanceId").GetString() == item.GetProperty("instanceId").GetString()).GetProperty("equippedHeroId").GetString();

    private static async Task<JsonElement> Read(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(Equipment, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static async Task CheckVersion(HttpClient client, string? known, long version, HttpStatusCode status, CancellationToken ct)
    {
        using var probe = new HttpRequestMessage(HttpMethod.Head, Equipment);
        if (known != null) probe.Headers.IfNoneMatch.ParseAdd(known);
        using var reply = await client.SendAsync(probe, ct);
        Assert.Equal(status, reply.StatusCode);
        Assert.True(reply.Headers.CacheControl?.NoStore);
        Assert.Equal($"\"{version}\"", reply.Headers.ETag?.ToString());
        Assert.Empty(await reply.Content.ReadAsByteArrayAsync(ct));
    }

    private static async Task<JsonElement> Send(HttpClient client, Mutation request, HttpStatusCode status, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(Equipment + "/actions", request, ct);
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
