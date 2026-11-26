using System.Security.Cryptography;
using System.Text;
using AstralOath.Server.Entry;
using AstralOath.Server.Mail;
using AstralOath.Server.Summon;
using MySqlConnector;

namespace AstralOath.Server.Inventory;
internal sealed partial class InventoryStore
{
    // Caller holds owner then mailbox; no connection, transaction, starter materials, or commit is opened here.
    internal static async Task GrantMailRewards(MySqlConnection connection, MySqlTransaction tx, string player, MailReward[] rewards, string deliveryId, SummonStore summon, CancellationToken ct)
    {
        await EnsureStarters(connection, tx, player, ct);
        var items = MailCatalog.Merge(rewards);
        await summon.CheckMailRewards(connection, tx, player, items, ct);
        var balances = await ReadMaterialBalances(connection, tx, player, ct);
        foreach (var item in items.Where(x => x.Kind is "material" or "energy"))
            if (balances.GetValueOrDefault(item.Key) + (long)item.Quantity > 1_000_000_000)
                throw new EntryProblem(409, "resource_overflow");
        var equipment = items.Where(x => x.Kind == "equipment").SelectMany(x => Enumerable.Repeat(x.Equipment ?? throw new InvalidDataException("Missing frozen equipment."), x.Quantity)).ToArray();
        int capacity = 0;
        if (equipment.Length > 0)
        {
            using (var create = Command(connection, tx, "INSERT INTO inventory_equipment_capacity(player_id,realm_id) VALUES(@player,@realm) ON DUPLICATE KEY UPDATE player_id=@player;", player))
                await create.ExecuteNonQueryAsync(ct);
            using var query = Command(connection, tx, "SELECT capacity-(SELECT COUNT(*) FROM inventory_equipment WHERE player_id=@player AND realm_id=@realm),capacity FROM inventory_equipment_capacity WHERE player_id=@player AND realm_id=@realm;", player);
            await using var reader = await query.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            if (reader.GetInt32(0) < equipment.Length)
                throw new EntryProblem(409, "equipment_inventory_full");
            capacity = reader.GetInt32(1);
        }

        string operation = Guid.NewGuid().ToString("N"), source = "mail-" + deliveryId;
        await BeginOperation(connection, tx, player, operation, SHA256.HashData(Encoding.UTF8.GetBytes(source)), ct);
        foreach (var item in items.Where(x => x.Kind is "material" or "energy"))
            await PostMaterial(connection, tx, player, operation, item.Key, item.Quantity, ct);
        await summon.PostMailRewards(connection, tx, player, items, operation, source, ct);
        if (equipment.Length > 0)
        {
            // One combined insertion gives every equipment piece a unique source_index across attachments.
            await InsertEquipment(connection, tx, player, capacity, equipment, source, ct);
            using var bump = Command(connection, tx, "UPDATE inventory_equipment_capacity SET revision=revision+1 WHERE player_id=@player AND realm_id=@realm;", player);
            await bump.ExecuteNonQueryAsync(ct);
        }
    }
}
