using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstralOath.Server.Entry;
using AstralOath.Shared.Battle;

[assembly: InternalsVisibleTo("AstralOath.Server.Tests")]

namespace AstralOath.Server.Summon;

internal sealed class SummonCatalog
{
    internal const int TotalWeight = 10_000_000;
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    private readonly SummonConfiguration config;
    private readonly SummonCandidate[] candidates;
    private readonly string revision;

    internal static SummonCatalog Load(ArtifactCatalog artifacts) =>
        new(ReadResource("AstralOath.SummonBanners"), ReadResource("AstralOath.BattleCatalog"), artifacts);

    internal SummonCatalog(string configuration, string heroes, ArtifactCatalog artifacts)
    {
        config = JsonSerializer.Deserialize<SummonConfiguration>(configuration, JsonOptions)
            ?? throw new InvalidDataException("Missing summon banner configuration.");
        if (config.SchemaVersion != 1 || config.WishlistSize != 7 || config.ResetHourUtc is < 0 or > 23 ||
            config.StorageCapacity < 10 || config.DevelopmentBooks < 0 || config.Banners.Length == 0 ||
            config.Heroes.Distinct(StringComparer.Ordinal).Count() != config.Heroes.Length ||
            config.Banners.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != config.Banners.Length)
            throw new InvalidDataException("Invalid summon banner configuration.");
        var heroCatalog = JsonSerializer.Deserialize<HeroSource>(heroes, JsonOptions)
            ?? throw new InvalidDataException("Missing hero catalog.");
        HeroGrades = heroCatalog.Heroes.ToDictionary(hero => hero.Id, hero => hero.BaseStars, StringComparer.Ordinal);
        ArtifactGrades = artifacts.Templates.ToDictionary(item => item.Id, item => item.Stars, StringComparer.Ordinal);
        var eligible = heroCatalog.Heroes.Where(hero => config.Heroes.Contains(hero.Id, StringComparer.Ordinal)).ToArray();
        if (eligible.Length != config.Heroes.Length)
            throw new InvalidDataException("An eligible summon hero is missing from the battle catalog.");
        candidates = eligible.Select(hero => new SummonCandidate("hero", hero.Id, hero.BaseStars, hero.Element is "light" or "dark"))
            .Concat(artifacts.Templates.Where(item => item.CanSummon)
                .Select(item => new SummonCandidate("artifact", item.Id, item.Stars, false)))
            .OrderBy(item => item.TemplateId, StringComparer.Ordinal).ToArray();
        WishHeroCandidates = candidates.Where(x => x.Kind == "hero" && x.Stars == 5 && !x.Moonlight)
            .Select(x => x.TemplateId).ToArray();
        WishArtifactCandidates = candidates.Where(x => x.Kind == "artifact" && x.Stars == 5)
            .Select(x => x.TemplateId).ToArray();
        if (WishHeroCandidates.Length < config.WishlistSize || WishArtifactCandidates.Length < config.WishlistSize)
            throw new InvalidDataException("Summon wishlist has too few eligible five-star candidates.");
        revision = Digest(configuration + "\n" + JsonSerializer.Serialize(candidates, JsonOptions));

        foreach (var banner in config.Banners)
        {
            if (string.IsNullOrWhiteSpace(banner.Id) || banner.BookmarksPerDraw <= 0 || banner.PityMisses < 0 ||
                banner.PityMisses > 0 && string.IsNullOrWhiteSpace(banner.FeaturedHero) ||
                banner.StartUtc is { } start && banner.EndUtc is { } end && start >= end)
                throw new InvalidDataException("Invalid summon banner: " + banner.Id);
            _ = Entries(banner, DefaultWishlist());
        }
    }

    internal int DevelopmentBooks => config.DevelopmentBooks;
    internal IReadOnlyDictionary<string, int> HeroGrades { get; }
    internal IReadOnlyDictionary<string, int> ArtifactGrades { get; }
    internal int StorageCapacity => config.StorageCapacity;
    internal string[] WishHeroCandidates { get; }
    internal string[] WishArtifactCandidates { get; }

    internal SummonBanner[] Active(DateTimeOffset now) => config.Banners
        .Where(x => (x.StartUtc == null || x.StartUtc <= now) && (x.EndUtc == null || now < x.EndUtc)).ToArray();

    internal DateTimeOffset LastReset(DateTimeOffset now)
    {
        var reset = new DateTimeOffset(now.UtcDateTime.Date.AddHours(config.ResetHourUtc), TimeSpan.Zero);
        return now < reset ? reset.AddDays(-1) : reset;
    }

    internal SummonWishlist DefaultWishlist() =>
        new(WishHeroCandidates.Take(config.WishlistSize).ToArray(),
            WishArtifactCandidates.Take(config.WishlistSize).ToArray());

    internal SummonWishlist ValidateWishlist(SummonWishlist input)
    {
        if (!Valid(input.Heroes, WishHeroCandidates) || !Valid(input.Artifacts, WishArtifactCandidates))
            throw new EntryProblem(400, "invalid_wishlist");
        return new(input.Heroes!.Order(StringComparer.Ordinal).ToArray(), input.Artifacts!.Order(StringComparer.Ordinal).ToArray());

        bool Valid(string[]? proposed, string[] choices) =>
            proposed?.Length == config.WishlistSize && proposed.Distinct(StringComparer.Ordinal).Count() == config.WishlistSize &&
            proposed.All(id => choices.Contains(id, StringComparer.Ordinal));
    }

