using System.Data;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Foundation;
using MySqlConnector;

namespace AstralOath.Server.Mail;
internal sealed partial class MailStore
{
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static void Operator(string id)
    {
        if (id.Length is < 1 or > 64 || id.Any(x => !char.IsAsciiLetterOrDigit(x) && x != '-' && x != '_' && x != '.'))
            throw new EntryProblem(400, "invalid_mail_operator");
    }

    private static async Task SavePreview(MySqlConnection db, MySqlTransaction tx, string id, string actor, string scope, MailFrozen mail, string[] players, int duplicate, int excluded, DateTime now, CancellationToken ct)
    {
        await Execute(Sql(db, tx, "INSERT INTO mail_preview(preview_id,realm_id,operator_id,scope,frozen,recipient_count,duplicate_count,excluded_count,created_at,expires_at) VALUES(@id,@realm,@actor,@scope,@frozen,@count,@duplicate,@excluded,@now,@expiry);", ("@id", id), ("@realm", EntryEndpoints.RealmId), ("@actor", actor), ("@scope", scope), ("@frozen", JsonSerializer.Serialize(mail, MailCatalog.Json)), ("@count", players.Length), ("@duplicate", duplicate), ("@excluded", excluded), ("@now", now), ("@expiry", now.AddHours(1))), ct);
        foreach (var player in players)
            await Execute(Sql(db, tx, "INSERT INTO mail_preview_recipient(preview_id,player_id) VALUES(@id,@player);", ("@id", id), ("@player", player)), ct);
    }

    internal Task<MailSelfPreview> PreviewSelf(string player, MailDraft input, CancellationToken ct)
    {
        var frozen = catalog.Freeze(input);
        return Transaction(player, async (db, tx, now) =>
        {
            string id = NewId();
            await SavePreview(db, tx, id, player, "self", frozen, [player], 0, 0, now, ct);
            return new MailSelfPreview(id, EntryEndpoints.RealmId, player, 1, frozen.ExpiresInDays, frozen, frozen.Rewards, now.AddHours(1));
        }, ct);
    }

    internal Task<MailSendReceipt> SendSelf(string player, MailSendInput input, CancellationToken ct)
    {
        string request = Id(input.RequestId), preview = Id(input.PreviewId);
        byte[] digest = Digest(new { action = "sendSelf", preview });
        return Transaction(player, async (db, tx, now) =>
        {
            var prior = await Receipt<MailSendReceipt>(db, tx, player, request, digest, ct);
            if (prior != null)
                return prior;
            var p = await Preview(db, tx, preview, player, "self", ct);
            string? batch;
            DateTime expires;
            using (var q = Sql(db, tx, "SELECT batch_id,expires_at FROM mail_batch WHERE preview_id=@id;", ("@id", preview)))
            await using (var r = await q.ExecuteReaderAsync(ct))
            {
                bool exists = await r.ReadAsync(ct);
                batch = exists ? r.GetString(0) : null;
                expires = exists ? r.GetDateTime(1) : now.AddDays(p.Mail.ExpiresInDays);
            }

            string? delivered = null;
            if (batch != null)
            {
                using var q = Sql(db, tx, "SELECT delivery_id FROM mail_batch_target WHERE batch_id=@batch AND player_id=@player;", ("@batch", batch), ("@player", player));
                delivered = await q.ExecuteScalarAsync(ct) as string;
            }

            if (delivered == null)
            {
                if (p.Expiry <= now)
                    throw new EntryProblem(409, "mail_preview_expired");
                batch = NewId();
                await CreateBatch(db, tx, preview, batch, expires, now, ct);
                delivered = await Deliver(db, tx, player, batch, p.Mail, expires, now, ct);
            }

            var result = new MailSendReceipt(request, batch!, delivered, await Summary(db, tx, player, now, ct));
            await SaveReceipt(db, tx, player, request, digest, result, ct);
            await OperationAudit.Record(db, tx, "development.gm.mail.send", "arena_player", player, "mail_batch", batch, null, ct);
            return result;
        }, ct);
    }

