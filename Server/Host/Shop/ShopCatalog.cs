using System.Globalization;
using System.Text.Json;

namespace AstralOath.Server.Shop;

internal sealed record ShopOffer(string Id, string Name, string Material, int Quantity, int Price, string Period, int Limit, string[]? Entries);

internal static class ShopCatalog
{
    internal static IReadOnlyList<ShopOffer> Arena { get; } = Load();

    private static ShopOffer[] Load()
    {
        using var stream = typeof(ShopCatalog).Assembly.GetManifestResourceStream("AstralOath.ArenaShop")
            ?? throw new InvalidDataException("Missing arena shop catalog.");
        var rows = JsonSerializer.Deserialize<ShopOffer[]>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Missing shop offers.");
        if (rows.Length == 0 || rows.Select(x => x.Id).Distinct().Count() != rows.Length ||
            rows.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) ||
                x.Period is not ("directory" or "none" or "week" or "day") ||
                (x.Period == "directory" ? x.Price != 0 || x.Quantity != 0 || x.Entries is not { Length: 6 } || x.Entries.Any(string.IsNullOrWhiteSpace) :
                x.Price < 1 || x.Quantity < 1 || string.IsNullOrWhiteSpace(x.Material)) ||
                (x.Period is "week" or "day" ? x.Limit < 1 : x.Limit != 0)))
            throw new InvalidDataException("Invalid arena shop catalog.");
        return rows;
    }

    internal static string PeriodKey(string period, DateTimeOffset instant)
    {
        var date = instant.ToOffset(TimeSpan.FromHours(8)).Date;
        if (period == "week") date = date.AddDays(-((int)date.DayOfWeek + 6) % 7);
        return period is "day" or "week" ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "all";
    }
}