    internal SummonWishlist RestoreWishlist(SummonWishlist? input)
    {
        if (input == null) return DefaultWishlist();
        // Preserve surviving picks when the pool changes, then fill vacated slots with eligible IDs.
        return new(KeepEligible(input.Heroes, WishHeroCandidates), KeepEligible(input.Artifacts, WishArtifactCandidates));

        string[] KeepEligible(string[]? prior, string[] choices) =>
            (prior ?? []).Where(id => choices.Contains(id, StringComparer.Ordinal)).Concat(choices)
                .Distinct(StringComparer.Ordinal).Take(config.WishlistSize).Order(StringComparer.Ordinal).ToArray();
    }

    internal string Revision(SummonBanner banner, SummonWishlist wishlist)
    {
        if (!banner.UsesWishlist) return revision;
        return Digest(revision + "\n" + string.Join(",", wishlist.Heroes!) + "\n" + string.Join(",", wishlist.Artifacts!));
    }

    internal SummonEntry[] Entries(SummonBanner banner, SummonWishlist wishes)
    {
        var entries = new List<SummonEntry>();
        foreach (var group in banner.Groups)
        {
            if (group.BasisPoints is <= 0 or > 10_000)
                throw new InvalidDataException("Invalid probability group in " + banner.Id);
            string? featured = group.Kind == "hero" ? banner.FeaturedHero : banner.FeaturedArtifact;
            var selected = candidates.Where(item => item.Kind == group.Kind && item.Stars == group.Stars && (
                group.Selection switch
                {
                    "all" => true,
                    "elemental" => !item.Moonlight,
                    "moonlight" => item.Moonlight,
                    "featured" => item.TemplateId == featured,
                    "other" => item.TemplateId != featured,
                    "wish" => (item.Kind == "hero" ? wishes.Heroes! : wishes.Artifacts!).Contains(item.TemplateId),
                    _ => throw new InvalidDataException("Unknown summon group selection: " + group.Selection)
                })).ToArray();

            int groupWeight = group.BasisPoints * (TotalWeight / 10_000);
            if (selected.Length == 0 || selected.Length > groupWeight)
                throw new InvalidDataException($"Banner {banner.Id} has no valid candidates in {group.Kind}/{group.Stars}/{group.Selection}.");
            for (int index = 0; index < selected.Length; index++)
            {
                var item = selected[index];
                int weight = groupWeight / selected.Length + (index < groupWeight % selected.Length ? 1 : 0);
                entries.Add(new SummonEntry(item.Kind, item.TemplateId, item.Stars, weight,
                    weight * 100m / TotalWeight, item.TemplateId == featured));
            }
        }
        if (entries.Sum(x => x.Weight) != TotalWeight ||
            entries.Count != entries.Select(x => x.TemplateId).Distinct(StringComparer.Ordinal).Count() ||
            banner.PityMisses > 0 && !entries.Any(x => x.Kind == "hero" && x.Featured))
            throw new InvalidDataException("Invalid total, duplicate candidate or pity target in summon banner " + banner.Id);
        return entries.ToArray();
    }

    internal static SummonEntry Select(SummonEntry[] entries, SummonBanner banner, ref int misses, Func<int, int> draw)
    {
        int remaining = banner.PityMisses > 0 && misses >= banner.PityMisses ? 0 : draw(TotalWeight);
        SummonEntry entry;
        if (banner.PityMisses > 0 && misses >= banner.PityMisses)
        {
            entry = entries.Single(x => x.Kind == "hero" && x.Featured);
        }
        else
        {
            if (remaining < 0 || remaining >= TotalWeight)
                throw new ArgumentOutOfRangeException(nameof(draw));
            entry = entries.First(item => (remaining -= item.Weight) < 0);
        }
        if (banner.PityMisses > 0)
            misses = entry.Kind == "hero" && entry.Featured ? 0 : misses + 1;
        return entry;
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Missing server resource " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record HeroSource(HeroSourceRow[] Heroes);
    private sealed record HeroSourceRow(string Id, string Element, int BaseStars);
}

internal sealed record SummonConfiguration(int SchemaVersion, int ResetHourUtc, int DevelopmentBooks, int StorageCapacity,
    int WishlistSize, string[] Heroes, SummonBanner[] Banners);
internal sealed record SummonBanner(string Id, string Title, string? FeaturedHero, string? FeaturedArtifact,
    int BookmarksPerDraw, bool DailyFree, bool AllowsEpicBookmarks, bool UsesWishlist, int PityMisses,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, SummonGroup[] Groups);
internal sealed record SummonGroup(string Kind, int Stars, string Selection, int BasisPoints);
internal sealed record SummonCandidate(string Kind, string TemplateId, int Stars, bool Moonlight);
internal sealed record SummonEntry(string Kind, string TemplateId, int Stars, int Weight, decimal Probability, bool Featured);
internal sealed record SummonWishlist(string[]? Heroes, string[]? Artifacts);
