using AstralOath.Server.Battle;

namespace AstralOath.Server.Arena;

internal sealed record ArenaDraftStartRequest(string? RequestId);
internal sealed record ArenaDraftActionRequest(string? RequestId, string? DraftId, int Version, string? Kind, string[]? HeroIds);

internal sealed record ArenaDraftSeatView(ArenaPlayer Player, string[] Prebans, string[] Picks,
    string? Ban, string[] Formation, bool Submitted, string? ProtectedHeroId);
internal sealed record ArenaDraftView(string Id, string RoomId, string Phase, int Version, int PrebanCount,
    string FirstPick, string? ActiveSide, string MySide, string[] EligibleHeroIds, ArenaDraftSeatView Host, ArenaDraftSeatView Guest,
    string? BattleId, string ContentHash, DateTimeOffset? DeadlineAt, bool BanProtection, int PicksRemainingInWindow);

internal sealed class ArenaDraft
{
    private static readonly bool[] Snake = [true, false, false, true, true, false, false, true, true, false];

    public required string Id { get; init; }
    public required string StartRequestId { get; init; }
    public required string RoomId { get; init; }
    public required string FirstPick { get; init; }
    public required int PrebanCount { get; init; }
    public required bool BanProtection { get; init; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public ArenaBattleTurnClock? BattleClock { get; set; }
    public required string ContentHash { get; init; }
    public uint BattleRulesVersion { get; init; }
    public required ArenaDraftSeat Host { get; init; }
    public required ArenaDraftSeat Guest { get; init; }
    public string Phase { get; set; } = "preban";
    public int Version { get; set; } = 1;
    public int PhaseVersion { get; set; } = 1;
    public ulong Seed { get; init; }
    public List<ArenaDraftReceipt> Receipts { get; init; } = [];
    public string? BattleId => Phase == "battle" ? Id : null;
    public string? ActiveSide => Phase == "pick" ? SideForPick() : null;
    public int PicksRemainingInWindow
    {
        get
        {
            if (Phase != "pick") return 0;
            int current = Host.Picks.Count + Guest.Picks.Count;
            return current + 1 < Snake.Length && Snake[current] == Snake[current + 1] ? 2 : 1;
        }
    }

    public string SideOf(ArenaPlayer player) => player.Id == Host.Player.Id ? "host" :
        player.Id == Guest.Player.Id ? "guest" : throw new ArenaProblem(403, "draft_not_seated");

    public bool Apply(ArenaPlayer player, ArenaDraftActionRequest body, ArenaBattleCatalog catalog, DateTimeOffset now)
    {
        string side = SideOf(player);
        string id = NormalizeRequestId(body.RequestId);
        if (body.DraftId != Id) throw new ArenaProblem(409, "draft_changed");
        if (body.Kind is not ("preban" or "pick" or "ban" or "formation") ||
            body.HeroIds is not { Length: <= 5 } heroes || heroes.Any(hero => !catalog.Contains(hero)) ||
            heroes.Distinct(StringComparer.Ordinal).Count() != heroes.Length)
            throw new ArenaProblem(400, "invalid_draft_action");
        var receipt = Receipts.Find(item => item.PlayerId == player.Id && item.RequestId == id);
        if (receipt is not null)
        {
            if (receipt.Kind != body.Kind || receipt.Version != body.Version || !receipt.HeroIds.SequenceEqual(heroes))
                throw new ArenaProblem(409, "draft_request_conflict");
            return false;
        }
        if (!string.Equals(catalog.ContentHash, ContentHash, StringComparison.Ordinal))
            throw new ArenaProblem(409, "battle_content_mismatch");
        if (Phase != body.Kind || body.Version < PhaseVersion || body.Version > Version)
            throw new ArenaProblem(409, "draft_version");
        var own = side == "host" ? Host : Guest;
        var opponent = side == "host" ? Guest : Host;
        if (own.Submitted)
            throw new ArenaProblem(409, "draft_already_submitted");
        switch (body.Kind)
        {
            case "preban":
                Require(heroes.Length == PrebanCount);
                own.Prebans = heroes;
                own.Submitted = true;
                break;
            case "pick":
                if (body.Version != Version || side != ActiveSide)
                    throw new ArenaProblem(409, "draft_turn");
                Require(heroes.Length == 1);
                if (!CanPick(heroes[0]))
                    throw new ArenaProblem(409, "draft_hero_unavailable");
                own.Picks.Add(heroes[0]);
                break;
            case "ban":
                Require(heroes.Length == 1 && CanBan(opponent, heroes[0]));
                own.Ban = heroes[0];
                own.Submitted = true;
                break;
            case "formation":
                Require(heroes.Length == 4 && heroes.All(hero => own.Picks.Contains(hero) && hero != opponent.Ban));
                own.Formation = heroes;
                own.Submitted = true;
                break;
        }
        Version++;
        Receipts.Add(new ArenaDraftReceipt(player.Id, id, body.Version, body.Kind, heroes));
        if (Phase == "pick")
        {
            int next = Host.Picks.Count + Guest.Picks.Count;
            if (next == Snake.Length) Advance("ban", now);
            else if (Snake[next - 1] != Snake[next]) ResetDeadline(now);
        }
        else if (Host.Submitted && Guest.Submitted)
            Advance(Phase switch { "preban" => "pick", "ban" => "formation", "formation" => "battle", _ => Phase }, now);
        return true;
    }

    public bool Cancel()
    {
        if (Phase == "cancelled") return false;
        Version++;
        Advance("cancelled", DateTimeOffset.UtcNow);
        return true;
    }

    public ArenaDraftView View(ArenaPlayer player, ArenaBattleCatalog catalog)
    {
        string side = SideOf(player);
        return new ArenaDraftView(Id, RoomId, Phase, Version, PrebanCount, FirstPick, ActiveSide, side,
            catalog.EligibleHeroIds, SeatView(Host, side == "host"), SeatView(Guest, side == "guest"),
            BattleId, ContentHash, DeadlineAt, BanProtection, PicksRemainingInWindow);
    }

    public static string NormalizeRequestId(string? id) => Guid.TryParseExact(id, "N", out var parsed) && parsed != Guid.Empty
        ? parsed.ToString("N") : throw new ArenaProblem(400, "invalid_draft_request");

    private ArenaDraftSeatView SeatView(ArenaDraftSeat seat, bool own)
    {
        bool revealPreban = Phase is "pick" or "ban" or "formation" or "battle";
        bool revealBan = Phase is "formation" or "battle";
        return new ArenaDraftSeatView(seat.Player, own || revealPreban ? seat.Prebans : [], seat.Picks.ToArray(),
            own || revealBan ? seat.Ban : null, own || Phase == "battle" ? seat.Formation : [], seat.Submitted, ProtectedHero(seat));
    }

    private string SideForPick()
    {
        bool firstPicks = Snake[Host.Picks.Count + Guest.Picks.Count];
        return firstPicks ? FirstPick : FirstPick == "host" ? "guest" : "host";
    }

    private void Advance(string phase, DateTimeOffset now)
    {
        Phase = phase;
        PhaseVersion = Version;
        Host.Submitted = Guest.Submitted = false;
        ResetDeadline(now);
    }

    public int AdvanceExpired(ArenaBattleCatalog catalog, DateTimeOffset now)
    {
        if (DeadlineAt is null || DeadlineAt > now) return 0;
        if (catalog.ContentHash != ContentHash) return Cancel() ? 1 : 0;
        var phase = Phase;
        int count = 0;
        while (Phase == phase && DeadlineAt <= now)
        {
            var seat = Phase == "pick"
                ? (ActiveSide == "host" ? Host : Guest)
                : (Host.Submitted ? Guest : Host);
            var opponent = seat == Host ? Guest : Host;
            var heroes = phase switch
            {
                "preban" => catalog.EligibleHeroIds.Except(opponent.Prebans).Take(PrebanCount).ToArray(),
                "pick" => [catalog.EligibleHeroIds.First(CanPick)],
                "ban" => [opponent.Picks.First(hero => CanBan(opponent, hero))],
                "formation" => seat.Picks.Where(hero => hero != opponent.Ban).ToArray(),
                _ => throw new InvalidOperationException("A terminal draft has a deadline.")
            };
            Apply(seat.Player, new ArenaDraftActionRequest(Guid.NewGuid().ToString("N"), Id, Version, phase, heroes), catalog, now);
            ++count;
        }
        return count;
    }

    public void ResetDeadline(DateTimeOffset now)
    {
        int picks = Host.Picks.Count + Guest.Picks.Count;
        DeadlineAt = Phase switch
        {
            "battle" or "cancelled" => null,
            "pick" when picks > 0 && picks < Snake.Length - 1 => now.AddSeconds(60),
            _ => now.AddSeconds(30)
        };
    }

    private string? ProtectedHero(ArenaDraftSeat seat) => BanProtection && seat.Picks.Count >= 3 ? seat.Picks[2] : null;

    private bool CanBan(ArenaDraftSeat opponent, string id) => opponent.Picks.Contains(id) && ProtectedHero(opponent) != id;

    private bool CanPick(string id) => !Host.Prebans.Contains(id) && !Guest.Prebans.Contains(id) &&
        !Host.Picks.Contains(id) && !Guest.Picks.Contains(id);

    private static void Require(bool valid)
    {
        if (!valid) throw new ArenaProblem(400, "invalid_draft_action");
    }
}

internal sealed class ArenaDraftSeat
{
    public required ArenaPlayer Player { get; init; }
    public string[] Prebans { get; set; } = [];
    public List<string> Picks { get; init; } = [];
    public string? Ban { get; set; }
    public string[] Formation { get; set; } = [];
    public bool Submitted { get; set; }
}

internal sealed record ArenaDraftReceipt(string PlayerId, string RequestId, int Version, string Kind, string[] HeroIds);
