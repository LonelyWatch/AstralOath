using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed partial class InventoryStore
{
    internal async Task<EquipmentEnhancementReceipt> EnhanceEquipment(string playerId, string instanceId,
        EquipmentEnhancementRequest input, CancellationToken ct)
    {
        instanceId = CanonicalId(instanceId);
        if (input.ExpectedVersion is null or < 0) throw new EntryProblem(400, "invalid_equipment_operation");
        var request = input with { RequestId = CanonicalId(input.RequestId) };
        byte[] digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { action = "enhance", instanceId, request }));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockInventoryOwner(connection, tx, playerId, ct);
        await EnsureStarters(connection, tx, playerId, ct);
        using (var receipt = Command(connection, tx, """
            SELECT fingerprint, receipt FROM inventory_equipment_operation
            WHERE player_id=@player AND realm_id=@realm AND request_id=@request;
            """, playerId))
        {
            receipt.Parameters.AddWithValue("@request", request.RequestId);
            await using var reader = await receipt.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (!reader.GetFieldValue<byte[]>(0).AsSpan().SequenceEqual(digest)) throw new EntryProblem(409, "request_conflict");
                var saved = JsonSerializer.Deserialize<EquipmentEnhancementReceipt>(reader.GetString(1))
                    ?? throw new InvalidDataException("Invalid equipment enhancement receipt.");
                await reader.DisposeAsync();
                await tx.CommitAsync(ct);
                return saved;
            }
        }
        var item = (await ReadEquipmentItems(connection, tx, playerId, null, ct)).FirstOrDefault(i => i.InstanceId == instanceId)
            ?? throw new EntryProblem(404, "equipment_not_found");
        long version;
        int capacity;
        using (var header = Command(connection, tx, """
            SELECT capacity, revision FROM inventory_equipment_capacity WHERE player_id=@player AND realm_id=@realm;
            """, playerId))
        await using (var reader = await header.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Missing equipment header.");
            capacity = reader.GetInt32(0); version = reader.GetInt64(1);
        }
        if (request.ExpectedVersion != version) throw new EntryProblem(409, "equipment_version_conflict");
        var plan = EquipmentEnhancementEngine.Calculate(item, request.Materials, RandomNumberGenerator.GetInt32);
        var available = await ReadMaterialBalances(connection, tx, playerId, ct);
        if (available.GetValueOrDefault("gold") < plan.Result.ConsumedGold)
            throw new EntryProblem(409, "equipment_gold_insufficient");
        // Validate the gross selection before netting returned powder into the ledger.
        // A future refund cannot pay for material the player does not currently own.
        if (plan.Materials.Any(m => available.GetValueOrDefault(m.Kind!) < m.Quantity))
            throw new EntryProblem(409, "equipment_material_insufficient");
        // The shared material ledger has an operation FK. Both namespaces use the same digest;
        // collision with an earlier artifact/GM request is rejected before any deduction.
        if (await BeginOperation(connection, tx, playerId, request.RequestId!, digest, ct))
            throw new EntryProblem(409, "request_conflict");
        var deltas = plan.Materials.ToDictionary(m => m.Kind!, m => -m.Quantity, StringComparer.Ordinal);
        deltas["gold"] = checked(-(int)plan.Result.ConsumedGold);
        if (plan.Result.ReturnedPowderKind is { } powder)
            deltas[powder] = deltas.GetValueOrDefault(powder) + plan.Result.ReturnedPowderQuantity;
        foreach (var delta in deltas.Where(d => d.Value != 0))
            await PostMaterial(connection, tx, playerId, request.RequestId!, delta.Key, delta.Value, ct);
        using (var update = Command(connection, tx, """
            UPDATE inventory_equipment SET experience=@xp, enhance_level=@level, main_value=@main
            WHERE player_id=@player AND realm_id=@realm AND instance_id=@item;
            UPDATE inventory_equipment_capacity SET revision=revision+1 WHERE player_id=@player AND realm_id=@realm;
            DELETE FROM inventory_equipment_substat WHERE instance_id=@item;
            """, playerId))
        {
            update.Parameters.AddWithValue("@xp", plan.After.Xp);
            update.Parameters.AddWithValue("@level", plan.After.EnhanceLevel);
            update.Parameters.AddWithValue("@main", plan.After.MainStat.Value);
            update.Parameters.AddWithValue("@item", instanceId);
            await update.ExecuteNonQueryAsync(ct);
        }
        for (int position = 0; position < plan.After.Substats.Count; position++)
        {
            var stat = plan.After.Substats[position];
            using var add = new MySqlCommand("""
                INSERT INTO inventory_equipment_substat(instance_id,position,kind,stat_value,enhancement_count)
                VALUES(@item,@position,@kind,@value,@count);
                """, connection, tx);
            add.Parameters.AddWithValue("@item", instanceId); add.Parameters.AddWithValue("@position", position);
            add.Parameters.AddWithValue("@kind", stat.Kind); add.Parameters.AddWithValue("@value", stat.Value);
            add.Parameters.AddWithValue("@count", stat.EnhancementCount);
            await add.ExecuteNonQueryAsync(ct);
        }
        await OperationAudit.Record(connection, tx, "inventory.equipment.enhance", "arena_player", playerId,
            "inventory_equipment", instanceId, null, ct);
        var items = await ReadEquipmentItems(connection, tx, playerId, null, ct);
        var result = new EquipmentEnhancementReceipt(new EquipmentInventory(capacity, items.Count, version + 1, items,
            await ReadEquipmentEnhancementBalances(connection, tx, playerId, ct)), plan.Result);
        using (var save = Command(connection, tx, """
            INSERT INTO inventory_equipment_operation(player_id,realm_id,request_id,fingerprint,receipt)
            VALUES(@player,@realm,@request,@digest,@receipt);
            """, playerId))
        {
            save.Parameters.AddWithValue("@request", request.RequestId); save.Parameters.AddWithValue("@digest", digest);
            save.Parameters.AddWithValue("@receipt", JsonSerializer.Serialize(result));
            await save.ExecuteNonQueryAsync(ct);
        }
        foreach (var step in plan.Steps)
        {
            using var log = Command(connection, tx, """
                INSERT INTO inventory_equipment_enhancement_roll(player_id,realm_id,request_id,instance_id,milestone,kind,before_value,after_value,added)
                VALUES(@player,@realm,@request,@item,@milestone,@kind,@before,@after,@added);
                """, playerId);
            log.Parameters.AddWithValue("@request", request.RequestId); log.Parameters.AddWithValue("@item", instanceId);
            log.Parameters.AddWithValue("@milestone", step.Milestone); log.Parameters.AddWithValue("@kind", step.Kind);
            log.Parameters.AddWithValue("@before", step.Before); log.Parameters.AddWithValue("@after", step.After);
            log.Parameters.AddWithValue("@added", step.Added);
            await log.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<EquipmentEnhancementBalances> ReadEquipmentEnhancementBalances(MySqlConnection connection,
        MySqlTransaction tx, string playerId, CancellationToken ct)
    {
        var balances = await ReadMaterialBalances(connection, tx, playerId, ct);
        return new(balances.GetValueOrDefault("gold"), EquipmentEnhancementCatalog.Rules.Materials
            .Select(m => new EquipmentEnhancementBalance(m.Id, balances.GetValueOrDefault(m.Id))).ToArray());
    }
}
