using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using AstralOath.Shared.Battle;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.Summon;

internal static class SummonEndpoints
{
    internal static void AddSummon(this IServiceCollection services)
    {
        services.AddSingleton(provider => SummonCatalog.Load(provider.GetRequiredService<ArtifactCatalog>()));
        services.AddSingleton(provider => new SummonStore(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("MySql"),
            provider.GetRequiredService<SummonCatalog>(),
            provider.GetRequiredService<IHostEnvironment>().IsDevelopment(), TimeProvider.System,
            provider.GetRequiredService<AstralOath.Server.Social.ChannelChatEvents>()));
    }

    internal static void MapSummon(this WebApplication app)
    {
        var group = app.MapGroup("/api/summon");
        group.WithRequestTimeout(TimeSpan.FromSeconds(10));
        group.WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
        group.MapGet("", (HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.Read(player, ct), ct));
        group.MapPut("/wishlist", (SummonWishlist request, HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.SaveWishlist(player, request, ct), ct));
        group.MapPost("/draw", (SummonRequest request, HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.Draw(player, request, ct), ct));
        group.MapGet("/draw/latest", (HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.Recover(player, null, ct), ct));
        group.MapGet("/draw/{requestId}", (string requestId, HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.Recover(player, requestId, ct), ct));
        group.MapGet("/storage", (HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.ReadStorage(player, ct), ct));
        group.MapPost("/storage/claim", (SummonStorageClaim request, HttpContext http, ArenaStore auth, SummonStore store, CancellationToken ct) =>
            Run(http, auth, player => store.ClaimStorage(player, request, ct), ct));

        async Task<IResult> Run<T>(HttpContext http, ArenaStore auth, Func<string, Task<T>> operation, CancellationToken ct)
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var player = await auth.Authenticate(http.Request.Headers.Authorization.ToString(), ct);
                return player == null ? Problem(401, "unauthorized") : Results.Ok(await operation(player.Id));
            }
            catch (EntryProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (ArenaProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (MySqlException error)
            {
                app.Logger.LogWarning("Summon database operation failed with MySQL error {ErrorNumber}.", error.Number);
                return Problem(503, "database_unavailable");
            }
        }

        static IResult Problem(int status, string code) => Results.Json(new { code }, statusCode: status);
    }
}
