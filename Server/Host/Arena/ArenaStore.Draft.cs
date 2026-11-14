using System.Data;
using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Server.Foundation;
using AstralOath.Shared.Battle;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal sealed partial class ArenaStore
{
    private const int MinimumHeroesForDraft = 10;

    public async Task<ArenaDraftView> StartDraft(string id, ArenaPlayer player, ArenaDraftStartRequest request,
        ArenaBattleCatalog catalog, CancellationToken token)
    {
        string key = ArenaDraft.NormalizeRequestId(request.RequestId);
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockActiveRoom(connection, transaction, id, token);
        await RequireMember(connection, transaction, id, player.Id, token);
        if (room.HostId != player.Id) throw new ArenaProblem(403, "draft_host_only");
        var current = await LoadDraft(connection, transaction, id, token);
        if (current?.StartRequestId == key && current.Host.Player.Id == player.Id)
        {
            await transaction.CommitAsync(token);
            return current.View(player, catalog);
        }
        if (room.Rule != "basic") throw new ArenaProblem(409, "draft_rule_unsupported");
        if (current is not null && current.Phase != "cancelled")
        {
            if (current.Phase != "battle" ||
                (await LoadBattle(connection, transaction, current, catalog, token)).Read(0).Snapshot.Outcome == BattleOutcome.InProgress)
                throw new ArenaProblem(409, "draft_already_started");
        }
        var members = await Members(connection, transaction, id, token);
        var opponent = members.FirstOrDefault(member => member.Id != player.Id);
        if (opponent is null) throw new ArenaProblem(409, "draft_needs_two_players");
        if (catalog.EligibleHeroIds.Length < MinimumHeroesForDraft + 2 * room.PrebanCount)
            throw new ArenaProblem(409, "draft_catalog_insufficient");
        var draft = new ArenaDraft
        {
            Id = Guid.NewGuid().ToString("N"), RoomId = id, StartRequestId = key,
            PrebanCount = room.PrebanCount, FirstPick = room.FirstPick, BanProtection = room.BanProtection,
            ContentHash = catalog.ContentHash, BattleRulesVersion = BattlePreviewFactory.RulesVersion,
            Seed = ArenaBattleCatalog.NewSeed(),
            Phase = room.PrebanCount == 0 ? "pick" : "preban",
            Host = new ArenaDraftSeat { Player = player }, Guest = new ArenaDraftSeat { Player = opponent }
        };
        draft.ResetDeadline(DateTimeOffset.UtcNow);
        if (current != null)
        {
            using var remove = Command(connection, transaction, "DELETE FROM arena_room_draft WHERE room_id = @room;", ("@room", id));
            await remove.ExecuteNonQueryAsync(token);
        }
        using var insert = Command(connection, transaction, """
            INSERT INTO arena_room_draft (room_id, draft_id, state)
            VALUES (@room, @draft, @state);
            """, ("@room", id), ("@draft", draft.Id), ("@state", JsonSerializer.Serialize(draft)));
        await insert.ExecuteNonQueryAsync(token);
        await RenewLease(connection, transaction, player.Id, token);
        await AuditRoom(connection, transaction, "arena.draft.start", player, id, token);
        await transaction.CommitAsync(token);
        return draft.View(player, catalog);
    }

    public async Task<ArenaDraftView?> ReadDraft(string id, ArenaPlayer player, ArenaBattleCatalog catalog, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockActiveRoom(connection, transaction, id, token);
        await RequireMember(connection, transaction, id, player.Id, token);
        var draft = await LoadDraft(connection, transaction, id, token);
        if (draft is not null)
        {
            _ = draft.SideOf(player);
            await AdvanceExpiredDraft(connection, transaction, draft, catalog, token);
        }
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        return draft?.View(player, catalog);
    }

    public async Task<ArenaDraftView> DraftAction(string id, ArenaPlayer player, ArenaDraftActionRequest request,
        ArenaBattleCatalog catalog, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockActiveRoom(connection, transaction, id, token);
        await RequireMember(connection, transaction, id, player.Id, token);
        var draft = await LoadDraft(connection, transaction, id, token) ?? throw new ArenaProblem(404, "draft_not_found");
        _ = draft.SideOf(player);
        await AdvanceExpiredDraft(connection, transaction, draft, catalog, token);
        var previousPhase = draft.Phase;
        bool changed;
        try { changed = draft.Apply(player, request, catalog, DateTimeOffset.UtcNow); }
        catch (ArenaProblem)
        {
            // Preexisting timeout transitions are authoritative even when late input fails.
            await transaction.CommitAsync(token);
            throw;
        }
        if (changed)
        {
            if (previousPhase != "battle" && draft.Phase == "battle")
                SynchronizeClock(draft, catalog.Start(draft.Id, draft.Seed, draft.Host.Formation, draft.Guest.Formation).Read(0).Snapshot,
                    catalog, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await SaveDraft(connection, transaction, draft, token);
            await AuditRoom(connection, transaction, "arena.draft." + request.Kind, player, id, token);
            if (previousPhase != "battle" && draft.Phase == "battle")
                await AuditRoom(connection, transaction, "arena.battle.start", player, id, token);
        }
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        return draft.View(player, catalog);
    }

    public async Task<BattlePreviewUpdate> ReadBattle(string id, ArenaPlayer player, ulong after, ArenaBattleCatalog catalog,
        CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var draft = await RequireBattle(connection, transaction, id, player, token);
        using var latest = Command(connection, transaction, """
            SELECT COALESCE(MAX(sequence), 0) FROM arena_battle_command WHERE draft_id = @draft;
            """, ("@draft", draft.Id));
        ulong revision = Convert.ToUInt64(await latest.ExecuteScalarAsync(token));
        var room = catalog.FindBattle(draft.Id, revision);
        if (room is null)
        {
            room = await LoadBattle(connection, transaction, draft, catalog, token);
            catalog.RememberBattle(draft.Id, revision, room);
        }
        room = await AdvanceBattleClock(connection, transaction, draft, room, catalog, token);
        var update = room.Read(after) with { Clock = draft.BattleClock?.View(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) };
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        return update;
    }

    public async Task<BattlePreviewCommandReply> BattleCommand(string id, ArenaPlayer player, BattlePreviewCommand request,
        ArenaBattleCatalog catalog, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var draft = await RequireBattle(connection, transaction, id, player, token);
        if (request.BattleId != draft.Id) throw new ArenaProblem(409, "battle_changed");
        var room = await LoadBattle(connection, transaction, draft, catalog, token);
        BattleSide side = draft.SideOf(player) == "host" ? BattleSide.Ally : BattleSide.Enemy;
        if (room.Read(0).Snapshot.GetUnit(request.ActorId)?.Slot.Side != side)
            throw new ArenaProblem(403, "battle_wrong_seat");
        // A valid late command cannot race ahead of the default action at its deadline.
        room = await AdvanceBattleClock(connection, transaction, draft, room, catalog, token);
        if (request.Sequence == room.Read(0).Snapshot.LastCommandSequence + 1 && draft.BattleClock != null &&
            !draft.BattleClock.DeadlineUnixMilliseconds.HasValue)
        {
            await transaction.CommitAsync(token);
            throw new ArenaProblem(409, "battle_presentation_pending");
        }
        var reply = room.Submit(side, request) ?? throw new ArenaProblem(403, "battle_wrong_seat");
        if (reply.Status == BattleCommandStatus.Accepted)
        {
            await StoreBattleCommand(connection, transaction, draft.Id, side, request, token);
            SynchronizeClock(draft, reply.Snapshot, catalog, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await SaveDraft(connection, transaction, draft, token);
            if (reply.Snapshot.Outcome != BattleOutcome.InProgress)
                await AuditRoom(connection, transaction, "arena.battle.finish", player, id, token);
        }
        else if (reply.Status != BattleCommandStatus.Duplicate)
            reply = reply with { Events = room.Read(0).Events };
        reply = reply with { Clock = draft.BattleClock?.View(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) };
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        if (reply.Status == BattleCommandStatus.Accepted)
            catalog.RememberBattle(draft.Id, request.Sequence, room);
        return reply;
    }

    private static async Task<ArenaDraft> RequireBattle(MySqlConnection connection, MySqlTransaction transaction, string id,
        ArenaPlayer player, CancellationToken token)
    {
        await LockActiveRoom(connection, transaction, id, token);
        await RequireMember(connection, transaction, id, player.Id, token);
        var draft = await LoadDraft(connection, transaction, id, token) ?? throw new ArenaProblem(404, "draft_not_found");
        _ = draft.SideOf(player);
        return draft.Phase == "battle" ? draft : throw new ArenaProblem(409, "battle_not_started");
    }

    private static async Task<BattlePreviewRoom> LoadBattle(MySqlConnection connection, MySqlTransaction transaction, ArenaDraft draft,
        ArenaBattleCatalog catalog, CancellationToken token)
    {
        if (draft.ContentHash != catalog.ContentHash || draft.BattleRulesVersion != BattlePreviewFactory.RulesVersion)
            throw new ArenaProblem(409, "battle_content_mismatch");
        var room = catalog.Start(draft.Id, draft.Seed, draft.Host.Formation, draft.Guest.Formation);
        using var command = Command(connection, transaction, """
            SELECT side, payload FROM arena_battle_command WHERE draft_id = @draft ORDER BY sequence;
            """, ("@draft", draft.Id));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            using var payload = JsonDocument.Parse(reader.GetString(1));
            bool serverTimeout = payload.RootElement.TryGetProperty("serverTimeout", out var origin) && origin.ValueKind == JsonValueKind.True;
            var saved = payload.RootElement.Deserialize<BattlePreviewCommand>()
                ?? throw new InvalidOperationException("Stored battle command is missing.");
            var result = serverTimeout ? room.SubmitTimeout((BattleSide)reader.GetByte(0), saved) : room.Submit((BattleSide)reader.GetByte(0), saved);
            if (result?.Status != BattleCommandStatus.Accepted)
                throw new InvalidOperationException("An accepted Arena battle command could not be replayed.");
        }
        return room;
    }

    private static async Task<ArenaDraft?> LoadDraft(MySqlConnection connection, MySqlTransaction transaction, string id,
        CancellationToken token)
    {
        using var lookup = Command(connection, transaction, "SELECT state FROM arena_room_draft WHERE room_id = @room;", ("@room", id));
        return await lookup.ExecuteScalarAsync(token) is string stored ? JsonSerializer.Deserialize<ArenaDraft>(stored) : null;
    }

    private static async Task SaveDraft(MySqlConnection connection, MySqlTransaction transaction, ArenaDraft draft, CancellationToken token)
    {
        using var update = Command(connection, transaction, "UPDATE arena_room_draft SET state = @state WHERE room_id = @room;",
            ("@room", draft.RoomId), ("@state", JsonSerializer.Serialize(draft)));
        await update.ExecuteNonQueryAsync(token);
    }

    private static async Task AdvanceExpiredDraft(MySqlConnection connection, MySqlTransaction transaction, ArenaDraft draft,
        ArenaBattleCatalog catalog, CancellationToken token)
    {
        if (draft.AdvanceExpired(catalog, DateTimeOffset.UtcNow) == 0) return;
        if (draft.Phase == "battle")
            SynchronizeClock(draft, catalog.Start(draft.Id, draft.Seed, draft.Host.Formation, draft.Guest.Formation).Read(0).Snapshot,
                catalog, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await SaveDraft(connection, transaction, draft, token);
        await OperationAudit.Record(connection, transaction, draft.Phase == "battle" ? "arena.battle.start_timeout" : "arena.draft.timeout",
            "server", "arena", "arena_room", draft.RoomId, null, token);
    }

    private static async Task CancelDraftIfSeated(MySqlConnection connection, MySqlTransaction transaction, string id,
        string playerId, CancellationToken token)
    {
        var draft = await LoadDraft(connection, transaction, id, token);
        if (draft == null || playerId != draft.Host.Player.Id && playerId != draft.Guest.Player.Id || !draft.Cancel())
            return;
        await SaveDraft(connection, transaction, draft, token);
        await OperationAudit.Record(connection, transaction, "arena.draft.cancel", "arena_player", playerId,
            "arena_room", id, null, token);
    }
}
