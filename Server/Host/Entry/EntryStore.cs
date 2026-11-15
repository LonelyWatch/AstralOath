using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AstralOath.Server.Arena;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Entry;

internal sealed class EntryStore(string? connectionString)
{
    private const int PasswordIterations = 600_000;
    private const int CodeAttempts = 5;
    private readonly byte[] challengeKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] UnknownPasswordSalt = RandomNumberGenerator.GetBytes(16);

    public async Task<bool> IsRegistered(string email, CancellationToken cancellation)
    {
        await using var connection = await Open(cancellation);
        using var command = Command(connection, null, "SELECT 1 FROM entry_account WHERE email = @email;",
            ("@email", email));
        return await command.ExecuteScalarAsync(cancellation) is not null;
    }

    public async Task<(string Code, byte[] Digest)> ReserveCode(string email, CancellationToken cancellation)
    {
        string code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(16);
        var digest = CodeDigest(email, code, salt);
        await using var connection = await Open(cancellation);
        using var cleanup = Command(connection, null, """
            DELETE FROM entry_email_challenge WHERE expires_at <= UTC_TIMESTAMP(6) LIMIT 64;
            """);
        await cleanup.ExecuteNonQueryAsync(cancellation);

        // Conditional upsert avoids gaps between checking and reserving the email cooldown across requests.
        using var reserve = Command(connection, null, """
            INSERT INTO entry_email_challenge (email, salt, digest, ready, attempts_left, resend_at, expires_at)
            VALUES (@email, @salt, @digest, FALSE, @attempts, UTC_TIMESTAMP(6) + INTERVAL 60 SECOND,
                UTC_TIMESTAMP(6) + INTERVAL 10 MINUTE) AS incoming
            ON DUPLICATE KEY UPDATE
                salt = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), incoming.salt, entry_email_challenge.salt),
                digest = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), incoming.digest, entry_email_challenge.digest),
                ready = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), FALSE, entry_email_challenge.ready),
                attempts_left = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), incoming.attempts_left, entry_email_challenge.attempts_left),
                expires_at = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), incoming.expires_at, entry_email_challenge.expires_at),
                resend_at = IF(entry_email_challenge.resend_at <= UTC_TIMESTAMP(6), incoming.resend_at, entry_email_challenge.resend_at);
            """, ("@email", email), ("@salt", salt), ("@digest", digest), ("@attempts", CodeAttempts));
        await reserve.ExecuteNonQueryAsync(cancellation);

        using var owned = Command(connection, null, """
            SELECT 1 FROM entry_email_challenge WHERE email = @email AND digest = @digest;
            """, ("@email", email), ("@digest", digest));
        if (await owned.ExecuteScalarAsync(cancellation) is null)
            throw new EntryProblem(429, "verification_cooldown");
        return (code, digest);
    }

    public async Task EnableCode(string email, byte[] digest, CancellationToken cancellation)
    {
        await using var connection = await Open(cancellation);
        using var command = Command(connection, null, """
            UPDATE entry_email_challenge SET ready = TRUE
            WHERE email = @email AND digest = @digest AND expires_at > UTC_TIMESTAMP(6);
            """, ("@email", email), ("@digest", digest));
        if (await command.ExecuteNonQueryAsync(cancellation) != 1)
            throw new EntryProblem(409, "invalid_code");
    }

    public async Task<EntrySession> Register(string username, string email, string password, string code,
        CancellationToken cancellation)
    {
        await using var connection = await Open(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        byte[] challengeSalt;
        byte[] expected;
        using (var challenge = Command(connection, transaction, """
            SELECT salt, digest FROM entry_email_challenge
            WHERE email = @email AND ready = TRUE AND attempts_left > 0 AND expires_at > UTC_TIMESTAMP(6)
            FOR UPDATE;
            """, ("@email", email)))
        {
            await using var reader = await challenge.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation)) throw new EntryProblem(400, "invalid_code");
            challengeSalt = reader.GetFieldValue<byte[]>(0);
            expected = reader.GetFieldValue<byte[]>(1);
        }
        if (!CryptographicOperations.FixedTimeEquals(expected, CodeDigest(email, code, challengeSalt)))
        {
            using var attempt = Command(connection, transaction, """
                UPDATE entry_email_challenge SET attempts_left = attempts_left - 1 WHERE email = @email;
                """, ("@email", email));
            await attempt.ExecuteNonQueryAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            throw new EntryProblem(400, "invalid_code");
        }

        string id = Guid.NewGuid().ToString("N");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = PasswordHash(password, salt, PasswordIterations);
        var (token, tokenHash) = NewToken();
        try
        {
            // The Arena row supplies the same identity even when the user signs in again.
            using var player = Command(connection, transaction, """
                INSERT INTO arena_player (id, token_hash, display_name, avatar_id, created_at)
                VALUES (@id, @unissued, @name, 'c1001', UTC_TIMESTAMP(6));
                """, ("@id", id), ("@unissued", RandomNumberGenerator.GetBytes(32)), ("@name", username));
            await player.ExecuteNonQueryAsync(cancellation);
            using var account = Command(connection, transaction, """
                INSERT INTO entry_account (player_id, user_key, email, password_salt, password_hash,
                    password_iterations, entry_hash, entry_expires_at, created_at)
                VALUES (@id, @name, @email, @salt, @hash, @iterations, @entry,
                    UTC_TIMESTAMP(6) + INTERVAL 15 MINUTE, UTC_TIMESTAMP(6));
                """, ("@id", id), ("@name", username.ToLowerInvariant()), ("@email", email),
                ("@salt", salt), ("@hash", hash), ("@iterations", PasswordIterations), ("@entry", tokenHash));
            await account.ExecuteNonQueryAsync(cancellation);
        }
        catch (MySqlException error) when (error.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new EntryProblem(409, "account_unavailable");
        }

        using var consume = Command(connection, transaction, "DELETE FROM entry_email_challenge WHERE email = @email;",
            ("@email", email));
        await consume.ExecuteNonQueryAsync(cancellation);
        await OperationAudit.Record(connection, transaction, "entry.account.register", "account", id,
            null, null, null, cancellation);
        await transaction.CommitAsync(cancellation);
        return new EntrySession(token);
    }

    public async Task<EntrySession> Login(string account, bool byEmail, string password, CancellationToken cancellation)
    {
        PasswordRecord? record;
        await using (var connection = await Open(cancellation))
        {
            string sql = byEmail
                ? "SELECT player_id, password_salt, password_hash, password_iterations FROM entry_account WHERE email = @account;"
                : "SELECT player_id, password_salt, password_hash, password_iterations FROM entry_account WHERE user_key = @account;";
            using var query = Command(connection, null, sql, ("@account", account));
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            record = await reader.ReadAsync(cancellation)
                ? new PasswordRecord(reader.GetString(0), reader.GetFieldValue<byte[]>(1),
                    reader.GetFieldValue<byte[]>(2), reader.GetInt32(3))
                : null;
        }

        var actual = PasswordHash(password, record?.Salt ?? UnknownPasswordSalt, record?.Iterations ?? PasswordIterations);
        if (record is null || !CryptographicOperations.FixedTimeEquals(record.Hash, actual))
            throw new EntryProblem(401, "invalid_credentials");

        var (token, tokenHash) = NewToken();
        await using var updateConnection = await Open(cancellation);
        await using var transaction = await updateConnection.BeginTransactionAsync(cancellation);
        using var update = Command(updateConnection, transaction, """
            UPDATE entry_account SET entry_hash = @hash,
                entry_expires_at = UTC_TIMESTAMP(6) + INTERVAL 15 MINUTE WHERE player_id = @id;
            """, ("@hash", tokenHash), ("@id", record.PlayerId));
        if (await update.ExecuteNonQueryAsync(cancellation) != 1)
            throw new EntryProblem(401, "invalid_credentials");
        await OperationAudit.Record(updateConnection, transaction, "entry.account.login", "account", record.PlayerId,
            null, null, null, cancellation);
        await transaction.CommitAsync(cancellation);
        return new EntrySession(token);
    }

    public async Task<ArenaSession> SelectRealm(string authorization, CancellationToken cancellation)
    {
        const string prefix = "Bearer ";
        if (authorization.Length != prefix.Length + 64 || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new EntryProblem(401, "unauthorized");
        byte[] entryHash;
        try { entryHash = SHA256.HashData(Convert.FromHexString(authorization.AsSpan(prefix.Length))); }
        catch (FormatException) { throw new EntryProblem(401, "unauthorized"); }

        await using var connection = await Open(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        ArenaPlayer player;
        using (var read = Command(connection, transaction, """
            SELECT p.id, p.display_name, p.avatar_id FROM entry_account a JOIN arena_player p ON p.id = a.player_id
            WHERE a.entry_hash = @entry AND a.entry_expires_at > UTC_TIMESTAMP(6) FOR UPDATE;
            """, ("@entry", entryHash)))
        {
            await using var reader = await read.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation)) throw new EntryProblem(401, "unauthorized");
            player = new ArenaPlayer(reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        var (token, tokenHash) = NewToken();
        using var updatePlayer = Command(connection, transaction, """
            UPDATE arena_player SET token_hash = @hash WHERE id = @id;
            """, ("@hash", tokenHash), ("@id", player.Id));
        await updatePlayer.ExecuteNonQueryAsync(cancellation);
        using var expire = Command(connection, transaction, """
            UPDATE entry_account SET arena_expires_at = UTC_TIMESTAMP(6) + INTERVAL 24 HOUR WHERE player_id = @id;
            """, ("@id", player.Id));
        await expire.ExecuteNonQueryAsync(cancellation);
        await OperationAudit.Record(connection, transaction, "entry.realm.select", "account", player.Id,
            "realm", EntryEndpoints.RealmId, null, cancellation);
        await transaction.CommitAsync(cancellation);
        return new ArenaSession(token, player);
    }

    private byte[] CodeDigest(string email, string code, byte[] salt) =>
        HMACSHA256.HashData(challengeKey, Encoding.ASCII.GetBytes(email + "\n" + Convert.ToHexString(salt) + "\n" + code));

    private static byte[] PasswordHash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    private static (string Token, byte[] Hash) NewToken()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        return (Convert.ToHexStringLower(secret), SHA256.HashData(secret));
    }

    private async Task<MySqlConnection> Open(CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new EntryProblem(503, "database_unavailable");
        var connection = new MySqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellation);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private sealed record PasswordRecord(string PlayerId, byte[] Salt, byte[] Hash, int Iterations);
}
