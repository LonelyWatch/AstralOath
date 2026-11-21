using System.Security.Cryptography;
using System.Text;
using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using AstralOath.Server.Inventory;
using AstralOath.Shared.Battle;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace AstralOath.Server.Battle;

internal static class OwnedBattleTrialEndpoints
{
    internal static void MapOwnedBattleTrial(this RouteGroupBuilder routes, BattlePreviewRooms rooms,
        BattleCatalogData catalog, ILogger logger)
    {
        using var artifactData = typeof(OwnedBattleTrialEndpoints).Assembly.GetManifestResourceStream("AstralOath.ArtifactCatalog")
            ?? throw new InvalidDataException("The artifact catalog resource is missing.");
        using var equipmentData = typeof(OwnedBattleTrialEndpoints).Assembly.GetManifestResourceStream("AstralOath.TrainingEquipment")
            ?? throw new InvalidDataException("The equipment catalog resource is missing.");
        string artifactHash = Convert.ToHexString(SHA256.HashData(artifactData));
        string equipmentHash = Convert.ToHexString(SHA256.HashData(equipmentData));
        string matchHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
            rooms.ContentHash + artifactHash + equipmentHash + EquipmentStatCalculator.RulesVersion)));

        routes.MapPost("/owned-trial", async (HttpContext http, OwnedBattleTrial request, ArenaStore sessions,
            InventoryStore inventory, CancellationToken cancellation) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var player = await sessions.Authenticate(http.Request.Headers.Authorization.ToString(), cancellation);
                if (player is null) return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
                var formation = await inventory.ReadTrialFormation(player.Id, request.HeroInstanceIds ?? [], cancellation);
                return rooms.TryCreate((id, seed) => BattlePreviewFactory.CreateOwned(id, seed, catalog, formation),
                    automaticEnemy: true, out var invitation, matchHash)
                    ? Results.Ok(invitation)
                    : Problem(StatusCodes.Status429TooManyRequests, "trial_rooms_full");
            }
            catch (EntryProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (ArenaProblem problem) { return Problem(problem.Status, problem.Code); }
            catch (MySqlException error)
            {
                logger.LogWarning("Owned battle trial database operation failed with MySQL error {ErrorNumber}.", error.Number);
                return Problem(StatusCodes.Status503ServiceUnavailable, "database_unavailable");
            }
        }).WithRequestTimeout(TimeSpan.FromSeconds(10))
            .WithMetadata(new RequestSizeLimitAttribute(1024));
    }

    private static IResult Problem(int status, string code) => Results.Json(new { code }, statusCode: status);

    private sealed record OwnedBattleTrial(string[]? HeroInstanceIds);
}
