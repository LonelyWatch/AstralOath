using System.Text;
using AstralOath.Shared.Updates;

internal static class CodeUpdateTests
{
    private const string ManifestJson = """
        {
          "formatVersion": 1,
          "baselineId": "player-a",
          "platform": "StandaloneWindows64",
          "architecture": "x64",
          "entryAssembly": "Game",
          "entryType": "Game.Entry",
          "assemblies": [
            {"name": "Game", "address": "code/hot/Game", "size": 3,
             "sha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
             "dependencies": ["Battle"]},
            {"name": "Battle", "address": "code/hot/Battle", "size": 3,
             "sha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
             "dependencies": []}
          ],
          "aotMetadata": [
            {"name": "mscorlib", "address": "code/aot/mscorlib", "size": 3,
             "sha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"}
          ]
        }
        """;

    public static void Run()
    {
        ManifestMatchesBuiltPlayer();
        ManifestRequiresKnownUniqueDependencies();
        ManifestVerifiesFileBytes();
        JournalRecoversOnlyOnNextBoot();
        JournalRejectsCorruptState();
        JournalRejectsChangedQuarantineVersion();
        JournalRejectsTruncatedQuarantine();
    }

    private static HotUpdateManifest Parse(string json = ManifestJson) =>
        HotUpdateManifest.Parse(Encoding.UTF8.GetBytes(json));

    private static void Validate(HotUpdateManifest manifest) =>
        manifest.Validate("player-a", "StandaloneWindows64", "x64");

    private static void ManifestMatchesBuiltPlayer()
    {
        var manifest = Parse();
        Validate(manifest);
        Expect<InvalidDataException>(() => manifest.Validate("player-b", "StandaloneWindows64", "x64"));
        Expect<InvalidDataException>(() => manifest.Validate("player-a", "Android", "ARM64"));
        Expect<InvalidDataException>(() => manifest.Validate("player-a", "StandaloneWindows64", "ARM64"));
        Expect<InvalidDataException>(() => Validate(Parse(ManifestJson.Replace("\"entryAssembly\": \"Game\"", "\"entryAssembly\": \"Missing\""))));
        Expect<InvalidDataException>(() => Parse("{"));
    }

    private static void ManifestRequiresKnownUniqueDependencies()
    {
        var manifest = Parse();
        Require(manifest.GetLoadOrder().Select(file => file.name).SequenceEqual(new[] { "Battle", "Game" }),
            "loads dependencies before their consumers");
        manifest.assemblies[1].dependencies = new[] { "Game" };
        Expect<InvalidDataException>(() => Validate(manifest));
        manifest.assemblies[1].dependencies = new[] { "Missing" };
        Expect<InvalidDataException>(() => Validate(manifest));
        manifest.assemblies[1].dependencies = Array.Empty<string>();
        manifest.assemblies[1].name = "game";
        manifest.assemblies[1].address = "code/hot/game";
        Expect<InvalidDataException>(() => Validate(manifest));
        manifest.assemblies[1].name = "Battle";
        manifest.assemblies[1].address = "code/hot/Game";
        Expect<InvalidDataException>(() => Validate(manifest));
        manifest.assemblies[1].address = "code/hot/Battle";
        manifest.aotMetadata[0].name = "Game";
        manifest.aotMetadata[0].address = "code/aot/Game";
        Expect<InvalidDataException>(() => Validate(manifest));
    }

    private static void ManifestVerifiesFileBytes()
    {
        var file = Parse().assemblies[0];
        HotUpdateManifest.VerifyFile(file, Encoding.ASCII.GetBytes("abc"));
        Expect<InvalidDataException>(() => HotUpdateManifest.VerifyFile(file, Encoding.ASCII.GetBytes("abd")));
        Expect<InvalidDataException>(() => HotUpdateManifest.VerifyFile(file, Encoding.ASCII.GetBytes("ab")));
        file.sha256 = "abc";
        Expect<InvalidDataException>(() => HotUpdateManifest.VerifyFile(file, Encoding.ASCII.GetBytes("abc")));
        file.size = 0;
        Expect<InvalidDataException>(() => HotUpdateManifest.VerifyFile(file, Array.Empty<byte>()));
    }

    private static void JournalRecoversOnlyOnNextBoot()
    {
        WithJournalPath(path =>
        {
            var journal = new CodeBootJournal(path);
            Require(journal.LastHealthyVersion == null, "new journal has no healthy release");
            journal.Begin(20);
            Expect<InvalidOperationException>(() => journal.Confirm(21));
            journal.Confirm(20);
            journal.Begin(21);
            Require(!journal.IsRejected(21), "pending release can start");
            Expect<InvalidOperationException>(() => journal.Begin(22));

            var restarted = new CodeBootJournal(path);
            Require(restarted.RecoverInterruptedBoot() == 21, "new boot isolated the interrupted release");
            Require(restarted.LastHealthyVersion == 20, "last confirmed release is available for recovery");
            Require(restarted.IsRejected(21), "failed version is quarantined");
            Expect<InvalidOperationException>(() => restarted.Begin(21));
            restarted.Begin(20);
            restarted.Confirm(20);

            var next = new CodeBootJournal(path);
            Require(next.RecoverInterruptedBoot() == null && next.IsRejected(21) && next.LastHealthyVersion == 20,
                "confirmation and quarantine both survive another restart");
        });
    }

    private static void JournalRejectsCorruptState()
    {
        WithJournalPath(path =>
        {
            var journal = new CodeBootJournal(path);
            journal.Begin(4);
            File.WriteAllText(path, "{");
            Expect<InvalidDataException>(() => new CodeBootJournal(path).RecoverInterruptedBoot());
            Require(File.ReadAllText(path) == "{", "corrupt state stayed intact");
            File.WriteAllText(path, "{}");
            Expect<InvalidDataException>(() => new CodeBootJournal(path).Begin(4));
        });
    }

    private static void JournalRejectsTruncatedQuarantine()
    {
        WithQuarantinedVersion(path =>
        {
            string truncated = string.Join("\n", File.ReadLines(path).Take(3));
            File.WriteAllText(path, truncated);
            Expect<InvalidDataException>(() => new CodeBootJournal(path).Begin(21));
            Require(File.ReadAllText(path) == truncated, "truncated journal stayed intact");
        });
    }

    private static void JournalRejectsChangedQuarantineVersion()
    {
        WithQuarantinedVersion(path =>
        {
            string[] lines = File.ReadAllLines(path);
            lines[3] = "22";
            string changed = string.Join("\n", lines);
            File.WriteAllText(path, changed);
            Expect<InvalidDataException>(() => new CodeBootJournal(path).IsRejected(21));
            Require(File.ReadAllText(path) == changed, "damaged journal stayed intact");
        });
    }

    private static void WithQuarantinedVersion(Action<string> action)
    {
        WithJournalPath(path =>
        {
            var journal = new CodeBootJournal(path);
            journal.Begin(20);
            journal.Confirm(20);
            journal.Begin(21);
            new CodeBootJournal(path).RecoverInterruptedBoot();
            action(path);
        });
    }

    private static void WithJournalPath(Action<string> action)
    {
        var directory = Directory.CreateTempSubdirectory();
        try { action(Path.Combine(directory.FullName, "journal.json")); }
        finally { directory.Delete(true); }
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private static void Require(bool condition, string scenario)
    {
        if (!condition)
            throw new Exception($"Failed: {scenario}");
    }
}
