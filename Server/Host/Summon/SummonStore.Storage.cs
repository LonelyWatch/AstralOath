using System.Data;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using MySqlConnector;

namespace AstralOath.Server.Summon;

internal sealed partial class SummonStore
{
    internal async Task<SummonStorage> ReadStorage(string player, CancellationToken ct)
    {
        await using var connection = await Open(ct);
        return new SummonStorage(catalog.StorageCapacity, await ReadStoredItems(connection, null, player, ct));
    }

    internal async Task<SummonStorage> ClaimStorage(string player, SummonStorageClaim input, CancellationToken ct)
    {
        if (input.InstanceIds is not { Length: >= 1 and <= 10 })
            throw new EntryProblem(400, "invalid_storage_claim");
        var requested = input.InstanceIds.Select(ParsedId).ToHashSet(StringComparer.Ordinal);
        if (requested.Count != input.InstanceIds.Length)
            throw new EntryProblem(400, "invalid_storage_claim");

        await using var connection = await Open(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await InventoryStore.LockInventoryOwner(connection, transaction, player, ct);
        var items = await ReadStoredItems(connection, transaction, player, ct);
        var selected = items.Where(item => requested.Contains(item.InstanceId)).ToArray();
        if (selected.Length != requested.Count)
            throw new EntryProblem(404, "summon_storage_item_missing");

        var room = await ReadRoom(connection, transaction, player, ct);
        int heroes = selected.Count(item => item.Kind == "hero");
        if (heroes > room.Heroes || selected.Length - heroes > room.Artifacts)
            throw new EntryProblem(409, "inventory_full");

        foreach (var item in selected)
        {
            await Grant(connection, transaction, player, item.Kind, item.TemplateId, item.Stars, item.InstanceId, ct);
            using var consume = Command(connection, transaction, player, """
                DELETE FROM summon_storage WHERE player_id = @player AND realm_id = @realm AND instance_id = @instance;
                """, ("@instance", item.InstanceId));
            await consume.ExecuteNonQueryAsync(ct);
            await OperationAudit.Record(connection, transaction, "summon.storage.claim", "arena_player", player,
                "summon_storage_item", item.InstanceId, null, ct);
        }
        await transaction.CommitAsync(ct);
        return new SummonStorage(catalog.StorageCapacity, items.Where(x => !requested.Contains(x.InstanceId)).ToArray());
    }

    private static async Task<SummonStorageItem[]> ReadStoredItems(MySqlConnection connection, MySqlTransaction? transaction,
        string player, CancellationToken ct)
    {
        var items = new List<SummonStorageItem>();
        using var query = Command(connection, transaction, player, """
            SELECT kind, template_id, stars, instance_id FROM summon_storage
            WHERE player_id = @player AND realm_id = @realm ORDER BY added_order;
            """);
        await using var read = await query.ExecuteReaderAsync(ct);
        while (await read.ReadAsync(ct))
            items.Add(new SummonStorageItem(read.GetString(0), read.GetString(1), read.GetInt32(2), read.GetString(3)));
        return items.ToArray();
    }

    private async Task<string[]> ReadOwnedWishCandidates(MySqlConnection connection, MySqlTransaction transaction,
        string player, CancellationToken ct)
    {
        var candidates = catalog.WishHeroCandidates.Concat(catalog.WishArtifactCandidates).ToHashSet(StringComparer.Ordinal);
        var owned = new List<string>();
        using var command = Command(connection, transaction, player, """
            SELECT template_id FROM inventory_hero WHERE player_id = @player AND realm_id = @realm AND stars >= 5
            UNION SELECT template_id FROM inventory_artifact WHERE player_id = @player AND realm_id = @realm
            UNION SELECT template_id FROM summon_storage WHERE player_id = @player AND realm_id = @realm AND stars = 5;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string id = reader.GetString(0);
            if (candidates.Contains(id)) owned.Add(id);
        }
        return owned.ToArray();
    }

    private async Task<AvailableRoom> ReadRoom(MySqlConnection connection, MySqlTransaction transaction, string player,
        CancellationToken ct)
    {
        int heroCapacity = await InventoryStore.EnsureStarters(connection, transaction, player, ct);
        using var counts = Command(connection, transaction, player, """
            SELECT
                (SELECT COUNT(*) FROM inventory_hero WHERE player_id = @player AND realm_id = @realm),
                (SELECT COUNT(*) FROM summon_storage WHERE player_id = @player AND realm_id = @realm);
            """);
        int heroes;
        int stored;
        await using (var read = await counts.ExecuteReaderAsync(ct))
        {
            if (!await read.ReadAsync(ct)) throw new InvalidOperationException("Inventory counts were not returned.");
            heroes = read.GetInt32(0);
            stored = read.GetInt32(1);
        }
        int artifactRoom = await InventoryStore.ReadArtifactRoom(connection, transaction, player, ct);
        return new AvailableRoom(Math.Max(0, heroCapacity - heroes), Math.Max(0, artifactRoom),
            Math.Max(0, catalog.StorageCapacity - stored));
    }

    private static async Task<bool> RecordCollection(MySqlConnection connection, MySqlTransaction transaction,
        string player, SummonEntry entry, CancellationToken ct)
    {
        // The registry remembers acquired templates after sale, consumption, or moving from storage.
        using (var known = Command(connection, transaction, player, """
            SELECT EXISTS (
                SELECT 1 FROM summon_collection
                WHERE player_id = @player AND realm_id = @realm AND kind = @kind AND template_id = @template
            ) OR EXISTS (
                SELECT 1 FROM inventory_hero
                WHERE @kind = 'hero' AND player_id = @player AND realm_id = @realm AND template_id = @template
            ) OR EXISTS (
                SELECT 1 FROM inventory_artifact
                WHERE @kind = 'artifact' AND player_id = @player AND realm_id = @realm AND template_id = @template
            );
            """, ("@kind", entry.Kind), ("@template", entry.TemplateId)))
        {
            if (Convert.ToBoolean(await known.ExecuteScalarAsync(ct)))
            {
                // Backfill the persistent record for an item acquired outside summoning.
                await SaveCollection(connection, transaction, player, entry, ct);
                return false;
            }
        }
        await SaveCollection(connection, transaction, player, entry, ct);
        return true;
    }

    private static async Task SaveCollection(MySqlConnection connection, MySqlTransaction transaction, string player,
        SummonEntry entry, CancellationToken ct)
    {
        using var record = Command(connection, transaction, player, """
            INSERT INTO summon_collection (player_id, realm_id, kind, template_id)
            VALUES (@player, @realm, @kind, @template)
            ON DUPLICATE KEY UPDATE template_id = @template;
            """, ("@kind", entry.Kind), ("@template", entry.TemplateId));
        await record.ExecuteNonQueryAsync(ct);
    }

    private static async Task Grant(MySqlConnection connection, MySqlTransaction transaction, string player,
        string kind, string template, int stars, string instance, CancellationToken ct, string source = "summon")
    {
        if (kind == "artifact")
        {
            await InventoryStore.GrantArtifact(connection, transaction, player, template, instance, ct, source);
            return;
        }
        using var issue = Command(connection, transaction, player, """
            INSERT INTO inventory_hero (instance_id, player_id, realm_id, template_id, level, stars)
            VALUES (@instance, @player, @realm, @template, 1, @stars);
            """, ("@instance", instance), ("@template", template), ("@stars", stars));
        await issue.ExecuteNonQueryAsync(ct);
    }

    private sealed record AvailableRoom(int Heroes, int Artifacts, int Storage);
}

internal sealed record SummonStorageClaim(string[]? InstanceIds);
internal sealed record SummonStorage(int Capacity, SummonStorageItem[] Items);
internal sealed record SummonStorageItem(string Kind, string TemplateId, int Stars, string InstanceId);
