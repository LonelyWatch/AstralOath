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
await fixture.BadSignatureAndTraversal();
await fixture.BadHashAndShortBody();
await fixture.ResumeAndFallback();
await fixture.NoSpaceAndCancellation();
await fixture.CompatibilityAndRollback();
await fixture.UnsignedFileRejected();
await fixture.CandidateRejectedBeforeActivation();
await fixture.RepairSameVersion();
await fixture.RejectReplacementWhenInstalledIdentityIsMissing();
Console.WriteLine("All 10 content scenarios passed");

sealed class Fixture : IAsyncDisposable
{
    private const string FilePath = "yooasset/demo.bundle";
    private static readonly byte[] Body = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
    private readonly string store = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly RSA key = RSA.Create(2048);
    private readonly HttpClient http = new();
    private WebApplication server;
    private byte[] manifest, signature, body = Body;
    private bool interrupt, ignoreRange, badRange, oversized;
    private int rangeRequests;
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

    public async Task ValidInstallAndRestart()
    {
        SetRelease(1);
        var updater = Updater();
        Require(updater.GetActiveRelease() == null, "fresh store has no active release");
        var release = await Install(updater);
        Require(release.Version == 1 && File.ReadAllBytes(Path.Combine(release.DirectoryPath, FilePath)).SequenceEqual(Body), "release payload");
        Require(Updater().GetActiveRelease().Version == 1, "restart recovered active release");
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
        SetRelease(2);
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
            SetRelease(version);
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
        SetRelease(5);
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
        Require(repaired.Version == 10 && File.ReadAllBytes(local).SequenceEqual(Body), "same signed version replaced corrupt payload");
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

    private async Task SendContent(HttpContext context)
    {
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

    private void SetRelease(long version, string path = FilePath, int schema = 1)
    {
        body = Body;
        manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version,
            protocolVersion = 1,
            schemaVersion = schema,
            files = new[] { new { path, size = Body.Length, sha256 = Convert.ToHexString(SHA256.HashData(Body)).ToLowerInvariant() } }
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
