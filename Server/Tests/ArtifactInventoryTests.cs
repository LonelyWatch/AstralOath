using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class ArtifactInventoryTests
{
    private const string Path = "/api/inventory/artifacts";

    [Fact]
    public void OriginalDescriptionsResolveForAllEnabledArtifactRanks()
    {
        var catalog = ArtifactCatalogReader.Load();
        Assert.Equal(42, catalog.Templates.Count);
        foreach (var template in catalog.Templates)
        {
            foreach (int level in new[] { 0, 15, 30 })
            {
                string text = template.DescriptionAt(level);
                Assert.NotEmpty(text);
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("}", text);
                Assert.DoesNotContain("#", text);
                Assert.True(template.AttackAt(level) >= template.AttackAt(0));
                Assert.True(template.HealthAt(level) >= template.HealthAt(0));
            }
        }
        Assert.Contains("1.5%", catalog.Get("ef317").DescriptionAt(0));
        Assert.Contains("3%", catalog.Get("ef317").DescriptionAt(30));
        Assert.Contains("10点", catalog.Get("efm03").DescriptionAt(0));
        Assert.Contains("20点", catalog.Get("efm03").DescriptionAt(30));
        Assert.Contains("25%", catalog.Get("efw01").DescriptionAt(0));
        Assert.Contains("50%", catalog.Get("efw01").DescriptionAt(30));
        Assert.Contains("35%", catalog.Get("efa07").DescriptionAt(0));
        Assert.Contains("70%", catalog.Get("efa07").DescriptionAt(30));
    }

    [MySqlFact]
    public async Task ArtifactOwnershipEquipBreakSaleAndRetryAreIsolatedAndTransactional()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection,
                    ["Entry:AllowLegacyArenaGuests"] = "true" }));
        });
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        using var anonymous = await first.GetAsync(Path, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        string owner = await Authenticate(first, ct);
        string stranger = await Authenticate(second, ct);
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync(ct);
        try
        {
            string[] heroes = InventoryTests.AssertStarters(await InventoryTests.Read(first, ct));
            string[] otherHeroes = InventoryTests.AssertStarters(await InventoryTests.Read(second, ct));
            JsonElement empty = await Read(first, ct);
            Assert.Equal(400, empty.GetProperty("capacity").GetInt32());
            Assert.Equal(0, empty.GetProperty("used").GetInt32());

            string target = await Seed(db, owner, "ef317", 0, ct);
            string duplicate = await Seed(db, owner, "ef317", 0, ct);
            string knightOnly = await Seed(db, owner, "efk03", 0, ct);
            Assert.Equal(3, (await Read(first, ct)).GetProperty("used").GetInt32());
            Assert.Equal(0, (await Read(second, ct)).GetProperty("used").GetInt32());

            await Send(first, knightOnly, "equip", new { requestId = Id(), heroInstanceId = heroes[1] }, HttpStatusCode.Conflict, ct);
            await Send(first, target, "equip", new { requestId = Id(), heroInstanceId = otherHeroes[0] }, HttpStatusCode.NotFound, ct);
            await Send(second, target, "lock", new { requestId = Id(), locked = true }, HttpStatusCode.NotFound, ct);
            var equipped = await Send(first, target, "equip", new { requestId = Id(), heroInstanceId = heroes[0] }, HttpStatusCode.OK, ct);
            Assert.Equal(heroes[0], Find(equipped, target).GetProperty("heroInstanceId").GetString());
            var moved = await Send(first, target, "equip", new { requestId = Id(), heroInstanceId = heroes[2] }, HttpStatusCode.OK, ct);
            Assert.Equal(heroes[2], Find(moved, target).GetProperty("heroInstanceId").GetString());
            await Send(first, target, "sell", new { requestId = Id() }, HttpStatusCode.Conflict, ct);

            var attempt = new { requestId = Id(), materials = new[] { new { kind = "artifact", instanceId = duplicate, quantity = 1 } } };
            var firstUse = await Send(first, target, "enhance", attempt, HttpStatusCode.OK, ct);
            Assert.Equal(1, Find(firstUse, target).GetProperty("limitBreaks").GetInt32());
            Assert.Equal(2, firstUse.GetProperty("used").GetInt32());
            var replay = await Send(first, target, "enhance", attempt, HttpStatusCode.OK, ct);
            Assert.Equal(1, Find(replay, target).GetProperty("limitBreaks").GetInt32());
            Assert.Equal(2, replay.GetProperty("used").GetInt32());

            // A capped package cannot consume any of the proposed same-template copies.
            var extras = new List<string>();
            for (int i = 0; i < 5; ++i)
                extras.Add(await Seed(db, owner, "ef317", 0, ct));
            var capped = await Send(first, target, "enhance", new
            {
                requestId = Id(),
                materials = extras.Select(id => new { kind = "artifact", instanceId = id, quantity = 1 }).ToArray()
            }, HttpStatusCode.Conflict, ct);
            Assert.Equal("artifact_break_cap", capped.GetProperty("code").GetString());
            Assert.Equal(7, (await Read(first, ct)).GetProperty("used").GetInt32());
            var raiseToCap = await Send(first, target, "enhance", new
            {
                requestId = Id(),
                materials = extras.Take(4).Select(id => new { kind = "artifact", instanceId = id, quantity = 1 }).ToArray()
            }, HttpStatusCode.OK, ct);
            Assert.Equal(5, Find(raiseToCap, target).GetProperty("limitBreaks").GetInt32());
            await Send(first, extras[4], "sell", new { requestId = Id() }, HttpStatusCode.OK, ct);

            // A concurrent replay is settled once under the same owner lock.
            var upgrade = new { requestId = Id(), materials = new[] { new { kind = "lesserCharm", quantity = 1 } } };
            int priorCharm = replay.GetProperty("materials").GetProperty("lesserCharm").GetInt32();
            var upgrades = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => Send(first, target, "enhance", upgrade, HttpStatusCode.OK, ct)));
            Assert.All(upgrades, result =>
            {
                Assert.Equal(priorCharm - 1, result.GetProperty("materials").GetProperty("lesserCharm").GetInt32());
                Assert.Equal(5, Find(result, target).GetProperty("limitBreaks").GetInt32());
            });
            using (var emptyGold = new MySqlCommand("""
                UPDATE inventory_artifact_material SET balance = 0
                WHERE player_id = @player AND realm_id = 'undying-memory' AND material_id = 'gold';
                """, db))
            {
                emptyGold.Parameters.AddWithValue("@player", owner);
                Assert.Equal(1, await emptyGold.ExecuteNonQueryAsync(ct));
            }
            var denied = await Send(first, target, "enhance",
                new { requestId = Id(), materials = upgrade.materials }, HttpStatusCode.Conflict, ct);
            Assert.Equal("artifact_material_insufficient", denied.GetProperty("code").GetString());
            var unchanged = await Read(first, ct);
            Assert.Equal(priorCharm - 1, unchanged.GetProperty("materials").GetProperty("lesserCharm").GetInt32());
            Assert.Equal(Find(upgrades[0], target).GetProperty("experience").GetInt32(),
                Find(unchanged, target).GetProperty("experience").GetInt32());
            await Send(first, target, "sell", new { requestId = attempt.requestId }, HttpStatusCode.Conflict, ct);

            await Send(first, target, "lock", new { requestId = Id(), locked = true }, HttpStatusCode.OK, ct);
            await Send(first, target, "unequip", new { requestId = Id() }, HttpStatusCode.OK, ct);
            await Send(first, target, "sell", new { requestId = Id() }, HttpStatusCode.Conflict, ct);
            await Send(first, target, "lock", new { requestId = Id(), locked = false }, HttpStatusCode.OK, ct);
            int powder = (await Read(first, ct)).GetProperty("materials").GetProperty("powder").GetInt32();
            var sale = new { requestId = Id() };
            var sold = await Send(first, target, "sell", sale, HttpStatusCode.OK, ct);
            Assert.Equal(1, sold.GetProperty("used").GetInt32());
            int newPowder = sold.GetProperty("materials").GetProperty("powder").GetInt32();
            Assert.True(newPowder > powder);
            var retry = await Send(first, target, "sell", sale, HttpStatusCode.OK, ct);
            Assert.Equal(newPowder, retry.GetProperty("materials").GetProperty("powder").GetInt32());
        }
        finally
        {
            using var cleanup = new MySqlCommand("DELETE FROM arena_player WHERE id IN (@player, @stranger);", db);
            cleanup.Parameters.AddWithValue("@player", owner);
            cleanup.Parameters.AddWithValue("@stranger", stranger);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [MySqlFact]
    public async Task OwnedArtifactTrialFreezesLoadoutAndRejectsAnotherPlayersHeroes()
    {
        const string trial = "/api/battle-preview/owned-trial";
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        await using var factory = new WebApplicationFactory<Program>();
        await using var app = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection,
                    ["Entry:AllowLegacyArenaGuests"] = "true", ["BattlePreview:Enabled"] = "true",
                    ["Server:BindAddress"] = "127.0.0.1" })));
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        using (var anonymous = await first.PostAsJsonAsync(trial, new { heroInstanceIds = new[] { Id() } }, ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        string owner = await Authenticate(first, ct);
        string stranger = await Authenticate(second, ct);
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync(ct);
        try
        {
            string[] heroes = InventoryTests.AssertStarters(await InventoryTests.Read(first, ct));
            string[] otherHeroes = InventoryTests.AssertStarters(await InventoryTests.Read(second, ct));
            using (var foreign = await first.PostAsJsonAsync(trial, new { heroInstanceIds = new[] { otherHeroes[0] } }, ct))
                Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using (var repeated = await first.PostAsJsonAsync(trial, new { heroInstanceIds = new[] { heroes[0], heroes[0] } }, ct))
                Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
            string artifact = await Seed(db, owner, "ef317", 0, ct);
            await Send(first, artifact, "equip", new { requestId = Id(), heroInstanceId = heroes[0] }, HttpStatusCode.OK, ct);
            var equipped = await StartTrial();
            string? matchHash = equipped.GetProperty("contentHash").GetString();
            string? battleHash = equipped.GetProperty("battleContentHash").GetString();
            Assert.NotEqual(battleHash, matchHash);
            Assert.Equal(matchHash, equipped.GetProperty("opening").GetProperty("contentHash").GetString());
            var opening = equipped.GetProperty("opening").GetProperty("snapshot");
            Assert.StartsWith("ally-", opening.GetProperty("currentActorId").GetString());
            var unit = TrialHero(opening);
            Assert.Equal("c1001", unit.GetProperty("definitionId").GetString());
            Assert.Equal(1, unit.GetProperty("level").GetInt32());
            Assert.Empty(equipped.GetProperty("enemyToken").GetString()!);
            int attack = unit.GetProperty("stats").GetProperty("attack").GetInt32();
            int health = unit.GetProperty("stats").GetProperty("maxHealth").GetInt32();

            await Send(first, artifact, "unequip", new { requestId = Id() }, HttpStatusCode.OK, ct);
            using var seat = app.CreateClient();
            seat.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", equipped.GetProperty("allyToken").GetString());
            string room = "/api/battle-preview/rooms/" + equipped.GetProperty("roomId").GetString();
            using var resumed = await seat.GetAsync(room, ct);
            resumed.EnsureSuccessStatusCode();
            var replay = await resumed.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(attack, TrialHero(replay.GetProperty("snapshot")).GetProperty("stats").GetProperty("attack").GetInt32());

            var command = new
            {
                rulesVersion = opening.GetProperty("rulesVersion").GetUInt32(),
                sequence = opening.GetProperty("lastCommandSequence").GetUInt64() + 1,
                commandId = Id(),
                actorId = "ally-1",
                skillId = "c1001.s1",
                targetId = "enemy-1",
                useSoulBurn = false,
                contentHash = matchHash
            };
            using (var legacy = await seat.PostAsJsonAsync(room + "/commands", command with { contentHash = battleHash }, ct))
                Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
            using var action = await seat.PostAsJsonAsync(room + "/commands", command, ct);
            action.EnsureSuccessStatusCode();
            var reply = await action.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(0, reply.GetProperty("status").GetInt32());
            var after = reply.GetProperty("snapshot");
            if (after.GetProperty("outcome").GetInt32() == 0)
                Assert.StartsWith("ally-", after.GetProperty("currentActorId").GetString());

            var unequipped = await StartTrial();
            var baseStats = TrialHero(unequipped.GetProperty("opening").GetProperty("snapshot")).GetProperty("stats");
            var template = ArtifactCatalogReader.Load().Get("ef317");
            Assert.Equal(template.AttackAt(0), attack - baseStats.GetProperty("attack").GetInt32());
            Assert.Equal(template.HealthAt(0), health - baseStats.GetProperty("maxHealth").GetInt32());

            async Task<JsonElement> StartTrial()
            {
                using var response = await first.PostAsJsonAsync(trial, new { heroInstanceIds = new[] { heroes[0] } }, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            }
        }
        finally
        {
            using var cleanup = new MySqlCommand("DELETE FROM arena_player WHERE id IN (@player, @stranger);", db);
            cleanup.Parameters.AddWithValue("@player", owner);
            cleanup.Parameters.AddWithValue("@stranger", stranger);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }

        static JsonElement TrialHero(JsonElement snapshot) => Assert.Single(snapshot.GetProperty("units").EnumerateArray(),
            unit => unit.GetProperty("instanceId").GetString()!.StartsWith("ally-", StringComparison.Ordinal));
    }

    private static string Id() => Guid.NewGuid().ToString("N");

    private static async Task<string> Authenticate(HttpClient client, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/arena/sessions", new { displayName = "arti_" + Id()[..8] }, ct);
        response.EnsureSuccessStatusCode();
        var reply = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reply.GetProperty("token").GetString());
        return reply.GetProperty("player").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Read(HttpClient client, CancellationToken ct)
    {
        using var response = await client.GetAsync(Path, ct);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static JsonElement Find(JsonElement inventory, string instance) => Assert.Single(
        inventory.GetProperty("artifacts").EnumerateArray(), item => item.GetProperty("instanceId").GetString() == instance);

    private static async Task<JsonElement> Send(HttpClient client, string instance, string action, object request,
        HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync($"{Path}/{instance}/{action}", request, ct);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static async Task<string> Seed(MySqlConnection connection, string owner, string template,
        int experience, CancellationToken ct)
    {
        string instance = Id();
        using var command = new MySqlCommand("""
            INSERT INTO inventory_artifact (instance_id, player_id, realm_id, template_id, experience, source)
            VALUES (@instance, @player, 'undying-memory', @template, @experience, 'test');
            """, connection);
        command.Parameters.AddWithValue("@instance", instance);
        command.Parameters.AddWithValue("@player", owner);
        command.Parameters.AddWithValue("@template", template);
        command.Parameters.AddWithValue("@experience", experience);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
        return instance;
    }
}
