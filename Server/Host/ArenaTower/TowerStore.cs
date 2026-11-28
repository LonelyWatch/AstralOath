using System.Data;
using System.Text.Json;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using AstralOath.Shared.Battle;
using MySqlConnector;

namespace AstralOath.Server.ArenaTower;

internal sealed record TowerChallenge(string RequestId, string[] HeroInstanceIds);

internal sealed class TowerStore(IConfiguration config, TowerCatalog tower, InventoryStore inventory, ArenaBattleCatalog cache)
{
    private sealed record Attempt(string Id, ulong Seed, string Hash, TowerHero[] Heroes, TowerStage Stage, ulong Revision);
    private const string Fields = "id,seed,rules_hash,allies,defense,revision";
    private const string Owner = "player_id=@player AND realm_id=@realm";

    private async Task<MySqlConnection> Open(string owner, CancellationToken ct)
    {
        var settings = config.GetConnectionString("MySql");
        if (string.IsNullOrWhiteSpace(settings)) throw new EntryProblem(503, "database_unavailable");
        var db = new MySqlConnection(settings);
        try
        {
            await db.OpenAsync(ct);
            if (!await PlayerNicknameRegistry.InRealm(db, owner, ct)) throw new EntryProblem(403, "invalid_realm");
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    private static MySqlCommand Sql(MySqlConnection db, MySqlTransaction? tx, string player, string sql,
        params (string Name, object Value)[] values)
    {
        var command = new MySqlCommand(sql, db, tx);
        command.Parameters.AddWithValue("@player", player);
        command.Parameters.AddWithValue("@realm", EntryEndpoints.RealmId);
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static async Task Lock(MySqlConnection db, MySqlTransaction tx, string owner, CancellationToken ct)
    {
        using var q = Sql(db, tx, owner, "SELECT id FROM arena_player WHERE id=@player FOR UPDATE");
        if (await q.ExecuteScalarAsync(ct) == null) throw new EntryProblem(401, "unauthorized");
    }

    private static async Task<int> Highest(MySqlConnection db, MySqlTransaction? tx, string owner, CancellationToken ct)
    {
        using var q = Sql(db, tx, owner, "SELECT COALESCE(MAX(stage),0) FROM arena_tower_clear WHERE " + Owner);
        return Convert.ToInt32(await q.ExecuteScalarAsync(ct));
    }

    internal async Task<object> List(string owner, CancellationToken ct)
    {
        await using var db = await Open(owner, ct);
        int highest = await Highest(db, null, owner, ct);
        return new { highestCleared = highest, currentStage = Math.Min(tower.Stages.Length, highest + 1), rankId = tower.Rank(highest),
            stages = tower.Stages.Select(s => new { s.Stage, s.HeroIds, s.Level, s.Reward, cleared = s.Stage <= highest, unlocked = s.Stage <= highest + 1 }) };
    }

    internal async Task<object> ClearTeam(string owner, int stage, CancellationToken ct)
    {
        tower.Stage(stage);
        await using var db = await Open(owner, ct);
        using var q = Sql(db, null, owner, """
            SELECT b.allies,b.defense FROM arena_tower_clear c JOIN arena_tower_battle b ON b.id=c.first_battle_id
            WHERE c.player_id=@player AND c.realm_id=@realm AND c.stage=@stage
            """, ("@stage", stage));
        await using var rows = await q.ExecuteReaderAsync(ct);
        if (!await rows.ReadAsync(ct)) throw new EntryProblem(404, "tower_not_cleared");
        var heroes = Decode<TowerHero[]>(rows.GetString(0));
        var defense = Decode<TowerStage>(rows.GetString(1));
        return new { stage, defenderHeroIds = defense.HeroIds,
            heroes = heroes.Select(h => new { h.InstanceId, h.HeroId, h.Level, h.Stars, h.Slot }) };
    }

    internal async Task<BattlePreviewInvitation> Challenge(string owner, int stage, TowerChallenge input, CancellationToken ct)
    {
        var defense = tower.Stage(stage);
        if (!Guid.TryParseExact(input.RequestId, "N", out var key) || key == Guid.Empty ||
            input.HeroInstanceIds is not { Length: >= 1 and <= 4 } || input.HeroInstanceIds.Any(i => !Guid.TryParseExact(i, "N", out _)))
            throw new EntryProblem(400, "invalid_tower_challenge");
        var ids = input.HeroInstanceIds.Select(i => Guid.ParseExact(i, "N").ToString("N")).ToArray();
        await using var db = await Open(owner, ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Lock(db, tx, owner, ct);
        using var lookup = Sql(db, tx, owner, $"SELECT {Fields} FROM arena_tower_battle WHERE {Owner} AND request_id=@req", ("@req", key.ToString("N")));
        var attempt = await ReadAttempt(lookup, ct);
        BattlePreviewRoom room;
        if (attempt != null)
        {
            if (attempt.Stage.Stage != stage || !attempt.Heroes.Select(h => h.InstanceId).SequenceEqual(ids)) throw new EntryProblem(409, "request_conflict");
            room = await Restore(db, tx, owner, attempt, ct);
        }
        else
        {
            if (stage > await Highest(db, tx, owner, ct) + 1) throw new EntryProblem(409, "tower_stage_locked");
            var owned = await inventory.ReadLockedFormation(db, tx, owner, ids, ct);
            var team = owned.Select((h, i) => TowerHero.Freeze(ids[i], i + 1, h)).ToArray();
            attempt = new Attempt("tower-" + Guid.NewGuid().ToString("N"), ArenaBattleCatalog.NewSeed(), tower.RulesHash, team, defense, 0);
            room = tower.CreateRoom(attempt.Id, attempt.Seed, team, defense);
            using var insert = Sql(db, tx, owner, """
                INSERT INTO arena_tower_battle(id,player_id,realm_id,request_id,stage,seed,rules_hash,allies,defense,created_at)
                VALUES(@id,@player,@realm,@req,@stage,@seed,@hash,@team,@defense,UTC_TIMESTAMP(6))
                """, ("@id", attempt.Id), ("@req", key.ToString("N")), ("@stage", stage), ("@seed", attempt.Seed), ("@hash", attempt.Hash),
                ("@team", Encode(team)), ("@defense", Encode(defense)));
            await insert.ExecuteNonQueryAsync(ct);
            await OperationAudit.Record(db, tx, "arena.tower.challenge", "arena_player", owner, "tower_battle", attempt.Id, null, ct);
            await Finish(db, tx, owner, attempt, room.Read(0).Snapshot.Outcome, ct);
        }
        await tx.CommitAsync(ct);
        cache.RememberBattle(attempt.Id, attempt.Revision, room);
        return new BattlePreviewInvitation(attempt.Id, "", "", DateTimeOffset.MaxValue, tower.RulesHash, room.Read(0))
            { BattleContentHash = tower.BattleHash };
    }

    internal async Task<BattlePreviewUpdate> Read(string owner, string id, ulong after, CancellationToken ct)
    {
        await using var db = await Open(owner, ct);
        // Attempt revision and the command cursor must describe the same committed snapshot.
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var attempt = await Owned(db, tx, owner, id, ct);
        var room = cache.FindBattle(id, attempt.Revision);
        if (room == null)
        {
            room = await Restore(db, tx, owner, attempt, ct);
            cache.RememberBattle(id, attempt.Revision, room);
        }
        await tx.CommitAsync(ct);
        return room.Read(after);
    }

    internal async Task<BattlePreviewCommandReply> Submit(string owner, string id, BattlePreviewCommand command, CancellationToken ct)
    {
        if (!command.IsValid() || command.BattleId != id || command.ContentHash != tower.RulesHash)
            throw new EntryProblem(400, "invalid_tower_command");
        await using var db = await Open(owner, ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Lock(db, tx, owner, ct);
        var attempt = await Owned(db, tx, owner, id, ct);
        // Never mutate the shared read cache before our command transaction commits.
        var room = await Restore(db, tx, owner, attempt, ct);
        var result = room.Submit(BattleSide.Ally, command) ?? throw new EntryProblem(403, "tower_wrong_side");
        if (result.Status == BattleCommandStatus.Accepted)
        {
            using var add = Sql(db, tx, owner, "INSERT INTO arena_tower_command(battle_id,sequence,payload) VALUES(@id,@seq,@cmd)",
                ("@id", id), ("@seq", command.Sequence), ("@cmd", Encode(command)));
            await add.ExecuteNonQueryAsync(ct);
            using var update = Sql(db, tx, owner, "UPDATE arena_tower_battle SET revision=revision+1 WHERE id=@id", ("@id", id));
            await update.ExecuteNonQueryAsync(ct);
            await Finish(db, tx, owner, attempt, result.Snapshot.Outcome, ct);
            attempt = attempt with { Revision = attempt.Revision + 1 };
        }
        await tx.CommitAsync(ct);
        cache.RememberBattle(id, attempt.Revision, room);
        return result;
    }

    private static async Task Finish(MySqlConnection db, MySqlTransaction tx, string owner, Attempt a, BattleOutcome outcome, CancellationToken ct)
    {
        if (outcome == BattleOutcome.InProgress) return;
        using var finish = Sql(db, tx, owner, "UPDATE arena_tower_battle SET outcome=@outcome WHERE id=@id", ("@outcome", (int)outcome), ("@id", a.Id));
        await finish.ExecuteNonQueryAsync(ct);
        await OperationAudit.Record(db, tx, "arena.tower.finish", "arena_player", owner, "tower_battle", a.Id, null, ct);
        if (outcome != BattleOutcome.AllyVictory || a.Stage.Stage <= await Highest(db, tx, owner, ct)) return;
        using var clear = Sql(db, tx, owner, """
            INSERT INTO arena_tower_clear(player_id,realm_id,stage,first_battle_id,credited_points,cleared_at)
            VALUES(@player,@realm,@stage,@id,@reward,UTC_TIMESTAMP(6))
            """, ("@stage", a.Stage.Stage), ("@id", a.Id), ("@reward", a.Stage.Reward));
        await clear.ExecuteNonQueryAsync(ct);
        using var reward = Sql(db, tx, owner, """
            INSERT INTO arena_tower_wallet(player_id,realm_id,currency_id,balance) VALUES(@player,@realm,'conquest_point',@amount)
            ON DUPLICATE KEY UPDATE balance=balance+@amount
            """, ("@amount", a.Stage.Reward));
        await reward.ExecuteNonQueryAsync(ct);
        await OperationAudit.Record(db, tx, "arena.tower.first_clear", "arena_player", owner, "tower_battle", a.Id, null, ct);
    }

    private async Task<Attempt> Owned(MySqlConnection db, MySqlTransaction tx, string owner, string id, CancellationToken ct)
    {
        using var q = Sql(db, tx, owner, $"SELECT {Fields} FROM arena_tower_battle WHERE {Owner} AND id=@id", ("@id", id));
        return await ReadAttempt(q, ct) ?? throw new EntryProblem(404, "tower_battle_not_found");
    }

    private async Task<Attempt?> ReadAttempt(MySqlCommand query, CancellationToken ct)
    {
        await using var r = await query.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        string hash = r.GetString(2);
        if (hash != tower.RulesHash) throw new EntryProblem(409, "battle_content_mismatch");
        return new Attempt(r.GetString(0), r.GetUInt64(1), hash, Decode<TowerHero[]>(r.GetString(3)),
            Decode<TowerStage>(r.GetString(4)), r.GetUInt64(5));
    }

    private async Task<BattlePreviewRoom> Restore(MySqlConnection db, MySqlTransaction tx, string owner, Attempt a, CancellationToken ct)
    {
        var room = tower.CreateRoom(a.Id, a.Seed, a.Heroes, a.Stage);
        using var q = Sql(db, tx, owner, "SELECT payload FROM arena_tower_command WHERE battle_id=@id ORDER BY sequence", ("@id", a.Id));
        await using var rows = await q.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
            if (room.Submit(BattleSide.Ally, Decode<BattlePreviewCommand>(rows.GetString(0)))?.Status != BattleCommandStatus.Accepted)
                throw new InvalidDataException("Tower command history diverged from the stored battle.");
        return room;
    }

    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, TowerCatalog.Json);
    private static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, TowerCatalog.Json) ?? throw new InvalidDataException("Invalid tower snapshot.");
}
