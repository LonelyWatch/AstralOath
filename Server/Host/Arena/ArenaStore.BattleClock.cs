using System.Data;
using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Server.Foundation;
using AstralOath.Shared.Battle;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal sealed record ArenaBattleReadyRequest(string BattleId, ulong CommandSequence, ulong EventSequence, string ActorId);

internal sealed partial class ArenaStore
{
    public async Task<BattlePreviewUpdate> BattleReady(string id, ArenaPlayer player, ArenaBattleReadyRequest request,
        ulong after, ArenaBattleCatalog catalog, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var draft = await RequireBattle(connection, transaction, id, player, token);
        if (request.BattleId != draft.Id) throw new ArenaProblem(409, "battle_changed");
        var room = await LoadBattle(connection, transaction, draft, catalog, token);
        room = await AdvanceBattleClock(connection, transaction, draft, room, catalog, token);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (draft.BattleClock?.Ready(draft.SideOf(player) == "host", request.CommandSequence,
                request.EventSequence, request.ActorId, now) == true)
            await SaveDraft(connection, transaction, draft, token);
        var update = room.Read(after) with { Clock = draft.BattleClock?.View(now) };
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        return update;
    }

    private static bool SynchronizeClock(ArenaDraft draft, BattleSnapshot snapshot, ArenaBattleCatalog catalog, long now)
    {
        if (snapshot.Outcome != BattleOutcome.InProgress)
        {
            bool removed = draft.BattleClock != null;
            draft.BattleClock = null;
            return removed;
        }
        var previous = draft.BattleClock;
        if (previous?.Matches(snapshot.LastCommandSequence, snapshot.LastEventSequence, snapshot.CurrentActorId) == true)
            return false;
        draft.BattleClock = new ArenaBattleTurnClock(snapshot.LastCommandSequence, snapshot.LastEventSequence,
            snapshot.CurrentActorId, now, previous?.DecisionSeconds ?? catalog.TurnSeconds,
            previous?.PresentationWaitSeconds ?? catalog.PresentationWaitSeconds);
        return true;
    }

    private static async Task<BattlePreviewRoom> AdvanceBattleClock(MySqlConnection connection, MySqlTransaction transaction,
        ArenaDraft draft, BattlePreviewRoom room, ArenaBattleCatalog catalog, CancellationToken token)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool changed = SynchronizeClock(draft, room.Read(0).Snapshot, catalog, now);
        if (draft.BattleClock != null)
        {
            changed |= draft.BattleClock.Advance(now);
            if (draft.BattleClock.IsExpired(now))
            {
                // Never mutate a cached committed room before the SQL transaction commits.
                room = await LoadBattle(connection, transaction, draft, catalog, token);
                var snapshot = room.Read(0).Snapshot;
                var choice = catalog.ChooseTimeoutAction(snapshot)
                    ?? throw new InvalidOperationException("Expired battle turn has no legal default action.");
                ulong sequence = snapshot.LastCommandSequence + 1;
                var command = new BattlePreviewCommand(snapshot.RulesVersion, sequence, "timeout:" + sequence,
                    snapshot.CurrentActorId, choice.SkillId, choice.TargetId, choice.UseSoulBurn, catalog.ContentHash)
                    { BattleId = snapshot.BattleId };
                var side = snapshot.GetUnit(snapshot.CurrentActorId).Slot.Side;
                var reply = room.SubmitTimeout(side, command);
                if (reply?.Status != BattleCommandStatus.Accepted)
                    throw new InvalidOperationException("Authoritative timeout action was rejected.");
                await StoreBattleCommand(connection, transaction, draft.Id, side, command, token, serverTimeout: true);
                await OperationAudit.Record(connection, transaction, "arena.battle.turn_timeout", "server", "arena",
                    "arena_room", draft.RoomId, null, token);
                if (reply.Snapshot.Outcome != BattleOutcome.InProgress)
                    await OperationAudit.Record(connection, transaction, "arena.battle.finish", "server", "arena",
                        "arena_room", draft.RoomId, null, token);
                // One expired action per transaction; the next presentation window starts now.
                SynchronizeClock(draft, reply.Snapshot, catalog, now);
                changed = true;
            }
        }
        if (changed) await SaveDraft(connection, transaction, draft, token);
        return room;
    }

    private static async Task StoreBattleCommand(MySqlConnection connection, MySqlTransaction transaction,
        string draftId, BattleSide side, BattlePreviewCommand request, CancellationToken token, bool serverTimeout = false)
    {
        using var insert = Command(connection, transaction, """
            INSERT INTO arena_battle_command (draft_id, sequence, side, payload)
            VALUES (@draft, @sequence, @side, @payload);
            """, ("@draft", draftId), ("@sequence", request.Sequence), ("@side", (int)side),
            ("@payload", SerializeBattleCommand(request, serverTimeout)));
        await insert.ExecuteNonQueryAsync(token);
    }

    private static string SerializeBattleCommand(BattlePreviewCommand request, bool serverTimeout)
    {
        if (!serverTimeout) return JsonSerializer.Serialize(request);
        var payload = JsonSerializer.SerializeToNode(request)!.AsObject();
        payload["serverTimeout"] = true;
        return payload.ToJsonString();
    }
}
