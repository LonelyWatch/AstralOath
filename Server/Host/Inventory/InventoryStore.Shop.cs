using System.Data;
using System.Security.Cryptography;
using System.Text;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Shop;
using MySqlConnector;

namespace AstralOath.Server.Inventory;

internal sealed record ShopBuy(string RequestId, string OfferId);
internal sealed record ShopLine(string Id, string Name, string Material, int Quantity, int Price, string Period, int Limit, int Remaining, int Owned, string[]? Entries);
internal sealed record ShopPage(long Balance, List<ShopLine> Offers);

internal sealed partial class InventoryStore
{
    internal async Task<ShopPage> ArenaShop(string player, ShopBuy? buy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new EntryProblem(503,"database_unavailable");
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockInventoryOwner(db, tx, player, ct);
        if (!await PlayerNicknameRegistry.InRealm(db,player,ct,tx)) throw new EntryProblem(403,"invalid_realm");
        await EnsureStarters(db,tx,player,ct);
        var now = DateTimeOffset.UtcNow;
        if (buy != null)
        {
            var id = CanonicalId(buy.RequestId);
            using var previous = Command(db,tx,"SELECT offer_id FROM shop_exchange WHERE player_id=@player AND realm_id=@realm AND request_id=@id",player);
            previous.Parameters.AddWithValue("@id",id);
            var existing = await previous.ExecuteScalarAsync(ct);
            if (existing != null && (string)existing != buy.OfferId) throw new EntryProblem(409,"request_conflict");
            if (existing == null)
            {
                var offer = ShopCatalog.Arena.SingleOrDefault(x => x.Id == buy.OfferId && x.Price > 0)
                    ?? throw new EntryProblem(400,"invalid_shop_offer");
                var period = ShopCatalog.PeriodKey(offer.Period, now);
                if (offer.Limit > 0 && await Bought(offer.Id, period) >= offer.Limit) throw new EntryProblem(409,"shop_limit");
                using var debit = Command(db,tx,"UPDATE arena_tower_wallet SET balance=balance-@cost WHERE player_id=@player AND realm_id=@realm AND currency_id='conquest_point' AND balance>=@cost",player);
                debit.Parameters.AddWithValue("@cost",offer.Price);
                if (await debit.ExecuteNonQueryAsync(ct) != 1) throw new EntryProblem(409,"insufficient_conquest");
                var materials = await ReadMaterialBalances(db,tx,player,ct);
                if ((long)materials.GetValueOrDefault(offer.Material)+offer.Quantity > 1_000_000_000) throw new EntryProblem(409,"resource_overflow");
                string operation = Guid.NewGuid().ToString("N");
                await BeginOperation(db,tx,player,operation,SHA256.HashData(Encoding.UTF8.GetBytes("shop/arena/"+id)),ct);
                await PostMaterial(db,tx,player,operation,offer.Material,offer.Quantity,ct);
                using var record = Command(db,tx,"INSERT INTO shop_exchange (player_id,realm_id,request_id,shop_id,offer_id,period_key,price,material_id,quantity) VALUES (@player,@realm,@id,'arena',@offer,@period,@cost,@material,@quantity)",player);
                record.Parameters.AddWithValue("@id",id);
                record.Parameters.AddWithValue("@offer",offer.Id);
                record.Parameters.AddWithValue("@period",period);
                record.Parameters.AddWithValue("@cost",offer.Price);
                record.Parameters.AddWithValue("@material",offer.Material);
                record.Parameters.AddWithValue("@quantity",offer.Quantity);
                await record.ExecuteNonQueryAsync(ct);
                await OperationAudit.Record(db,tx,"shop.exchange","arena_player",player,"shop_offer",offer.Id,null,ct);
            }
        }
        using var wallet = Command(db,tx,"SELECT balance FROM arena_tower_wallet WHERE player_id=@player AND realm_id=@realm AND currency_id='conquest_point'",player);
        long balance = Convert.ToInt64(await wallet.ExecuteScalarAsync(ct));
        var owned = await ReadMaterialBalances(db,tx,player,ct);
        var offers = new List<ShopLine>();
        foreach (var row in ShopCatalog.Arena)
        {
            int used = row.Limit > 0 ? await Bought(row.Id,ShopCatalog.PeriodKey(row.Period,now)) : 0;
            offers.Add(new(row.Id,row.Name,row.Material,row.Quantity,row.Price,row.Period,row.Limit,Math.Max(0,row.Limit-used),owned.GetValueOrDefault(row.Material),row.Entries));
        }
        await tx.CommitAsync(ct);
        return new ShopPage(balance,offers);

        async Task<int> Bought(string offer, string period)
        {
            using var count = Command(db,tx,"SELECT COUNT(*) FROM shop_exchange WHERE player_id=@player AND realm_id=@realm AND shop_id='arena' AND offer_id=@offer AND period_key=@period",player);
            count.Parameters.AddWithValue("@offer",offer);
            count.Parameters.AddWithValue("@period",period);
            return Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
    }
}
