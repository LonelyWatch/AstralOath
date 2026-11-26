using System.Threading.Channels;
using System.Text.Json;
using AstralOath.Server.Arena;
using AstralOath.Server.Entry;
using MySqlConnector;

namespace AstralOath.Server.Mail;
internal sealed class MailEventHub
{
    private readonly object gate = new();
    private readonly Dictionary<string, Dictionary<Guid, Channel<long>>> subscriptions = new();
    private readonly Dictionary<string, long> publishedVersions = new();

    internal string[] ActivePlayers()
    {
        lock (gate)
            return subscriptions.Keys.Order(StringComparer.Ordinal).ToArray();
    }
    internal (Guid Id, Channel<long> Channel) Subscribe(string player)
    {
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        lock (gate)
        {
            if (!subscriptions.TryGetValue(player, out var channels))
                subscriptions[player] = channels = new();
            channels[id] = channel;
        }

        return (id, channel);
    }

    internal void Unsubscribe(string player, Guid id)
    {
        lock (gate)
        {
            if (subscriptions.TryGetValue(player, out var channels))
            {
                channels.Remove(id);
                if (channels.Count == 0)
                {
                    subscriptions.Remove(player);
                    publishedVersions.Remove(player);
                }
            }
        }
    }

    internal void Publish(string player, long version)
    {
        lock (gate)
        {
            if (subscriptions.TryGetValue(player, out var channels)
                && (!publishedVersions.TryGetValue(player, out long previous) || version > previous))
            {
                publishedVersions[player] = version;
                foreach (var c in channels.Values)
                    c.Writer.TryWrite(version);
            }
        }
    }
}

internal static class MailEventStream
{
    internal static async Task Stream(HttpContext h, ArenaStore auth, MailStore store, MailEventHub hub, CancellationToken ct)
    {
        h.Response.Headers.CacheControl = "no-store";
        ArenaPlayer? player;
        try
        {
            player = await auth.Authenticate(h.Request.Headers.Authorization.ToString(), ct);
        }
        catch (EntryProblem p)
        {
            h.Response.StatusCode = p.Status;
            return;
        }
        catch (ArenaProblem p)
        {
            h.Response.StatusCode = p.Status;
            return;
        }
        catch (MySqlException)
        {
            h.Response.StatusCode = 503;
            return;
        }

        if (player == null)
        {
            h.Response.StatusCode = 401;
            return;
        }

        var sub = hub.Subscribe(player.Id);
        try
        {
            var summary = await store.ReadSummary(player.Id, ct);
            long version = summary.Version;
            h.Response.ContentType = "application/x-ndjson";
            if (!await Send("sync", version))
                return;
            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                string kind = "mail";
                long next;
                try
                {
                    next = await sub.Channel.Reader.ReadAsync(heartbeat.Token);
                    if (next <= version)
                        continue;
                }
                catch (OperationCanceledException)when (!ct.IsCancellationRequested)
                {
                    kind = "heartbeat";
                    next = (await store.ReadSummary(player.Id, ct)).Version;
                }

                version = Math.Max(version, next);
                if (!await Send(kind, version))
                    break;
            }
        }
        catch (OperationCanceledException)when (ct.IsCancellationRequested)
        {
        }
        catch (EntryProblem)
        {
        }
        catch (ArenaProblem)
        {
        }
        catch (MySqlException)
        {
        }
        finally
        {
            hub.Unsubscribe(player.Id, sub.Id);
        }

        async Task<bool> Send(string kind, long version)
        {
            var current = await auth.Authenticate(h.Request.Headers.Authorization.ToString(), ct);
            if (current?.Id != player.Id)
            {
                if (!h.Response.HasStarted)
                    h.Response.StatusCode = 401;
                return false;
            }

            await h.Response.WriteAsync(JsonSerializer.Serialize(new { kind, version }) + "\n", ct);
            await h.Response.Body.FlushAsync(ct);
            return true;
        }
    }
}

internal sealed class MailWorker(MailStore store, MailEventHub hub, ILogger<MailWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        long subscriberPosition = 0;
        int tick = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                subscriberPosition = await store.DispatchOutbox(subscriberPosition, hub, ct);
                if (tick++ % 5 == 0)
                    await store.ProcessPending(ct);
                if (tick % 15 == 0)
                    await store.ExpirePending(ct);
            }
            catch (EntryProblem)
            {
            }
            catch (MySqlException e)
            {
                log.LogWarning("Mail worker database error {Number}.", e.Number);
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

internal sealed partial class MailStore
{
    internal async Task<long> DispatchOutbox(long subscriberPosition, MailEventHub hub, CancellationToken ct)
    {
        // mailbox.version and the matching outbox row commit atomically under each owner's lock.
        // Global outbox AUTO_INCREMENT ids are allocation order, not commit order. Read the latest
        // committed owner versions instead, coalescing all durable events without a lossy ID cursor.
        // Only active subscriptions are inspected, in bounded fair batches; restart reads current
        // headers rather than replaying all historical outbox rows. Each Host has its own hub.
        string[] players = hub.ActivePlayers();
        if (players.Length == 0)
            return 0;
        int start = (int)(subscriberPosition % players.Length);
        string[] batch = Enumerable.Range(0, Math.Min(100, players.Length))
            .Select(offset => players[(start + offset) % players.Length]).ToArray();
        await using var db = await Open(ct);
        string[] names = Enumerable.Range(0, batch.Length).Select(i => "@p" + i).ToArray();
        using var q = Sql(db, null,
            "SELECT player_id,version FROM mailbox WHERE realm_id=@realm AND player_id IN (" + string.Join(',', names) + ");",
            ("@realm", EntryEndpoints.RealmId));
        for (int index = 0; index < batch.Length; index++)
            q.Parameters.AddWithValue(names[index], batch[index]);
        await using var r = await q.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            hub.Publish(r.GetString(0), r.GetInt64(1));

        return (start + batch.Length) % players.Length;
    }

    internal async Task ExpirePending(CancellationToken ct)
    {
        var players = new List<string>();
        await using (var db = await Open(ct))
        {
            using var q = Sql(db, null, "SELECT DISTINCT player_id FROM mail_delivery WHERE outcome='active' AND expires_at<=UTC_TIMESTAMP(6) LIMIT 100;");
            await using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                players.Add(r.GetString(0));
        }

        foreach (var player in players)
            await ReadSummary(player, ct);
    }
}
