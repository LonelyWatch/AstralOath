using System.Buffers.Binary;
using System.Security.Cryptography;
using MySqlConnector;

namespace AstralOath.Server.Foundation;

internal static class PlayerPublicIdentity
{
    internal static async Task Assign(MySqlConnection connection, MySqlTransaction transaction, string player,
        CancellationToken cancellation)
    {
        using var entropy = RandomNumberGenerator.Create();
        await Assign(connection, transaction, player, entropy, cancellation);
    }

    internal static async Task Assign(MySqlConnection connection, MySqlTransaction transaction, string player,
        RandomNumberGenerator entropy, CancellationToken cancellation)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            using var insert = new MySqlCommand("INSERT INTO player_public_identity(player_id,uid) VALUES(@p,@uid)", connection, transaction);
            insert.Parameters.AddWithValue("@p", player);
            insert.Parameters.AddWithValue("@uid", Next(entropy));
            try { await insert.ExecuteNonQueryAsync(cancellation); return; }
            catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && attempt < 15)
            {
                // A UID collision retries; an existing player mapping must never be replaced.
                using var existing = new MySqlCommand("SELECT uid FROM player_public_identity WHERE player_id=@p", connection, transaction);
                existing.Parameters.AddWithValue("@p", player);
                if (await existing.ExecuteScalarAsync(cancellation) is not null) return;
            }
        }
    }

    private static long Next(RandomNumberGenerator entropy)
    {
        const ulong width = 9_000_000_000UL;
        const ulong limit = ulong.MaxValue - ulong.MaxValue % width;
        Span<byte> bytes = stackalloc byte[8];
        ulong sample;
        do { entropy.GetBytes(bytes); sample = BinaryPrimitives.ReadUInt64LittleEndian(bytes); } while (sample >= limit);
        return 1_000_000_000L + (long)(sample % width);
    }

    internal static async Task InitializeExisting(MySqlConnection connection, CancellationToken cancellation)
    {
        var players = new List<string>();
        using (var missing = new MySqlCommand("""
            SELECT p.id FROM arena_player p LEFT JOIN player_public_identity u ON u.player_id=p.id
            WHERE u.player_id IS NULL ORDER BY p.id
            """, connection))
        await using (var reader = await missing.ExecuteReaderAsync(cancellation))
            while (await reader.ReadAsync(cancellation)) players.Add(reader.GetString(0));
        foreach (string player in players)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellation);
            await Assign(connection, transaction, player, cancellation);
            await transaction.CommitAsync(cancellation);
        }
        // Stored profile receipts are returned verbatim on retry, so initialize
        // their public identity during the migration as well as the current rows.
        using var receipts = new MySqlCommand("""
            UPDATE player_profile_operation o JOIN player_public_identity u ON u.player_id=o.player_id
            SET o.receipt=JSON_SET(o.receipt,'$.uid',CAST(u.uid AS CHAR))
            WHERE JSON_EXTRACT(o.receipt,'$.uid') IS NULL
            """, connection);
        await receipts.ExecuteNonQueryAsync(cancellation);
    }
}
