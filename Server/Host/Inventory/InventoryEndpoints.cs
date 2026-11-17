using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal static class InventoryEndpoints
{
    internal static void AddInventory(this IServiceCollection services) =>
        services.AddSingleton(provider => new InventoryStore(provider.GetRequiredService<IConfiguration>()
            .GetConnectionString("MySql")));

    internal static void MapInventory(this WebApplication app)
    {
        var inventory = app.MapGroup("/api/inventory");
        inventory.WithRequestTimeout(TimeSpan.FromSeconds(10));
        inventory.MapGet("/heroes", async (HttpContext http, ArenaStore sessions, InventoryStore store,
            CancellationToken cancellation) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var player = await sessions.Authenticate(http.Request.Headers.Authorization.ToString(), cancellation);
                if (player is null) return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
                return Results.Ok(await store.ReadHeroes(player.Id, cancellation));
            }
            catch (EntryProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (ArenaProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (MySqlException error)
            {
                app.Logger.LogWarning("Inventory database operation failed with MySQL error {ErrorNumber}.", error.Number);
                return Problem(StatusCodes.Status503ServiceUnavailable, "database_unavailable");
            }
        });
    }

    private static IResult Problem(int status, string code) => Results.Json(new { code }, statusCode: status);
}
