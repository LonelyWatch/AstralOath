using AstralOath.Server.Arena;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.ArenaTower;

internal static class TowerEndpoints
{
    internal static void MapArenaTower(this WebApplication app)
    {
        var group = app.MapGroup("/api/arena/tower");
        group.WithRequestTimeout(TimeSpan.FromSeconds(15)).WithMetadata(new RequestSizeLimitAttribute(4096));
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var auth = http.RequestServices.GetRequiredService<ArenaStore>();
                var player = await auth.Authenticate(http.Request.Headers.Authorization.ToString(), http.RequestAborted);
                if (player == null) return Error(401, "unauthorized");
                http.Items[typeof(ArenaPlayer)] = player;
                return await next(ctx);
            }
            catch (EntryProblem p) { return Error(p.Status, p.Code); }
            catch (ArenaProblem p) { return Error(p.Status, p.Code); }
            catch (MySqlException e)
            {
                app.Logger.LogWarning("Tower database operation failed with MySQL error {Number}.", e.Number);
                return Error(503, "database_unavailable");
            }
        });
        group.MapGet("", async (HttpContext http, TowerStore store, CancellationToken ct) =>
            Results.Ok(await store.List(Owner(http), ct)));
        group.MapGet("/stages/{stage:int}/clear-team", async (int stage, HttpContext http, TowerStore store, CancellationToken ct) =>
            Results.Ok(await store.ClearTeam(Owner(http), stage, ct)));
        group.MapPost("/stages/{stage:int}/challenge", async (int stage, TowerChallenge input, HttpContext http, TowerStore store, CancellationToken ct) =>
            Results.Ok(await store.Challenge(Owner(http), stage, input, ct)));
        group.MapGet("/battles/{id}", async (string id, ulong? after, HttpContext http, TowerStore store, CancellationToken ct) =>
            Results.Ok(await store.Read(Owner(http), id, after ?? 0, ct)));
        group.MapPost("/battles/{id}/commands", async (string id, BattlePreviewCommand command, HttpContext http, TowerStore store, CancellationToken ct) =>
            Results.Ok(await store.Submit(Owner(http), id, command, ct)));
    }

    private static string Owner(HttpContext http) => ((ArenaPlayer)http.Items[typeof(ArenaPlayer)]!).Id;
    private static IResult Error(int status, string code) => Results.Json(new { code }, statusCode: status);
}
