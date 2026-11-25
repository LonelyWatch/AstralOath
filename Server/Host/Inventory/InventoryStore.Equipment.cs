using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed partial class InventoryStore
{
    private static readonly Lazy<IReadOnlyList<TrainingEquipment>> TrainingItems = new(TrainingEquipment.Read);
    internal static string[] DevelopmentEquipmentIds => TrainingItems.Value.Select(item => item.TemplateId).Distinct().ToArray();

    internal Task<EquipmentInventory> ReadEquipment(string playerId, CancellationToken cancellation) =>
        EquipmentTransaction(playerId, null, cancellation);

    internal async Task<long> ReadEquipmentVersion(string playerId, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        using var query = Command(connection, null, """
            SELECT revision FROM inventory_equipment_capacity WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        return Convert.ToInt64(await query.ExecuteScalarAsync(cancellation));
    }

    internal Task<EquipmentInventory> ChangeEquipment(string playerId, EquipmentMutation request, CancellationToken cancellation)
    {
        if (request.Action is not ("equip" or "unequip") || request.ExpectedVersion is null or < 0)
            throw new EntryProblem(400, "invalid_equipment_operation");
        return EquipmentTransaction(playerId, request with
        {
            RequestId = CanonicalId(request.RequestId),
            InstanceId = CanonicalId(request.InstanceId),
            HeroInstanceId = CanonicalId(request.HeroInstanceId)
        }, cancellation);
    }

    private async Task<EquipmentInventory> EquipmentTransaction(string playerId, EquipmentMutation? request,
        CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation);
        await LockInventoryOwner(connection, tx, playerId, cancellation);
        await EnsureStarters(connection, tx, playerId, cancellation);
        using (var header = Command(connection, tx, """
            INSERT INTO inventory_equipment_capacity (player_id, realm_id) VALUES (@player, @realm)
            ON DUPLICATE KEY UPDATE player_id = @player;
            """, playerId))
            await header.ExecuteNonQueryAsync(cancellation);
        int capacity;
        long version;
        bool granted;
        using (var header = Command(connection, tx, """
            SELECT capacity, revision, training_granted FROM inventory_equipment_capacity
            WHERE player_id = @player AND realm_id = @realm;
            """, playerId))
        await using (var reader = await header.ExecuteReaderAsync(cancellation))
        {
            if (!await reader.ReadAsync(cancellation)) throw new InvalidDataException("Missing equipment header.");
            capacity = reader.GetInt32(0);
            version = reader.GetInt64(1);
            granted = reader.GetBoolean(2);
        }

        if (development && grantTrainingEquipment && !granted)
        {
            await GrantTrainingEquipment(connection, tx, playerId, capacity, cancellation);
            version++;
        }
        byte[]? digest = null;
        if (request != null)
        {
            digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
            var saved = await ReadEquipmentReceipt(connection, tx, playerId, request.RequestId!, digest, cancellation);
            if (saved != null)
            {
                await tx.CommitAsync(cancellation);
                return saved;
            }
            if (request.ExpectedVersion != version) throw new EntryProblem(409, "equipment_version_conflict");
            if (await ApplyEquipmentAction(connection, tx, playerId, request, cancellation))
            {
                using var bump = Command(connection, tx, """
                    UPDATE inventory_equipment_capacity SET revision = revision + 1
                    WHERE player_id = @player AND realm_id = @realm;
                    """, playerId);
                await bump.ExecuteNonQueryAsync(cancellation);
                version++;
                await OperationAudit.Record(connection, tx, "inventory.equipment." + request.Action, "arena_player",
                    playerId, "inventory_equipment", request.InstanceId, null, cancellation);
            }
        }

        var items = await ReadEquipmentItems(connection, tx, playerId, null, cancellation);
        var result = new EquipmentInventory(capacity, items.Count, version, items,
            await ReadEquipmentEnhancementBalances(connection, tx, playerId, cancellation));
        if (request != null)
        {
            using var save = Command(connection, tx, """
                INSERT INTO inventory_equipment_operation (player_id, realm_id, request_id, fingerprint, receipt)
                VALUES (@player, @realm, @request, @digest, @receipt);
                """, playerId);
            save.Parameters.AddWithValue("@request", request.RequestId);
            save.Parameters.AddWithValue("@digest", digest);
            save.Parameters.AddWithValue("@receipt", JsonSerializer.Serialize(result));
            await save.ExecuteNonQueryAsync(cancellation);
        }
        await tx.CommitAsync(cancellation);
        return result;
    }

    private static async Task<EquipmentInventory?> ReadEquipmentReceipt(MySqlConnection connection, MySqlTransaction tx,
        string playerId, string requestId, byte[] digest, CancellationToken ct)
    {
        using var query = Command(connection, tx, """
            SELECT fingerprint, receipt FROM inventory_equipment_operation
            WHERE player_id = @player AND realm_id = @realm AND request_id = @request;
            """, playerId);
        query.Parameters.AddWithValue("@request", requestId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (!reader.GetFieldValue<byte[]>(0).AsSpan().SequenceEqual(digest))
            throw new EntryProblem(409, "request_conflict");
        return JsonSerializer.Deserialize<EquipmentInventory>(reader.GetString(1))
            ?? throw new InvalidDataException("Invalid equipment receipt.");
    }

    private static async Task<bool> ApplyEquipmentAction(MySqlConnection connection, MySqlTransaction tx, string playerId,
        EquipmentMutation request, CancellationToken ct)
    {
        string slot;
        string? wearer;
        using (var query = Command(connection, tx, """
            SELECT e.slot, h.instance_id FROM inventory_equipment e
            LEFT JOIN inventory_equipment_loadout l ON l.instance_id = e.instance_id
            LEFT JOIN inventory_hero h ON h.acquisition_order = l.hero_order
            WHERE e.player_id = @player AND e.realm_id = @realm AND e.instance_id = @item;
            """, playerId))
        {
            query.Parameters.AddWithValue("@item", request.InstanceId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new EntryProblem(404, "equipment_not_found");
            slot = reader.GetString(0);
            wearer = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        if (request.Slot != null && request.Slot != slot) throw new EntryProblem(400, "equipment_slot_mismatch");
        using var hero = Command(connection, tx, """
            SELECT acquisition_order FROM inventory_hero WHERE player_id = @player AND realm_id = @realm AND instance_id = @hero;
            """, playerId);
        hero.Parameters.AddWithValue("@hero", request.HeroInstanceId);
        object heroOrder = await hero.ExecuteScalarAsync(ct) ?? throw new EntryProblem(404, "equipment_hero_not_found");
        if (request.Action == "unequip")
        {
            if (wearer != request.HeroInstanceId) throw new EntryProblem(409, "equipment_wearer_changed");
            using var remove = Command(connection, tx, """
                DELETE FROM inventory_equipment_loadout WHERE player_id = @player AND realm_id = @realm AND instance_id = @item;
                """, playerId);
            remove.Parameters.AddWithValue("@item", request.InstanceId);
            await remove.ExecuteNonQueryAsync(ct);
            return true;
        }
        if (wearer == request.HeroInstanceId) return false;
        using var equip = Command(connection, tx, """
            DELETE FROM inventory_equipment_loadout WHERE player_id = @player AND realm_id = @realm
                AND (instance_id = @item OR (hero_order = @order AND slot = @slot));
            INSERT INTO inventory_equipment_loadout (player_id,realm_id,hero_order,slot,instance_id)
            VALUES (@player,@realm,@order,@slot,@item);
            """, playerId);
        equip.Parameters.AddWithValue("@order", heroOrder);
        equip.Parameters.AddWithValue("@slot", slot);
        equip.Parameters.AddWithValue("@item", request.InstanceId);
        await equip.ExecuteNonQueryAsync(ct);
        return true;
    }

    private static async Task GrantTrainingEquipment(MySqlConnection connection, MySqlTransaction tx, string playerId,
        int capacity, CancellationToken ct)
    {
        await InsertEquipment(connection, tx, playerId, capacity, TrainingItems.Value.Select(item => item.ToGrant()).ToArray(),
            "development-training-v1", ct);
        using var issued = Command(connection, tx, """
            UPDATE inventory_equipment_capacity SET revision = revision + 1, training_granted = TRUE
            WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        await issued.ExecuteNonQueryAsync(ct);
        await OperationAudit.Record(connection, tx, "inventory.equipment.development_grant", "arena_player",
            playerId, "realm", EntryEndpoints.RealmId, null, ct);
    }

    internal static EquipmentGrant GetTrainingGrant(string template) =>
        TrainingItems.Value.FirstOrDefault(row => row.TemplateId == template)?.ToGrant()
        ?? throw new EntryProblem(400, "invalid_gm_template");

    internal static async Task GrantGmEquipment(MySqlConnection connection, MySqlTransaction tx, string playerId,
        EquipmentGrant item, int quantity, string requestId, CancellationToken ct)
    {
        using var header = Command(connection, tx, """
            SELECT capacity FROM inventory_equipment_capacity WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        int capacity = Convert.ToInt32(await header.ExecuteScalarAsync(ct));
        await InsertEquipment(connection, tx, playerId, capacity, Enumerable.Repeat(item, quantity).ToArray(), "gm-" + requestId, ct);
        using var revision = Command(connection, tx, """
            UPDATE inventory_equipment_capacity SET revision = revision + 1 WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        await revision.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertEquipment(MySqlConnection connection, MySqlTransaction tx, string playerId,
        int capacity, IReadOnlyList<EquipmentGrant> items, string source, CancellationToken ct)
    {
        using var count = Command(connection, tx, """
            SELECT COUNT(*) FROM inventory_equipment WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        if (Convert.ToInt32(await count.ExecuteScalarAsync(ct)) + items.Count > capacity)
            throw new EntryProblem(409, "equipment_inventory_full");
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            string instance = Guid.NewGuid().ToString("N");
            using var add = Command(connection, tx, """
                INSERT INTO inventory_equipment (instance_id,player_id,realm_id,template_id,slot,set_id,item_level,
                    enhance_level,grade,main_kind,main_value,source_ref,source_index,experience,enhancement_profile_id)
                VALUES (@id,@player,@realm,@template,@slot,@set,@level,@enhance,@grade,@kind,@value,@source,@index,@xp,@profile);
                """, playerId);
            add.Parameters.AddWithValue("@id", instance);
            add.Parameters.AddWithValue("@template", item.TemplateId);
            add.Parameters.AddWithValue("@slot", item.Slot);
            add.Parameters.AddWithValue("@set", item.SetId);
            add.Parameters.AddWithValue("@grade", item.Grade);
            add.Parameters.AddWithValue("@level", item.ItemLevel);
            add.Parameters.AddWithValue("@enhance", item.EnhanceLevel);
            add.Parameters.AddWithValue("@kind", item.MainStat.Kind);
            add.Parameters.AddWithValue("@value", item.MainStat.Value);
            add.Parameters.AddWithValue("@index", index);
            add.Parameters.AddWithValue("@source", source);
            var profile = EquipmentEnhancementCatalog.Rules.Profiles.FirstOrDefault(p => p.Id == item.EnhancementProfileId);
            add.Parameters.AddWithValue("@profile", item.EnhancementProfileId);
            add.Parameters.AddWithValue("@xp", profile?.XpThresholds[item.EnhanceLevel] ?? 0L);
            await add.ExecuteNonQueryAsync(ct);
            for (int i = 0; i < item.Substats.Length; i++)
            {
                using var sub = new MySqlCommand("""
                    INSERT INTO inventory_equipment_substat (instance_id,position,kind,stat_value) VALUES (@id,@pos,@kind,@value);
                    """, connection, tx);
                sub.Parameters.AddWithValue("@id", instance);
                sub.Parameters.AddWithValue("@pos", i);
                sub.Parameters.AddWithValue("@kind", item.Substats[i].Kind);
                sub.Parameters.AddWithValue("@value", item.Substats[i].Value);
                await sub.ExecuteNonQueryAsync(ct);
            }
        }
    }

    internal static Task<IReadOnlyList<OwnedEquipment>> ReadEquippedItems(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, IReadOnlyList<string> heroInstanceIds, CancellationToken cancellation) =>
        ReadEquipmentItems(connection, transaction, playerId, heroInstanceIds, cancellation);

    private static async Task<IReadOnlyList<OwnedEquipment>> ReadEquipmentItems(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, IReadOnlyList<string>? heroInstanceIds, CancellationToken cancellation)
    {
        if (heroInstanceIds is { Count: 0 }) return [];
        using var query = Command(connection, transaction, """
            SELECT e.instance_id,e.template_id,e.slot,e.set_id,e.item_level,e.enhance_level,e.grade,e.main_kind,e.main_value,
                   h.instance_id,s.kind,s.stat_value,e.experience,e.enhancement_profile_id
            FROM inventory_equipment e
            LEFT JOIN inventory_equipment_loadout l ON l.instance_id = e.instance_id
            LEFT JOIN inventory_hero h ON h.acquisition_order = l.hero_order
            LEFT JOIN inventory_equipment_substat s ON s.instance_id = e.instance_id
            WHERE e.player_id = @player AND e.realm_id = @realm
            """, playerId);
        if (heroInstanceIds != null)
        {
            var parameters = new string[heroInstanceIds.Count];
            for (int i = 0; i < parameters.Length; i++)
            {
                parameters[i] = "@hero" + i;
                query.Parameters.AddWithValue(parameters[i], heroInstanceIds[i]);
            }
            query.CommandText += " AND h.instance_id IN (" + string.Join(", ", parameters) + ")";
        }
        query.CommandText += " ORDER BY e.acquisition_order, s.position;";
        var items = new List<OwnedEquipment>();
        List<OwnedEquipmentStat>? substats = null;
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
        {
            var id = reader.GetString(0);
            if (items.Count == 0 || items[^1].InstanceId != id)
            {
                substats = new List<OwnedEquipmentStat>();
                var profile = reader.IsDBNull(13) ? null : EquipmentEnhancementCatalog.Rules.Profiles
                    .FirstOrDefault(p => p.Id == reader.GetString(13));
                bool supported = profile is { Supported: true } && profile.ItemLevel == reader.GetInt32(4) &&
                    profile.Grade == reader.GetString(6) && profile.Slot == reader.GetString(2) &&
                    profile.MainKind == reader.GetString(7) && profile.MainValues[reader.GetInt32(5)] == reader.GetInt32(8);
                items.Add(new OwnedEquipment(id, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6),
                    new OwnedEquipmentStat(reader.GetString(7), reader.GetInt32(8)), substats,
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt64(12), supported,
                    supported ? null : profile?.BlockReason ?? "equipment_profile_unsupported"));
            }
            if (!reader.IsDBNull(10)) substats!.Add(new OwnedEquipmentStat(reader.GetString(10), reader.GetInt32(11)));
        }
        return items;
    }
}
