using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Shared.Battle;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class EquipmentTrialTests
{
    [MySqlFact]
    public async Task TrialFreezesSixSlotsByHeroInstanceAndKeepsTheOriginalSnapshotAfterTransfer()
    {
        const string gear = "/api/inventory/equipment";
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MySql"] = connection, ["Entry:AllowLegacyArenaGuests"] = "true",
                ["BattlePreview:Enabled"] = "true", ["Inventory:DevelopmentEquipmentGrant"] = "true",
                ["Server:BindAddress"] = "127.0.0.1"
            })));
        using var player = app.CreateClient();
        var owner = await InventoryTests.OpenGuest(player, "gear_" + Guid.NewGuid().ToString("N")[..8], ct);
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync(ct);
        try
        {
            string first = InventoryTests.AssertStarters(await InventoryTests.Read(player, ct))[0];
            string second = Guid.NewGuid().ToString("N");
            using (var duplicate = new MySqlCommand("""
                INSERT INTO inventory_hero (instance_id, player_id, realm_id, template_id, level, stars)
                SELECT @new, player_id, realm_id, template_id, level, stars FROM inventory_hero
                WHERE instance_id = @original AND player_id = @owner AND realm_id = @realm
                """, db))
            {
                duplicate.Parameters.AddWithValue("@new", second);
                duplicate.Parameters.AddWithValue("@original", first);
                duplicate.Parameters.AddWithValue("@owner", owner);
                duplicate.Parameters.AddWithValue("@realm", EntryEndpoints.RealmId);
                Assert.Equal(1, await duplicate.ExecuteNonQueryAsync(ct));
            }
            JsonElement inventory = await player.GetFromJsonAsync<JsonElement>(gear, ct);
            var pieces = inventory.GetProperty("items").EnumerateArray().Where(item =>
                item.GetProperty("templateId").GetString() != "training_critical_weapon").ToArray();
            Assert.Equal(6, pieces.Length);
            foreach (var piece in pieces) await Equip(player, piece, second);
            var equipped = await Start();
            string rules = equipped.GetProperty("battleContentHash").GetString()! + CatalogHash("AstralOath.ArtifactCatalog")
                + CatalogHash("AstralOath.TrainingEquipment") + EquipmentStatCalculator.RulesVersion;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(rules))), equipped.GetProperty("contentHash").GetString());
            var opening = equipped.GetProperty("opening").GetProperty("snapshot");
            var bare = Stats(opening, "ally-2");
            var worn = Stats(opening, "ally-1");
            int basisSpeed = bare.GetProperty("speed").GetInt32();
            Assert.Equal(basisSpeed * 125 / 100 + 7, worn.GetProperty("speed").GetInt32());
            int basisAttack = bare.GetProperty("attack").GetInt32();
            Assert.Equal(basisAttack * 105 / 100 + 20, worn.GetProperty("attack").GetInt32());
            Assert.Equal(bare.GetProperty("critChanceBp").GetInt32() + 1200, worn.GetProperty("critChanceBp").GetInt32());

            var weapon = Assert.Single(pieces, item => item.GetProperty("slot").GetString() == "weapon");
            using var secondClient = app.CreateClient();
            secondClient.DefaultRequestHeaders.Authorization = player.DefaultRequestHeaders.Authorization;
            await Equip(secondClient, weapon, first);
            using var seat = app.CreateClient();
            seat.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", equipped.GetProperty("allyToken").GetString());
            var resumed = await seat.GetFromJsonAsync<JsonElement>("/api/battle-preview/rooms/" + equipped.GetProperty("roomId").GetString(), ct);
            Assert.Equal(worn.GetRawText(), Stats(resumed.GetProperty("snapshot"), "ally-1").GetRawText());
            Assert.Equal(bare.GetRawText(), Stats(resumed.GetProperty("snapshot"), "ally-2").GetRawText());
            Assert.Equal(equipped.GetProperty("contentHash").GetString(), resumed.GetProperty("contentHash").GetString());
            var next = (await Start()).GetProperty("opening").GetProperty("snapshot");
            Assert.Equal(basisSpeed + 5, Stats(next, "ally-1").GetProperty("speed").GetInt32());
            Assert.Equal(basisSpeed + 2, Stats(next, "ally-2").GetProperty("speed").GetInt32());

            async Task Equip(HttpClient client, JsonElement item, string wearer)
            {
                using var response = await client.PostAsJsonAsync(gear + "/actions", new
                {
                    requestId = Guid.NewGuid().ToString("N"), action = "equip", heroInstanceId = wearer,
                    instanceId = item.GetProperty("instanceId").GetString(), slot = item.GetProperty("slot").GetString(),
                    expectedVersion = inventory.GetProperty("version").GetInt64()
                }, ct);
                response.EnsureSuccessStatusCode();
                inventory = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            }

            async Task<JsonElement> Start()
            {
                using var response = await player.PostAsJsonAsync("/api/battle-preview/owned-trial", new { heroInstanceIds = new[] { second, first } }, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            }
        }
        finally
        {
            using var cleanup = new MySqlCommand("DELETE FROM arena_player WHERE id = @player", db);
            cleanup.Parameters.AddWithValue("@player", owner);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static JsonElement Stats(JsonElement snapshot, string member) =>
        Assert.Single(snapshot.GetProperty("units").EnumerateArray(), unit => unit.GetProperty("instanceId").GetString() == member).GetProperty("stats");

    private static string CatalogHash(string name)
    {
        using var stream = typeof(EntryEndpoints).Assembly.GetManifestResourceStream(name)!;
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
