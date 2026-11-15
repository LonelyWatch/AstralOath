using System.Net.Mail;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.Entry;

internal static class EntryEndpoints
{
    internal const string RealmId = "undying-memory";

    public static IServiceCollection AddEntry(this IServiceCollection services)
    {
        services.AddSingleton(provider => new EntryStore(provider.GetRequiredService<IConfiguration>()
            .GetConnectionString("MySql")));
        services.AddSingleton<RegistrationMail>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("entry-email", context => PerAddress(context, 6));
            options.AddPolicy("entry-credentials", context => PerAddress(context, 12));
            options.AddPolicy("entry-realm", context => PerAddress(context, 30));
        });
        return services;
    }

    public static void MapEntry(this WebApplication app)
    {
        var minimumVersion = app.Configuration["Entry:MinimumAppVersion"]?.Trim() ?? "";
        Version? minimum = NormalizeVersion(minimumVersion);
        if (minimumVersion.Length != 0 && minimum is null)
            throw new InvalidOperationException("Entry:MinimumAppVersion requires a numeric version such as 1.0.0.");

        var entry = app.MapGroup("/api/entry");
        entry.WithRequestTimeout(TimeSpan.FromSeconds(18));
        entry.WithMetadata(new RequestSizeLimitAttribute(4096));
        entry.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (EntryProblem problem)
            {
                return Results.Json(new { code = problem.Code }, statusCode: problem.Status);
            }
            catch (MySqlException error)
            {
                app.Logger.LogWarning("Entry database operation failed with MySQL error {ErrorNumber}.", error.Number);
                return Results.Json(new { code = "database_unavailable" }, statusCode: 503);
            }
            catch (SmtpException error)
            {
                app.Logger.LogWarning("Registration SMTP failed with status {StatusCode}.", (int)error.StatusCode);
                return Results.Json(new { code = "smtp_unavailable" }, statusCode: 503);
            }
        });

        entry.MapGet("/version", () => Results.Ok(new { minimumAppVersion = minimumVersion, RealmId }));

        entry.MapPost("/email-code", async (EntryEmailRequest request, EntryStore store, RegistrationMail mail,
            CancellationToken cancellation) =>
        {
            var email = Email(request.Email) ?? throw new EntryProblem(400, "invalid_email");
            // An existing verified address must not receive repeated registration messages.
            if (await store.IsRegistered(email, cancellation)) return Results.NoContent();
            mail.EnsureConfigured();
            var challenge = await store.ReserveCode(email, cancellation);
            await mail.Send(email, challenge.Code, cancellation);
            await store.EnableCode(email, challenge.Digest, cancellation);
            return Results.NoContent();
        }).RequireRateLimiting("entry-email");

        entry.MapPost("/register", async (EntryRegisterRequest request, EntryStore store, CancellationToken cancellation) =>
        {
            string name = request.Username?.Trim() ?? "";
            string? email = Email(request.Email);
            if (!ValidName(name) || email is null || !ValidPassword(request.Password))
                throw new EntryProblem(400, "invalid_registration");
            if (request.Code is not { Length: 6 } code || code.AsSpan().ContainsAnyExceptInRange('0', '9'))
                throw new EntryProblem(400, "invalid_code");
            return Results.Ok(await store.Register(name, email, request.Password!, code, cancellation));
        }).RequireRateLimiting("entry-credentials");

        entry.MapPost("/login", async (EntryLoginRequest request, EntryStore store, CancellationToken cancellation) =>
        {
            string name = request.Account?.Trim() ?? "";
            bool byEmail = name.Contains('@');
            string? account = byEmail ? Email(name) : ValidName(name) ? name.ToLowerInvariant() : null;
            if (account is null || !ValidPassword(request.Password)) throw new EntryProblem(401, "invalid_credentials");
            return Results.Ok(await store.Login(account, byEmail, request.Password!, cancellation));
        }).RequireRateLimiting("entry-credentials");

        entry.MapPost("/select-realm", async (EntryRealmRequest request, HttpContext context, EntryStore store,
            CancellationToken cancellation) =>
        {
            if (!string.Equals(request.RealmId, RealmId, StringComparison.Ordinal))
                throw new EntryProblem(400, "invalid_realm");
            if (minimum is not null && (NormalizeVersion(request.AppVersion) is not { } actual || actual < minimum))
                throw new EntryProblem(StatusCodes.Status426UpgradeRequired, "client_update_required");
            return Results.Ok(await store.SelectRealm(context.Request.Headers.Authorization.ToString(), cancellation));
        }).RequireRateLimiting("entry-realm");
    }

    private static Version? NormalizeVersion(string? input) => Version.TryParse(input, out var number)
        ? new Version(number.Major, number.Minor, Math.Max(0, number.Build), Math.Max(0, number.Revision))
        : null;

    private static RateLimitPartition<string> PerAddress(HttpContext context, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });

    private static bool ValidName(string name) =>
        name.Length is >= 3 and <= 24 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool ValidPassword(string? password) =>
        password is { Length: >= 12 and <= 128 } && !string.IsNullOrWhiteSpace(password);

    internal static string? Email(string? input)
    {
        string value = input?.Trim() ?? "";
        if (value.Length is < 3 or > 254 || value.Any(c => c is <= ' ' or >= '\u007f') ||
            !MailAddress.TryCreate(value, out var address) || address.User.Length > 64 ||
            !string.Equals(address.Address, value, StringComparison.Ordinal) ||
            address.DisplayName.Length != 0 || !address.Host.Contains('.') ||
            Uri.CheckHostName(address.Host) != UriHostNameType.Dns)
            return null;
        return value.ToLowerInvariant();
    }
}

internal sealed class EntryProblem(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal sealed record EntryEmailRequest(string? Email);
internal sealed record EntryRegisterRequest(string? Username, string? Email, string? Password, string? Code);
internal sealed record EntryLoginRequest(string? Account, string? Password);
internal sealed record EntryRealmRequest(string? RealmId, string? AppVersion);
internal sealed record EntrySession(string Token);
