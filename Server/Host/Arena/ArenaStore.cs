using System.Data;
using System.Security.Cryptography;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal sealed partial class ArenaStore(string? connectionString)
{
    private const int Capacity = 50;
    private const int PinIterations = 100_000;
    private const int LeaseSeconds = 60;
    private const int SweepPageSize = 64;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(connectionString);

    public async Task<ArenaSession> CreateSession(string name, string avatar, CancellationToken token)
    {
        var player = new ArenaPlayer(Guid.NewGuid().ToString("N"), name, avatar);
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        await using var connection = await Open(token);
        using var insert = Command(connection, null, """
            INSERT INTO arena_player (id, token_hash, display_name, avatar_id, created_at)
            VALUES (@id, @hash, @name, @avatar, UTC_TIMESTAMP(6));
            """, ("@id", player.Id), ("@hash", SHA256.HashData(secret)), ("@name", name), ("@avatar", avatar));
        await insert.ExecuteNonQueryAsync(token);
        return new ArenaSession(Convert.ToHexStringLower(secret), player);
    }

    public async Task<ArenaPlayer?> Authenticate(string authorization, CancellationToken token)
    {
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || authorization.Length != prefix.Length + 64)
            return null;
        byte[] secret;
        try { secret = Convert.FromHexString(authorization.AsSpan(prefix.Length)); }
        catch (FormatException) { return null; }
        await using var connection = await Open(token);
        using var command = Command(connection, null, """
            SELECT id, display_name, avatar_id FROM arena_player WHERE token_hash = @hash;
            """, ("@hash", SHA256.HashData(secret)));
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadPlayer(reader, 0) : null;
    }

    public async Task<IReadOnlyList<ArenaRoom>> Directory(string visibility, string sort, string direction, string query,
        CancellationToken token)
    {
        await using var connection = await Open(token);
        await SweepExpiredRooms(connection, token);
        using var command = Command(connection, null, """
            SELECT r.id, r.name, r.password_hash IS NOT NULL, r.allow_chat, r.rule, r.created_at,
                   p.id, p.display_name, p.avatar_id,
                   (SELECT COUNT(*) FROM arena_room_member m
                    LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
                    WHERE m.room_id = r.id AND COALESCE(l.expires_at, TIMESTAMPADD(SECOND, @lease, m.joined_at)) > UTC_TIMESTAMP(6)) AS member_count,
                   COALESCE(s.preban_count, 2), COALESCE(s.first_pick, 'host'), COALESCE(s.ban_protection, 1)
            FROM arena_room r
            LEFT JOIN arena_room_draft_settings s ON s.room_id = r.id
            JOIN arena_player p ON p.id = r.host_id
            WHERE (@visibility = 'all' OR (r.password_hash IS NULL) = (@visibility = 'public'))
              AND (@query = '' OR LOCATE(@query, r.name) > 0)
            HAVING member_count > 0
            ORDER BY CASE WHEN @sort = 'players' AND @direction = 'asc' THEN member_count END ASC,
                     CASE WHEN @sort = 'players' AND @direction = 'desc' THEN member_count END DESC,
                     CASE WHEN @sort = 'recent' AND @direction = 'asc' THEN r.created_at END ASC,
                     r.created_at DESC, r.id ASC
            LIMIT 100;
            """, ("@visibility", visibility), ("@query", query), ("@sort", sort), ("@direction", direction), ("@lease", LeaseSeconds));
        await using var reader = await command.ExecuteReaderAsync(token);
        var rooms = new List<ArenaRoom>();
        while (await reader.ReadAsync(token))
            rooms.Add(new ArenaRoom(reader.GetString(0), reader.GetString(1), ReadPlayer(reader, 6),
                reader.GetBoolean(2), reader.GetBoolean(3), reader.GetString(4), reader.GetInt32(9), Capacity, Utc(reader, 5),
                reader.GetInt32(10), reader.GetString(11), reader.GetBoolean(12)));
        return rooms;
    }

    public async Task<ArenaRoomState> CreateRoom(ArenaPlayer player, string name, string? password, bool allowChat,
        string rule, int prebanCount, string firstPick, bool banProtection, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var salt = password is null ? null : RandomNumberGenerator.GetBytes(16);
        var hash = password is null ? null : HashPin(password, salt!);
        await using var connection = await Open(token);
        await SweepPriorRoom(connection, player.Id, exceptRoomId: null, token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockPlayer(connection, transaction, player.Id, token);
        if (await FindMembership(connection, transaction, player.Id, token) is not null)
            throw new ArenaProblem(409, "already_in_room");
        using var insert = Command(connection, transaction, """
            INSERT INTO arena_room (id, host_id, name, password_salt, password_hash, allow_chat, rule, created_at)
            VALUES (@id, @host, @name, @salt, @hash, @chat, @rule, UTC_TIMESTAMP(6));
            """, ("@id", id), ("@host", player.Id), ("@name", name), ("@salt", salt), ("@hash", hash),
            ("@chat", allowChat), ("@rule", rule));
        await insert.ExecuteNonQueryAsync(token);
        using var settings = Command(connection, transaction, """
            INSERT INTO arena_room_draft_settings (room_id, preban_count, first_pick, ban_protection)
            VALUES (@id, @preban, @first, @protection);
            """, ("@id", id), ("@preban", prebanCount), ("@first", firstPick), ("@protection", banProtection));
        await settings.ExecuteNonQueryAsync(token);
        await InsertMembership(connection, transaction, id, player.Id, token);
        var room = await LockRoom(connection, transaction, id, token);
        var state = State(room, new[] { player });
        await AuditRoom(connection, transaction, "arena.room.create", player, id, token);
        await transaction.CommitAsync(token);
        return state;
    }

    public async Task<ArenaRoomState> JoinRoom(string id, ArenaPlayer player, string? password, CancellationToken token)
    {
        await using var connection = await Open(token);
        await SweepPriorRoom(connection, player.Id, id, token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockActiveRoom(connection, transaction, id, token);
        await LockPlayer(connection, transaction, player.Id, token);
        var membership = await FindMembership(connection, transaction, player.Id, token);
        var members = await Members(connection, transaction, id, token);
        if (membership != id)
        {
            if (membership is not null) throw new ArenaProblem(409, "already_in_room");
            if (room.PasswordHash is not null &&
                (string.IsNullOrEmpty(password) || !CryptographicOperations.FixedTimeEquals(
                    room.PasswordHash, HashPin(password, room.PasswordSalt!))))
                throw new ArenaProblem(403, "incorrect_password");
            if (members.Count >= Capacity) throw new ArenaProblem(409, "room_full");
            await InsertMembership(connection, transaction, id, player.Id, token);
            members.Add(player);
            await AuditRoom(connection, transaction, "arena.room.join", player, id, token);
        }
        else await RenewLease(connection, transaction, player.Id, token);
        var state = State(room, members);
        await transaction.CommitAsync(token);
        return state;
    }

    public async Task<ArenaRoomState?> CurrentRoom(ArenaPlayer player, CancellationToken token)
    {
        string? id;
        await using (var connection = await Open(token))
            id = await FindMembership(connection, null, player.Id, token);
        if (id is null) return null;
        try { return await ReadRoom(id, player, token); }
        catch (ArenaProblem problem) when (problem.Code is "room_not_found" or "not_in_room") { return null; }
    }

    public async Task<ArenaRoomState> ReadRoom(string id, ArenaPlayer player, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockActiveRoom(connection, transaction, id, token);
        var members = await Members(connection, transaction, id, token);
        if (!members.Any(member => member.Id == player.Id))
        {
            await transaction.CommitAsync(token);
            throw new ArenaProblem(403, "not_in_room");
        }
        await RenewLease(connection, transaction, player.Id, token);
        await transaction.CommitAsync(token);
        return State(room, members);
    }

    public async Task LeaveRoom(string id, ArenaPlayer player, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        RoomRecord room;
        try { room = await LockActiveRoom(connection, transaction, id, token); }
        catch (ArenaProblem problem) when (problem.Code == "room_not_found") { return; }
        await LockPlayer(connection, transaction, player.Id, token);
        if (await FindMembership(connection, transaction, player.Id, token) != id)
        {
            await transaction.CommitAsync(token);
            return;
        }
        using var remove = Command(connection, transaction,
            "DELETE FROM arena_room_member WHERE player_id = @player;",
            ("@player", player.Id));
        await remove.ExecuteNonQueryAsync(token);
        await CancelDraftIfSeated(connection, transaction, id, player.Id, token);
        if (room.HostId == player.Id)
        {
            var members = await Members(connection, transaction, id, token);
            await TransferOrRemoveRoom(connection, transaction, id, members.FirstOrDefault()?.Id, token);
        }
        await AuditRoom(connection, transaction, "arena.room.leave", player, id, token);
        await transaction.CommitAsync(token);
    }

    public async Task<IReadOnlyList<ArenaMessage>> ReadMessages(string id, ArenaPlayer player, long after,
        CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockRoom(connection, transaction, id, token, shared: true);
        await RequireActiveMember(connection, transaction, id, player.Id, token);
        // A new reader gets the most recent history; an existing reader pages
        // forward in ascending cursor order even if it missed several polls.
        const string select = """
            SELECT m.id, p.id, p.display_name, p.avatar_id, m.body, m.created_at, s.sticker_id
            FROM arena_room_message m JOIN arena_player p ON p.id = m.player_id
            LEFT JOIN arena_room_message_sticker s ON s.message_id = m.id
            WHERE m.room_id = @room
            """;
        string query = select + (after == 0 ? " ORDER BY m.id DESC LIMIT 50;"
            : " AND m.id > @after ORDER BY m.id ASC LIMIT 100;");
        using var command = Command(connection, transaction, query, ("@room", id), ("@after", after));
        var messages = new List<ArenaMessage>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
                messages.Add(new ArenaMessage(reader.GetInt64(0), ReadPlayer(reader, 1), reader.GetString(4), Utc(reader, 5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
        await transaction.CommitAsync(token);
        if (after == 0) messages.Reverse();
        return messages;
    }

    public async Task<ArenaMessage> SendMessage(string id, ArenaPlayer player, string text, string? stickerId,
        CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockActiveRoom(connection, transaction, id, token);
        await RequireMember(connection, transaction, id, player.Id, token);
        if (!room.AllowChat) throw new ArenaProblem(403, "chat_disabled");
        var now = DateTime.UtcNow;
        using var recent = Command(connection, transaction, """
            SELECT 1 FROM arena_room_message
            WHERE room_id = @room AND player_id = @player AND created_at > @threshold LIMIT 1;
            """, ("@room", id), ("@player", player.Id), ("@threshold", now.AddMilliseconds(-500)));
        if (await recent.ExecuteScalarAsync(token) is not null) throw new ArenaProblem(429, "rate_limited");
        using var insert = Command(connection, transaction, """
            INSERT INTO arena_room_message (room_id, player_id, body, created_at) VALUES (@room, @player, @body, @now);
            """, ("@room", id), ("@player", player.Id), ("@body", text), ("@now", now));
        await insert.ExecuteNonQueryAsync(token);
        long messageId = insert.LastInsertedId;
        if (stickerId is not null)
        {
            using var sticker = Command(connection, transaction, """
                INSERT INTO arena_room_message_sticker (message_id, sticker_id) VALUES (@message, @sticker);
                """, ("@message", messageId), ("@sticker", stickerId));
            await sticker.ExecuteNonQueryAsync(token);
        }
        await RenewLease(connection, transaction, player.Id, token);
        await AuditRoom(connection, transaction, "arena.chat.send", player, id, token);
        await transaction.CommitAsync(token);
        return new ArenaMessage(messageId, player, text, now, stickerId);
    }

    public async Task RecordTelemetry(ArenaPlayer player, string eventId, string operation, CancellationToken token)
    {
        await using var connection = await Open(token);
        await OperationAudit.Record(connection, null, operation, "arena_player", player.Id, null, null, eventId, token);
    }

    private static Task AuditRoom(MySqlConnection connection, MySqlTransaction transaction, string operation,
        ArenaPlayer player, string roomId, CancellationToken token) =>
        OperationAudit.Record(connection, transaction, operation, "arena_player", player.Id, "arena_room", roomId, null, token);

    public async Task SweepExpiredRooms(CancellationToken token)
    {
        await using var connection = await Open(token);
        await SweepExpiredRooms(connection, token);
    }

    private static async Task SweepExpiredRooms(MySqlConnection connection, CancellationToken token)
    {
        // Read candidate IDs without row locks. Each candidate is rechecked under its room lock.
        using var command = Command(connection, null, """
            SELECT m.room_id
            FROM arena_room_member_lease l JOIN arena_room_member m ON m.player_id = l.player_id
            WHERE l.expires_at <= UTC_TIMESTAMP(6)
            UNION DISTINCT
            SELECT m.room_id
            FROM arena_room_member m LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
            WHERE l.player_id IS NULL AND m.joined_at <= TIMESTAMPADD(SECOND, -@lease, UTC_TIMESTAMP(6))
            ORDER BY room_id LIMIT @limit;
            """, ("@limit", SweepPageSize), ("@lease", LeaseSeconds));
        var expired = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) expired.Add(reader.GetString(0));
        foreach (string id in expired) await SweepRoom(connection, id, token);
    }

    private static async Task SweepPriorRoom(MySqlConnection connection, string playerId, string? exceptRoomId,
        CancellationToken token)
    {
        using var command = Command(connection, null, """
            SELECT m.room_id FROM arena_room_member m
            LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
            WHERE m.player_id = @player
              AND COALESCE(l.expires_at, TIMESTAMPADD(SECOND, @lease, m.joined_at)) <= UTC_TIMESTAMP(6);
            """, ("@player", playerId), ("@lease", LeaseSeconds));
        if (await command.ExecuteScalarAsync(token) is string id && id != exceptRoomId)
            await SweepRoom(connection, id, token);
    }

    private static async Task SweepRoom(MySqlConnection connection, string id, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        RoomRecord room;
        try { room = await LockRoom(connection, transaction, id, token); }
        catch (ArenaProblem problem) when (problem.Code == "room_not_found") { return; }
        await RemoveExpiredMembers(connection, transaction, room, token);
        await transaction.CommitAsync(token);
    }

    private static async Task<RoomRecord> LockActiveRoom(MySqlConnection connection, MySqlTransaction transaction,
        string id, CancellationToken token)
    {
        var room = await LockRoom(connection, transaction, id, token);
        var active = await RemoveExpiredMembers(connection, transaction, room, token);
        if (active is not null) return active;
        await transaction.CommitAsync(token);
        throw new ArenaProblem(404, "room_not_found");
    }

    private static async Task<RoomRecord?> RemoveExpiredMembers(MySqlConnection connection, MySqlTransaction transaction,
        RoomRecord room, CancellationToken token)
    {
        using var lookup = Command(connection, transaction, """
            SELECT m.player_id FROM arena_room_member m
            LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
            WHERE m.room_id = @room
              AND COALESCE(l.expires_at, TIMESTAMPADD(SECOND, @lease, m.joined_at)) <= UTC_TIMESTAMP(6)
            ORDER BY m.joined_at, m.player_id;
            """, ("@room", room.Id), ("@lease", LeaseSeconds));
        var expired = new List<string>();
        await using (var reader = await lookup.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) expired.Add(reader.GetString(0));
        if (expired.Count == 0) return room;
        foreach (string playerId in expired)
        {
            using var remove = Command(connection, transaction,
                "DELETE FROM arena_room_member WHERE player_id = @player AND room_id = @room;",
                ("@player", playerId), ("@room", room.Id));
            await remove.ExecuteNonQueryAsync(token);
            await CancelDraftIfSeated(connection, transaction, room.Id, playerId, token);
            await OperationAudit.Record(connection, transaction, "arena.room.expire", "arena_player", playerId,
                "arena_room", room.Id, null, token);
        }
        if (!expired.Contains(room.HostId)) return room;
        using var findHost = Command(connection, transaction, """
            SELECT player_id FROM arena_room_member WHERE room_id = @room ORDER BY joined_at, player_id LIMIT 1;
            """, ("@room", room.Id));
        string? nextHost = (string?)await findHost.ExecuteScalarAsync(token);
        await TransferOrRemoveRoom(connection, transaction, room.Id, nextHost, token);
        return nextHost is null ? null : room with { HostId = nextHost };
    }

    private static async Task TransferOrRemoveRoom(MySqlConnection connection, MySqlTransaction transaction, string id,
        string? nextHost, CancellationToken token)
    {
        using var command = nextHost is null
            ? Command(connection, transaction, "DELETE FROM arena_room WHERE id = @id;", ("@id", id))
            : Command(connection, transaction, "UPDATE arena_room SET host_id = @host WHERE id = @id;",
                ("@host", nextHost), ("@id", id));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task RenewLease(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        CancellationToken token)
    {
        using var renew = Command(connection, transaction, """
            INSERT INTO arena_room_member_lease (player_id, expires_at)
            VALUES (@player, TIMESTAMPADD(SECOND, @lease, UTC_TIMESTAMP(6)))
            ON DUPLICATE KEY UPDATE expires_at = IF(expires_at <= TIMESTAMPADD(SECOND, @threshold, UTC_TIMESTAMP(6)),
                TIMESTAMPADD(SECOND, @lease, UTC_TIMESTAMP(6)), expires_at);
            """, ("@player", playerId), ("@lease", LeaseSeconds), ("@threshold", LeaseSeconds - 10));
        await renew.ExecuteNonQueryAsync(token);
    }

    private async Task<MySqlConnection> Open(CancellationToken token)
    {
        if (!IsConfigured) throw new ArenaProblem(503, "database_unavailable");
        var connection = new MySqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task LockPlayer(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        CancellationToken token)
    {
        using var command = Command(connection, transaction,
            "SELECT id FROM arena_player WHERE id = @player FOR UPDATE;", ("@player", playerId));
        if (await command.ExecuteScalarAsync(token) is null) throw new ArenaProblem(401, "unauthorized");
    }

    private static async Task<RoomRecord> LockRoom(MySqlConnection connection, MySqlTransaction transaction,
        string id, CancellationToken token, bool shared = false)
    {
        const string query = """
            SELECT r.id, r.host_id, r.name, r.password_salt, r.password_hash, r.allow_chat, r.rule, r.created_at,
                   COALESCE(s.preban_count, 2), COALESCE(s.first_pick, 'host'), COALESCE(s.ban_protection, 1)
            FROM arena_room r LEFT JOIN arena_room_draft_settings s ON s.room_id = r.id WHERE r.id = @id
            """;
        using var command = Command(connection, transaction, query + (shared ? " FOR SHARE;" : " FOR UPDATE;"), ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new ArenaProblem(404, "room_not_found");
        return new RoomRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4),
            reader.GetBoolean(5), reader.GetString(6), Utc(reader, 7), reader.GetInt32(8), reader.GetString(9), reader.GetBoolean(10));
    }

    private static async Task<List<ArenaPlayer>> Members(MySqlConnection connection, MySqlTransaction transaction,
        string id, CancellationToken token)
    {
        using var command = Command(connection, transaction, """
            SELECT p.id, p.display_name, p.avatar_id
            FROM arena_room_member m JOIN arena_player p ON p.id = m.player_id
            WHERE m.room_id = @room ORDER BY m.joined_at, m.player_id;
            """, ("@room", id));
        await using var reader = await command.ExecuteReaderAsync(token);
        var players = new List<ArenaPlayer>();
        while (await reader.ReadAsync(token)) players.Add(ReadPlayer(reader, 0));
        return players;
    }

    private static async Task<string?> FindMembership(MySqlConnection connection, MySqlTransaction? transaction,
        string playerId, CancellationToken token)
    {
        using var command = Command(connection, transaction,
            "SELECT room_id FROM arena_room_member WHERE player_id = @player;", ("@player", playerId));
        return (string?)await command.ExecuteScalarAsync(token);
    }

    private static async Task RequireActiveMember(MySqlConnection connection, MySqlTransaction transaction,
        string id, string playerId, CancellationToken token)
    {
        using var command = Command(connection, transaction, """
            SELECT 1 FROM arena_room_member m
            LEFT JOIN arena_room_member_lease l ON l.player_id = m.player_id
            WHERE m.player_id = @player AND m.room_id = @room
              AND COALESCE(l.expires_at, TIMESTAMPADD(SECOND, @lease, m.joined_at)) > UTC_TIMESTAMP(6);
            """, ("@player", playerId), ("@room", id), ("@lease", LeaseSeconds));
        if (await command.ExecuteScalarAsync(token) is null) throw new ArenaProblem(403, "not_in_room");
    }

    private static async Task RequireMember(MySqlConnection connection, MySqlTransaction transaction,
        string id, string playerId, CancellationToken token)
    {
        if (await FindMembership(connection, transaction, playerId, token) == id) return;
        await transaction.CommitAsync(token);
        throw new ArenaProblem(403, "not_in_room");
    }

    private static async Task InsertMembership(MySqlConnection connection, MySqlTransaction transaction,
        string id, string playerId, CancellationToken token)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO arena_room_member (player_id, room_id, joined_at) VALUES (@player, @room, UTC_TIMESTAMP(6));
            """, ("@player", playerId), ("@room", id));
        await command.ExecuteNonQueryAsync(token);
        await RenewLease(connection, transaction, playerId, token);
    }

    private static ArenaRoomState State(RoomRecord room, IReadOnlyList<ArenaPlayer> members) =>
        new(new ArenaRoom(room.Id, room.Name, members.Single(player => player.Id == room.HostId),
            room.PasswordHash is not null, room.AllowChat, room.Rule, members.Count, Capacity, room.CreatedAt,
            room.PrebanCount, room.FirstPick, room.BanProtection), members);

    private static byte[] HashPin(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, PinIterations, HashAlgorithmName.SHA256, 32);

    private static ArenaPlayer ReadPlayer(MySqlDataReader reader, int index) =>
        new(reader.GetString(index), reader.GetString(index + 1), reader.GetString(index + 2));

    private static DateTime Utc(MySqlDataReader reader, int index) =>
        DateTime.SpecifyKind(reader.GetDateTime(index), DateTimeKind.Utc);

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private sealed record RoomRecord(string Id, string HostId, string Name, byte[]? PasswordSalt,
        byte[]? PasswordHash, bool AllowChat, string Rule, DateTime CreatedAt, int PrebanCount, string FirstPick, bool BanProtection);
}
