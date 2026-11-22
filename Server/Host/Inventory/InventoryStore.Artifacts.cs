using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Shared.Battle;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed partial class InventoryStore
{
    internal const int ArtifactCapacity = 400;
    private const string DevelopmentGrantId = "00000000000000000000000000000000";

    public Task<ArtifactInventory> ReadArtifacts(string playerId, CancellationToken token) => InArtifactTransaction(
        playerId, (_, _) => Task.CompletedTask, token);

    public Task<ArtifactInventory> ChangeArtifact(string playerId, string instanceId, string action, ArtifactMutation input,
        CancellationToken token)
    {
        instanceId = CanonicalId(instanceId);
        var request = input with { RequestId = CanonicalId(input.RequestId) };
        Validate(action, request);
        byte[] digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { instanceId, action, request }));
        return InArtifactTransaction(playerId, async (connection, transaction) =>
        {
            if (await BeginOperation(connection, transaction, playerId, request.RequestId!, digest, token))
                return;

            var current = await ReadArtifact(connection, transaction, playerId, instanceId, token)
                ?? throw new EntryProblem(404, "artifact_not_found");
            switch (action)
            {
                case "equip":
                    await EquipArtifact(connection, transaction, playerId, current, CanonicalId(request.HeroInstanceId), token);
                    break;
                case "unequip":
                    await RunArtifactSql(connection, transaction, playerId, """
                        DELETE FROM inventory_artifact_loadout WHERE player_id = @player AND realm_id = @realm AND artifact_id = @id;
                        """, token, ("@id", instanceId));
                    break;
                case "lock":
                    await RunArtifactSql(connection, transaction, playerId, """
                        UPDATE inventory_artifact SET locked = @locked WHERE player_id = @player AND realm_id = @realm AND instance_id = @id;
                        """, token, ("@id", instanceId), ("@locked", request.Locked!.Value));
                    break;
                case "enhance":
                    await EnhanceArtifact(connection, transaction, playerId, current, request, token);
                    break;
                case "sell":
                    await SellArtifact(connection, transaction, playerId, current, request.RequestId!, token);
                    break;
            }
            await OperationAudit.Record(connection, transaction, "inventory.artifact." + action, "arena_player", playerId,
                "inventory_artifact", instanceId, null, token);
        }, token);
    }

    private async Task<ArtifactInventory> InArtifactTransaction(string playerId,
        Func<MySqlConnection, MySqlTransaction, Task> action, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await LockInventoryOwner(connection, transaction, playerId, token);
        await EnsureStarters(connection, transaction, playerId, token);
        if (development)
            await GrantDevelopmentMaterialsOnce(connection, transaction, playerId, token);
        await action(connection, transaction);
        var artifacts = await ReadOwnedArtifacts(connection, transaction, playerId, null, token);
        var materials = await ReadMaterialBalances(connection, transaction, playerId, token);
        await transaction.CommitAsync(token);
        return new ArtifactInventory(ArtifactCapacity, artifacts.Count, artifacts, new ArtifactMaterials(
            materials.GetValueOrDefault("gold"), materials.GetValueOrDefault("lesserCharm"),
            materials.GetValueOrDefault("greaterCharm"), materials.GetValueOrDefault("epicCharm"),
            materials.GetValueOrDefault("bottle"), materials.GetValueOrDefault("powder")));
    }

    internal static async Task LockInventoryOwner(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, CancellationToken token)
    {
        using var query = Command(connection, transaction,
            "SELECT id FROM arena_player WHERE id = @player FOR UPDATE;", playerId);
        if (await query.ExecuteScalarAsync(token) is null)
            throw new EntryProblem(401, "unauthorized");
    }

    // These helpers are called by the summon transaction under the same owner lock.
    internal static async Task<int> ReadArtifactRoom(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, CancellationToken token)
    {
        using var count = Command(connection, transaction, """
            SELECT COUNT(*) FROM inventory_artifact WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        return Math.Max(0, ArtifactCapacity - Convert.ToInt32(await count.ExecuteScalarAsync(token)));
    }

    internal static Task GrantArtifact(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        string templateId, string instanceId, CancellationToken token, string source = "summon") =>
        RunArtifactSql(connection, transaction, playerId, """
            INSERT INTO inventory_artifact (instance_id, player_id, realm_id, template_id, source)
            VALUES (@id, @player, @realm, @template, @source);
            """, token, ("@id", instanceId), ("@template", templateId), ("@source", source));

    internal static async Task<bool> BeginOperation(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, string requestId, byte[] digest, CancellationToken token)
    {
        using var read = Command(connection, transaction, """
            SELECT fingerprint FROM inventory_artifact_operation
            WHERE player_id = @player AND realm_id = @realm AND request_id = @request;
            """, playerId);
        read.Parameters.AddWithValue("@request", requestId);
        if (await read.ExecuteScalarAsync(token) is byte[] previous)
        {
            if (!previous.AsSpan().SequenceEqual(digest))
                throw new EntryProblem(409, "request_conflict");
            return true;
        }
        await RunArtifactSql(connection, transaction, playerId, """
            INSERT INTO inventory_artifact_operation (player_id, realm_id, request_id, fingerprint, created_at)
            VALUES (@player, @realm, @request, @digest, UTC_TIMESTAMP(6));
            """, token, ("@request", requestId), ("@digest", digest));
        return false;
    }

    internal static async Task GrantDevelopmentMaterialsOnce(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, CancellationToken token)
    {
        using var query = Command(connection, transaction,
            "SELECT player_id FROM entry_account WHERE player_id = @player;", playerId);
        if (await query.ExecuteScalarAsync(token) is not null ||
            await BeginOperation(connection, transaction, playerId, DevelopmentGrantId, new byte[32], token))
            return;
        await PostMaterial(connection, transaction, playerId, DevelopmentGrantId, "gold", 100_000, token);
        await PostMaterial(connection, transaction, playerId, DevelopmentGrantId, "lesserCharm", 10, token);
        await PostMaterial(connection, transaction, playerId, DevelopmentGrantId, "greaterCharm", 5, token);
        await PostMaterial(connection, transaction, playerId, DevelopmentGrantId, "epicCharm", 1, token);
        await OperationAudit.Record(connection, transaction, "inventory.artifact.development_materials", "arena_player",
            playerId, "realm", EntryEndpoints.RealmId, null, token);
    }

    private async Task<List<OwnedArtifact>> ReadOwnedArtifacts(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, string? instance, CancellationToken token)
    {
        using var query = Command(connection, transaction, """
            SELECT a.instance_id, a.template_id, a.experience, a.limit_breaks, a.locked, l.hero_id, a.source
            FROM inventory_artifact a LEFT JOIN inventory_artifact_loadout l ON l.artifact_id = a.instance_id
            WHERE a.player_id = @player AND a.realm_id = @realm AND (@id IS NULL OR a.instance_id = @id)
            ORDER BY a.acquisition_order LIMIT 401;
            """, playerId);
        query.Parameters.AddWithValue("@id", instance);
        var artifacts = new List<OwnedArtifact>();
        await using var result = await query.ExecuteReaderAsync(token);
        while (await result.ReadAsync(token))
        {
            int experience = result.GetInt32(2);
            int breaks = result.GetInt32(3);
            artifacts.Add(new OwnedArtifact(result.GetString(0), result.GetString(1), Catalog.LevelForExperience(experience, breaks),
                experience, breaks, result.GetBoolean(4), result.IsDBNull(5) ? null : result.GetString(5), result.GetString(6)));
        }
        if (artifacts.Count > ArtifactCapacity)
            throw new EntryProblem(503, "inventory_unavailable");
        return artifacts;
    }

    private async Task<OwnedArtifact?> ReadArtifact(MySqlConnection connection, MySqlTransaction transaction,
        string playerId, string id, CancellationToken token) =>
        (await ReadOwnedArtifacts(connection, transaction, playerId, id, token)).SingleOrDefault();

    private async Task EquipArtifact(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        OwnedArtifact artifact, string heroId, CancellationToken token)
    {
        using var find = Command(connection, transaction, """
            SELECT template_id FROM inventory_hero WHERE player_id = @player AND realm_id = @realm AND instance_id = @hero;
            """, playerId);
        find.Parameters.AddWithValue("@hero", heroId);
        if (await find.ExecuteScalarAsync(token) is not string heroTemplate)
            throw new EntryProblem(404, "hero_not_found");
        ArtifactTemplate unit = GetArtifactTemplate(artifact.TemplateId);
        if (!string.IsNullOrEmpty(unit.HeroClass) && unit.HeroClass != heroes?.HeroClassFor(heroTemplate))
            throw new EntryProblem(409, "artifact_class_mismatch");
        await RunArtifactSql(connection, transaction, playerId, """
            DELETE FROM inventory_artifact_loadout WHERE player_id = @player AND realm_id = @realm
                AND (hero_id = @hero OR artifact_id = @id);
            INSERT INTO inventory_artifact_loadout (hero_id, artifact_id, player_id, realm_id)
            VALUES (@hero, @id, @player, @realm);
            """, token, ("@hero", heroId), ("@id", artifact.InstanceId));
    }

    private async Task EnhanceArtifact(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        OwnedArtifact target, ArtifactMutation request, CancellationToken token)
    {
        var template = GetArtifactTemplate(target.TemplateId);
        var deltas = new Dictionary<string, int>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        int experience = 0;
        int breaks = target.LimitBreaks;
        foreach (var selection in request.Materials!)
        {
            var material = selection!;
            string kind = material.Kind!;
            int count = material.Quantity;
            if (kind == "artifact")
            {
                string materialId = CanonicalId(material.InstanceId);
                if (materialId == target.InstanceId || !consumed.Add(materialId))
                    throw new EntryProblem(400, "invalid_artifact_material");
                var copy = await ReadArtifact(connection, transaction, playerId, materialId, token)
                    ?? throw new EntryProblem(404, "artifact_not_found");
                EnsureConsumable(copy);
                var source = GetArtifactTemplate(copy.TemplateId);
                experience += Catalog.MaterialExperience(source.Stars, copy.Experience);
                if (copy.TemplateId == target.TemplateId)
                    ++breaks;
            }
            else
            {
                deltas[kind] = deltas.GetValueOrDefault(kind) - count;
                if (kind == "bottle")
                {
                    if (!template.CanUseBottle)
                        throw new EntryProblem(409, "artifact_bottle_ineligible");
                    breaks += count;
                }
                else
                    experience += Catalog.CharmExperience(kind) * count;
            }
        }
        if (breaks > 5)
            throw new EntryProblem(409, "artifact_break_cap");
        int upgraded = Math.Min(target.Experience + experience, Catalog.ExperienceForLevel(15 + 3 * breaks));
        if (upgraded == target.Experience && breaks == target.LimitBreaks)
            throw new EntryProblem(409, "artifact_enhancement_cap");

        int cost = Catalog.EnhancementCost(experience);
        if (cost > 0)
            deltas["gold"] = -cost;
        foreach (var delta in deltas)
            await PostMaterial(connection, transaction, playerId, request.RequestId!, delta.Key, delta.Value, token);
        foreach (string material in consumed)
            await DeleteArtifact(connection, transaction, playerId, material, token);

        await RunArtifactSql(connection, transaction, playerId, """
            UPDATE inventory_artifact SET experience = @experience, limit_breaks = @breaks
            WHERE player_id = @player AND realm_id = @realm AND instance_id = @id;
            """, token, ("@id", target.InstanceId), ("@experience", upgraded), ("@breaks", breaks));
    }

    private async Task SellArtifact(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        OwnedArtifact target, string requestId, CancellationToken token)
    {
        EnsureConsumable(target);
        var template = GetArtifactTemplate(target.TemplateId);
        if (template.SellGold > 0)
            await PostMaterial(connection, transaction, playerId, requestId, "gold", template.SellGold, token);
        if (template.SellPowder > 0)
            await PostMaterial(connection, transaction, playerId, requestId, "powder", template.SellPowder, token);
        await DeleteArtifact(connection, transaction, playerId, target.InstanceId, token);
    }

    private ArtifactCatalog Catalog => artifactCatalog ?? throw new EntryProblem(503, "artifact_catalog_unavailable");

    private ArtifactTemplate GetArtifactTemplate(string id) =>
        Catalog.TryGet(id, out var known) ? known
            : throw new EntryProblem(409, "artifact_template_unavailable");

    private static void EnsureConsumable(OwnedArtifact item)
    {
        if (item.Locked) throw new EntryProblem(409, "artifact_locked");
        if (item.HeroInstanceId is not null) throw new EntryProblem(409, "artifact_equipped");
    }

    private static Task DeleteArtifact(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        string instance, CancellationToken token) => RunArtifactSql(connection, transaction, playerId, """
            DELETE FROM inventory_artifact WHERE player_id = @player AND realm_id = @realm AND instance_id = @id;
            """, token, ("@id", instance));

    internal static async Task<Dictionary<string, int>> ReadMaterialBalances(MySqlConnection connection,
        MySqlTransaction transaction, string playerId, CancellationToken token)
    {
        var balances = new Dictionary<string, int>(StringComparer.Ordinal);
        using var query = Command(connection, transaction, """
            SELECT material_id, balance FROM inventory_artifact_material
            WHERE player_id = @player AND realm_id = @realm;
            """, playerId);
        await using var result = await query.ExecuteReaderAsync(token);
        while (await result.ReadAsync(token))
            balances.Add(result.GetString(0), result.GetInt32(1));
        return balances;
    }

    internal static async Task PostMaterial(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        string request, string material, int delta, CancellationToken token)
    {
        if (delta > 0)
        {
            await RunArtifactSql(connection, transaction, playerId, """
                INSERT INTO inventory_artifact_material (player_id, realm_id, material_id, balance)
                VALUES (@player, @realm, @material, @delta) AS credit
                ON DUPLICATE KEY UPDATE balance = inventory_artifact_material.balance + credit.balance;
                """, token, ("@material", material), ("@delta", delta));
        }
        else
        {
            using var debit = Command(connection, transaction, """
                UPDATE inventory_artifact_material SET balance = balance + @delta
                WHERE player_id = @player AND realm_id = @realm AND material_id = @material AND balance >= -@delta;
                """, playerId);
            debit.Parameters.AddWithValue("@material", material);
            debit.Parameters.AddWithValue("@delta", delta);
            if (await debit.ExecuteNonQueryAsync(token) != 1)
                throw new EntryProblem(409, "artifact_material_insufficient");
        }
        await RunArtifactSql(connection, transaction, playerId, """
            INSERT INTO inventory_artifact_material_entry (player_id, realm_id, request_id, material_id, delta)
            VALUES (@player, @realm, @request, @material, @delta);
            """, token, ("@request", request), ("@material", material), ("@delta", delta));
    }

    private static async Task RunArtifactSql(MySqlConnection connection, MySqlTransaction transaction, string playerId,
        string script, CancellationToken token, params (string Name, object Value)[] parameters)
    {
        using var statement = Command(connection, transaction, script, playerId);
        foreach (var (name, value) in parameters)
            statement.Parameters.AddWithValue(name, value);
        await statement.ExecuteNonQueryAsync(token);
    }

    private static string CanonicalId(string? value) => Guid.TryParseExact(value, "N", out var id)
        ? id.ToString("N")
        : throw new EntryProblem(400, "invalid_inventory_id");

    private static void Validate(string action, ArtifactMutation request)
    {
        if (request.RequestId == DevelopmentGrantId)
            throw new EntryProblem(400, "invalid_inventory_id");
        bool valid = action switch
        {
            "equip" => request.HeroInstanceId is not null && request.Locked is null && request.Materials is null,
            "lock" => request.Locked is not null && request.HeroInstanceId is null && request.Materials is null,
            "unequip" or "sell" => request.HeroInstanceId is null && request.Locked is null && request.Materials is null,
            "enhance" => request.HeroInstanceId is null && request.Locked is null && request.Materials is { Length: > 0 and <= 20 }
                && request.Materials.All(material => material is not null && material.Kind is not null
                    && material.Quantity is >= 1 and <= 20
                    && (material.Kind == "artifact" ? material.InstanceId is not null && material.Quantity == 1
                        : material.InstanceId is null && material.Kind is ("bottle" or "lesserCharm" or "greaterCharm" or "epicCharm"))),
            _ => false
        };
        if (!valid) throw new EntryProblem(400, "invalid_artifact_operation");
    }
}
