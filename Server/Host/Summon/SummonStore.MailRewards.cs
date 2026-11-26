using AstralOath.Server.Entry;
using AstralOath.Server.Mail;
using MySqlConnector;

namespace AstralOath.Server.Summon;
internal sealed partial class SummonStore
{
    internal static string[] MailCurrencyKeys => GmWalletColumns.Keys.ToArray();
    internal static string[] MailMaterialKeys => GmMaterials.Where(x => x != "energy").ToArray();

    internal async Task CheckMailRewards(MySqlConnection connection, MySqlTransaction tx, string player, MailReward[] items, CancellationToken ct)
    {
        var room = await ReadRoom(connection, tx, player, ct);
        if (items.Where(x => x.Kind == "hero").Sum(x => x.Quantity) > room.Heroes)
            throw new EntryProblem(409, "hero_inventory_full");
        if (items.Where(x => x.Kind == "artifact").Sum(x => x.Quantity) > room.Artifacts)
            throw new EntryProblem(409, "artifact_inventory_full");
        // ReadWallet initializes the real shared wallet in this caller-owned transaction; mail uses development=false.
        var w = await ReadWallet(connection, tx, player, ct);
        var balances = new Dictionary<string, int>
        {
            {
                "bookmarks",
                w.Bookmarks
            },
            {
                "epicBookmarks",
                w.EpicBookmarks
            },
            {
                "skystones",
                w.Skystones
            },
            {
                "goldTransmitStones",
                w.GoldTransmitStones
            },
            {
                "galaxyBookmarks",
                w.GalaxyBookmarks
            },
            {
                "mysticMedals",
                w.MysticMedals
            }
        };
        foreach (var reward in items.Where(x => x.Kind == "currency"))
            if (!GmWalletColumns.ContainsKey(reward.Key) || balances[reward.Key] + (long)reward.Quantity > 1_000_000_000)
                throw new EntryProblem(409, "resource_overflow");
    }

    internal async Task PostMailRewards(MySqlConnection connection, MySqlTransaction tx, string player, MailReward[] items, string operation, string source, CancellationToken ct)
    {
        var w = await ReadWallet(connection, tx, player, ct);
        var balances = new Dictionary<string, int>
        {
            {
                "bookmarks",
                w.Bookmarks
            },
            {
                "epicBookmarks",
                w.EpicBookmarks
            },
            {
                "skystones",
                w.Skystones
            },
            {
                "goldTransmitStones",
                w.GoldTransmitStones
            },
            {
                "galaxyBookmarks",
                w.GalaxyBookmarks
            },
            {
                "mysticMedals",
                w.MysticMedals
            }
        };
        foreach (var item in items.Where(x => x.Kind == "currency"))
            await SetCurrencyBalance(connection, tx, player, source, GmWalletColumns[item.Key], checked(balances[item.Key] + item.Quantity), item.Quantity, ct);
        foreach (var item in items.Where(x => x.Kind is "hero" or "artifact"))
        {
            await SaveCollection(connection, tx, player, new SummonEntry(item.Kind, item.Key, item.Stars, 0, 0m, false), ct);
            for (int i = 0; i < item.Quantity; i++)
                await Grant(connection, tx, player, item.Kind, item.Key, item.Stars, Guid.NewGuid().ToString("N"), ct, source);
        }
    }
}