    private static async Task<(MailFrozen Mail, DateTime Expiry, int Count, int Duplicate, int Excluded)> Preview(MySqlConnection db, MySqlTransaction tx, string id, string actor, string? scope, CancellationToken ct)
    {
        using var q = Sql(db, tx, "SELECT frozen,expires_at,recipient_count,duplicate_count,excluded_count FROM mail_preview WHERE preview_id=@id AND operator_id=@actor AND (@scope IS NULL OR scope=@scope) FOR UPDATE;", ("@id", id), ("@actor", actor), ("@scope", scope));
        await using var r = await q.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new EntryProblem(404, "mail_preview_not_found");
        return (JsonSerializer.Deserialize<MailFrozen>(r.GetString(0), MailCatalog.Json)!, DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
    }

    private static async Task CreateBatch(MySqlConnection db, MySqlTransaction tx, string preview, string batch, DateTime expires, DateTime now, CancellationToken ct)
    {
        await Execute(Sql(db, tx, "INSERT INTO mail_batch(batch_id,preview_id,expires_at,created_at) VALUES(@batch,@preview,@expiry,@now); INSERT INTO mail_batch_target(batch_id,player_id) SELECT @batch,player_id FROM mail_preview_recipient WHERE preview_id=@preview;", ("@batch", batch), ("@preview", preview), ("@expiry", expires), ("@now", now)), ct);
    }

    private static async Task<string> Deliver(MySqlConnection db, MySqlTransaction tx, string player, string batch, MailFrozen mail, DateTime expiry, DateTime now, CancellationToken ct)
    {
        if (expiry <= now)
            throw new EntryProblem(409, "mail_expired");
        using (var q = OwnerSql(db, tx, player, "SELECT COUNT(*) FROM mail_delivery WHERE realm_id=@realm AND player_id=@player AND outcome='active';"))
            if (Convert.ToInt32(await q.ExecuteScalarAsync(ct)) >= 100)
                throw new EntryProblem(409, "mailbox_full");
        string id = NewId();
        await Execute(OwnerSql(db, tx, player, "INSERT INTO mail_delivery(delivery_id,realm_id,player_id,batch_id,delivered_at,expires_at,frozen) VALUES(@id,@realm,@player,@batch,@now,@expiry,@frozen); UPDATE mail_batch_target SET outcome='delivered',error_code=NULL,delivery_id=@id WHERE batch_id=@batch AND player_id=@player;", ("@id", id), ("@batch", batch), ("@now", now), ("@expiry", expiry), ("@frozen", JsonSerializer.Serialize(mail, MailCatalog.Json))), ct);
        await Bump(db, tx, player, ct);
        await OperationAudit.Record(db, tx, "mail.delivery", "arena_player", player, "mail_delivery", id, null, ct);
        return id;
    }

    internal async Task<MailOperatorPreview> PreviewOperator(MailOperatorPreviewRequest input, CancellationToken ct)
    {
        Operator(input.OperatorId);
        if (input.RealmId != EntryEndpoints.RealmId || input.Scope is not ("ids" or "whitelist" or "allInRealm") || input.PlayerIds.Length > 100_000 || input.DenyIds.Length > 100_000 || (input.Scope == "allInRealm" ? input.PlayerIds.Length != 0 : input.PlayerIds.Length == 0))
            throw new EntryProblem(400, "invalid_mail_scope");
        var frozen = catalog.Freeze(input.Draft);
        var given = input.PlayerIds.Select(Id).ToArray();
        var deny = input.DenyIds.Select(Id).ToHashSet();
        await using var db = await Open(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var now = await Utc(db, tx, ct);
        var members = new HashSet<string>();
        using (var q = Sql(db, tx, "SELECT player_id FROM player_realm_membership WHERE realm_id=@realm AND registered=TRUE;", ("@realm", input.RealmId)))
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                members.Add(r.GetString(0));
        }

        if (given.Any(x => !members.Contains(x)) || deny.Any(x => !members.Contains(x)))
            throw new EntryProblem(400, "mail_unknown_recipient");
        var basis = input.Scope == "allInRealm" ? members : given.ToHashSet();
        int duplicate = given.Length - given.Distinct().Count(), excluded = basis.Count(deny.Contains);
        var players = basis.Where(x => !deny.Contains(x)).Order().ToArray();
        string id = NewId();
        if (players.Length > 100_000)
            throw new EntryProblem(400, "mail_recipient_limit");
        await SavePreview(db, tx, id, input.OperatorId, input.Scope, frozen, players, duplicate, excluded, now, ct);
        await tx.CommitAsync(ct);
        return new(id, input.RealmId, players.Length, duplicate, excluded, frozen.ExpiresInDays, now.AddHours(1), frozen, frozen.Rewards.Select(x => new MailRewardTotal(x.Kind, x.Key, (long)x.Quantity * players.Length, x.Name, x.Icon)).ToArray());
    }

    internal async Task<MailBatchStatus> ConfirmOperator(string previewId, string operatorId, CancellationToken ct)
    {
        Operator(operatorId);
        previewId = Id(previewId);
        string batch;
        await using (var db = await Open(ct))
        await using (var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            var p = await Preview(db, tx, previewId, operatorId, null, ct);
            var now = await Utc(db, tx, ct);
            using var q = Sql(db, tx, "SELECT batch_id FROM mail_batch WHERE preview_id=@id;", ("@id", previewId));
            batch = await q.ExecuteScalarAsync(ct) as string ?? "";
            if (batch.Length == 0)
            {
                if (p.Count == 0)
                    throw new EntryProblem(409, "mail_empty_recipients");
                if (p.Expiry <= now)
                    throw new EntryProblem(409, "mail_preview_expired");
                batch = NewId();
                await CreateBatch(db, tx, previewId, batch, now.AddDays(p.Mail.ExpiresInDays), now, ct);
                await OperationAudit.Record(db, tx, "mail.batch.confirm", "mail_operator", operatorId, "mail_batch", batch, null, ct);
            }

            await tx.CommitAsync(ct);
        }

        await ProcessBatch(batch, ct);
        return await BatchStatus(batch, ct);
    }

    internal async Task<MailBatchStatus> RetryBatch(string batchId, string operatorId, CancellationToken ct)
    {
        Operator(operatorId);
        batchId = Id(batchId);
        await using (var db = await Open(ct))
        await using (var tx = await db.BeginTransactionAsync(ct))
        {
            using var owner = Sql(db, tx, "SELECT p.operator_id FROM mail_batch b JOIN mail_preview p USING(preview_id) WHERE batch_id=@id;", ("@id", batchId));
            if (await owner.ExecuteScalarAsync(ct)is not string actor || actor != operatorId)
                throw new EntryProblem(404, "mail_batch_not_found");
            await Execute(Sql(db, tx, "UPDATE mail_batch_target SET outcome='pending',error_code=NULL WHERE batch_id=@id AND outcome='failed' AND error_code='mailbox_full';", ("@id", batchId)), ct);
            await OperationAudit.Record(db, tx, "mail.batch.retry", "mail_operator", operatorId, "mail_batch", batchId, null, ct);
            await tx.CommitAsync(ct);
        }

        await ProcessBatch(batchId, ct);
        return await BatchStatus(batchId, ct);
    }

    internal async Task ProcessPending(CancellationToken ct)
    {
        var batches = new List<string>();
        await using (var db = await Open(ct))
        {
            using var q = Sql(db, null, "SELECT DISTINCT batch_id FROM mail_batch_target WHERE outcome='pending' LIMIT 20;");
            await using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                batches.Add(r.GetString(0));
        }

        foreach (string batch in batches)
            await ProcessBatch(batch, ct);
    }

    private async Task ProcessBatch(string batch, CancellationToken ct)
    {
        MailFrozen frozen;
        DateTime expiry;
        var players = new List<string>();
        await using (var db = await Open(ct))
        {
            using (var q = Sql(db, null, "SELECT frozen,b.expires_at FROM mail_batch b JOIN mail_preview p USING(preview_id) WHERE batch_id=@batch;", ("@batch", batch)))
            await using (var r = await q.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct))
                    throw new EntryProblem(404, "mail_batch_not_found");
                frozen = JsonSerializer.Deserialize<MailFrozen>(r.GetString(0), MailCatalog.Json)!;
                expiry = r.GetDateTime(1);
            }

            using var targets = Sql(db, null, "SELECT player_id FROM mail_batch_target WHERE batch_id=@batch AND outcome='pending' LIMIT 500;", ("@batch", batch));
            await using var reader = await targets.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                players.Add(reader.GetString(0));
        }

