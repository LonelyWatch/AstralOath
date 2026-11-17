using System.Data;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed class InventoryStore(string? connectionString)
{
    internal const int DefaultCapacity = 1000;

    private static readonly (string TemplateId, byte Level, byte Stars)[] StarterRoster =
    [
        ("c1001", 1, 3),
        ("c1005", 1, 4),
        ("c1062", 1, 4)
    ];

    public async Task<HeroInventory> ReadHeroes(string playerId, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation);
        int capacity = await EnsureStarters(connection, transaction, playerId, cancellation);
        var heroes = new List<OwnedHero>();
        using (var read = Command(connection, transaction, """
            SELECT instance_id, template_id, level, stars
            FROM inventory_hero WHERE player_id = @player AND realm_id = @realm
            ORDER BY acquisition_order LIMIT 1001;
            """, playerId))
        {
            await using var result = await read.ExecuteReaderAsync(cancellation);
            while (await result.ReadAsync(cancellation))
                heroes.Add(new OwnedHero(result.GetString(0), result.GetString(1), result.GetInt32(2), result.GetInt32(3)));
        }
        if (heroes.Count > capacity)
            throw new EntryProblem(503, "inventory_unavailable");
        await transaction.CommitAsync(cancellation);
        return new HeroInventory(capacity, heroes.Count, heroes);
    }

    internal static async Task<int> EnsureStarters(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, CancellationToken cancellation)
    {
        const string grantedCapacity = """
            SELECT capacity FROM inventory_capacity
            WHERE player_id = @player AND realm_id = @realm AND starter_granted = TRUE
            """;
        using (var existing = Command(connection, transaction, grantedCapacity + ";", playerId))
        {
            var value = await existing.ExecuteScalarAsync(cancellation);
            if (value is not null) return Convert.ToInt32(value);
        }

        // Start with the player lock, matching the order of realm selection.
        using (var owner = Command(connection, transaction, """
            SELECT id FROM arena_player WHERE id = @player FOR UPDATE;
            """, playerId))
        {
            if (await owner.ExecuteScalarAsync(cancellation) is null)
                throw new EntryProblem(401, "unauthorized");
        }
        using (var existing = Command(connection, transaction, grantedCapacity + " FOR UPDATE;", playerId))
        {
            var value = await existing.ExecuteScalarAsync(cancellation);
            if (value is not null) return Convert.ToInt32(value);
        }
        using (var create = Command(connection, transaction, """
            INSERT INTO inventory_capacity (player_id, realm_id, capacity, starter_granted)
            VALUES (@player, @realm, @capacity, TRUE)
            ON DUPLICATE KEY UPDATE starter_granted = TRUE;
            """, playerId))
        {
            create.Parameters.AddWithValue("@capacity", DefaultCapacity);
            await create.ExecuteNonQueryAsync(cancellation);
        }

        using (var grant = Command(connection, transaction, """
            INSERT INTO inventory_hero (instance_id, player_id, realm_id, template_id, level, stars)
            VALUES (@instance, @player, @realm, @template, @level, @stars);
            """, playerId))
        {
            var instance = grant.Parameters.Add("@instance", MySqlDbType.String);
            var template = grant.Parameters.Add("@template", MySqlDbType.String);
            var level = grant.Parameters.Add("@level", MySqlDbType.UByte);
            var stars = grant.Parameters.Add("@stars", MySqlDbType.UByte);
            foreach (var starter in StarterRoster)
            {
                instance.Value = Guid.NewGuid().ToString("N");
                template.Value = starter.TemplateId;
                level.Value = starter.Level;
                stars.Value = starter.Stars;
                await grant.ExecuteNonQueryAsync(cancellation);
            }
        }
        await OperationAudit.Record(connection, transaction, "inventory.hero.starter_granted", "arena_player", playerId,
            "realm", EntryEndpoints.RealmId, null, cancellation);
        using var capacity = Command(connection, transaction, grantedCapacity + ";", playerId);
        return Convert.ToInt32(await capacity.ExecuteScalarAsync(cancellation));
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction transaction, string sql, string playerId)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@player", playerId);
        command.Parameters.AddWithValue("@realm", EntryEndpoints.RealmId);
        return command;
    }
}

internal sealed record HeroInventory(int Capacity, int Used, IReadOnlyList<OwnedHero> Heroes);
internal sealed record OwnedHero(string InstanceId, string TemplateId, int Level, int Stars);
