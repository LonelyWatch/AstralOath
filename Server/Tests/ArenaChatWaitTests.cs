using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AstralOath.Server.Foundation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class ArenaChatWaitTests
{
    private const string Arena = "/api/arena";

    [MySqlFact]
    public async Task WaitingForChatReturnsCommittedMessageToBothReaders()
    {
        const int waitMs = 4000;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        await DatabaseMigrator.ApplyAsync(connection, token);
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = source.WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection })));
        using var owner = factory.CreateClient();
        using var guest = factory.CreateClient();
        using var outsider = factory.CreateClient();
        await Authorize(owner, "聊天房主", token);
        await Authorize(guest, "聊天访客", token);
        await Authorize(outsider, "未入场观众", token);
        var created = await Post(owner, "/rooms", new { name = "聊天验证", allowChat = true, rule = "basic" }, token);
        string room = "/rooms/" + created.GetProperty("room").GetProperty("id").GetString();
        string messages = room + "/messages";
        try
        {
            await Post(guest, room + "/join", new { }, token);
            using var unauthorizedHistory = await outsider.GetAsync(Arena + messages + $"?waitMs={waitMs}", token);
            Assert.Equal(HttpStatusCode.Forbidden, unauthorizedHistory.StatusCode);

            var ownerRead = Get(owner, messages + $"?waitMs={waitMs}", token);
            var guestRead = Get(guest, messages + $"?waitMs={waitMs}", token);
            await Task.Delay(180, token);
            Assert.False(ownerRead.IsCompleted);
            Assert.False(guestRead.IsCompleted);

            var sent = await Post(guest, messages, new { kind = "sticker", stickerId = "c1001_smile_001" }, token);
            long id = sent.GetProperty("id").GetInt64();
            var delivered = await Task.WhenAll(ownerRead, guestRead).WaitAsync(TimeSpan.FromSeconds(2), token);
            Assert.All(delivered, batch =>
            {
                var message = Assert.Single(batch.GetProperty("messages").EnumerateArray());
                Assert.Equal(id, message.GetProperty("id").GetInt64());
                Assert.Equal("c1001_smile_001", message.GetProperty("stickerId").GetString());
            });
            var history = await Get(owner, messages + "?waitMs=8000", token).WaitAsync(TimeSpan.FromSeconds(2), token);
            Assert.Equal(id, Assert.Single(history.GetProperty("messages").EnumerateArray()).GetProperty("id").GetInt64());

            var empty = await Get(owner, messages + $"?after={id}&waitMs=100", token).WaitAsync(TimeSpan.FromSeconds(2), token);
            Assert.Empty(empty.GetProperty("messages").EnumerateArray());

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            var abortable = Get(owner, messages + $"?after={id}&waitMs={waitMs}", cancel.Token);
            await Task.Delay(180, token);
            Assert.False(abortable.IsCompleted);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abortable);
        }
        finally
        {
            using var guestLeave = await guest.PostAsync(Arena + room + "/leave", null, token);
            using var ownerLeave = await owner.PostAsync(Arena + room + "/leave", null, token);
        }
    }

    private static async Task Authorize(HttpClient client, string name, CancellationToken token)
    {
        var session = await Post(client, "/sessions", new { displayName = name, avatarId = "c1001" }, token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
    }

    private static async Task<JsonElement> Post(HttpClient client, string route, object body, CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync(Arena + route, body, token);
        return await Json(response, token);
    }

    private static async Task<JsonElement> Get(HttpClient client, string route, CancellationToken token)
    {
        using var response = await client.GetAsync(Arena + route, token);
        return await Json(response, token);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(token);
    }
}
