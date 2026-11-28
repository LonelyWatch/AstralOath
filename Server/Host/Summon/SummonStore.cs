using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Server.Social;
using AstralOath.Server.Foundation;
using AstralOath.Server.Inventory;
using MySqlConnector;

namespace AstralOath.Server.Summon;

internal sealed partial class SummonStore(string? connectionString, SummonCatalog catalog, bool development,
    TimeProvider clock, ChannelChatEvents? chatEvents = null)
{
    private const string Covenant = "covenant";
    private const string Free = "free";
    private const string Bookmarks = "bookmarks";
    private const string EpicBookmarks = "epic_bookmarks";

    internal async Task<SummonState> Read(string player, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var connection = await Open(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await InventoryStore.LockInventoryOwner(connection, transaction, player, ct);
        var wallet = await ReadWallet(connection, transaction, player, ct);
        var state = await State(connection, transaction, player, wallet, now, ct);
        await transaction.CommitAsync(ct);
        return state;
    }

    internal async Task<SummonState> SaveWishlist(string player, SummonWishlist input, CancellationToken ct)
    {
        var wishlist = catalog.ValidateWishlist(input);
        var now = clock.GetUtcNow();
        await using var connection = await Open(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await InventoryStore.LockInventoryOwner(connection, transaction, player, ct);
        var wallet = await ReadWallet(connection, transaction, player, ct);
        using var update = Command(connection, transaction, player, """
            UPDATE summon_wallet SET wishlist = @wish WHERE player_id = @player AND realm_id = @realm;
            """, ("@wish", JsonSerializer.Serialize(wishlist, SummonCatalog.JsonOptions)));
        await update.ExecuteNonQueryAsync(ct);
        var state = await State(connection, transaction, player, wallet with { Wishlist = wishlist }, now, ct);
        await transaction.CommitAsync(ct);
        return state;
    }

    internal async Task<SummonReceipt> Draw(string player, SummonRequest input, CancellationToken ct)
    {
        string requestId = ParsedId(input.RequestId);
        if (input.Channel is < 1 or > 9999 || input.Count is not (1 or 10) || string.IsNullOrWhiteSpace(input.BannerId) || input.BannerId.Length > 48 ||
            input.Revision?.Length != 64 || input.Payment is not (Free or Bookmarks or EpicBookmarks))
            throw new EntryProblem(400, "invalid_summon_request");

        await using var connection = await Open(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await InventoryStore.LockInventoryOwner(connection, transaction, player, ct);
        var now = clock.GetUtcNow();
        var existing = await ReadReceipt(connection, transaction, player, requestId, ct);
        if (existing != null)
        {
            if (existing.BannerId != input.BannerId || existing.Revision != input.Revision ||
                existing.Count != input.Count || existing.Payment != input.Payment || existing.Channel != input.Channel)
                throw new EntryProblem(409, "summon_request_conflict");
            await transaction.CommitAsync(ct);
            return existing;
        }

        var banner = catalog.Active(now).SingleOrDefault(x => x.Id == input.BannerId)
            ?? throw new EntryProblem(409, "banner_unavailable");
        var wallet = await ReadWallet(connection, transaction, player, ct);
        var revision = catalog.Revision(banner, wallet.Wishlist);
        if (!string.Equals(revision, input.Revision, StringComparison.Ordinal))
            throw new EntryProblem(409, "banner_changed");

        DateOnly period = DateOnly.FromDateTime(catalog.LastReset(now).UtcDateTime);
        int cost = Pay(banner, input, wallet, period);
        var entries = catalog.Entries(banner, wallet.Wishlist);
        var room = await ReadRoom(connection, transaction, player, ct);
        bool hasHeroes = entries.Any(entry => entry.Kind == "hero");
        bool hasArtifacts = entries.Any(entry => entry.Kind == "artifact");
        int worstCaseRoom = Math.Min(hasHeroes ? room.Heroes : input.Count, hasArtifacts ? room.Artifacts : input.Count);
        if (Math.Min(room.Storage, input.Count) + Math.Min(worstCaseRoom, input.Count) < input.Count)
            throw new EntryProblem(409, "summon_storage_full");

        int misses = await ReadPity(connection, transaction, player, banner, ct);
        var drawn = new List<SummonResult>(input.Count);
        for (int position = 1; position <= input.Count; ++position)
        {
            var item = SummonCatalog.Select(entries, banner, ref misses, RandomNumberGenerator.GetInt32);
            string id = Guid.NewGuid().ToString("N");
            bool isNew = await RecordCollection(connection, transaction, player, item, ct);
            bool inStorage = item.Kind == "hero" ? room.Heroes == 0 : room.Artifacts == 0;
            if (!inStorage)
            {
                await Grant(connection, transaction, player, item.Kind, item.TemplateId, item.Stars, id, ct);
                room = item.Kind == "hero" ? room with { Heroes = room.Heroes - 1 }
                    : room with { Artifacts = room.Artifacts - 1 };
            }
            drawn.Add(new SummonResult(position, item.Kind, item.TemplateId, item.Stars, id, isNew, inStorage));
        }

        wallet = input.Payment switch
        {
            Free => wallet with { FreePeriod = period },
            EpicBookmarks => wallet with { EpicBookmarks = wallet.EpicBookmarks - cost },
            _ => wallet with { Bookmarks = wallet.Bookmarks - cost }
        };
        using (var update = Command(connection, transaction, player, """
            UPDATE summon_wallet SET bookmarks = @books, epic_bookmarks = @epic, free_period = @period
            WHERE player_id = @player AND realm_id = @realm;
            """, ("@books", wallet.Bookmarks), ("@epic", wallet.EpicBookmarks), ("@period", wallet.FreePeriod)))
            await update.ExecuteNonQueryAsync(ct);
        int balance = input.Payment == Free ? 0 : input.Payment == EpicBookmarks ? wallet.EpicBookmarks : wallet.Bookmarks;
        await WalletLedger(connection, transaction, player, requestId, input.Payment, -cost, balance, ct);

        if (banner.PityMisses > 0)
        {
            using var pity = Command(connection, transaction, player, """
                INSERT INTO summon_pity (player_id, realm_id, banner_id, misses) VALUES (@player, @realm, @banner, @misses)
                ON DUPLICATE KEY UPDATE misses = @misses;
                """, ("@banner", PityKey(banner)), ("@misses", misses));
            await pity.ExecuteNonQueryAsync(ct);
        }
        var receipt = new SummonReceipt(requestId, banner.Id, revision, input.Count, input.Payment!, cost, wallet.Bookmarks,
            wallet.EpicBookmarks, IsFree(wallet, period), misses, drawn.ToArray(), input.Channel);
        using (var save = Command(connection, transaction, player, """
            INSERT INTO summon_receipt (player_id, realm_id, request_id, receipt)
            VALUES (@player, @realm, @request, @receipt);
            """, ("@request", requestId), ("@receipt", JsonSerializer.Serialize(receipt, SummonCatalog.JsonOptions))))
            await save.ExecuteNonQueryAsync(ct);

        foreach (var result in drawn.Where(x => x.InStorage))
        {
            using var insert = Command(connection, transaction, player, """
                INSERT INTO summon_storage (instance_id, player_id, realm_id, request_id, draw_position, kind, template_id, stars)
                VALUES (@instance, @player, @realm, @request, @position, @kind, @template, @stars);
                """, ("@instance", result.InstanceId), ("@request", requestId), ("@position", result.Position),
                ("@kind", result.Kind), ("@template", result.TemplateId), ("@stars", result.Stars));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await OperationAudit.Record(connection, transaction, "summon.draw.committed", "arena_player", player,
            "summon_request", requestId, null, ct);
        var broadcasts = drawn.Where(result => result.Stars == 5).ToArray();
        if (broadcasts.Length != 0)
        {
            await ChannelChatStore.LockOrder(connection, transaction, input.Channel, ct);
            foreach (var reward in broadcasts)
            {
                using var announcement = Command(connection, transaction, player, """
                    INSERT INTO channel_chat_message(realm_id,channel_no,player_id,kind,template_id,created_at)
                    VALUES (@realm,@channel,@player,@kind,@template,UTC_TIMESTAMP(6))
                    """, ("@channel", input.Channel), ("@kind", reward.Kind == "hero" ? "hero5" : "artifact5"),
                    ("@template", reward.TemplateId));
                await announcement.ExecuteNonQueryAsync(ct);
            }
        }
        await transaction.CommitAsync(ct);
        if (broadcasts.Length != 0) chatEvents?.Publish(EntryEndpoints.RealmId, input.Channel);
        return receipt;
    }

    internal async Task<SummonReceipt> Recover(string player, string? requestId, CancellationToken ct)
    {
        await using var connection = await Open(ct);
        return await ReadReceipt(connection, null, player, requestId == null ? null : ParsedId(requestId), ct)
            ?? throw new EntryProblem(404, "summon_receipt_missing");
    }

    private async Task<SummonState> State(MySqlConnection connection, MySqlTransaction transaction, string player,
        Wallet wallet, DateTimeOffset now, CancellationToken ct)
    {
        var banners = new List<SummonBannerView>();
        foreach (var banner in catalog.Active(now))
        {
            banners.Add(new SummonBannerView(banner.Id, catalog.Revision(banner, wallet.Wishlist), banner.Title,
                banner.FeaturedHero, banner.FeaturedArtifact, await ReadPity(connection, transaction, player, banner, ct),
                banner.PityMisses, banner.BookmarksPerDraw, banner.DailyFree, banner.AllowsEpicBookmarks, banner.UsesWishlist,
                banner.StartUtc, banner.EndUtc, catalog.Entries(banner, wallet.Wishlist)));
        }
        var reset = catalog.LastReset(now);
        return new SummonState(now, IsFree(wallet, DateOnly.FromDateTime(reset.UtcDateTime)), reset.AddDays(1),
            wallet.Bookmarks, wallet.EpicBookmarks, wallet.GoldTransmitStones, wallet.GalaxyBookmarks,
            wallet.MysticMedals, wallet.Skystones, wallet.Wishlist.Heroes!, wallet.Wishlist.Artifacts!,
            catalog.WishHeroCandidates, catalog.WishArtifactCandidates,
            await ReadOwnedWishCandidates(connection, transaction, player, ct), banners.ToArray());
    }

    private static bool IsFree(Wallet wallet, DateOnly period) => wallet.FreePeriod == null || wallet.FreePeriod < period;

    private static int Pay(SummonBanner banner, SummonRequest request, Wallet wallet, DateOnly period)
    {
        if (request.Payment == Free)
        {
            if (banner.Id != Covenant || !banner.DailyFree || request.Count != 1)
                throw new EntryProblem(400, "invalid_payment");
            if (!IsFree(wallet, period)) throw new EntryProblem(409, "daily_summon_used");
            return 0;
        }
        if (request.Payment == EpicBookmarks && !banner.AllowsEpicBookmarks)
            throw new EntryProblem(400, "invalid_payment");
        int amount = checked(request.Count * banner.BookmarksPerDraw);
        if ((request.Payment == EpicBookmarks ? wallet.EpicBookmarks : wallet.Bookmarks) < amount)
            throw new EntryProblem(409, "insufficient_bookmarks");
        return amount;
    }

    private static async Task<SummonReceipt?> ReadReceipt(MySqlConnection connection, MySqlTransaction? transaction,
        string player, string? request, CancellationToken ct)
    {
        const string owner = "SELECT receipt FROM summon_receipt WHERE player_id = @player AND realm_id = @realm ";
        using var read = request == null
            ? Command(connection, transaction, player, owner + "ORDER BY issue_order DESC LIMIT 1;")
            : Command(connection, transaction, player, owner + "AND request_id = @request;", ("@request", request));
        return await read.ExecuteScalarAsync(ct) is string json
            ? JsonSerializer.Deserialize<SummonReceipt>(json, SummonCatalog.JsonOptions)
            : null;
    }

    private static async Task<int> ReadPity(MySqlConnection connection, MySqlTransaction transaction,
        string player, SummonBanner banner, CancellationToken ct)
    {
        if (banner.PityMisses == 0) return 0;
        using var read = Command(connection, transaction, player, """
            SELECT misses FROM summon_pity WHERE player_id = @player AND realm_id = @realm AND banner_id = @banner;
            """, ("@banner", PityKey(banner)));
        return Math.Min(banner.PityMisses, Convert.ToInt32(await read.ExecuteScalarAsync(ct)));
    }

    private static string PityKey(SummonBanner banner) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join('|', banner.Id, banner.FeaturedHero, banner.StartUtc?.ToUniversalTime().ToString("O")))))[..48];

    private async Task<Wallet> ReadWallet(MySqlConnection connection, MySqlTransaction transaction, string player,
        CancellationToken ct)
    {
        const string select = """
            SELECT bookmarks, epic_bookmarks, free_period, wishlist,
                   COALESCE(gold_transmit_stones, 0), COALESCE(galaxy_bookmarks, 0),
                   COALESCE(mystic_medals, 0), COALESCE(skystones, 0)
            FROM summon_wallet LEFT JOIN summon_wallet_materials USING (player_id, realm_id)
            WHERE player_id = @player AND realm_id = @realm;
            """;
        using (var current = Command(connection, transaction, player, select))
        {
            await using var reader = await current.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new Wallet(reader.GetInt32(0), reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                    catalog.RestoreWishlist(reader.IsDBNull(3) ? null :
                        JsonSerializer.Deserialize<SummonWishlist>(reader.GetString(3), SummonCatalog.JsonOptions)),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
        }

        int starter = 0;
        if (development)
        {
            using var account = Command(connection, transaction, player, "SELECT player_id FROM entry_account WHERE player_id = @player;");
            if (await account.ExecuteScalarAsync(ct) == null) starter = catalog.DevelopmentBooks;
        }
        using (var create = Command(connection, transaction, player, """
            INSERT INTO summon_wallet (player_id, realm_id, bookmarks) VALUES (@player, @realm, @books);
            """, ("@books", starter)))
            await create.ExecuteNonQueryAsync(ct);
        if (starter > 0)
            await WalletLedger(connection, transaction, player, "development-starter", Bookmarks, starter, starter, ct);
        return new Wallet(starter, 0, null, catalog.DefaultWishlist(), 0, 0, 0, 0);
    }

    private static async Task WalletLedger(MySqlConnection connection, MySqlTransaction transaction, string player,
        string operationId, string currency, int amount, int balance, CancellationToken ct)
    {
        using var entry = Command(connection, transaction, player, """
            INSERT INTO summon_wallet_entry (player_id, realm_id, operation_id, currency, amount, balance_after)
            VALUES (@player, @realm, @operation, @currency, @amount, @balance);
            """, ("@operation", operationId), ("@currency", currency), ("@amount", amount), ("@balance", balance));
        await entry.ExecuteNonQueryAsync(ct);
    }

    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new EntryProblem(503, "database_unavailable");
        var connection = new MySqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static string ParsedId(string? id) => Guid.TryParseExact(id, "N", out Guid value) && value != Guid.Empty
        ? value.ToString("N") : throw new EntryProblem(400, "invalid_request_id");

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string player,
        string sql, params (string Name, object? Value)[] args)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@player", player);
        command.Parameters.AddWithValue("@realm", EntryEndpoints.RealmId);
        foreach (var arg in args) command.Parameters.AddWithValue(arg.Name, arg.Value);
        return command;
    }

    private sealed record Wallet(int Bookmarks, int EpicBookmarks, DateOnly? FreePeriod, SummonWishlist Wishlist,
        int GoldTransmitStones, int GalaxyBookmarks, int MysticMedals, int Skystones);
}

