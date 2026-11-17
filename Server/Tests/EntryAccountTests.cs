using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

public sealed class EntryAccountTests
{
    private const string Entry = "/api/entry";
    private const string RealmId = "undying-memory";

    [Fact]
    public async Task AnnouncesMinimumVersionAndRejectsInvalidInput()
    {
        await using var source = new WebApplicationFactory<Program>();
        await using var app = Configure(source, new Dictionary<string, string?>
        {
            ["Entry:MinimumAppVersion"] = "1.4.2"
        });
        using var client = app.CreateClient();
        var policy = await client.GetFromJsonAsync<JsonElement>(Entry + "/version");
        Assert.Equal("1.4.2", policy.GetProperty("minimumAppVersion").GetString());
        Assert.Equal(RealmId, policy.GetProperty("realmId").GetString());

        using var invalidAddress = await client.PostAsJsonAsync(Entry + "/email-code", new { email = "Display <wrong@address.example>" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAddress.StatusCode);
        using var outdated = await client.PostAsJsonAsync(Entry + "/select-realm", new { realmId = RealmId, appVersion = "1.4.1" });
        Assert.Equal(HttpStatusCode.UpgradeRequired, outdated.StatusCode);
        var rejection = await outdated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("client_update_required", rejection.GetProperty("code").GetString());
        using var unauthorized = await client.PostAsJsonAsync(Entry + "/select-realm", new { realmId = RealmId, appVersion = "1.4.2" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [MySqlFact]
    public async Task VerifiedRegistrationAndRealmSelectionUseTheSameIdentityAcrossLogins()
    {
        string connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        await DatabaseMigrator.ApplyAsync(connection, ct);
        using var smtp = new TcpListener(IPAddress.Loopback, 0);
        smtp.Start(2);
        var message = CaptureMailAsync(smtp, ct);
        await using var source = new WebApplicationFactory<Program>();
        await using var app = Configure(source, new Dictionary<string, string?>
        {
            ["ConnectionStrings:MySql"] = connection,
            ["Entry:Smtp:Host"] = "127.0.0.1",
            ["Entry:Smtp:Port"] = ((IPEndPoint)smtp.LocalEndpoint).Port.ToString(),
            ["Entry:Smtp:From"] = "local@astraloath.example",
            ["Entry:Smtp:UseStartTls"] = "false"
        });
        using var client = app.CreateClient();
        string username = "test_" + Guid.NewGuid().ToString("N")[..16];
        string email = username + "@example.test";
        const string password = "sample password for isolated test";
        using var sent = await client.PostAsJsonAsync(Entry + "/email-code", new { email }, ct);
        Assert.True(sent.StatusCode == HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync(ct));
        string raw = await message;
        Assert.Contains(email, raw);
        string code = Regex.Match(raw, @"Your AstralOath verification code is (\d{6})\.").Groups[1].Value;
        Assert.Equal(6, code.Length);

        using var resend = await client.PostAsJsonAsync(Entry + "/email-code", new { email }, ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
        string wrongCode = code == "000000" ? "111111" : "000000";
        using var wrong = await client.PostAsJsonAsync(Entry + "/register", new { username, email, password, code = wrongCode }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var registered = await Post(client, "/register", new { username, email, password, code }, ct);
        string secondName = username + "_b";
        string secondEmail = secondName + "@example.test";
        var secondMail = CaptureMailAsync(smtp, ct);
        using var secondRequest = await client.PostAsJsonAsync(Entry + "/email-code", new { email = secondEmail }, ct);
        Assert.Equal(HttpStatusCode.NoContent, secondRequest.StatusCode);
        string secondCode = Regex.Match(await secondMail, @"Your AstralOath verification code is (\d{6})\.").Groups[1].Value;
        await Post(client, "/register", new { username = secondName, email = secondEmail, password, code = secondCode }, ct);
        var firstCredentials = await ReadCredentials(connection, email, ct);
        var secondCredentials = await ReadCredentials(connection, secondEmail, ct);
        Assert.False(firstCredentials.Salt.SequenceEqual(secondCredentials.Salt));
        Assert.False(firstCredentials.Hash.SequenceEqual(secondCredentials.Hash));
        foreach (var (salt, hash, iterations) in new[] { firstCredentials, secondCredentials })
        {
            Assert.Equal(16, salt.Length);
            Assert.Equal(32, hash.Length);
            Assert.Equal(600_000, iterations);
            Assert.Equal(hash, Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32));
        }
        using var replay = await client.PostAsJsonAsync(Entry + "/register", new { username, email, password, code }, ct);
        Assert.False(replay.IsSuccessStatusCode);

        Authorize(client, registered);
        using var premature = await client.GetAsync("/api/arena/rooms", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, premature.StatusCode);
        using var unselectedInventory = await client.GetAsync("/api/inventory/heroes", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unselectedInventory.StatusCode);
        using var wrongRealm = await client.PostAsJsonAsync(Entry + "/select-realm", new { realmId = "unknown" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongRealm.StatusCode);
        var firstSession = await Post(client, "/select-realm", new { realmId = RealmId }, ct);
        string firstPlayerId = firstSession.GetProperty("player").GetProperty("id").GetString()!;
        Assert.Equal(3L, await CountOwnedHeroes(connection, firstPlayerId, ct));
        // Existing clients retry realm selection if the downstream resource update is cancelled.
        firstSession = await Post(client, "/select-realm", new { realmId = RealmId }, ct);
        Assert.Equal(firstPlayerId, firstSession.GetProperty("player").GetProperty("id").GetString());
        Assert.Equal(3L, await CountOwnedHeroes(connection, firstPlayerId, ct));
        Authorize(client, firstSession);
        using var rooms = await client.GetAsync("/api/arena/rooms", ct);
        Assert.Equal(HttpStatusCode.OK, rooms.StatusCode);
        var starterIds = InventoryTests.AssertStarters(await InventoryTests.Read(client, ct));

        using var wrongPassword = await client.PostAsJsonAsync(Entry + "/login", new { account = email, password = password + "!" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        using var secondClient = app.CreateClient();
        Authorize(secondClient, await Post(secondClient, "/login", new { account = email.ToUpperInvariant(), password }, ct));
        var secondSession = await Post(secondClient, "/select-realm", new { realmId = RealmId }, ct);
        Assert.Equal(firstSession.GetProperty("player").GetProperty("id").GetString(),
            secondSession.GetProperty("player").GetProperty("id").GetString());
        Assert.NotEqual(firstSession.GetProperty("token").GetString(), secondSession.GetProperty("token").GetString());
        using var replaced = await client.GetAsync("/api/arena/rooms", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replaced.StatusCode);
        using var replacedInventory = await client.GetAsync("/api/inventory/heroes", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replacedInventory.StatusCode);
        Authorize(secondClient, secondSession);
        Assert.Equal(starterIds, InventoryTests.AssertStarters(await InventoryTests.Read(secondClient, ct)));

        // The same owning account remains stable if the title screen retries realm selection.
        using var otherClient = app.CreateClient();
        Authorize(otherClient, await Post(otherClient, "/login", new { account = secondName, password }, ct));
        var otherSession = await Post(otherClient, "/select-realm", new { realmId = RealmId }, ct);
        Authorize(otherClient, otherSession);
        Assert.Empty(starterIds.Intersect(InventoryTests.AssertStarters(await InventoryTests.Read(otherClient, ct))));
    }

    private static async Task<long> CountOwnedHeroes(string connection, string playerId, CancellationToken ct)
    {
        await using var database = new MySqlConnection(connection);
        await database.OpenAsync(ct);
        using var command = new MySqlCommand("""
            SELECT COUNT(*) FROM inventory_hero
            WHERE player_id = @player AND realm_id = 'undying-memory';
            """, database);
        command.Parameters.AddWithValue("@player", playerId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<(byte[] Salt, byte[] Hash, int Iterations)> ReadCredentials(
        string connection, string email, CancellationToken ct)
    {
        await using var database = new MySqlConnection(connection);
        await database.OpenAsync(ct);
        using var command = new MySqlCommand("""
            SELECT password_salt, password_hash, password_iterations
            FROM entry_account WHERE email = @email;
            """, database);
        command.Parameters.AddWithValue("@email", email);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        return (reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<byte[]>(1), reader.GetInt32(2));
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> source,
        Dictionary<string, string?> settings) => source.WithWebHostBuilder(web =>
    {
        web.UseEnvironment("Development");
        web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    });

    private static async Task<JsonElement> Post(HttpClient client, string path, object body, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(Entry + path, body, ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static void Authorize(HttpClient client, JsonElement response) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.GetProperty("token").GetString());

    private static async Task<string> CaptureMailAsync(TcpListener listener, CancellationToken ct)
    {
        using var smtp = await listener.AcceptTcpClientAsync(ct);
        await using var stream = smtp.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost");
        var body = new StringBuilder();
        bool data = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (data)
            {
                if (line != ".") { body.AppendLine(line); continue; }
                data = false;
                await writer.WriteLineAsync("250 queued");
                continue;
            }

            if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                data = true;
                await writer.WriteLineAsync("354 end with dot");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 bye");
                return body.ToString();
            }
            else await writer.WriteLineAsync("250 localhost");
        }
        return body.ToString();
    }
}
