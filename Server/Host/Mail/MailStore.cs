using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using AstralOath.Server.Summon;
using MySqlConnector;

namespace AstralOath.Server.Mail;
internal sealed partial class MailStore(string? connectionString, MailCatalog catalog)
{
    private readonly SummonStore summon = new(connectionString, SummonCatalog.Load(AstralOath.Server.Battle.ArtifactCatalogReader.Load()), false, TimeProvider.System);
    internal static string Id(string? id) => Guid.TryParseExact(id, "N", out var guid) && guid != Guid.Empty ? guid.ToString("N") : throw new EntryProblem(400, "invalid_mail_request");
    internal object SafeCatalog() => catalog.SafeCatalog();
    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new EntryProblem(503, "database_unavailable");
        var db = new MySqlConnection(connectionString);
        try
        {
            await db.OpenAsync(ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static MySqlCommand Sql(MySqlConnection db, MySqlTransaction? tx, string text, params (string Key, object? Value)[] values)
    {
        var command = new MySqlCommand(text, db, tx);
        foreach (var pair in values)
            command.Parameters.AddWithValue(pair.Key, pair.Value);
        return command;
    }

    private static MySqlCommand OwnerSql(MySqlConnection db, MySqlTransaction tx, string player, string text, params (string Key, object? Value)[] values) => Sql(db, tx, text, values.Concat([("@player", player), ("@realm", EntryEndpoints.RealmId)]).ToArray());
    private static async Task Execute(MySqlCommand command, CancellationToken ct)
    {
        using (command)
            await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<DateTime> Utc(MySqlConnection db, MySqlTransaction tx, CancellationToken ct)
    {
        using var q = Sql(db, tx, "SELECT UTC_TIMESTAMP(6);");
        return DateTime.SpecifyKind((DateTime)(await q.ExecuteScalarAsync(ct))!, DateTimeKind.Utc);
    }

    private static async Task Bump(MySqlConnection db, MySqlTransaction tx, string player, CancellationToken ct)
    {
        await Execute(OwnerSql(db, tx, player, "UPDATE mailbox SET version=version+1 WHERE realm_id=@realm AND player_id=@player; INSERT INTO mail_outbox(realm_id,player_id,version) SELECT realm_id,player_id,version FROM mailbox WHERE realm_id=@realm AND player_id=@player;"), ct);
    }

    private static async Task Lock(MySqlConnection db, MySqlTransaction tx, string player, CancellationToken ct)
    {
        await InventoryStore.LockInventoryOwner(db, tx, player, ct);
        await Execute(OwnerSql(db, tx, player, "INSERT INTO mailbox(realm_id,player_id) VALUES(@realm,@player) ON DUPLICATE KEY UPDATE player_id=@player;"), ct);
        using var q = OwnerSql(db, tx, player, "SELECT version FROM mailbox WHERE realm_id=@realm AND player_id=@player FOR UPDATE;");
        await q.ExecuteScalarAsync(ct);
    }

    private static async Task Expire(MySqlConnection db, MySqlTransaction tx, string player, DateTime now, CancellationToken ct)
    {
        using var update = OwnerSql(db, tx, player, "UPDATE mail_delivery SET outcome='expired',history_at=@now WHERE realm_id=@realm AND player_id=@player AND outcome='active' AND expires_at<=@now;", ("@now", now));
        if (await update.ExecuteNonQueryAsync(ct) > 0)
            await Bump(db, tx, player, ct);
    }

    private async Task<T> Transaction<T>(string player, Func<MySqlConnection, MySqlTransaction, DateTime, Task<T>> work, CancellationToken ct)
    {
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Lock(db, tx, player, ct);
        var now = await Utc(db, tx, ct);
        await Expire(db, tx, player, now, ct);
        await tx.SaveAsync("mail_action", ct);
        try
        {
            var result = await work(db, tx, now);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (EntryProblem)
        {
            // Expiry is authoritative even if the requested action is rejected. Roll back all action writes first.
            await tx.RollbackAsync("mail_action", ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }

    private async Task<MailSummary> Summary(MySqlConnection db, MySqlTransaction tx, string player, DateTime now, CancellationToken ct)
    {
        long version;
        int count;
        DateTime? expiry;
        using (var q = OwnerSql(db, tx, player, "SELECT version,(SELECT COUNT(*) FROM mail_delivery WHERE realm_id=@realm AND player_id=@player AND outcome='active'),(SELECT MIN(expires_at) FROM mail_delivery WHERE realm_id=@realm AND player_id=@player AND outcome='active') FROM mailbox WHERE realm_id=@realm AND player_id=@player;"))
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            version = r.GetInt64(0);
            count = r.GetInt32(1);
            expiry = r.IsDBNull(2) ? null : DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
        }

        // Every balance mutation appends to one of these persistent ledgers under this same owner lock.
        // The count is a monotonic resource revision; retaining the ledgers is part of the contract.
        using var resources = OwnerSql(db, tx, player, """
            SELECT
              (SELECT COUNT(*) FROM inventory_artifact_material_entry WHERE realm_id=@realm AND player_id=@player)
                + (SELECT COUNT(*) FROM summon_wallet_entry WHERE realm_id=@realm AND player_id=@player),
              COALESCE((SELECT balance FROM inventory_artifact_material WHERE realm_id=@realm AND player_id=@player AND material_id='energy'),0),
              COALESCE((SELECT balance FROM inventory_artifact_material WHERE realm_id=@realm AND player_id=@player AND material_id='gold'),0),
              COALESCE((SELECT skystones FROM summon_wallet_materials WHERE realm_id=@realm AND player_id=@player),0);
            """);
        await using var resourceRows = await resources.ExecuteReaderAsync(ct);
        await resourceRows.ReadAsync(ct);
        long resourceRevision = resourceRows.GetInt64(0);
        return new(EntryEndpoints.RealmId, player, version, count, 100, now, expiry,
            new(resourceRows.GetInt32(1), catalog.Energy.NaturalCap, catalog.Energy.AbsoluteCap, resourceRevision),
            new(resourceRevision, resourceRows.GetInt64(2), resourceRows.GetInt64(3)));
    }

    internal Task<MailSummary> ReadSummary(string player, CancellationToken ct) => Transaction(player, (db, tx, now) => Summary(db, tx, player, now, ct), ct);
    private const string SelectItem = "SELECT delivery_id,delivered_at,expires_at,read_at,history_at,outcome,frozen FROM mail_delivery ";
    private static MailItem Item(MySqlDataReader r)
    {
        var f = JsonSerializer.Deserialize<MailFrozen>(r.GetString(6), MailCatalog.Json) ?? throw new InvalidDataException("Invalid frozen mail.");
        DateTime Date(int i) => DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
        return new(r.GetString(0), f.LetterType, f.TemplateId, f.Title, f.Body, f.SenderId, f.SenderName, f.SenderIcon, Date(1), Date(2), r.IsDBNull(3) ? null : Date(3), r.IsDBNull(4) ? null : Date(4), r.GetString(5), f.BulkEligible, f.Attachments, f.Rewards, f.Presentation, f.Cover);
    }

    internal Task<MailPage> ReadPage(string player, bool history, string? cursor, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100)
            throw new EntryProblem(400, "invalid_mail_request");
        DateTime? cursorTime = null;
        string? cursorId = null;
        if (!string.IsNullOrEmpty(cursor))
            try
            {
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
                cursorTime = new DateTime(long.Parse(parts[0]), DateTimeKind.Utc);
                cursorId = Id(parts[1]);
            }
            catch
            {
                throw new EntryProblem(400, "invalid_mail_request");
            }

        return Transaction(player, async (db, tx, now) =>
        {
            var all = new List<MailItem>();
            string time = history ? "history_at" : "delivered_at";
            using (var q = OwnerSql(db, tx, player, SelectItem + "WHERE realm_id=@realm AND player_id=@player AND " + (history ? "outcome<>'active' AND history_at>=@cutoff" : "outcome='active'") + " ORDER BY " + time + " DESC,delivery_id DESC LIMIT 100;", ("@cutoff", now.AddDays(-90))))
            await using (var r = await q.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    all.Add(Item(r));
            }

            var remaining = all.Where(x => cursorTime == null || (history ? x.HistoryAt!.Value : x.DeliveredAt) < cursorTime || ((history ? x.HistoryAt!.Value : x.DeliveredAt) == cursorTime && string.CompareOrdinal(x.DeliveryId, cursorId) < 0)).ToArray();
            var items = remaining.Take(limit).ToArray();
            string? next = remaining.Length > limit ? Convert.ToBase64String(Encoding.UTF8.GetBytes((history ? items[^1].HistoryAt!.Value : items[^1].DeliveredAt).Ticks + "|" + items[^1].DeliveryId)) : null;
            return new MailPage(items, next, await Summary(db, tx, player, now, ct));
        }, ct);
    }

    private static async Task<MailItem?> ReadItem(MySqlConnection db, MySqlTransaction tx, string player, string id, CancellationToken ct)
    {
        using var q = OwnerSql(db, tx, player, SelectItem + "WHERE realm_id=@realm AND player_id=@player AND delivery_id=@id FOR UPDATE;", ("@id", id));
        await using var r = await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Item(r) : null;
    }

    internal Task<MailItem> Detail(string player, string id, CancellationToken ct) => Transaction(player, async (db, tx, now) =>
    {
        id = Id(id);
        var item = await ReadItem(db, tx, player, id, ct) ?? throw new EntryProblem(404, "mail_not_found");
        if (item.ReadAt == null)
        {
            await Execute(OwnerSql(db, tx, player, "UPDATE mail_delivery SET read_at=@now WHERE delivery_id=@id;", ("@id", id), ("@now", now)), ct);
            item = item with
            {
                ReadAt = now
            };
        }

        return item;
    }, ct);
    private static byte[] Digest(object value) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, MailCatalog.Json));
    private static async Task<T?> Receipt<T>(MySqlConnection db, MySqlTransaction tx, string player, string id, byte[] digest, CancellationToken ct)
        where T : class
    {
        using var q = OwnerSql(db, tx, player, "SELECT fingerprint,receipt FROM mail_operation WHERE realm_id=@realm AND player_id=@player AND request_id=@id;", ("@id", id));
        await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        if (!r.GetFieldValue<byte[]>(0).AsSpan().SequenceEqual(digest))
            throw new EntryProblem(409, "mail_request_conflict");
        return JsonSerializer.Deserialize<T>(r.GetString(1), MailCatalog.Json) ?? throw new InvalidDataException("Invalid mail receipt.");
    }

    private static Task SaveReceipt(MySqlConnection db, MySqlTransaction tx, string player, string id, byte[] digest, object result, CancellationToken ct) => Execute(OwnerSql(db, tx, player, "INSERT INTO mail_operation(realm_id,player_id,request_id,fingerprint,receipt) VALUES(@realm,@player,@id,@digest,@receipt);", ("@id", id), ("@digest", digest), ("@receipt", JsonSerializer.Serialize(result, MailCatalog.Json))), ct);
    internal Task<MailClaimReceipt> Claim(string player, MailClaimInput input, CancellationToken ct) => ClaimCore(player, Id(input.RequestId), [Id(input.DeliveryId)], false, ct);
    internal Task<MailClaimReceipt> ClaimAll(string player, MailClaimAllInput input, CancellationToken ct)
    {
        if (input.DeliveryIds is not { Length: > 0 and <= 100 })
            throw new EntryProblem(400, "invalid_mail_request");
        return ClaimCore(player, Id(input.RequestId), input.DeliveryIds.Select(Id).Distinct().Order().ToArray(), true, ct);
    }

    private Task<MailClaimReceipt> ClaimCore(string player, string request, string[] ids, bool bulk, CancellationToken ct) => Transaction(player, async (db, tx, now) =>
    {
        byte[] digest = Digest(new { action = bulk ? "claimAll" : "claim", ids });
        var prior = await Receipt<MailClaimReceipt>(db, tx, player, request, digest, ct);
        if (prior != null)
            return prior;
        var claimed = new List<string>();
        var skipped = new List<MailSkip>();
        var rewards = new List<MailReward>();
        string presentation = bulk ? "bundle" : "item", cover = "";
        foreach (string id in ids)
        {
            var item = await ReadItem(db, tx, player, id, ct);
            if (item == null)
                throw new EntryProblem(404, "mail_not_found");
            string? reason = item.Outcome == "claimed" ? "mail_already_claimed" : item.Outcome == "expired" ? "mail_expired" : bulk && !item.BulkEligible ? "gm_mail_requires_detail" : null;
            if (reason != null)
            {
                if (!bulk)
                    throw new EntryProblem(409, reason);
                skipped.Add(new(id, reason));
                continue;
            }

            await tx.SaveAsync("mail_reward", ct);
            try
            {
                await InventoryStore.GrantMailRewards(db, tx, player, item.Rewards, id, summon, ct);
            }
            catch (EntryProblem problem)when (bulk && problem.Code is "hero_inventory_full" or "artifact_inventory_full" or "equipment_inventory_full" or "resource_overflow")
            {
                await tx.RollbackAsync("mail_reward", ct);
                skipped.Add(new(id, problem.Code));
                continue;
            }

            await Execute(OwnerSql(db, tx, player, "UPDATE mail_delivery SET outcome='claimed',history_at=@now,claim_operation=@request WHERE delivery_id=@id;", ("@id", id), ("@request", request), ("@now", now)), ct);
            await OperationAudit.Record(db, tx, "mail.claim", "arena_player", player, "mail_delivery", id, null, ct);
            claimed.Add(id);
            rewards.AddRange(item.Rewards);
            if (!bulk)
            {
                presentation = item.Presentation;
                cover = item.Cover;
            }
        }

        if (claimed.Count == 0)
            throw new EntryProblem(409, "no_eligible_mail");
        await Bump(db, tx, player, ct);
        var merged = MailCatalog.Merge(rewards);
        var result = new MailClaimReceipt(request, claimed.ToArray(), skipped.ToArray(), merged, merged.Select(x => x.Kind).Distinct().ToArray(), await Summary(db, tx, player, now, ct), presentation, cover);
        await SaveReceipt(db, tx, player, request, digest, result, ct);
        return result;
    }, ct);
}