internal sealed record SummonRequest(string? RequestId, string? BannerId, string? Revision, int Count, string? Payment, int Channel = 1);
internal sealed record SummonBannerView(string Id, string Revision, string Title, string? FeaturedHero, string? FeaturedArtifact,
    int Misses, int GuaranteeAfter, int BookmarksPerDraw, bool DailyFree, bool AllowsEpicBookmarks, bool UsesWishlist,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, SummonEntry[] Entries);
internal sealed record SummonState(DateTimeOffset ServerTimeUtc, bool FreeAvailable, DateTimeOffset FreeResetUtc,
    int Bookmarks, int EpicBookmarks, int GoldTransmitStones, int GalaxyBookmarks, int MysticMedals, int Skystones,
    string[] WishHeroes, string[] WishArtifacts, string[] WishHeroCandidates,
    string[] WishArtifactCandidates, string[] OwnedWishIds, SummonBannerView[] Banners);
internal sealed record SummonReceipt(string RequestId, string BannerId, string Revision, int Count, string Payment,
    int Cost, int Bookmarks, int EpicBookmarks, bool FreeAvailable, int Misses, SummonResult[] Results, int Channel = 1);
internal sealed record SummonResult(int Position, string Kind, string TemplateId, int Stars, string InstanceId,
    bool IsNew, bool InStorage);
