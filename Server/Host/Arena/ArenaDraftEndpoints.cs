using AstralOath.Server.Battle;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using static AstralOath.Server.Arena.ArenaEndpoints;

namespace AstralOath.Server.Arena;

internal static class ArenaDraftEndpoints
{
    public static void MapArenaDraft(this RouteGroupBuilder authenticated)
    {
        var room = authenticated.MapGroup("/rooms/{id}");
        room.MapPost("/draft/settings", async (string id, ArenaDraftSettingsRequest request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
        {
            if (request is { PrebanCount: null, FirstPick: null, BanProtection: null } ||
                request.PrebanCount is < 0 or > 3 || request.FirstPick is not (null or "host" or "guest"))
                throw new ArenaProblem(400, "invalid_draft_settings");
            return Results.Ok(await store.UpdateDraftSettings(RoomId(id), Player(http), request, catalog, cancellation));
        }).WithMetadata(new RequestSizeLimitAttribute(512));
        room.MapPost("/draft/start", async (string id, ArenaDraftStartRequest request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
            Results.Ok(await store.StartDraft(RoomId(id), Player(http), request, catalog, cancellation)))
            .WithMetadata(new RequestSizeLimitAttribute(1024));
        room.MapGet("/draft", async (string id, HttpContext http, ArenaStore store, ArenaBattleCatalog catalog,
            CancellationToken cancellation) =>
        {
            var draft = await store.ReadDraft(RoomId(id), Player(http), catalog, cancellation);
            return draft is null ? Results.NoContent() : Results.Ok(draft);
        });
        room.MapPost("/draft/preview", async (string id, ArenaDraftPreviewRequest request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
        {
            if (request.Sequence <= 0 || request.Version <= 0 || request.DraftId is not { Length: 32 } ||
                request.HeroId is { Length: 0 or > 80 })
                throw new ArenaProblem(400, "invalid_draft_action");
            return Results.Ok(await store.UpdateDraftPreview(RoomId(id), Player(http), request, catalog, cancellation));
        }).WithMetadata(new RequestSizeLimitAttribute(512));
        room.MapPost("/draft/actions", async (string id, ArenaDraftActionRequest request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
            Results.Ok(await store.DraftAction(RoomId(id), Player(http), request, catalog, cancellation)))
            .WithMetadata(new RequestSizeLimitAttribute(2048));

        room.MapGet("/battle", async (string id, ulong? after, HttpContext http, ArenaStore store, ArenaBattleCatalog catalog,
            CancellationToken cancellation) =>
            Results.Ok(await store.ReadBattle(RoomId(id), Player(http), after ?? 0, catalog, cancellation)));
        room.MapPost("/battle/commands", async (string id, BattlePreviewCommand request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
        {
            if (!request.IsValid()) throw new ArenaProblem(400, "invalid_battle_command");
            if (request.ContentHash != catalog.ContentHash) throw new ArenaProblem(409, "battle_content_mismatch");
            return Results.Ok(await store.BattleCommand(RoomId(id), Player(http), request, catalog, cancellation));
        }).WithMetadata(new RequestSizeLimitAttribute(4096));
        room.MapPost("/battle/ready", async (string id, ulong? after, ArenaBattleReadyRequest request, HttpContext http,
            ArenaStore store, ArenaBattleCatalog catalog, CancellationToken cancellation) =>
        {
            if (string.IsNullOrWhiteSpace(request.BattleId) || request.BattleId.Length > 80 ||
                string.IsNullOrWhiteSpace(request.ActorId) || request.ActorId.Length > 80)
                throw new ArenaProblem(400, "invalid_battle_ready");
            return Results.Ok(await store.BattleReady(RoomId(id), Player(http), request, after ?? 0, catalog, cancellation));
        }).WithMetadata(new RequestSizeLimitAttribute(1024));
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ArenaDraftSettingsRequest(int? PrebanCount, string? FirstPick, bool? BanProtection);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ArenaDraftPreviewRequest(string? DraftId, int Version, string? HeroId, int Sequence);
