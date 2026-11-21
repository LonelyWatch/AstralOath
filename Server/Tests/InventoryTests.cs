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

public sealed class InventoryTests
{
    private const string HeroesPath = "/api/inventory/heroes";

    [MySqlFact]
    public async Task ConcurrentGuestReadsGrantStartersOnceAndKeepDuplicateTemplatesAndOwnersSeparate()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = Configure(factory, connection);
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        using var unauthenticated = await first.GetAsync(HeroesPath, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        // Equal names are deliberately allowed, and never identify the inventory owner.
        string name = "guest_" + Guid.NewGuid().ToString("N")[..12];
        string firstId = await OpenGuest(first, name, ct);
        string secondId = await OpenGuest(second, name, ct);
        try
        {
            JsonElement[] snapshots = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Read(first, ct)));
            string[] firstIds = AssertStarters(snapshots[0]);
            Assert.All(snapshots, snapshot => Assert.Equal(firstIds, AssertStarters(snapshot)));
            string[] secondIds = AssertStarters(await Read(second, ct));
            Assert.Empty(firstIds.Intersect(secondIds));

            await using var database = new MySqlConnection(connection);
            await database.OpenAsync(ct);
            using (var counter = new MySqlCommand("""
                SELECT COUNT(*) FROM game_operation_audit
                WHERE operation = 'inventory.hero.starter_granted' AND actor_id = @player;
                """, database))
            {
                counter.Parameters.AddWithValue("@player", firstId);
                Assert.Equal(1L, Convert.ToInt64(await counter.ExecuteScalarAsync(ct)));
            }

            string clone = Guid.NewGuid().ToString("N");
            using (var insert = new MySqlCommand("""
                INSERT INTO inventory_hero (instance_id, player_id, realm_id, template_id, level, stars)
                VALUES (@instance, @player, 'undying-memory', 'c1001', 7, 3);
                """, database))
            {
                insert.Parameters.AddWithValue("@instance", clone);
                insert.Parameters.AddWithValue("@player", firstId);
                Assert.Equal(1, await insert.ExecuteNonQueryAsync(ct));
            }

            var withDuplicate = await Read(first, ct);
            Assert.Equal(4, withDuplicate.GetProperty("used").GetInt32());
            var duplicate = Assert.Single(withDuplicate.GetProperty("heroes").EnumerateArray(),
                entry => entry.GetProperty("instanceId").GetString() == clone);
            Assert.Equal("c1001", duplicate.GetProperty("templateId").GetString());
            Assert.Equal(7, duplicate.GetProperty("level").GetInt32());
            Assert.Equal(2, withDuplicate.GetProperty("heroes").EnumerateArray()
                .Count(entry => entry.GetProperty("templateId").GetString() == "c1001"));

            string[] forgedView = AssertStarters(await second.GetFromJsonAsync<JsonElement>(
                HeroesPath + "?playerId=" + firstId + "&realmId=other", ct));
            Assert.Equal(secondIds, forgedView);

            await using var restarted = Configure(factory, connection);
            using var returned = restarted.CreateClient();
            returned.DefaultRequestHeaders.Authorization = first.DefaultRequestHeaders.Authorization;
            var persisted = await Read(returned, ct);
            Assert.Equal(withDuplicate.GetRawText(), persisted.GetRawText());
        }
        finally
        {
            await using var database = new MySqlConnection(connection);
            await database.OpenAsync(CancellationToken.None);
            using var cleanup = new MySqlCommand("DELETE FROM arena_player WHERE id IN (@first, @second);", database);
            cleanup.Parameters.AddWithValue("@first", firstId);
            cleanup.Parameters.AddWithValue("@second", secondId);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal static string[] AssertStarters(JsonElement inventory)
    {
        Assert.Equal(1000, inventory.GetProperty("capacity").GetInt32());
        Assert.Equal(3, inventory.GetProperty("used").GetInt32());
        var heroes = inventory.GetProperty("heroes").EnumerateArray().ToArray();
        Assert.Equal(["c1001", "c1005", "c1062"], heroes.Select(hero => hero.GetProperty("templateId").GetString()));
        Assert.Equal([3, 4, 4], heroes.Select(hero => hero.GetProperty("stars").GetInt32()));
        Assert.All(heroes, hero => Assert.Equal(1, hero.GetProperty("level").GetInt32()));
        string[] ids = heroes.Select(hero => hero.GetProperty("instanceId").GetString()!).ToArray();
        Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "N", out _)));
        Assert.Equal(3, ids.Distinct().Count());
        return ids;
    }

    internal static async Task<JsonElement> Read(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(HeroesPath, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory, string connection)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection,
                    ["Entry:AllowLegacyArenaGuests"] = "true" }));
        });

    internal static async Task<string> OpenGuest(HttpClient client, string name, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/arena/sessions", new { displayName = name }, ct);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return session.GetProperty("player").GetProperty("id").GetString()!;
    }
}
