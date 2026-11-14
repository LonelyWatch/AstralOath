namespace AstralOath.Server.Arena;

internal static class ArenaChatStickers
{
    private static readonly HashSet<string> Keys = ReadKeys();

    internal static bool Contains(string key) => Keys.Contains(key);

    private static HashSet<string> ReadKeys()
    {
        using var source = typeof(ArenaChatStickers).Assembly.GetManifestResourceStream("AstralOath.ArenaChatStickers")
            ?? throw new InvalidOperationException("The Arena sticker catalogue is missing.");
        using var reader = new StreamReader(source);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } key)
            if (key.Length is < 1 or > 48 || !key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') || !keys.Add(key))
                throw new InvalidOperationException("The Arena sticker catalogue contains an invalid or duplicate key.");
        if (keys.Count == 0) throw new InvalidOperationException("The Arena sticker catalogue is empty.");
        return keys;
    }
}
