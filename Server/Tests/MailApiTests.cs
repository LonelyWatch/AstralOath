using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class MailApiTests
{
    [Theory]
    [InlineData("catalog")]
    [InlineData("summary")]
    [InlineData("inbox")]
    [InlineData("history")]
    [InlineData("events")]
    public async Task AnonymousCannotReadMailbox(string path)
    {
        await using var app = new Factory();
        using var http = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/mail/" + path)).StatusCode);
    }

    [Fact]
    public async Task AnonymousCannotSendOrClaim()
    {
        await using var app = new Factory();
        using var http = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/development/gm/mail/preview", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/mail/claim", new { requestId = Guid.NewGuid().ToString("N"), deliveryId = Guid.NewGuid().ToString("N") })).StatusCode);
    }

    internal sealed class Factory(string? database = null, string environment = "Development", string bind = "127.0.0.1") : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> { ["environment"] = environment, ["Server:BindAddress"] = bind, ["Server:CertificatePath"] = "testserver-only.pfx" }));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MySql"] = database, ["Arena:AllowGuestSessions"] = "true" }));
            return base.CreateHost(builder);
        }
    }
}
