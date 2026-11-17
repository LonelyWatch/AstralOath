using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AstralOath.Shared.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

await using var fixture = await Fixture.StartAsync();
await fixture.ValidInstallAndRestart();
await fixture.ReuseVerifiedPayloadAcrossReleases();
await fixture.ConsentPrecedesPayloadAndKeepsActiveRelease();
await fixture.BadSignatureAndTraversal();
await fixture.BadHashAndShortBody();
await fixture.ResumeAndFallback();
await fixture.NoSpaceAndCancellation();
await fixture.CompatibilityAndRollback();
await fixture.UnsignedFileRejected();
await fixture.CandidateRejectedBeforeActivation();
await fixture.RepairSameVersion();
await fixture.RejectReplacementWhenInstalledIdentityIsMissing();
await fixture.ActivateVerifiedHistoricalRelease();
await fixture.RepairRestoresAfterTransientRollbackLock(lockCandidate: false);
await fixture.RepairRestoresAfterTransientRollbackLock(lockCandidate: true);
await fixture.RepairFailsBoundedlyUnderPersistentRollbackLock();
await fixture.PermanentRollbackErrorKeepsBothFailures();
CodeUpdateTests.Run();
await fixture.StartupLeaseSerializesCodeBoots();
Console.WriteLine("All content and code update scenarios passed");

sealed partial class Fixture : IAsyncDisposable
{
    private const string FilePath = "yooasset/demo.bundle";
    private static readonly byte[] Body = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    private readonly string store = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly RSA key = RSA.Create(2048);
    private readonly HttpClient http = new();
    private WebApplication server;
    private byte[] manifest, signature, body = Body;
    private bool interrupt, ignoreRange, badRange, oversized;
    private int rangeRequests, contentRequests;
    private CancellationTokenSource cancelOnContent;

    public static async Task<Fixture> StartAsync()
    {
        var fixture = new Fixture();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        fixture.server = builder.Build();
        fixture.server.MapGet("/manifest.json", async context => await context.Response.Body.WriteAsync(fixture.manifest));
        fixture.server.MapGet("/manifest.sig", async context => await context.Response.Body.WriteAsync(fixture.signature));
        fixture.server.MapGet("/" + FilePath, fixture.SendContent);
        fixture.server.MapGet("/replacement.bytes", fixture.SendContent);
        await fixture.server.StartAsync();
        return fixture;
    }

    public async Task StartupLeaseSerializesCodeBoots()
    {
        string journalPath = Path.Combine(store, "concurrent-boot", "code-boot.state");
        var first = new CodeBootJournal(journalPath);
        var second = new CodeBootJournal(journalPath);
        using var testDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<IDisposable> waiting;
        using (await first.AcquireStartupLeaseAsync(testDeadline.Token))
        {
            Require(first.RecoverInterruptedBoot() == null, "new startup has no interrupted boot");
            first.Begin(20);
            waiting = second.AcquireStartupLeaseAsync(testDeadline.Token);
            Require(!waiting.IsCompleted, "the second client cannot inspect a running boot");

            using var cancelled = new CancellationTokenSource();
            Task<IDisposable> abandoning = second.AcquireStartupLeaseAsync(cancelled.Token);
            Require(!abandoning.IsCompleted, "the cancelled client initially waits for the startup lock");
            cancelled.Cancel();
            await Expect<OperationCanceledException>(() => abandoning);
            Require(!waiting.IsCompleted, "cancelling one waiter does not release another client's lock");

            first.Confirm(20);
        }

        using (await waiting)
        {
            Require(second.RecoverInterruptedBoot() == null && second.LastHealthyVersion == 20 && !second.IsRejected(20),
                "the second client observes the confirmed version without quarantining it");
            second.Begin(21);
            // Closing only the file handle simulates the OS releasing it after a process crash.
        }

        using (await first.AcquireStartupLeaseAsync(testDeadline.Token))
        {
            Require(first.RecoverInterruptedBoot() == 21 && first.IsRejected(21) && first.LastHealthyVersion == 20,
                "an unconfirmed boot is still isolated after the owning process exits");
        }
    }

