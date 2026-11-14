using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Shared.Battle;
using Microsoft.Extensions.Caching.Memory;

namespace AstralOath.Server.Battle;

internal sealed class ArenaBattleCatalog : IDisposable
{
    private readonly BattleCatalogData catalog;
    private readonly HashSet<string> heroes;
    private readonly BattleAiPolicy ai;
    private readonly MemoryCache battleReads = new(new MemoryCacheOptions { SizeLimit = 32 });
    public string ContentHash { get; }

    public ArenaBattleCatalog()
    {
        using var stream = typeof(ArenaBattleCatalog).Assembly.GetManifestResourceStream("AstralOath.BattleCatalog")
            ?? throw new InvalidOperationException("The generated battle catalog is missing from the server build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = reader.ReadToEnd().TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);
        catalog = JsonSerializer.Deserialize<BattleCatalogData>(data, new JsonSerializerOptions { IncludeFields = true })
            ?? throw new InvalidOperationException("The battle catalog could not be read.");
        heroes = new HashSet<string>(catalog.heroes.Select(row => row.id), StringComparer.Ordinal);
        EligibleHeroIds = heroes.Order(StringComparer.Ordinal).ToArray();
        ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
        ai = new BattleAiPolicy(catalog);
    }

    public string[] EligibleHeroIds { get; }
    public bool Contains(string? id) => id != null && heroes.Contains(id);
    public static ulong NewSeed() => BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    public BattlePreviewRoom? FindBattle(string id, ulong revision) =>
        battleReads.TryGetValue<BattleRead>(id, out var cached) && cached?.Revision == revision ? cached.Room : null;

    public void RememberBattle(string id, ulong revision, BattlePreviewRoom room) =>
        battleReads.Set(id, new BattleRead(revision, room), new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(10)
        });

    public void Dispose() => battleReads.Dispose();

    private sealed record BattleRead(ulong Revision, BattlePreviewRoom Room);

    public BattlePreviewRoom Start(string id, ulong seed, string[] hostFormation, string[] guestFormation)
    {
        var match = BattlePreviewFactory.Create(id, seed, catalog, hostFormation, guestFormation, duel: true);
        return new BattlePreviewRoom(match, "", "", DateTimeOffset.MaxValue, ContentHash, ai);
    }
}
