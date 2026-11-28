using System.Data;
using AstralOath.Server.Entry;
using AstralOath.Shared.Battle;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed partial class InventoryStore
{
    internal async Task<IReadOnlyList<BattleOwnedHero>> ReadTrialFormation(string playerId,
        IReadOnlyList<string> requested, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation);
        await LockInventoryOwner(connection, transaction, playerId, cancellation);
        var result = await ReadLockedFormation(connection, transaction, playerId, requested, cancellation);
        await transaction.CommitAsync(cancellation);
        return result;
    }

    internal async Task<IReadOnlyList<BattleOwnedHero>> ReadLockedFormation(MySqlConnection connection,
        MySqlTransaction transaction, string playerId, IReadOnlyList<string> requested, CancellationToken cancellation)
    {
        if (requested is not { Count: >= 1 and <= 4 })
            throw new EntryProblem(400, "trial_formation_invalid");

        var ids = new string[requested.Count];
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < requested.Count; i++)
        {
            if (!Guid.TryParseExact(requested[i], "N", out Guid parsed))
                throw new EntryProblem(400, "trial_formation_invalid");
            ids[i] = parsed.ToString("N");
            if (!distinct.Add(ids[i]))
                throw new EntryProblem(400, "trial_formation_invalid");
        }

        if (artifactCatalog == null)
            throw new EntryProblem(503, "database_unavailable");

        await EnsureStarters(connection, transaction, playerId, cancellation);
        var gear = await ReadEquippedItems(connection, transaction, playerId, ids, cancellation);
        var byHero = gear.ToLookup(item => item.EquippedHeroId!, item =>
            new EquipmentPiece(item.SetId, new EquipmentStat(item.MainStat.Kind, item.MainStat.Value),
                item.Substats.Select(stat => new EquipmentStat(stat.Kind, stat.Value)).ToArray()), StringComparer.Ordinal);
        var selected = new Dictionary<string, BattleOwnedHero>(ids.Length, StringComparer.Ordinal);
        var parameters = ids.Select((_, index) => "@hero" + index).ToArray();
        using (var query = Command(connection, transaction, """
            SELECT h.instance_id, h.template_id, h.level, h.stars, a.template_id, a.experience, a.limit_breaks
            FROM inventory_hero h
            LEFT JOIN inventory_artifact_loadout l
                ON l.hero_id = h.instance_id AND l.player_id = h.player_id AND l.realm_id = h.realm_id
            LEFT JOIN inventory_artifact a
                ON a.instance_id = l.artifact_id AND a.player_id = h.player_id AND a.realm_id = h.realm_id
            WHERE h.player_id = @player AND h.realm_id = @realm AND h.instance_id IN (
            """ + string.Join(", ", parameters) + ");", playerId))
        {
            for (int i = 0; i < ids.Length; i++)
                query.Parameters.AddWithValue(parameters[i], ids[i]);

            await using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                ArtifactTemplate? artifact = null;
                int artifactLevel = 0;
                if (!reader.IsDBNull(4))
                {
                    if (!artifactCatalog.TryGet(reader.GetString(4), out artifact))
                        throw new EntryProblem(503, "trial_loadout_unavailable");
                    artifactLevel = artifactCatalog.LevelForExperience(reader.GetInt32(5), reader.GetInt32(6));
                }
                var heroId = reader.GetString(0);
                selected.Add(heroId, new BattleOwnedHero(reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
                    0, artifact, artifactLevel, byHero[heroId].ToArray()));
            }
        }

        if (selected.Count != ids.Length)
            throw new EntryProblem(404, "trial_hero_not_found");
        return ids.Select(id => selected[id]).ToArray();
    }
}
