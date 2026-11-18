using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AstralOath.Shared.Battle;

namespace AstralOath.Server.Battle;

internal static class BattlePreviewEndpoints
{
    public static void MapBattlePreview(this WebApplication app)
    {
        var (catalog, hash) = ArenaBattleCatalog.ReadEmbeddedCatalog();
        var ai = new BattleAiPolicy(catalog);
        var rooms = new BattlePreviewRooms((id, seed) => BattlePreviewFactory.Create(id, seed, catalog, duel: true), hash, ai);
        // Validate the same authoritative roster and program during startup.
        _ = BattlePreviewFactory.Create("startup-validation", 0, catalog, duel: true);
        var routes = app.MapGroup("/api/battle-preview");
        routes.MapOwnedBattleTrial(rooms, catalog, app.Logger);
        routes.MapPost("/rooms", () => rooms.TryCreate(out var room)
            ? Results.Ok(room) : Results.StatusCode(StatusCodes.Status429TooManyRequests));
        routes.MapGet("/rooms/{id}", (string id, ulong? after, HttpRequest request) =>
        {
            if (!rooms.TryGet(id, Token(request), out var room, out _)) return Results.NotFound();
            return Results.Ok(room.Read(after ?? 0));
        });
        routes.MapPost("/rooms/{id}/commands", (string id, BattlePreviewCommand command, HttpRequest request) =>
        {
            if (!rooms.TryGet(id, Token(request), out var room, out var side)) return Results.NotFound();
            if (command.ContentHash != room.ContentHash || !command.IsValid()) return Results.BadRequest();
            var reply = room.Submit(side, command);
            if (reply == null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(reply);
        });
    }

    private static string? Token(HttpRequest request)
    {
        string header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length == 71 ? header[7..] : null;
    }
}

internal sealed record BattlePreviewCommand(
    uint RulesVersion, ulong Sequence, string CommandId, string ActorId, string SkillId, string? TargetId,
    bool UseSoulBurn, string ContentHash)
{
    public string? BattleId { get; init; }

    internal bool IsValid() =>
        CommandId is { Length: > 0 and <= 78 } && !string.IsNullOrWhiteSpace(CommandId) && IsKey(ActorId) && IsKey(SkillId) && (TargetId == null || IsKey(TargetId));

    private static bool IsKey(string? value) => value is { Length: > 0 and <= 80 } && !string.IsNullOrWhiteSpace(value);
}

internal sealed class BattlePreviewRooms
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private const int MaximumRooms = 32;
    private readonly Dictionary<string, BattlePreviewRoom> rooms = new(StringComparer.Ordinal);
    private readonly Func<string, ulong, BattleMatch> createMatch;
    private readonly TimeProvider clock;
    private readonly BattleAiPolicy ai;

    public BattlePreviewRooms(Func<string, ulong, BattleMatch> createMatch, string contentHash, BattleAiPolicy ai, TimeProvider? clock = null)
    {
        this.createMatch = createMatch;
        this.ai = ai;
        ContentHash = contentHash;
        this.clock = clock ?? TimeProvider.System;
    }
    public string ContentHash { get; }

    public bool TryCreate(out BattlePreviewInvitation? invitation) =>
        TryCreate(createMatch, automaticEnemy: false, out invitation);

    internal bool TryCreate(Func<string, ulong, BattleMatch> matchFactory, bool automaticEnemy,
        out BattlePreviewInvitation? invitation, string? matchContentHash = null)
    {
        lock (rooms)
        {
            var now = clock.GetUtcNow();
            foreach (var expired in rooms.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                rooms.Remove(expired);
            if (rooms.Count >= MaximumRooms)
            {
                invitation = null;
                return false;
            }
            string id = Guid.NewGuid().ToString("N");
            ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(sizeof(ulong)));
            string ally = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string enemy = automaticEnemy ? string.Empty : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var room = new BattlePreviewRoom(matchFactory(id, seed), ally, enemy, now.Add(Lifetime), matchContentHash ?? ContentHash, ai,
                automaticEnemy);
            rooms.Add(id, room);
            invitation = new BattlePreviewInvitation(id, ally, enemy, room.ExpiresAt, room.ContentHash, room.Read(0))
                { BattleContentHash = matchContentHash is null ? null : ContentHash };
            return true;
        }
    }

    public bool TryGet(string id, string? token, out BattlePreviewRoom room, out BattleSide side)
    {
        lock (rooms)
        {
            side = default;
            room = null!;
            if (token == null || !rooms.TryGetValue(id, out var candidate)) return false;
            if (candidate.ExpiresAt <= clock.GetUtcNow())
            {
                rooms.Remove(id);
                return false;
            }
            if (!candidate.TryAuthorize(token, out side)) return false;
            room = candidate;
            return true;
        }
    }
}

internal sealed class BattlePreviewRoom
{
    private const int MaximumRetainedEvents = 2048;
    private readonly object gate = new();
    private readonly BattleMatch match;
    private readonly byte[] allyToken;
    private readonly byte[] enemyToken;
    internal string ContentHash { get; }
    private readonly List<BattleFlowEvent> history;
    private readonly BattleAiPolicy ai;
    private readonly bool automaticEnemy;
    private readonly Dictionary<BattleSnapshot, BattlePreviewCommandReply> commandReplies = new();

