using System.Net;
using AstralOath.Server.Arena;
using AstralOath.Server.Battle;
using AstralOath.Server.Foundation;
using AstralOath.Server.Entry;
using AstralOath.Server.Inventory;
using AstralOath.Server.Summon;
using AstralOath.Server.Mail;
using AstralOath.Server.Social;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Host.UseWindowsService(options => options.ServiceName = "AstralOath");
var connectionString = builder.Configuration.GetConnectionString("MySql");

if (args is ["migrate"])
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ConnectionStrings:MySql is required to migrate.");

    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    Console.CancelKeyPress += (_, signal) =>
    {
        signal.Cancel = true;
        deadline.Cancel();
    };
    await DatabaseMigrator.ApplyAsync(connectionString, deadline.Token);
    Console.WriteLine("MySQL migrations applied.");
    return;
}

if (args.FirstOrDefault() == "mail")
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    Console.CancelKeyPress += (_, signal) => { signal.Cancel = true; deadline.Cancel(); };
    Environment.ExitCode = await MailCli.Run(args, connectionString, deadline.Token);
    return;
}

var settings = builder.Configuration.GetSection("Server");
var isDevelopment = builder.Environment.IsDevelopment();
var bindAddress = IPAddress.Parse(settings["BindAddress"] ?? "127.0.0.1");
var port = settings.GetValue("Port", 7728);
var certificatePath = settings["CertificatePath"];
var useHttps = !isDevelopment || settings.GetValue<bool>("UseHttps") || !string.IsNullOrWhiteSpace(certificatePath);
if (!isDevelopment && string.IsNullOrWhiteSpace(certificatePath))
    throw new InvalidOperationException("Server:CertificatePath is required outside Development.");
if (!useHttps && !IPAddress.IsLoopback(bindAddress))
    throw new InvalidOperationException("Cleartext development endpoints require a loopback address.");
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // The fixed listener below owns the address and TLS settings. Ignore
    // Kestrel's default endpoint loader, including config changes after startup.
    kestrel.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
    kestrel.Listen(bindAddress, port, listener =>
    {
        if (!string.IsNullOrWhiteSpace(certificatePath))
            listener.UseHttps(certificatePath, settings["CertificatePassword"]);
        else if (useHttps)
            listener.UseHttps();
    });
});
builder.Services.AddSingleton(services => new EchoStore(services.GetRequiredService<IConfiguration>().GetConnectionString("MySql")));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
    "foundation_database", failureStatus: HealthStatus.Unhealthy, tags: ["ready"], timeout: TimeSpan.FromSeconds(3));
builder.Services.AddRequestTimeouts();
builder.Services.AddArena();
builder.Services.AddEntry();
builder.Services.AddInventory();
builder.Services.AddSummon();
builder.Services.AddMail();
builder.Services.AddSocial();

var app = builder.Build();
if (app.Configuration.GetValue<bool>(WebHostDefaults.PreferHostingUrlsKey)
    || app.Configuration.GetSection("Kestrel:Endpoints").Exists())
    throw new InvalidOperationException("Configure the listener under Server. preferHostingUrls and Kestrel:Endpoints are unsupported.");
app.UseRequestTimeouts();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapFoundationEcho(isDevelopment);
app.MapArena();
app.MapEntry();
app.MapInventory();
app.MapSummon();
app.MapMail();
app.MapSocial();
if (isDevelopment && IPAddress.IsLoopback(bindAddress))
{
    app.MapDevelopmentGm();
    app.MapDevelopmentMail();
}
if (app.Configuration.GetValue<bool>("BattlePreview:Enabled"))
{
    if (!isDevelopment || !IPAddress.IsLoopback(bindAddress))
        throw new InvalidOperationException("The battle preview room is available only in loopback Development.");
    app.MapBattlePreview();
}
app.Run();

public partial class Program { }
