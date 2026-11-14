using AstralOath.Server.Battle;
using Microsoft.AspNetCore.Mvc;
using static AstralOath.Server.Arena.ArenaEndpoints;

namespace AstralOath.Server.Arena;

internal static class ArenaDraftEndpoints
{
    public static void MapArenaDraft(this RouteGroupBuilder authenticated)
    {
        var room = authenticated.MapGroup("/rooms/{id}");
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
    }
}