    public async Task ValidInstallAndRestart()
    {
        SetRelease(1);
        var updater = Updater();
        Require(updater.GetActiveRelease() == null, "fresh store has no active release");
        var release = await Install(updater);
        Require(release.Version == 1 && File.ReadAllBytes(Path.Combine(release.DirectoryPath, FilePath)).SequenceEqual(Body), "release payload");
        Require(release.WasActivated, "the first signed install requires a fresh process");
        var restarted = Updater();
        Require(restarted.GetActiveRelease().Version == 1, "restart recovered active release");
        Require(!(await Install(restarted)).WasActivated, "already active content does not request another restart");
    }


    public async Task ConsentPrecedesPayloadAndKeepsActiveRelease()
    {
        const int version = 2;
        SetRelease(version, includeReplacement: true);
        int before = contentRequests;
        var updater = Updater();
        var progress = new List<ContentDownloadProgress>();
        bool prompted = false;
        var rejected = await updater.InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"),
            new Uri(server.Urls.Single() + "/manifest.sig"), default, approveDownload: (bytes, _) =>
            {
                prompted = true;
                Require(bytes == Body.Length && contentRequests == before, "confirmation precedes payload and reports size");
                return Task.FromResult(false);
            }, downloadProgress: progress.Add);
        Require(prompted && rejected == null && progress.Count == 0 && contentRequests == before
            && updater.GetActiveRelease().Version == 1, "decline keeps the previous release");
        Require(!Directory.Exists(Path.Combine(store, "releases", version.ToString())), "decline does not create a release");
        var bundled = await updater.InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"),
            new Uri(server.Urls.Single() + "/manifest.sig"), default, approveDownload: (_, _) =>
                throw new Exception("the package already contains this version"), bundledVersion: version);
        Require(bundled == null && contentRequests == before, "signed bundled release needs no downloads");
        var fixtureStore = Path.Combine(store, "consent");
        var isolated = new ContentUpdater(fixtureStore, http, key, 1, 1);
        var installed = await isolated.InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"),
            new Uri(server.Urls.Single() + "/manifest.sig"), default, approveDownload: (_, _) => Task.FromResult(true),
            downloadProgress: progress.Add);
        Require(installed.Version == version && progress.Last().Received == 2 * Body.Length &&
            progress.Last().Total == 2 * Body.Length, "cold install publishes the full download byte progress");
        await isolated.InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"),
            new Uri(server.Urls.Single() + "/manifest.sig"), default,
            approveDownload: (_, _) => throw new Exception("an installed version must not ask for download approval"));
        // Restore the fixture to the first release for subsequent failure scenarios.
        SetRelease(1);
    }
    public async Task ReuseVerifiedPayloadAcrossReleases()
    {
        var location = Path.Combine(store, "cross-release");
        var updater = new ContentUpdater(location, http, key, 1, 1);
        var index = new Uri(server.Urls.Single() + "/manifest.json");
        var sig = new Uri(server.Urls.Single() + "/manifest.sig");

        SetRelease(1);
        var first = await Install(updater);
        SetRelease(2, includeReplacement: true);
        int before = contentRequests;
        var progress = new List<ContentDownloadProgress>();
        var second = await updater.InstallAsync(index, sig, default, approveDownload: (bytes, _) =>
        {
            Require(bytes == Body.Length && contentRequests == before, "incremental approval counts only the missing file");
            return Task.FromResult(true);
        }, downloadProgress: progress.Add);
        Require(second.WasActivated && contentRequests == before + 1 && progress.Last().Received == Body.Length &&
            progress.Last().Total == Body.Length && File.ReadAllBytes(Path.Combine(second.DirectoryPath, FilePath)).SequenceEqual(Body),
            "incremental transfer reuses the signed unchanged bundle");

        string secondBundle = Path.Combine(second.DirectoryPath, FilePath);
        File.WriteAllBytes(secondBundle, new byte[Body.Length]);
        SetRelease(3);
        before = contentRequests;
        var third = await updater.InstallAsync(index, sig, default,
            approveDownload: (_, _) => throw new Exception("the verified previous release covers the damaged active bundle"));
        Require(third.WasActivated && contentRequests == before &&
            File.ReadAllBytes(Path.Combine(third.DirectoryPath, FilePath)).SequenceEqual(Body),
            "damaged active file is skipped while verified previous content can be reused");
        Require(File.ReadAllBytes(Path.Combine(first.DirectoryPath, FilePath)).SequenceEqual(Body),
            "reuse and corruption in a later release do not change the previous file");

        File.WriteAllBytes(Path.Combine(third.DirectoryPath, FilePath), new byte[Body.Length]);
        SetRelease(4);
        var fourth = await updater.InstallAsync(index, sig, default, approveDownload: (bytes, _) =>
        {
            Require(bytes == Body.Length, "corrupt local files still require a download");
            return Task.FromResult(true);
        });
        Require(contentRequests == before + 1 &&
            File.ReadAllBytes(Path.Combine(fourth.DirectoryPath, FilePath)).SequenceEqual(Body),
            "a damaged cache falls back to the signed network file");
        SetRelease(1);
    }

    public async Task BadSignatureAndTraversal()
    {
        SetRelease(2);
        signature[0] ^= 1;
        await Expect<InvalidDataException>(() => Install(Updater()));
        SetRelease(2, "../escape");
        await Expect<InvalidDataException>(() => Install(Updater()));
        Require(Updater().GetActiveRelease().Version == 1, "invalid manifest did not switch pointer");
    }

    public async Task BadHashAndShortBody()
    {
        SetRelease(2, "replacement.bytes");
        body = new byte[64];
        await Expect<InvalidDataException>(() => Install(Updater()));
        body = new byte[8];
        await Expect<InvalidDataException>(() => Install(Updater()));
        body = new byte[65];
        oversized = true;
        await Expect<InvalidDataException>(() => Install(Updater()));
        oversized = false;
        Require(Updater().GetActiveRelease().Version == 1, "bad payload did not switch pointer");
    }

    public async Task ResumeAndFallback()
    {
        for (long version = 3; version <= 4; version++)
        {
            SetRelease(version, content: Body.Select(value => (byte)(value + version)).ToArray());
            interrupt = true;
            await Expect<InvalidDataException>(() => Install(Updater()));
            interrupt = false;
            int priorRanges = rangeRequests;
            if (version == 3)
            {
                badRange = true;
                await Expect<InvalidDataException>(() => Install(Updater()));
                badRange = false;
                Require(Updater().GetActiveRelease().Version == 1, "invalid Content-Range did not activate");
                priorRanges++;
            }
            ignoreRange = version == 4;
            Require((await Install(Updater())).Version == version && rangeRequests == priorRanges + 1,
                "restart requested the persisted offset");
            ignoreRange = false;
        }
    }

    public async Task NoSpaceAndCancellation()
    {
        SetRelease(5, content: Body.Reverse().ToArray());
        await Expect<IOException>(() => Install(Updater(availableBytes: () => 0)));
        using var cancel = new CancellationTokenSource();
        cancelOnContent = cancel;
        await Expect<OperationCanceledException>(() => Install(Updater(), cancel.Token));
        cancelOnContent = null;
        Require(Updater().GetActiveRelease().Version == 4, "cancel kept old release");
        Require((await Install(Updater())).Version == 5, "restart recovered after cancellation");
    }

    public async Task CompatibilityAndRollback()
    {
        Require((await Updater().RollbackAsync(default)).Version == 4, "compatible rollback");
        SetRelease(6, schema: 2);
        await Expect<InvalidDataException>(() => Install(Updater()));
        Require((await Install(Updater(schema: 2))).Version == 6, "new client activates compatible schema");
        await Expect<InvalidDataException>(() => Updater(schema: 2).RollbackAsync(default));
        Require(Updater(schema: 2).GetActiveRelease().Version == 6, "incompatible rollback kept active");
        SetRelease(5, schema: 2);
        await Expect<InvalidDataException>(() => Install(Updater(schema: 2)));
    }

    public async Task UnsignedFileRejected()
    {
        SetRelease(7, schema: 2);
        var updater = Updater(schema: 2);
        var release = await Install(updater);
        File.WriteAllText(Path.Combine(release.DirectoryPath, "unsigned.txt"), "not in manifest");
        await Expect<InvalidDataException>(() => Task.FromResult(updater.GetActiveRelease()));
    }

    public async Task CandidateRejectedBeforeActivation()
    {
        // Version 7 is intentionally corrupt from the previous scenario. Activate a sound baseline first.
        SetRelease(8, schema: 2);
        var updater = Updater(schema: 2);
        await Install(updater);
        SetRelease(9, schema: 2);
        var manifestUri = new Uri(server.Urls.Single() + "/manifest.json");
        var signatureUri = new Uri(server.Urls.Single() + "/manifest.sig");
        await Expect<InvalidOperationException>(() => updater.InstallAsync(manifestUri, signatureUri, default, (candidate, token) =>
        {
            Require(candidate.Version == 9 && File.Exists(Path.Combine(candidate.DirectoryPath, FilePath)), "candidate available to consumer");
            throw new InvalidOperationException("The consumer rejected this candidate.");
        }));
        Require(updater.GetActiveRelease().Version == 8, "rejected candidate left the pointer intact");
        Require((await Install(updater)).Version == 9, "validated candidate can be activated on retry");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Expect<OperationCanceledException>(() => Task.FromResult(updater.GetActiveRelease(cancel.Token)));
    }

    public async Task RepairSameVersion()
    {
        SetRelease(10, schema: 2);
        var updater = Updater(schema: 2);
        var release = await Install(updater);
        string local = Path.Combine(release.DirectoryPath, FilePath);
        File.WriteAllBytes(local, new byte[Body.Length]);

        using var cancel = new CancellationTokenSource();
        cancelOnContent = cancel;
        try { await Expect<OperationCanceledException>(() => Install(Updater(schema: 2), cancel.Token)); }
        finally { cancelOnContent = null; }
        Require(File.ReadAllBytes(local).All(value => value == 0), "cancelled repair left the installed directory intact");

        SetRelease(10, "changed-identity.bytes", schema: 2);
        await Expect<InvalidDataException>(() => Install(Updater(schema: 2)));
        SetRelease(10, schema: 2);
        using var candidateCancellation = new CancellationTokenSource();
        var manifestUri = new Uri(server.Urls.Single() + "/manifest.json");
        var signatureUri = new Uri(server.Urls.Single() + "/manifest.sig");
        await Expect<OperationCanceledException>(() => Updater(schema: 2).InstallAsync(
            manifestUri, signatureUri, candidateCancellation.Token, (candidate, token) =>
            {
                Require(File.ReadAllBytes(Path.Combine(candidate.DirectoryPath, FilePath)).SequenceEqual(Body),
                    "candidate receives the repaired payload before activation");
                candidateCancellation.Cancel();
                return Task.CompletedTask;
            }));
        Require(File.ReadAllBytes(local).All(value => value == 0), "candidate cancellation restored quarantine");

        var repaired = await Install(Updater(schema: 2));
        Require(repaired.Version == 10 && repaired.WasActivated && File.ReadAllBytes(local).SequenceEqual(Body),
            "same signed version replaced corrupt payload and requires a fresh process");
        Require(Updater(schema: 2).GetActiveRelease().Version == 10, "repaired release survived restart");
        Require((await Updater(schema: 2).RollbackAsync(default)).Version == 9, "repair preserved the previous version");
    }

    public async Task RejectReplacementWhenInstalledIdentityIsMissing()
    {
        SetRelease(11, schema: 2);
        var updater = Updater(schema: 2);
        var release = await Install(updater);
        string directory = Path.GetDirectoryName(release.DirectoryPath);
        Directory.Delete(directory, recursive: true);

        SetRelease(11, "replacement.bytes", schema: 2);
        server.MapGet("/replacement.bytes", SendContent);
        await Expect<InvalidDataException>(() => Install(Updater(schema: 2)));
        Require(!Directory.Exists(directory), "missing version identity could not be replaced");
        Require((await Updater(schema: 2).RollbackAsync(default)).Version == 9, "missing identity left previous release available");
        await Expect<InvalidDataException>(() => Install(Updater(schema: 2)));
        Require(Updater(schema: 2).GetActiveRelease().Version == 9, "missing previous identity left active release intact");
    }

    public async Task ActivateVerifiedHistoricalRelease()
    {
        var updater = Updater(schema: 2);
        SetRelease(12, schema: 2);
        var historical = await Install(updater);
        SetRelease(13, schema: 2);
        await Install(updater);
        SetRelease(14, schema: 2);
        await Install(updater);

        Require(updater.GetRelease(12).Version == 12, "explicit read works beyond the previous pointer");
        Require(updater.GetActiveRelease().Version == 14, "explicit read left the pointer intact");
        await Expect<InvalidDataException>(() => updater.ActivateReleaseAsync(4, default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Expect<OperationCanceledException>(() => updater.ActivateReleaseAsync(12, cancellation.Token));
        Require(updater.GetActiveRelease().Version == 14, "incompatible or cancelled recovery kept the current release");

        string signaturePath = Path.Combine(Path.GetDirectoryName(historical.DirectoryPath), "manifest.sig");
        byte[] validSignature = File.ReadAllBytes(signaturePath);
        File.WriteAllBytes(signaturePath, new byte[validSignature.Length]);
        await Expect<InvalidDataException>(() => Task.FromResult(updater.GetRelease(12)));
        await Expect<InvalidDataException>(() => updater.ActivateReleaseAsync(12, default));
        File.WriteAllBytes(signaturePath, validSignature);

        string payloadPath = Path.Combine(historical.DirectoryPath, FilePath);
        File.WriteAllBytes(payloadPath, new byte[Body.Length]);
        await Expect<InvalidDataException>(() => updater.ActivateReleaseAsync(12, default));
        Require(updater.GetActiveRelease().Version == 14, "damaged historical release did not activate");
        File.WriteAllBytes(payloadPath, Body);

        Require((await updater.ActivateReleaseAsync(12, default)).Version == 12, "verified history activated explicitly");
        Require(Updater(schema: 2).GetActiveRelease().Version == 12, "explicit activation survived restart");
        await updater.ActivateReleaseAsync(12, default);
        Require((await updater.RollbackAsync(default)).Version == 14, "repeated activation preserved rollback");
    }

    private async Task SendContent(HttpContext context)
    {
        Interlocked.Increment(ref contentRequests);
        if (cancelOnContent != null)
            cancelOnContent.Cancel();
        var rangeHeader = context.Request.Headers.Range.ToString();
        var offset = 0;
        if (RangeHeaderValue.TryParse(rangeHeader, out var range))
        {
            rangeRequests++;
            if (!ignoreRange)
            {
                offset = (int)range.Ranges.Single().From.Value;
                context.Response.StatusCode = (int)HttpStatusCode.PartialContent;
                context.Response.Headers.ContentRange = $"bytes {offset + (badRange ? 1 : 0)}-{body.Length - 1}/{body.Length}";
            }
        }
        var length = body.Length - offset;
        if (interrupt)
        {
            context.Response.ContentLength = length;
            await context.Response.Body.WriteAsync(body.AsMemory(offset, 16));
            await context.Response.Body.FlushAsync();
            await Task.Delay(100);
            context.Abort();
            return;
        }
        if (!oversized)
            context.Response.ContentLength = length;
        await context.Response.Body.WriteAsync(body.AsMemory(offset, length));
    }

    private ContentUpdater Updater(int schema = 1, Func<long> availableBytes = null) => new(store, http, key, 1, schema, availableBytes);

    private Task<ContentRelease> Install(ContentUpdater updater, CancellationToken token = default) =>
        updater.InstallAsync(new Uri(server.Urls.Single() + "/manifest.json"), new Uri(server.Urls.Single() + "/manifest.sig"), token);

    private void SetRelease(long version, string path = FilePath, int schema = 1, bool includeReplacement = false,
        byte[] content = null)
    {
        body = content ?? Body;
        var paths = includeReplacement ? new[] { path, "replacement.bytes" } : new[] { path };
        string sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version,
            protocolVersion = 1,
            schemaVersion = schema,
            files = paths.Select(file => new { path = file, size = body.Length, sha256 })
        });
        signature = key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static async Task Expect<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private static void Require(bool result, string scenario)
    {
        if (!result) throw new Exception($"Failed: {scenario}");
    }

    public async ValueTask DisposeAsync()
    {
        await server.DisposeAsync();
        http.Dispose();
        key.Dispose();
        if (Directory.Exists(store)) Directory.Delete(store, true);
    }
}
