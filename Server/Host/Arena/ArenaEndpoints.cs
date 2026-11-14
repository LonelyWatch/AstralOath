using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace AstralOath.Server.Arena;

internal static class ArenaEndpoints
{
    private const string SessionLimit = "arena-sessions";
    private const string JoinLimit = "arena-joins";
    private const string TelemetryLimit = "arena-telemetry";
    private static readonly HashSet<string> Rules = new(StringComparer.Ordinal)
        { "basic", "draft", "wild", "round", "no_duplicate_class" };

    public static IServiceCollection AddArena(this IServiceCollection services)
    {
        services.AddSingleton(provider => new ArenaStore(provider.GetRequiredService<IConfiguration>()
            .GetConnectionString("MySql")));
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(SessionLimit, context => FixedWindow(context, 30));
            options.AddPolicy(JoinLimit, context => FixedWindow(context, 50));
            options.AddPolicy(TelemetryLimit, context => FixedWindow(context, 120));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
                await context.HttpContext.Response.WriteAsJsonAsync(new { code = "rate_limited" }, token);
        });
        return services;
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });

    public static void MapArena(this WebApplication app)
    {
        var arena = app.MapGroup("/api/arena");
        arena.WithRequestTimeout(TimeSpan.FromSeconds(10));
        arena.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (ArenaProblem problem) { return Results.Json(new { code = problem.Code }, statusCode: problem.Status); }
            catch (MySqlException error)
            {
                // Provider details and input never appear in the public error or log.
                app.Logger.LogWarning("Arena database operation failed with MySQL error {ErrorNumber}.", error.Number);
                return Results.Json(new { code = "database_unavailable" }, statusCode: 503);
            }
        });
        arena.MapPost("/sessions", async (ArenaPlayerRequest request, ArenaStore store, CancellationToken cancellation) =>
        {
            string name = Normalize(request.DisplayName, 24, "invalid_player");
            string avatar = request.AvatarId ?? "c1001";
            if (avatar.Length != 5 || avatar[0] != 'c' || avatar.AsSpan(1).ContainsAnyExceptInRange('0', '9'))
                throw new ArenaProblem(400, "invalid_player");
            return Results.Ok(await store.CreateSession(name, avatar, cancellation));
        }).RequireRateLimiting(SessionLimit);

        var authenticated = arena.MapGroup("");
        authenticated.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var store = http.RequestServices.GetRequiredService<ArenaStore>();
            var player = await store.Authenticate(http.Request.Headers.Authorization.ToString(), http.RequestAborted);
            if (player is null) throw new ArenaProblem(401, "unauthorized");
            http.Items[typeof(ArenaPlayer)] = player;
            return await next(context);
        });
        authenticated.MapPost("/telemetry", async (ArenaTelemetryRequest request, HttpContext http, ArenaStore store,
            CancellationToken cancellation) =>
        {
            if (!Guid.TryParseExact(request.EventId, "N", out var eventId) || eventId == Guid.Empty)
                throw new ArenaProblem(400, "invalid_telemetry");
            string operation = request.Name switch
            {
                "directory_refresh" => "arena.directory.refresh",
                "directory_filter" => "arena.directory.filter",
                "directory_search" => "arena.directory.search",
                "create_dialog_open" => "arena.create_dialog.open",
                _ => throw new ArenaProblem(400, "invalid_telemetry")
            };
            await store.RecordTelemetry(Player(http), eventId.ToString("N"), operation, cancellation);
            return Results.NoContent();
        }).WithMetadata(new RequestSizeLimitAttribute(512)).RequireRateLimiting(TelemetryLimit);
        authenticated.MapGet("/rooms", async (string? visibility, string? sort, string? direction, string? query,
            ArenaStore store, CancellationToken cancellation) =>
        {
            visibility ??= "all";
            sort ??= "recent";
            direction ??= "desc";
            if (visibility is not ("all" or "public" or "private") || sort is not ("recent" or "players") ||
                direction is not ("asc" or "desc"))
                throw new ArenaProblem(400, "invalid_directory");
            query = string.IsNullOrWhiteSpace(query) ? "" : Normalize(query, 28, "invalid_directory");
            return Results.Ok(new { rooms = await store.Directory(visibility, sort, direction, query, cancellation) });
        });
        authenticated.MapPost("/rooms", async (ArenaCreateRoomRequest request, HttpContext http, ArenaStore store,
            CancellationToken cancellation) =>
        {
            string name = Normalize(request.Name, 28, "invalid_room");
            string? password = string.IsNullOrEmpty(request.Password) ? null : request.Password;
            if (password is not null && !IsPin(password) || request.Rule is null || !Rules.Contains(request.Rule))
                throw new ArenaProblem(400, "invalid_room");
            return Results.Ok(await store.CreateRoom(Player(http), name, password, request.AllowChat, request.Rule, cancellation));
        });
        authenticated.MapGet("/rooms/current", async (HttpContext http, ArenaStore store, CancellationToken cancellation) =>
        {
            var current = await store.CurrentRoom(Player(http), cancellation);
            return current is null ? Results.NoContent() : Results.Ok(current);
        });
        authenticated.MapGet("/rooms/{id}", async (string id, HttpContext http, ArenaStore store, CancellationToken cancellation) =>
            Results.Ok(await store.ReadRoom(RoomId(id), Player(http), cancellation)));
        authenticated.MapPost("/rooms/{id}/join", async (string id, ArenaJoinRequest? request, HttpContext http, ArenaStore store,
            CancellationToken cancellation) =>
        {
            var password = request?.Password;
            if (!string.IsNullOrEmpty(password) && !IsPin(password)) throw new ArenaProblem(403, "incorrect_password");
            return Results.Ok(await store.JoinRoom(RoomId(id), Player(http), password, cancellation));
        }).RequireRateLimiting(JoinLimit);
        authenticated.MapPost("/rooms/{id}/leave", async (string id, HttpContext http, ArenaStore store, CancellationToken cancellation) =>
        {
            await store.LeaveRoom(RoomId(id), Player(http), cancellation);
            return Results.NoContent();
        });
        authenticated.MapGet("/rooms/{id}/messages", async (string id, long? after, HttpContext http, ArenaStore store,
            CancellationToken cancellation) =>
        {
            if (after < 0) throw new ArenaProblem(400, "invalid_message");
            return Results.Ok(new { messages = await store.ReadMessages(RoomId(id), Player(http), after ?? 0, cancellation) });
        });
        authenticated.MapPost("/rooms/{id}/messages", async (string id, ArenaChatRequest request, HttpContext http, ArenaStore store,
            CancellationToken cancellation) =>
            Results.Ok(await store.SendMessage(RoomId(id), Player(http), Normalize(request.Text, 240, "invalid_message"), cancellation)));
    }

    private static ArenaPlayer Player(HttpContext http) => (ArenaPlayer)http.Items[typeof(ArenaPlayer)]!;
    private static string RoomId(string id) => Guid.TryParseExact(id, "N", out var parsed)
        ? parsed.ToString("N") : throw new ArenaProblem(404, "room_not_found");
    private static bool IsPin(string value) => value.Length == 4 && !value.AsSpan().ContainsAnyExceptInRange('0', '9');

    private static string Normalize(string? value, int maximum, string code)
    {
        if (value is null || value.Length > maximum * 2) throw new ArenaProblem(400, code);
        var trimmed = value.Trim();
        int length = 0;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || ++length > maximum) throw new ArenaProblem(400, code);
        }
        return length == 0 ? throw new ArenaProblem(400, code) : trimmed;
    }
}

internal sealed class ArenaProblem(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal sealed record ArenaPlayerRequest(string? DisplayName, string? AvatarId);
internal sealed record ArenaCreateRoomRequest(string? Name, string? Password, bool AllowChat, string? Rule);
internal sealed record ArenaJoinRequest(string? Password);
internal sealed record ArenaChatRequest(string? Text);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ArenaTelemetryRequest(string? EventId, string? Name);
internal sealed record ArenaPlayer(string Id, string DisplayName, string AvatarId);
internal sealed record ArenaSession(string Token, ArenaPlayer Player);
internal sealed record ArenaRoom(string Id, string Name, ArenaPlayer Host, bool RequiresPassword, bool AllowChat,
    string Rule, int MemberCount, int Capacity, DateTime CreatedAt);
internal sealed record ArenaRoomState(ArenaRoom Room, IReadOnlyList<ArenaPlayer> Members);
internal sealed record ArenaMessage(long Id, ArenaPlayer Player, string Text, DateTime CreatedAt);