        foreach (string player in players)
        {
            try
            {
                await Transaction(player, async (db, tx, now) =>
                {
                    using var q = Sql(db, tx, "SELECT outcome FROM mail_batch_target WHERE batch_id=@batch AND player_id=@player FOR UPDATE;", ("@batch", batch), ("@player", player));
                    if ((await q.ExecuteScalarAsync(ct) as string) != "pending")
                        return false;
                    try
                    {
                        await Deliver(db, tx, player, batch, frozen, expiry, now, ct);
                    }
                    catch (EntryProblem p)when (p.Code is "mailbox_full" or "mail_expired")
                    {
                        await Execute(Sql(db, tx, "UPDATE mail_batch_target SET outcome='failed',error_code=@error WHERE batch_id=@batch AND player_id=@player;", ("@error", p.Code), ("@batch", batch), ("@player", player)), ct);
                    }

                    return true;
                }, ct);
            }
            catch (EntryProblem problem)when (problem.Status == 401)
            {
                // Deleted recipients have no owner row to lock and must not block the remaining batch.
                await using var db = await Open(ct);
                await Execute(Sql(db, null, "UPDATE mail_batch_target SET outcome='failed',error_code='recipient_unavailable' WHERE batch_id=@batch AND player_id=@player AND outcome='pending' AND NOT EXISTS(SELECT 1 FROM arena_player WHERE id=@player);", ("@batch", batch), ("@player", player)), ct);
            }
        }
    }

    internal async Task<MailBatchStatus> BatchStatus(string batchId, CancellationToken ct)
    {
        batchId = Id(batchId);
        await using var db = await Open(ct);
        string preview;
        DateTime expiry;
        using (var q = Sql(db, null, "SELECT preview_id,expires_at FROM mail_batch WHERE batch_id=@id;", ("@id", batchId)))
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
                throw new EntryProblem(404, "mail_batch_not_found");
            preview = r.GetString(0);
            expiry = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
        }

        int delivered = 0, pending = 0, failed = 0;
        var failures = new List<MailBatchFailure>();
        using (var q = Sql(db, null, "SELECT outcome,error_code,COUNT(*) FROM mail_batch_target WHERE batch_id=@id GROUP BY outcome,error_code;", ("@id", batchId)))
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                string state = r.GetString(0);
                int count = r.GetInt32(2);
                if (state == "delivered")
                    delivered += count;
                else if (state == "pending")
                    pending += count;
                else
                {
                    failed += count;
                    failures.Add(new(r.IsDBNull(1) ? "unknown" : r.GetString(1), count));
                }
            }
        }

        using var claims = Sql(db, null, "SELECT COUNT(*) FROM mail_delivery WHERE batch_id=@id AND outcome='claimed';", ("@id", batchId));
        int claimed = Convert.ToInt32(await claims.ExecuteScalarAsync(ct));
        return new(batchId, preview, expiry, delivered + pending + failed, delivered, pending, failed, claimed, failures.ToArray());
    }
}
