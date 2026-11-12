using System.Diagnostics;
using System.Security.Cryptography;

sealed partial class Fixture
{
    public async Task RepairRestoresAfterTransientRollbackLock(bool lockCandidate)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows rollback sharing-lock regression skipped on this platform.");
            return;
        }

        long version = lockCandidate ? 16 : 15;
        string local = await PrepareCorruptRelease(version);
        string quarantine = Path.Combine(store, "releases", version + ".repair");
        using var cancellation = new CancellationTokenSource();
        FileStream held = null;
        Task unlock = Task.CompletedTask;
        try
        {
            await Expect<OperationCanceledException>(() => InstallWithCandidate(cancellation.Token, (candidate, token) =>
            {
                string lockedFile = lockCandidate ? Path.Combine(candidate.DirectoryPath, FilePath)
                    : Path.Combine(quarantine, "files", FilePath);
                held = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                cancellation.Cancel();
                unlock = Task.Run(async () =>
                {
                    await Task.Delay(250);
                    held.Dispose();
                });
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await unlock;
            held?.Dispose();
        }

        Require(File.ReadAllBytes(local).All(value => value == 0), "transient rollback lock restored the original payload");
        Require(!Directory.Exists(quarantine), "transient rollback lock restored the quarantine directory");
        Require(File.ReadAllBytes(Path.Combine(RepairStaging(), "files", FilePath)).SequenceEqual(Body),
            "transient rollback lock retained the validated candidate for retry");
        Console.WriteLine("Rollback restored after releasing " + (lockCandidate ? "candidate" : "quarantine") + " sharing lock");
    }

    public async Task RepairFailsBoundedlyUnderPersistentRollbackLock()
    {
        if (!OperatingSystem.IsWindows()) return;
        const long version = 17;
        string local = await PrepareCorruptRelease(version);
        string quarantine = Path.Combine(store, "releases", version + ".repair");
        using var cancellation = new CancellationTokenSource();
        FileStream held = null;
        var elapsed = new Stopwatch();
        try
        {
            await RequireRollbackFailure(() => InstallWithCandidate(cancellation.Token, (candidate, token) =>
            {
                held = new FileStream(Path.Combine(quarantine, "files", FilePath), FileMode.Open, FileAccess.Read, FileShare.Read);
                cancellation.Cancel();
                elapsed.Start();
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(10)));
            elapsed.Stop();
            Require(elapsed.Elapsed >= TimeSpan.FromMilliseconds(500), "persistent sharing lock received a recovery window");
            Require(!File.Exists(local), "failed recovery did not activate a replacement");
            Require(File.ReadAllBytes(Path.Combine(quarantine, "files", FilePath)).All(value => value == 0),
                "persistent sharing lock retained the original backup");
            Require(File.ReadAllBytes(Path.Combine(RepairStaging(), "files", FilePath)).SequenceEqual(Body),
                "persistent sharing lock retained the validated candidate");
        }
        finally { held?.Dispose(); }

        var repaired = await Install(Updater(schema: 2));
        Require(repaired.Version == version && File.ReadAllBytes(local).SequenceEqual(Body),
            "a later install recovered after the persistent lock was released");
        Console.WriteLine("Persistent rollback sharing lock failed with both errors in " + elapsed.ElapsedMilliseconds + " ms");
    }

    public async Task PermanentRollbackErrorKeepsBothFailures()
    {
        const long version = 18;
        string local = await PrepareCorruptRelease(version);
        string quarantine = Path.Combine(store, "releases", version + ".repair");
        string marker = Path.Combine(RepairStaging(), "keep.txt");
        using var cancellation = new CancellationTokenSource();
        await RequireRollbackFailure(() => InstallWithCandidate(cancellation.Token, (candidate, token) =>
        {
            Directory.CreateDirectory(RepairStaging());
            File.WriteAllText(marker, "keep");
            cancellation.Cancel();
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(10)));

        Require(File.ReadAllText(marker) == "keep", "rollback did not delete a conflicting destination");
        Require(File.ReadAllBytes(local).SequenceEqual(Body), "permanent rollback failure retained the candidate");
        Require(File.ReadAllBytes(Path.Combine(quarantine, "files", FilePath)).All(value => value == 0),
            "permanent rollback failure retained the original backup");
    }

    private async Task<string> PrepareCorruptRelease(long version)
    {
        SetRelease(version, schema: 2);
        var release = await Install(Updater(schema: 2));
        string local = Path.Combine(release.DirectoryPath, FilePath);
        File.WriteAllBytes(local, new byte[Body.Length]);
        return local;
    }

    private Task<AstralOath.Shared.Updates.ContentRelease> InstallWithCandidate(CancellationToken token,
        Func<AstralOath.Shared.Updates.ContentRelease, CancellationToken, Task> validate) =>
        Updater(schema: 2).InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"),
            new Uri(server.Urls.Single() + "/manifest.sig"), token, validate);

    private string RepairStaging() => Path.Combine(store, "staging", Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant());

    private static async Task RequireRollbackFailure(Func<Task> operation)
    {
        try { await operation(); }
        catch (AggregateException error)
        {
            Require(error.InnerExceptions.Count == 2 && error.InnerExceptions[0] is OperationCanceledException &&
                error.InnerExceptions[1] is IOException, "rollback failure retained the initiating cancellation and IO failure");
            return;
        }
        throw new Exception("Expected both the initiating cancellation and the rollback failure.");
    }
}