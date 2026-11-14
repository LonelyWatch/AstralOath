using System.Data;
using System.Security.Cryptography;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal sealed class ArenaStore(string? connectionString)
{
    private const int Capacity = 50;
    private const int PinIterations = 100_000;

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
        using var command = Command(connection, null, """
            SELECT r.id, r.name, r.password_hash IS NOT NULL, r.allow_chat, r.rule, r.created_at,
                   p.id, p.display_name, p.avatar_id,
                   (SELECT COUNT(*) FROM arena_room_member m WHERE m.room_id = r.id) AS member_count
            FROM arena_room r
            JOIN arena_player p ON p.id = r.host_id
            WHERE (@visibility = 'all' OR (r.password_hash IS NULL) = (@visibility = 'public'))
              AND (@query = '' OR LOCATE(@query, r.name) > 0)
            ORDER BY CASE WHEN @sort = 'players' AND @direction = 'asc' THEN member_count END ASC,
                     CASE WHEN @sort = 'players' AND @direction = 'desc' THEN member_count END DESC,
                     CASE WHEN @sort = 'recent' AND @direction = 'asc' THEN r.created_at END ASC,
                     r.created_at DESC, r.id ASC
            LIMIT 100;
            """, ("@visibility", visibility), ("@query", query), ("@sort", sort), ("@direction", direction));
        await using var reader = await command.ExecuteReaderAsync(token);
        var rooms = new List<ArenaRoom>();
        while (await reader.ReadAsync(token))
            rooms.Add(new ArenaRoom(reader.GetString(0), reader.GetString(1), ReadPlayer(reader, 6),
                reader.GetBoolean(2), reader.GetBoolean(3), reader.GetString(4), reader.GetInt32(9), Capacity, Utc(reader, 5)));
        return rooms;
    }

    public async Task<ArenaRoomState> CreateRoom(ArenaPlayer player, string name, string? password, bool allowChat,
        string rule, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var salt = password is null ? null : RandomNumberGenerator.GetBytes(16);
        var hash = password is null ? null : HashPin(password, salt!);
        await using var connection = await Open(token);
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
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockRoom(connection, transaction, id, token);
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
        var state = State(room, members);
        await transaction.CommitAsync(token);
        return state;
    }

    public async Task<ArenaRoomState?> CurrentRoom(ArenaPlayer player, CancellationToken token)
    {
        await using var connection = await Open(token);
        string? id = await FindMembership(connection, null, player.Id, token);
        if (id is null) return null;
        try { return await ReadRoom(id, player, token); }
        catch (ArenaProblem problem) when (problem.Code is "room_not_found" or "not_in_room") { return null; }
    }

    public async Task<ArenaRoomState> ReadRoom(string id, ArenaPlayer player, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockRoom(connection, transaction, id, token, shared: true);
        var members = await Members(connection, transaction, id, token);
        if (!members.Any(member => member.Id == player.Id)) throw new ArenaProblem(403, "not_in_room");
        await transaction.CommitAsync(token);
        return State(room, members);
    }

    public async Task LeaveRoom(string id, ArenaPlayer player, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        RoomRecord room;
        try { room = await LockRoom(connection, transaction, id, token); }
        catch (ArenaProblem problem) when (problem.Code == "room_not_found") { return; }
        await LockPlayer(connection, transaction, player.Id, token);
        if (await FindMembership(connection, transaction, player.Id, token) != id) return;
        using var remove = Command(connection, transaction,
            "DELETE FROM arena_room_member WHERE player_id = @player;",
            ("@player", player.Id));
        await remove.ExecuteNonQueryAsync(token);
        if (room.HostId == player.Id)
        {
            var members = await Members(connection, transaction, id, token);
            using var next = members.Count == 0
                ? Command(connection, transaction, "DELETE FROM arena_room WHERE id = @id;", ("@id", id))
                : Command(connection, transaction, "UPDATE arena_room SET host_id = @host WHERE id = @id;",
                    ("@host", members[0].Id), ("@id", id));
            await next.ExecuteNonQueryAsync(token);
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
        await RequireMember(connection, transaction, id, player.Id, token);
        // A new reader gets the most recent history; an existing reader pages
        // forward in ascending cursor order even if it missed several polls.
        string query = after == 0 ? """
            SELECT m.id, p.id, p.display_name, p.avatar_id, m.body, m.created_at
            FROM arena_room_message m JOIN arena_player p ON p.id = m.player_id
            WHERE m.room_id = @room ORDER BY m.id DESC LIMIT 50;
            """ : """
            SELECT m.id, p.id, p.display_name, p.avatar_id, m.body, m.created_at
            FROM arena_room_message m JOIN arena_player p ON p.id = m.player_id
            WHERE m.room_id = @room AND m.id > @after ORDER BY m.id ASC LIMIT 100;
            """;
        using var command = Command(connection, transaction, query, ("@room", id), ("@after", after));
        var messages = new List<ArenaMessage>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
                messages.Add(new ArenaMessage(reader.GetInt64(0), ReadPlayer(reader, 1), reader.GetString(4), Utc(reader, 5)));
        await transaction.CommitAsync(token);
        if (after == 0) messages.Reverse();
        return messages;
    }

    public async Task<ArenaMessage> SendMessage(string id, ArenaPlayer player, string text, CancellationToken token)
    {
        await using var connection = await Open(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var room = await LockRoom(connection, transaction, id, token);
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
        await AuditRoom(connection, transaction, "arena.chat.send", player, id, token);
        await transaction.CommitAsync(token);
        return new ArenaMessage(messageId, player, text, now);
    }

    public async Task RecordTelemetry(ArenaPlayer player, string eventId, string operation, CancellationToken token)
    {
        await using var connection = await Open(token);
        await OperationAudit.Record(connection, null, operation, "arena_player", player.Id, null, null, eventId, token);
    }

    private static Task AuditRoom(MySqlConnection connection, MySqlTransaction transaction, string operation,
        ArenaPlayer player, string roomId, CancellationToken token) =>
        OperationAudit.Record(connection, transaction, operation, "arena_player", player.Id, "arena_room", roomId, null, token);

    private async Task<MySqlConnection> Open(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArenaProblem(503, "database_unavailable");
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
            SELECT id, host_id, name, password_salt, password_hash, allow_chat, rule, created_at
            FROM arena_room WHERE id = @id
            """;
        using var command = Command(connection, transaction, query + (shared ? " FOR SHARE;" : " FOR UPDATE;"), ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new ArenaProblem(404, "room_not_found");
        return new RoomRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<byte[]>(4),
            reader.GetBoolean(5), reader.GetString(6), Utc(reader, 7));
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

    private static async Task RequireMember(MySqlConnection connection, MySqlTransaction transaction,
        string id, string playerId, CancellationToken token)
    {
        if (await FindMembership(connection, transaction, playerId, token) != id)
            throw new ArenaProblem(403, "not_in_room");
    }

    private static async Task InsertMembership(MySqlConnection connection, MySqlTransaction transaction,
        string id, string playerId, CancellationToken token)
    {
        using var command = Command(connection, transaction, """
            INSERT INTO arena_room_member (player_id, room_id, joined_at) VALUES (@player, @room, UTC_TIMESTAMP(6));
            """, ("@player", playerId), ("@room", id));
        await command.ExecuteNonQueryAsync(token);
    }

    private static ArenaRoomState State(RoomRecord room, IReadOnlyList<ArenaPlayer> members) =>
        new(new ArenaRoom(room.Id, room.Name, members.Single(player => player.Id == room.HostId),
            room.PasswordHash is not null, room.AllowChat, room.Rule, members.Count, Capacity, room.CreatedAt), members);

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
        byte[]? PasswordHash, bool AllowChat, string Rule, DateTime CreatedAt);
}
