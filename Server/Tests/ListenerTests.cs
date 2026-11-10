using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class ListenerTests
{
    [Fact]
    public async Task KestrelRejectsHostingUrlsPreferenceFromLateConfiguration()
    {
        await using var source = new WebApplicationFactory<Program>();
        await using var factory = source.WithWebHostBuilder(host => host.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["preferHostingUrls"] = "true",
                ["urls"] = "http://127.0.0.1:0"
            })));
        factory.UseKestrel(0);
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("preferHostingUrls", failure.Message, StringComparison.Ordinal);
    }
}
