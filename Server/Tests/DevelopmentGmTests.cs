using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Xunit;

public sealed class DevelopmentGmTests
{
    private const string Route = "/api/development/gm";

    [Theory]
    [InlineData("Production", "127.0.0.1")]
    [InlineData("Development", "0.0.0.0")]
    public async Task ServiceDoesNotPublishGmOutsideLoopbackDevelopment(string environment, string bindAddress)
    {
        await using var app = new ListenerFactory(environment, bindAddress);
        using var http = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync(Route + "/actions", new {})).StatusCode);
    }

    // These values select Kestrel's listener before WebApplication.Build; supply them as host arguments.
    private sealed class ListenerFactory(string environment, string bindAddress) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["environment"] = environment,
                ["Server:BindAddress"] = bindAddress, ["Server:CertificatePath"] = "testserver-only.pfx"
            }));
            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task AnAnonymousDevelopmentClientCannotReadOrWrite()
    {
        await using var factory = App(null);
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(Route + "/actions",
            new { requestId = Guid.NewGuid().ToString("N"), action = "set", key = "gold", value = 30 })).StatusCode);
    }

    [MySqlFact]
    public async Task AccountWritesAreIsolatedAndRetryable()
    {
        var database = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(database, ct);
        await using var factory = App(database);
        using var a = factory.CreateClient();
        using var b = factory.CreateClient();
        var ids = new List<string>();
        try
        {
            foreach (var http in new[] { a, b })
            {
                using var login = await http.PostAsJsonAsync("/api/arena/sessions",
                    new { displayName = "GM测试" + Guid.NewGuid().ToString("N")[..6] }, ct);
                login.EnsureSuccessStatusCode();
                var session = await login.Content.ReadFromJsonAsync<JsonElement>(ct);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
                ids.Add(session.GetProperty("player").GetProperty("id").GetString()!);
            }
            var initial = await b.GetFromJsonAsync<JsonElement>(Route, ct);
            var resources = initial.GetProperty("resources");
            var gold = resources.EnumerateArray().Single(x => x.GetProperty("key").GetString() == "gold").GetProperty("value").GetInt32();
            var template = initial.GetProperty("heroes")[0].GetString();
            var request = new { action = "hero", key = template, value = 2, requestId = Guid.NewGuid().ToString("N") };
            foreach (var reply in await Task.WhenAll(a.PostAsJsonAsync(Route + "/actions", request, ct), a.PostAsJsonAsync(Route + "/actions", request, ct)))
                using (reply) reply.EnsureSuccessStatusCode();
            using var conflict = await a.PostAsJsonAsync(Route + "/actions", request with { value = 3 }, ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var change = await a.PostAsJsonAsync(Route + "/actions", new { requestId = Guid.NewGuid().ToString("N"), action = "set", key = "gold", value = 13 }, ct);
            change.EnsureSuccessStatusCode();
            foreach ((string action, string key) in new[]
            {
                ("artifact", initial.GetProperty("artifacts")[0].GetString()!),
                ("equipment", initial.GetProperty("equipment")[0].GetString()!),
                ("set", "skystones"), ("set", "bookmarks")
            })
            {
                using var reply = await a.PostAsJsonAsync(Route + "/actions",
                    new { action, key, value = 2, requestId = Guid.NewGuid().ToString("N") }, ct);
                reply.EnsureSuccessStatusCode();
            }
            using var invalid = await a.PostAsJsonAsync(Route + "/actions",
                new { action = "equipment", key = "invalid", value = 2, requestId = Guid.NewGuid().ToString("N") }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var own = await a.GetFromJsonAsync<JsonElement>(Route, ct);
            Assert.Equal(13, own.GetProperty("resources").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "gold").GetProperty("value").GetInt32());
            var after = await b.GetFromJsonAsync<JsonElement>(Route, ct);
            Assert.Equal(gold, after.GetProperty("resources").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "gold").GetProperty("value").GetInt32());
            await using var db = new MySqlConnection(database);
            await db.OpenAsync(ct);
            using var count = new MySqlCommand("SELECT COUNT(*) FROM game_operation_audit WHERE operation='development.gm.hero' AND actor_id=@player", db);
            count.Parameters.AddWithValue("@player", ids[0]);
            Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync(ct)));
            using var grants = new MySqlCommand("""
                SELECT (SELECT COUNT(*) FROM inventory_equipment WHERE player_id=@player AND source_ref LIKE 'gm-%'),
                       (SELECT COUNT(*) FROM inventory_artifact WHERE player_id=@player AND source='development-gm'),
                       (SELECT COUNT(*) FROM inventory_hero WHERE player_id=@player);
                """, db);
            grants.Parameters.AddWithValue("@player", ids[0]);
            await using var read = await grants.ExecuteReaderAsync(ct);
            Assert.True(await read.ReadAsync(ct));
            Assert.Equal(2, read.GetInt32(0));
            Assert.Equal(2, read.GetInt32(1));
            Assert.Equal(5, read.GetInt32(2)); // three starters plus the deduplicated grant
        }
        finally
        {
            await using var db = new MySqlConnection(database);
            await db.OpenAsync();
            foreach (string id in ids)
            {
                using var delete = new MySqlCommand("DELETE FROM arena_player WHERE id=@player", db);
                delete.Parameters.AddWithValue("@player", id);
                await delete.ExecuteNonQueryAsync();
            }
        }
    }

    private static WebApplicationFactory<Program> App(string? connection) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["ConnectionStrings:MySql"] = connection, ["Entry:AllowLegacyArenaGuests"] = "true"
            }));
        });
}
