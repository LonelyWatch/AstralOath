using AstralOath.Server.Arena;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal static class InventoryEndpoints
{
    internal static void AddInventory(this IServiceCollection services)
    {
        services.AddSingleton(_ => ArtifactCatalogReader.Load());
        services.AddSingleton(provider => new InventoryStore(provider.GetRequiredService<IConfiguration>()
            .GetConnectionString("MySql"), provider.GetRequiredService<AstralOath.Shared.Battle.ArtifactCatalog>(),
            provider.GetRequiredService<ArenaBattleCatalog>(),
            provider.GetRequiredService<IHostEnvironment>().IsDevelopment()));
    }

    internal static void MapInventory(this WebApplication app)
    {
        var inventory = app.MapGroup("/api/inventory");
        inventory.WithRequestTimeout(TimeSpan.FromSeconds(10));
        inventory.MapGet("/heroes", (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ReadHeroes(id, cancellation), cancellation));
        inventory.MapGet("/artifacts", (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ReadArtifacts(id, cancellation), cancellation));
        inventory.MapPost("/artifacts/{instanceId}/{action}", (HttpContext http, string instanceId, string action,
            ArtifactMutation request, ArenaStore sessions, InventoryStore store, CancellationToken cancellation) =>
            Handle(app, http, sessions, id => store.ChangeArtifact(id, instanceId, action, request, cancellation), cancellation))
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    private static async Task<IResult> Handle<T>(WebApplication app, HttpContext http, ArenaStore sessions,
        Func<string, Task<T>> work, CancellationToken cancellation)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var player = await sessions.Authenticate(http.Request.Headers.Authorization.ToString(), cancellation);
            if (player is null) return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
            return Results.Ok(await work(player.Id));
        }
        catch (EntryProblem problem) { return Problem(problem.Status, problem.Code); }
        catch (ArenaProblem problem) { return Problem(problem.Status, problem.Code); }
        catch (MySqlException error)
        {
            app.Logger.LogWarning("Inventory database operation failed with MySQL error {ErrorNumber}.", error.Number);
            return Problem(StatusCodes.Status503ServiceUnavailable, "database_unavailable");
        }
    }

    private static IResult Problem(int status, string code) => Results.Json(new { code }, statusCode: status);
}
