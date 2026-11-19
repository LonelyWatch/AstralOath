using AstralOath.Server.Summon;
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

public sealed class SummonTests
{
    [Fact]
    public void PickupEndsAtItsPublishedDeadlineAndCovenantStaysOpen()
    {
        var catalog = SummonCatalog.Load(AstralOath.Server.Battle.ArtifactCatalogReader.Load());
        var pickup = Assert.Single(catalog.Active(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)),
            banner => banner.Id == "iseria");
        Assert.True(pickup.EndUtc.HasValue);
        var end = pickup.EndUtc.Value;
        Assert.Contains(catalog.Active(end.AddTicks(-1)), banner => banner.Id == pickup.Id);
        Assert.DoesNotContain(catalog.Active(end), banner => banner.Id == pickup.Id);
        Assert.Contains(catalog.Active(end), banner => banner.Id == "covenant" && banner.EndUtc == null);
    }

    [Fact]
    public void WeightedBoundariesAndPityFollowDrawOrder()
    {
        const int total = SummonCatalog.TotalWeight;
        SummonEntry featured = new("hero", "c1024", 5, total / 100, 1m, true);
        SummonEntry artifact = new("artifact", "efr02", 5, total - featured.Weight, 99m, true);
        var entries = new[] { featured, artifact };
        SummonBanner banner = new("iseria", "UP", featured.TemplateId, artifact.TemplateId, 5,
            false, false, false, 120, null, null, []);
        int misses = 0;

        Assert.Equal(featured, SummonCatalog.Select(entries, banner, ref misses, _ => 0));
        Assert.Equal(featured, SummonCatalog.Select(entries, banner, ref misses, _ => featured.Weight - 1));
        Assert.Equal(artifact, SummonCatalog.Select(entries, banner, ref misses, _ => featured.Weight));
        Assert.Equal(1, misses);

        misses = 119;
        Assert.Equal(artifact, SummonCatalog.Select(entries, banner, ref misses, _ => total - 1));
        Assert.Equal(120, misses);
        Assert.Equal(featured, SummonCatalog.Select(entries, banner, ref misses, _ => throw new InvalidOperationException()));
        Assert.Equal(0, misses);
        Assert.Equal(artifact, SummonCatalog.Select(entries, banner, ref misses, _ => total - 1));
        Assert.Equal(1, misses);
    }

    [Fact]
    public async Task SummonRequiresAuthenticationAtEveryEntry()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = Configure(factory, null);
        using var client = app.CreateClient();

        using var status = await client.GetAsync("/api/summon");
        using var draw = await client.PostAsJsonAsync("/api/summon/draw", new { requestId = Guid.NewGuid().ToString("N") });
        using var latest = await client.GetAsync("/api/summon/draw/latest");
        using var storage = await client.GetAsync("/api/summon/storage");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, draw.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, latest.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, storage.StatusCode);
    }

    [MySqlFact]
    public async Task DrawIsDurableAndIdempotentAcrossConcurrentRequestsAndRestarts()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = Configure(factory, connection);
        using var client = app.CreateClient();
        string playerId = await OpenGuest(client, ct);
        try
        {
            var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/summon", ct);
            Assert.Equal(60, snapshot.GetProperty("bookmarks").GetInt32());
            foreach (string key in new[] { "goldTransmitStones", "galaxyBookmarks", "mysticMedals", "skystones" })
                Assert.Equal(0, snapshot.GetProperty(key).GetInt32());
            await using (var walletDatabase = new MySqlConnection(connection))
            {
                await walletDatabase.OpenAsync(ct);
                using var grant = new MySqlCommand("""
                    INSERT INTO summon_wallet_materials
                        (player_id, realm_id, gold_transmit_stones, galaxy_bookmarks, mystic_medals, skystones)
                    SELECT player_id, realm_id, 7, 11, 29, 501 FROM summon_wallet WHERE player_id = @player;
                    """, walletDatabase);
                grant.Parameters.AddWithValue("@player", playerId);
                Assert.Equal(1, await grant.ExecuteNonQueryAsync(ct));
            }
            Assert.True(snapshot.GetProperty("freeAvailable").GetBoolean());
            string revision = snapshot.GetProperty("banners").EnumerateArray()
                .Single(banner => banner.GetProperty("id").GetString() == "covenant").GetProperty("revision").GetString()!;
            string requestId = Guid.NewGuid().ToString("N");
            var request = new { requestId, bannerId = "covenant", revision, count = 10, payment = "bookmarks" };

            var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => client.PostAsJsonAsync("/api/summon/draw", request, ct)));
            string[] bodies;
            try
            {
                Assert.All(responses, response => response.EnsureSuccessStatusCode());
                bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync(ct)));
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }
            Assert.All(bodies, body => Assert.Equal(bodies[0], body));
            using var receipt = JsonDocument.Parse(bodies[0]);
            Assert.Equal(10, receipt.RootElement.GetProperty("bookmarks").GetInt32());
            Assert.Equal(50, receipt.RootElement.GetProperty("cost").GetInt32());
            Assert.Equal(Enumerable.Range(1, 10), receipt.RootElement.GetProperty("results").EnumerateArray()
                .Select(result => result.GetProperty("position").GetInt32()));

            using var conflicting = await client.PostAsJsonAsync("/api/summon/draw",
                new { requestId, bannerId = "covenant", revision, count = 1, payment = "bookmarks" }, ct);
            Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

            await using var restarted = Configure(factory, connection);
            using var resumed = restarted.CreateClient();
            resumed.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            Assert.Equal(bodies[0], await resumed.GetStringAsync("/api/summon/draw/" + requestId, ct));
            Assert.Equal(bodies[0], await resumed.GetStringAsync("/api/summon/draw/latest", ct));
            var balances = await resumed.GetFromJsonAsync<JsonElement>("/api/summon", ct);
            Assert.Equal(7, balances.GetProperty("goldTransmitStones").GetInt32());
            Assert.Equal(11, balances.GetProperty("galaxyBookmarks").GetInt32());
            Assert.Equal(29, balances.GetProperty("mysticMedals").GetInt32());
            Assert.Equal(501, balances.GetProperty("skystones").GetInt32());

            var free = new { requestId = Guid.NewGuid().ToString("N"), bannerId = "covenant", revision, count = 1, payment = "free" };
            using var first = await client.PostAsJsonAsync("/api/summon/draw", free, ct);
            first.EnsureSuccessStatusCode();
            using var used = await client.PostAsJsonAsync("/api/summon/draw", new { requestId = Guid.NewGuid().ToString("N"),
                bannerId = "covenant", revision, count = 1, payment = "free" }, ct);
            Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
            Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/summon", ct)).GetProperty("freeAvailable").GetBoolean());
        }
        finally
        {
            await using var database = new MySqlConnection(connection);
            await database.OpenAsync(CancellationToken.None);
            using var cleanup = new MySqlCommand("DELETE FROM arena_player WHERE id = @player;", database);
            cleanup.Parameters.AddWithValue("@player", playerId);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [MySqlFact]
    public async Task WishlistAndOverflowUseOwnedSlotsAndKeepAwardIds()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = Configure(factory, connection);
        using var client = app.CreateClient();
        string playerId = await OpenGuest(client, ct);
        await using var database = new MySqlConnection(connection);
        await database.OpenAsync(ct);
        try
        {
            var state = await client.GetFromJsonAsync<JsonElement>("/api/summon", ct);
            var choices = state.GetProperty("wishHeroCandidates").EnumerateArray().Select(entry => entry.GetString()).ToArray();
            var heroes = choices.TakeLast(7).ToArray();
            var artifacts = state.GetProperty("wishArtifactCandidates").EnumerateArray().Select(entry => entry.GetString())
                .TakeLast(7).ToArray();
            var covenant = Covenant(state);
            var originalRevision = covenant.GetProperty("revision").GetString();
            using var invalid = await client.PutAsJsonAsync("/api/summon/wishlist",
                new { heroes = Enumerable.Repeat(heroes[0], 7), artifacts }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var save = await client.PutAsJsonAsync("/api/summon/wishlist", new { heroes, artifacts }, ct);
            save.EnsureSuccessStatusCode();
            var changed = Covenant(await save.Content.ReadFromJsonAsync<JsonElement>(ct));
            string revision = changed.GetProperty("revision").GetString()!;
            Assert.NotEqual(originalRevision, revision);
            Assert.Equal(100m, changed.GetProperty("entries").EnumerateArray().Sum(x => x.GetProperty("probability").GetDecimal()));
            Assert.Equal(heroes, changed.GetProperty("entries").EnumerateArray()
                .Where(x => x.GetProperty("stars").GetInt32() == 5 && x.GetProperty("kind").GetString() == "hero" &&
                    x.GetProperty("templateId").GetString() != "c1022")
                .Select(x => x.GetProperty("templateId").GetString()));
            using var expired = await client.PostAsJsonAsync("/api/summon/draw", new { requestId = Guid.NewGuid().ToString("N"),
                bannerId = "covenant", revision = originalRevision, count = 1, payment = "bookmarks" }, ct);
            Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);

            using var starters = await client.GetAsync("/api/inventory/heroes", ct);
            starters.EnsureSuccessStatusCode();
            await Execute("""
                INSERT INTO inventory_hero (instance_id, player_id, realm_id, template_id, level, stars)
                WITH RECURSIVE sequence AS (SELECT 1 AS n UNION ALL SELECT n+1 FROM sequence WHERE n<997)
                SELECT REPLACE(UUID(), '-', ''), @player, 'undying-memory', 'c1018', 1, 3 FROM sequence;
                """);
            await Execute("""
                INSERT INTO inventory_artifact (instance_id, player_id, realm_id, template_id, source)
                WITH RECURSIVE sequence AS (SELECT 1 AS n UNION ALL SELECT n+1 FROM sequence WHERE n<400)
                SELECT REPLACE(UUID(), '-', ''), @player, 'undying-memory', 'ef301', 'summon' FROM sequence;
                """);

            using var draw = await client.PostAsJsonAsync("/api/summon/draw", new { requestId = Guid.NewGuid().ToString("N"),
                bannerId = "covenant", revision, count = 10, payment = "bookmarks" }, ct);
            draw.EnsureSuccessStatusCode();
            var receipt = await draw.Content.ReadFromJsonAsync<JsonElement>(ct);
            var results = receipt.GetProperty("results").EnumerateArray().ToArray();
            Assert.Equal(10, results.Length);
            Assert.All(results, result => Assert.True(result.GetProperty("inStorage").GetBoolean()));
            var first = results[0];
            string instanceId = first.GetProperty("instanceId").GetString()!;
            using var whileFull = await client.PostAsJsonAsync("/api/summon/storage/claim", new { instanceIds = new[] { instanceId } }, ct);
            Assert.Equal(HttpStatusCode.Conflict, whileFull.StatusCode);

            if (first.GetProperty("kind").GetString() == "hero")
                await Execute("DELETE FROM inventory_hero WHERE player_id = @player AND template_id = 'c1018' LIMIT 1;");
            else
                await Execute("DELETE FROM inventory_artifact WHERE player_id = @player AND template_id = 'ef301' LIMIT 1;");
            using var claim = await client.PostAsJsonAsync("/api/summon/storage/claim", new { instanceIds = new[] { instanceId } }, ct);
            claim.EnsureSuccessStatusCode();
            var storage = await claim.Content.ReadFromJsonAsync<JsonElement>(ct);
            var remaining = storage.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(9, remaining.Length);
            Assert.DoesNotContain(remaining, item => item.GetProperty("instanceId").GetString() == instanceId);

            using var duplicateClaim = await client.PostAsJsonAsync("/api/summon/storage/claim",
                new { instanceIds = new[] { instanceId } }, ct);
            Assert.Equal(HttpStatusCode.NotFound, duplicateClaim.StatusCode);
            if (first.GetProperty("kind").GetString() == "hero")
            {
                using var owned = await client.GetAsync("/api/inventory/heroes", ct);
                owned.EnsureSuccessStatusCode();
                var inventory = await owned.Content.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Contains(inventory.GetProperty("heroes").EnumerateArray(),
                    hero => hero.GetProperty("instanceId").GetString() == instanceId);
            }
            else
            {
                using var check = new MySqlCommand("SELECT COUNT(*) FROM inventory_artifact WHERE instance_id = @id AND player_id = @player;", database);
                check.Parameters.AddWithValue("@id", instanceId);
                check.Parameters.AddWithValue("@player", playerId);
                Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync(ct)));
            }
        }
        finally { await Execute("DELETE FROM arena_player WHERE id = @player;"); }

        async Task Execute(string sql)
        {
            using var command = new MySqlCommand(sql, database);
            command.Parameters.AddWithValue("@player", playerId);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static JsonElement Covenant(JsonElement state) =>
        state.GetProperty("banners").EnumerateArray().Single(banner => banner.GetProperty("id").GetString() == "covenant");

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory, string? connection)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection,
                    ["Entry:AllowLegacyArenaGuests"] = "true" }));
        });

    private static async Task<string> OpenGuest(HttpClient client, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/arena/sessions",
            new { displayName = "召唤测试" + Guid.NewGuid().ToString("N")[..6] }, ct);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return session.GetProperty("player").GetProperty("id").GetString()!;
    }
}