    public BattlePreviewRoom(BattleMatch match, string ally, string enemy, DateTimeOffset expiresAt, string contentHash, BattleAiPolicy ai)
        : this(match, ally, enemy, expiresAt, contentHash, ai, automaticEnemy: false) { }

    internal BattlePreviewRoom(BattleMatch match, string ally, string enemy, DateTimeOffset expiresAt,
        string contentHash, BattleAiPolicy ai, bool automaticEnemy)
    {
        this.match = match;
        this.ai = ai;
        this.automaticEnemy = automaticEnemy;
        allyToken = System.Text.Encoding.ASCII.GetBytes(ally);
        enemyToken = System.Text.Encoding.ASCII.GetBytes(enemy);
        ContentHash = contentHash;
        ExpiresAt = expiresAt;
        history = match.OpeningEvents.ToList();
        if (automaticEnemy)
            ResolveAutomaticTurns(history);
    }
    public DateTimeOffset ExpiresAt { get; }

    public bool TryAuthorize(string token, out BattleSide side)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(token);
        bool ally = CryptographicOperations.FixedTimeEquals(bytes, allyToken);
        bool enemy = !automaticEnemy && CryptographicOperations.FixedTimeEquals(bytes, enemyToken);
        side = ally ? BattleSide.Ally : BattleSide.Enemy;
        return ally || enemy;
    }

    public BattlePreviewUpdate Read(ulong after)
    {
        lock (gate)
        {
            var snapshot = match.Snapshot;
            bool reset = after > snapshot.LastEventSequence || (history.Count > 0 && after < history[0].Sequence - 1);
            return new BattlePreviewUpdate(ContentHash, snapshot, reset, reset
                ? Array.Empty<BattleFlowEvent>() : history.Where(item => item.Sequence > after).ToArray());
        }
    }

    public BattlePreviewCommandReply? Submit(BattleSide side, BattlePreviewCommand command)
        => SubmitCore(side, command, $"{(int)side}:{command.CommandId}");

    // A separate entry point makes the identity namespace impossible to select in a client payload.
    public BattlePreviewCommandReply? SubmitTimeout(BattleSide side, BattlePreviewCommand command)
        => SubmitCore(side, command, $"server-timeout:{(int)side}:{command.Sequence}");

    private BattlePreviewCommandReply? SubmitCore(BattleSide side, BattlePreviewCommand command, string identity)
    {
        lock (gate)
        {
            var snapshot = match.Snapshot;
            var unit = snapshot.GetUnit(command.ActorId);
            if (unit == null || unit.Slot.Side != side) return null;
            // Scope idempotency keys to the issuing seat.
            var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, command.RulesVersion, command.Sequence,
                identity, command.ActorId, command.SkillId, command.TargetId!, command.UseSoulBurn));
            if (result.Status == BattleCommandStatus.Duplicate && commandReplies.TryGetValue(result.Snapshot, out var reply))
                return reply with { Status = BattleCommandStatus.Duplicate };
            if (result.Status != BattleCommandStatus.Accepted) return BattlePreviewCommandReply.From(result);

            var events = new List<BattleFlowEvent>(result.Events);
            ResolveAutomaticTurns(events);
            history.AddRange(events);
            if (history.Count > MaximumRetainedEvents)
                history.RemoveRange(0, history.Count - MaximumRetainedEvents);
            if (events.Count == result.Events.Count) return BattlePreviewCommandReply.From(result);
            var completed = new BattlePreviewCommandReply(BattleCommandStatus.Accepted, match.Snapshot, events.AsReadOnly());
            commandReplies.Add(result.Snapshot, completed);
            return completed;
        }
    }

    private void ResolveAutomaticTurns(List<BattleFlowEvent> events)
    {
        var snapshot = match.Snapshot;
        while (snapshot.Outcome == BattleOutcome.InProgress)
        {
            bool enemyTurn = automaticEnemy && snapshot.GetUnit(snapshot.CurrentActorId)?.Slot.Side == BattleSide.Enemy;
            if (!enemyTurn && !ai.IsForcedAction(snapshot)) break;
            var choice = ai.ChooseAction(snapshot);
            if (choice == null) break;
            ulong sequence = snapshot.LastCommandSequence + 1;
            string actorType = enemyTurn ? "trial-enemy" : "forced";
            var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence,
                $"{actorType}:{sequence}", snapshot.CurrentActorId, choice.SkillId, choice.TargetId));
            if (result.Status != BattleCommandStatus.Accepted)
                throw new InvalidOperationException($"Automatic battle action was rejected: {result.Status}.");
            events.AddRange(result.Events);
            snapshot = match.Snapshot;
        }
    }
}

internal sealed record BattlePreviewCommandReply(BattleCommandStatus Status, BattleSnapshot Snapshot,
    IReadOnlyList<BattleFlowEvent> Events)
{
    public BattleTurnClockView? Clock { get; init; }
    public static BattlePreviewCommandReply From(BattleCommandResult result) => new(result.Status, result.Snapshot, result.Events);
}

internal sealed record BattlePreviewUpdate(string ContentHash, BattleSnapshot Snapshot, bool SnapshotReset,
    IReadOnlyList<BattleFlowEvent> Events)
{
    public BattleTurnClockView? Clock { get; init; }
}
internal sealed record BattlePreviewInvitation(string RoomId, string AllyToken, string EnemyToken,
    DateTimeOffset ExpiresAt, string ContentHash, BattlePreviewUpdate Opening)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BattleContentHash { get; init; }
}
