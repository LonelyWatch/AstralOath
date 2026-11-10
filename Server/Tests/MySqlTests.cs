using System.Net;
using AstralOath.Protocol;
using AstralOath.Server.Foundation;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class MySqlTests
{
    [MySqlFact]
    public async Task ParallelRetriesSurviveRestartAndConflictingPayloadIsRejected()
    {
        var connection = Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await DatabaseMigrator.ApplyAsync(connection, deadline.Token);
        using var baseFactory = new WebApplicationFactory<Program>();
        using var first = WithDatabase(baseFactory, connection);
        using var client = first.CreateClient();
        var request = new EchoRequest { RequestId = Guid.NewGuid().ToString("N"), ProtocolVersion = 1, Message = "数据库" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReadEchoAsync(client, request)));
        var expected = responses[0];
        Assert.True(expected.Sequence > 0);
        Assert.Equal(request.RequestId, expected.RequestId);
        Assert.Equal(1u, expected.ProtocolVersion);
        Assert.Equal("数据库", expected.Message);
        Assert.All(responses, response => Assert.Equal(expected, response));

        first.Dispose();
        using var restarted = WithDatabase(baseFactory, connection);
        using var restartedClient = restarted.CreateClient();
        Assert.Equal(expected, await ReadEchoAsync(restartedClient, request));
        request.Message = "冲突";
        using var conflict = await EchoTests.PostAsync(restartedClient, request);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        request.Message = "数据库";
        Assert.Equal(expected, await ReadEchoAsync(restartedClient, request));
    }

    private static WebApplicationFactory<Program> WithDatabase(WebApplicationFactory<Program> factory, string connection)
        => factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = connection })));

    private static async Task<EchoResponse> ReadEchoAsync(HttpClient client, EchoRequest request)
    {
        using var response = await EchoTests.PostAsync(client, request);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        return EchoResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
    }
}

internal sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASTRALOATH_MYSQL_TEST")))
            Skip = "Set ASTRALOATH_MYSQL_TEST to a disposable MySQL 8.4 database.";
    }
}
