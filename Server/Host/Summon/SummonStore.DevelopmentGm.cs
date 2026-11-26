using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using MySqlConnector;

namespace AstralOath.Server.Summon;

internal sealed record DevelopmentGmAction(string? RequestId, string? Action, string? Key, [property: JsonRequired] int Value,
    GmForgeInput? Forge = null);
internal sealed record DevelopmentGmResource(string Key, int Value);
internal sealed record DevelopmentGmState(string PlayerId, string RealmId, DevelopmentGmResource[] Resources,
    string[] Heroes, string[] Artifacts, string[] Equipment, ForgeRules ForgeRules);

internal sealed partial class SummonStore
{
    private static readonly string[] GmMaterials = new[] { "gold", "lesserCharm", "greaterCharm", "epicCharm", "bottle", "powder", "energy" }
        .Concat(EquipmentEnhancementCatalog.Rules.Materials.Select(material => material.Id)).ToArray();
    private static readonly Dictionary<string, string> GmWalletColumns = new(StringComparer.Ordinal)
    {
        ["bookmarks"] = "bookmarks", ["epicBookmarks"] = "epic_bookmarks", ["goldTransmitStones"] = "gold_transmit_stones",
        ["galaxyBookmarks"] = "galaxy_bookmarks", ["mysticMedals"] = "mystic_medals", ["skystones"] = "skystones"
    };

    internal async Task<DevelopmentGmState> DevelopmentGm(string player, DevelopmentGmAction? input,
        InventoryStore inventory, CancellationToken ct)
    {
        if (!development) throw new EntryProblem(404, "not_found");
        string? request = null;
        EquipmentGrant? equipment = null;
        if (input != null)
        {
            request = ParsedId(input.RequestId);
            if (input.Action != "forgeEquipment" && input.Forge != null) throw new EntryProblem(400, "invalid_gm_forge");
            bool allowed = input.Action switch
            {
                "set" => input.Key != null && (GmWalletColumns.ContainsKey(input.Key) || GmMaterials.Contains(input.Key))
                    && input.Value is >= 0 and <= 1_000_000_000,
                "hero" => input.Key != null && catalog.HeroGrades.ContainsKey(input.Key) && input.Value is >= 1 and <= 10,
                "artifact" => input.Key != null && catalog.ArtifactGrades.ContainsKey(input.Key) && input.Value is >= 1 and <= 10,
                "equipment" => InventoryStore.DevelopmentEquipmentIds.Contains(input.Key) && input.Value is >= 1 and <= 10,
                "forgeEquipment" => input.Value is >= 1 and <= 10,
                _ => false
            };
            if (!allowed) throw new EntryProblem(400, "invalid_gm_action");
            equipment = input.Action switch
            {
                "equipment" => InventoryStore.GetTrainingGrant(input.Key!),
                "forgeEquipment" => DevelopmentEquipmentForge.Create(input.Key, input.Forge),
                _ => null
            };
            // Initialize the normal header and its configured starter grant before issuing additional equipment.
            if (equipment != null) await inventory.ReadEquipment(player, ct);
        }

        await using var connection = await Open(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await InventoryStore.LockInventoryOwner(connection, tx, player, ct);
        await InventoryStore.EnsureStarters(connection, tx, player, ct);
        await InventoryStore.GrantDevelopmentMaterialsOnce(connection, tx, player, ct);
        var wallet = await ReadWallet(connection, tx, player, ct);
        var materials = await InventoryStore.ReadMaterialBalances(connection, tx, player, ct);
        var resources = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bookmarks"] = wallet.Bookmarks, ["epicBookmarks"] = wallet.EpicBookmarks,
            ["goldTransmitStones"] = wallet.GoldTransmitStones, ["galaxyBookmarks"] = wallet.GalaxyBookmarks,
            ["mysticMedals"] = wallet.MysticMedals, ["skystones"] = wallet.Skystones
        };
        foreach (string key in GmMaterials) resources[key] = materials.GetValueOrDefault(key);
        if (input != null)
        {
            // Keep pre-forge request digests valid across a server upgrade.
            var fingerprint = input.Forge == null
                ? JsonSerializer.SerializeToUtf8Bytes(new { kind = "development.gm", input.Action, input.Key, input.Value })
                : JsonSerializer.SerializeToUtf8Bytes(new { kind = "development.gm", input.Action, input.Key, input.Value, input.Forge });
            var digest = SHA256.HashData(fingerprint);
            if (!await InventoryStore.BeginOperation(connection, tx, player, request!, digest, ct))
            {
                string key = input.Key!;
                if (input.Action == "set")
                {
                    int delta = input.Value - resources[key];
                    if (delta != 0)
                    {
                        if (GmWalletColumns.TryGetValue(key, out var column))
                            await SetGmCurrency(connection, tx, player, request!, column, input.Value, delta, ct);
                        else await InventoryStore.PostMaterial(connection, tx, player, request!, key, delta, ct);
                    }
                    resources[key] = input.Value;
                }
                else if (equipment != null)
                    await InventoryStore.GrantGmEquipment(connection, tx, player, equipment, input.Value, request!, ct);
                else
                {
                    var room = await ReadRoom(connection, tx, player, ct);
                    bool hero = input.Action == "hero";
                    if (input.Value > (hero ? room.Heroes : room.Artifacts)) throw new EntryProblem(409, "gm_inventory_full");
                    int stars = (hero ? catalog.HeroGrades : catalog.ArtifactGrades)[key];
                    await SaveCollection(connection, tx, player, new SummonEntry(input.Action!, key, stars, 0, 0m, false), ct);
                    for (int i = 0; i < input.Value; i++)
                        await Grant(connection, tx, player, input.Action!, key, stars, Guid.NewGuid().ToString("N"), ct, "development-gm");
                }
                await OperationAudit.Record(connection, tx, "development.gm." + input.Action, "arena_player", player,
                    "gm_operation", request, null, ct);
            }
        }
        await tx.CommitAsync(ct);
        return new DevelopmentGmState(player, EntryEndpoints.RealmId, resources.Select(pair => new DevelopmentGmResource(pair.Key, pair.Value)).ToArray(),
            catalog.HeroGrades.Keys.Order(StringComparer.Ordinal).ToArray(), catalog.ArtifactGrades.Keys.Order(StringComparer.Ordinal).ToArray(),
            InventoryStore.DevelopmentEquipmentIds, DevelopmentEquipmentForge.Rules);
    }

    private static async Task SetGmCurrency(MySqlConnection connection, MySqlTransaction tx, string player, string request,
        string column, int balance, int delta, CancellationToken ct) =>
        await SetCurrencyBalance(connection,tx,player,"gm-"+request,column,balance,delta,ct);

    private static async Task SetCurrencyBalance(MySqlConnection connection, MySqlTransaction tx, string player, string operation,
        string column, int balance, int delta, CancellationToken ct)
    {
        // Column names come exclusively from GmWalletColumns above. All account data is parameterized.
        string sql = column is "bookmarks" or "epic_bookmarks"
            ? $"UPDATE summon_wallet SET {column}=@balance WHERE player_id=@player AND realm_id=@realm"
            : $"INSERT INTO summon_wallet_materials (player_id,realm_id,{column}) VALUES (@player,@realm,@balance) ON DUPLICATE KEY UPDATE {column}=@balance";
        using var update = Command(connection, tx, player, sql, ("@balance", balance));
        await update.ExecuteNonQueryAsync(ct);
        await WalletLedger(connection, tx, player, operation, column, delta, balance, ct);
    }
}
