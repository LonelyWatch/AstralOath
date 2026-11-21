using Microsoft.AspNetCore.Http.Features;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal static class ArenaRoomEventStream
{
    private const int HeartbeatMs = 15_000;

    public static async Task<IResult> Handle(string id, HttpContext http, ArenaStore store, ArenaRoomEvents events,
        IHostApplicationLifetime lifetime, CancellationToken cancellation)
    {
        string room = ArenaEndpoints.RoomId(id);
        var player = ArenaEndpoints.Player(http);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.ApplicationStopping);
        var token = stopped.Token;
        // Subscribe first so a commit during membership validation is queued behind the initial sync.
        using var subscription = events.Subscribe(room, RoomChange.Room | RoomChange.Chat);
        await store.KeepRoomStreamAlive(room, player, token);
        var response = http.Response;
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "application/x-ndjson";
        response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await Send("sync");
            long due = Environment.TickCount64 + HeartbeatMs;
            while (!token.IsCancellationRequested)
            {
                int remaining = (int)Math.Max(0, due - Environment.TickCount64);
                if (remaining == 0)
                {
                    if (await store.Authenticate(http.Request.Headers.Authorization.ToString(), token) != player)
                        break;
                    await store.KeepRoomStreamAlive(room, player, token);
                    await Send("heartbeat");
                    due = Environment.TickCount64 + HeartbeatMs;
                    continue;
                }
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(token);
                tick.CancelAfter(remaining);
                RoomChange changes;
                try { changes = await subscription.ReadAsync(tick.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { continue; }
                if ((changes & RoomChange.Room) != 0)
                {
                    // A leaving client gets a final room invalidation and cannot retain an open subscription.
                    await Send("room");
                    await store.KeepRoomStreamAlive(room, player, token);
                }
                if ((changes & RoomChange.Chat) != 0) await Send("chat");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ArenaProblem error) when (error.Status is 403 or 404) { }
        catch (MySqlException error)
        {
            http.RequestServices.GetRequiredService<ILogger<ArenaStore>>()
                .LogWarning("Arena event stream database operation failed with MySQL error {ErrorNumber}.", error.Number);
        }
        catch (IOException) { /* A peer may close between a notification and its flush. */ }
        return Results.Empty;

        async Task Send(string kind)
        {
            await response.WriteAsync("{\"kind\":\"" + kind + "\"}\n", token);
            await response.Body.FlushAsync(token);
        }
    }
}
