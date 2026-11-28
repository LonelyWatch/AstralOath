using AstralOath.Server.Arena;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using AstralOath.Server.Summon;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
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
            provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
            provider.GetRequiredService<IConfiguration>().GetValue<bool>("Inventory:DevelopmentEquipmentGrant")));
    }

    internal static void MapInventory(this WebApplication app)
    {
        var inventory = app.MapGroup("/api/inventory");
        var shop = app.MapGroup("/api/shop/arena");
        shop.WithRequestTimeout(TimeSpan.FromSeconds(10));
        shop.MapGet("", (HttpContext http, ArenaStore sessions, InventoryStore store, CancellationToken ct) =>
            Handle(app, http, sessions, id => store.ArenaShop(id, null, ct), ct));
        shop.MapPost("/exchanges", (ShopBuy input, HttpContext http, ArenaStore sessions, InventoryStore store, CancellationToken ct) =>
            Handle(app, http, sessions, id => store.ArenaShop(id, input, ct), ct))
            .WithMetadata(new RequestSizeLimitAttribute(2048));
        inventory.WithRequestTimeout(TimeSpan.FromSeconds(10));
        inventory.MapGet("/heroes", (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ReadHeroes(id, cancellation), cancellation));
        inventory.MapGet("/artifacts", (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ReadArtifacts(id, cancellation), cancellation));
        inventory.MapGet("/equipment", (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ReadEquipment(id, cancellation), cancellation));
        inventory.MapGet("/equipment/enhancement-rules", (HttpContext http, ArenaStore sessions,
            CancellationToken cancellation) => Handle(app, http, sessions,
                _ => Task.FromResult(EquipmentEnhancementCatalog.Rules), cancellation));
        inventory.MapPost("/equipment/{instanceId}/enhance", (HttpContext http, string instanceId,
            EquipmentEnhancementRequest request, ArenaStore sessions, InventoryStore store, CancellationToken cancellation) =>
            Handle(app, http, sessions, id => store.EnhanceEquipment(id, instanceId, request, cancellation), cancellation))
            .WithMetadata(new RequestSizeLimitAttribute(4096));
        inventory.MapMethods("/equipment", [HttpMethods.Head], (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) => Handle(app, http, sessions, id => store.ReadEquipmentVersion(id, cancellation),
                cancellation, version => EquipmentVersionResponse(http, version)));
        inventory.MapPost("/equipment/actions", (HttpContext http, EquipmentMutation request, ArenaStore sessions,
            InventoryStore store, CancellationToken cancellation) => Handle(app, http, sessions,
                id => store.ChangeEquipment(id, request, cancellation), cancellation))
            .WithMetadata(new RequestSizeLimitAttribute(4096));
        inventory.MapPost("/artifacts/{instanceId}/{action}", (HttpContext http, string instanceId, string action,
            ArtifactMutation request, ArenaStore sessions, InventoryStore store, CancellationToken cancellation) =>
            Handle(app, http, sessions, id => store.ChangeArtifact(id, instanceId, action, request, cancellation), cancellation))
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    internal static void MapDevelopmentGm(this WebApplication app)
    {
        var gm = app.MapGroup("/api/development/gm");
        gm.WithRequestTimeout(TimeSpan.FromSeconds(10));
        gm.WithMetadata(new RequestSizeLimitAttribute(4096));
        gm.MapGet("", (HttpContext http, ArenaStore auth, SummonStore store, InventoryStore inventory, CancellationToken ct) =>
            Handle(app, http, auth, id => store.DevelopmentGm(id, null, inventory, ct), ct));
        gm.MapPost("/actions", (DevelopmentGmAction action, HttpContext http, ArenaStore auth, SummonStore store,
            InventoryStore inventory, CancellationToken ct) =>
            Handle(app, http, auth, id => store.DevelopmentGm(id, action, inventory, ct), ct));
    }

    private static IResult EquipmentVersionResponse(HttpContext http, long version)
    {
        var tag = new EntityTagHeaderValue(FormattableString.Invariant($"\"{version}\""));
        http.Response.GetTypedHeaders().ETag = tag;
        bool unchanged = http.Request.GetTypedHeaders().IfNoneMatch?.Any(known =>
            known == EntityTagHeaderValue.Any || tag.Compare(known, useStrongComparison: false)) == true;
        return Results.StatusCode(unchanged ? StatusCodes.Status304NotModified : StatusCodes.Status200OK);
    }

    private static async Task<IResult> Handle<T>(WebApplication app, HttpContext http, ArenaStore sessions,
        Func<string, Task<T>> work, CancellationToken cancellation, Func<T, IResult>? respond = null)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var player = await sessions.Authenticate(http.Request.Headers.Authorization.ToString(), cancellation);
            if (player is null) return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
            var result = await work(player.Id);
            return respond == null ? Results.Ok(result) : respond(result);
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
