using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.Mail;
internal static class MailEndpoints
{
    internal static void AddMail(this IServiceCollection services)
    {
        services.AddSingleton(_ => MailCatalog.Load());
        services.AddSingleton(p => new MailStore(p.GetRequiredService<IConfiguration>().GetConnectionString("MySql"), p.GetRequiredService<MailCatalog>()));
        services.AddSingleton<MailEventHub>();
        services.AddHostedService<MailWorker>();
    }

    internal static void MapMail(this WebApplication app)
    {
        var g = app.MapGroup("/api/mail");
        g.WithRequestTimeout(TimeSpan.FromSeconds(30));
        g.MapGet("/catalog", (HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, _ => Task.FromResult(s.SafeCatalog()), ct));
        g.MapGet("/summary", (HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.ReadSummary(p, ct), ct));
        g.MapGet("/inbox", (HttpContext h, ArenaStore a, MailStore s, string? cursor, int? limit, CancellationToken ct) => Run(h, a, p => s.ReadPage(p, false, cursor, limit ?? 50, ct), ct));
        g.MapGet("/history", (HttpContext h, ArenaStore a, MailStore s, string? cursor, int? limit, CancellationToken ct) => Run(h, a, p => s.ReadPage(p, true, cursor, limit ?? 50, ct), ct));
        g.MapGet("/{id}", (string id, HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.Detail(p, id, ct), ct));
        g.MapPost("/claim", (MailClaimInput input, HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.Claim(p, input, ct), ct)).WithMetadata(new RequestSizeLimitAttribute(2048));
        g.MapPost("/claim-all", (MailClaimAllInput input, HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.ClaimAll(p, input, ct), ct)).WithMetadata(new RequestSizeLimitAttribute(8192));
        app.MapGet("/api/mail/events", MailEventStream.Stream).DisableRequestTimeout();
    }

    internal static void MapDevelopmentMail(this WebApplication app)
    {
        var g = app.MapGroup("/api/development/gm/mail");
        g.WithRequestTimeout(TimeSpan.FromSeconds(30));
        g.MapPost("/preview", (MailDraft input, HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.PreviewSelf(p, input, ct), ct)).WithMetadata(new RequestSizeLimitAttribute(32768));
        g.MapPost("/send", (MailSendInput input, HttpContext h, ArenaStore a, MailStore s, CancellationToken ct) => Run(h, a, p => s.SendSelf(p, input, ct), ct)).WithMetadata(new RequestSizeLimitAttribute(2048));
    }

    private static async Task<IResult> Run<T>(HttpContext h, ArenaStore auth, Func<string, Task<T>> action, CancellationToken ct)
    {
        h.Response.Headers.CacheControl = "no-store";
        try
        {
            var player = await auth.Authenticate(h.Request.Headers.Authorization.ToString(), ct);
            return player == null ? Problem(401, "unauthorized") : Results.Ok(await action(player.Id));
        }
        catch (EntryProblem p)
        {
            return Problem(p.Status, p.Code);
        }
        catch (ArenaProblem p)
        {
            return Problem(p.Status, p.Code);
        }
        catch (MySqlException)
        {
            return Problem(503, "database_unavailable");
        }
    }

    internal static IResult Problem(int status, string code) => Results.Json(new { code, error = code }, statusCode: status);
}
