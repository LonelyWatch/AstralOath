using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Shared.Updates
{
    public sealed class ContentRelease
    {
        internal ContentRelease(long version, string directoryPath)
        {
            Version = version;
            DirectoryPath = directoryPath;
        }

        public long Version { get; }
        public string DirectoryPath { get; }
    }

    public sealed partial class ContentUpdater
    {
        private const int MaxManifestBytes = 1024 * 1024;
        private const long DiskSafetyBytes = 8 * 1024 * 1024;
        private readonly string store;
        private readonly HttpClient http;
        private readonly RSA trustedKey;
        private readonly int protocolVersion;
        private readonly int schemaVersion;
        private readonly Func<long> availableBytes;

        public ContentUpdater(string storePath, HttpClient http, RSA trustedKey, int protocolVersion, int schemaVersion,
            Func<long> availableBytes = null)
        {
            store = Path.GetFullPath(storePath);
            this.http = http ?? throw new ArgumentNullException(nameof(http));
            this.trustedKey = trustedKey ?? throw new ArgumentNullException(nameof(trustedKey));
            this.protocolVersion = protocolVersion;
            this.schemaVersion = schemaVersion;
            this.availableBytes = availableBytes ?? AvailableSpace;
            if (trustedKey.KeySize < 2048 || protocolVersion < 1 || schemaVersion < 1)
                throw new ArgumentException("A 2048-bit RSA key and positive compatibility versions are required.");
        }

        public ContentRelease GetActiveRelease(CancellationToken token = default)
        {
            using (AcquireStore())
            {
                token.ThrowIfCancellationRequested();
                long active = ReadPointer().active;
                return active == 0 ? null : ReadRelease(active, token);
            }
        }

        public ContentRelease GetRelease(long version, CancellationToken token = default)
        {
            using (AcquireStore())
                return ReadRelease(version, token);
        }

        public Task<ContentRelease> ActivateReleaseAsync(long version, CancellationToken token)
        {
            using (AcquireStore())
            {
                var release = ReadRelease(version, token);
                var pointer = ReadPointer();
                token.ThrowIfCancellationRequested();
                if (pointer.active != version)
                    SwitchPointer(version, pointer.active, token);
                return Task.FromResult(release);
            }
        }

        public async Task<ContentRelease> InstallAsync(Uri manifestUri, Uri signatureUri, CancellationToken token,
            Func<ContentRelease, CancellationToken, Task> validateCandidate = null)
        {
            RequireHttpUri(manifestUri);
            RequireHttpUri(signatureUri);
            using (AcquireStore())
            {
                var pointer = ReadPointer();
                byte[] bytes = await FetchAsync(manifestUri, MaxManifestBytes, token).ConfigureAwait(false);
                byte[] signature = await FetchAsync(signatureUri, trustedKey.KeySize / 8, token).ConfigureAwait(false);
                var manifest = ParseVerified(bytes, signature);
                RequireCompatible(manifest);
                if (manifest.Version < pointer.active)
                    throw new InvalidDataException("Downgrades require an explicit rollback.");

                string releases = SafeDirectory(store, "releases");
                string target = SafePath(releases, VersionName(manifest.Version));
                string recovery = SafePath(releases, VersionName(manifest.Version) + ".repair");
                bool installedTarget = Directory.Exists(target);
                string installedPath = installedTarget ? target : recovery;
                ContentRelease release = null;
                if (Directory.Exists(installedPath))
                {
                    byte[] installed = ReadBounded(SafePath(installedPath, "manifest.json"), MaxManifestBytes);
                    if (Digest(bytes) != Digest(installed))
                        throw new InvalidDataException("This content version already exists with a different manifest.");
                    if (installedTarget)
                    {
                        try { release = ReadRelease(manifest.Version, token); }
                        catch (InvalidDataException) { }
                        catch (FileNotFoundException) { }
                        catch (DirectoryNotFoundException) { }
                    }
                }
                else if (manifest.Version == pointer.active || manifest.Version == pointer.previous)
                {
                    throw new InvalidDataException("The recorded content version has no local manifest to confirm its identity.");
                }

                string stagedRepair = null;
                string quarantined = null;
                if (release == null)
                {
                    string staging = SafeDirectory(SafeDirectory(store, "staging"), Digest(bytes));
                    string payload = SafeDirectory(staging, "files");
                    long required = DiskSafetyBytes + bytes.Length + signature.Length;
                    foreach (var entry in manifest.Files)
                    {
                        string local = ResolveFile(payload, entry.Path, true);
                        long have = File.Exists(local) ? new FileInfo(local).Length : 0;
                        if (have > entry.Size || (have == entry.Size && File.Exists(local) && !Matches(local, entry, token)))
                        {
                            File.Delete(local);
                            have = 0;
                        }
                        required = checked(required + entry.Size - have);
                    }
                    if (availableBytes() < required)
                        throw new IOException("Insufficient disk space for the remaining content and safety reserve.");

                    foreach (var entry in manifest.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        string local = ResolveFile(payload, entry.Path, true);
                        if (!File.Exists(local) || new FileInfo(local).Length != entry.Size)
                        {
                            Uri source = new Uri(manifestUri, entry.Path);
                            await DownloadAsync(source, local, entry, token).ConfigureAwait(false);
                        }
                        if (!Matches(local, entry, token))
                        {
                            File.Delete(local);
                            throw new InvalidDataException("Payload hash mismatch: " + entry.Path);
                        }
                    }
                    WriteDurable(SafePath(staging, "manifest.json"), bytes);
                    WriteDurable(SafePath(staging, "manifest.sig"), signature);
                    token.ThrowIfCancellationRequested();
                    VerifyPayload(payload, manifest, token);
                    if (installedTarget)
                    {
                        quarantined = Directory.Exists(recovery)
                            ? SafePath(releases, VersionName(manifest.Version) + ".repair-" + Guid.NewGuid().ToString("N"))
                            : recovery;
                        Directory.Move(target, quarantined);
                    }
                    try
                    {
                        Directory.Move(staging, target);
                        if (quarantined != null || Directory.Exists(recovery))
                            stagedRepair = staging;
                    }
                    catch (Exception installError)
                    {
                        try
                        {
                            if (quarantined != null)
                                await ContentDirectoryRollback.MoveAsync(quarantined, target).ConfigureAwait(false);
                        }
                        catch (Exception rollbackError)
                        {
                            throw new AggregateException("Content installation and directory recovery both failed; local directories were retained.",
                                installError, rollbackError);
                        }
                        throw;
                    }
                    release = new ContentRelease(manifest.Version, SafePath(target, "files"));
                }

                try
                {
                    if (validateCandidate != null)
                        await validateCandidate(release, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (pointer.active != manifest.Version)
                        SwitchPointer(manifest.Version, pointer.active, token);
                }
                catch (Exception activationError)
                {
                    try
                    {
                        if (stagedRepair != null)
                        {
                            await ContentDirectoryRollback.MoveAsync(target, stagedRepair).ConfigureAwait(false);
                            if (quarantined != null)
                                await ContentDirectoryRollback.MoveAsync(quarantined, target).ConfigureAwait(false);
                        }
                    }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException("Content activation and directory recovery both failed; local directories were retained.",
                            activationError, rollbackError);
                    }
                    throw;
                }
                return release;
            }
        }

        public Task<ContentRelease> RollbackAsync(CancellationToken token)
        {
            using (AcquireStore())
            {
                var pointer = ReadPointer();
                if (pointer.previous == 0)
                    throw new InvalidOperationException("There is no previous release.");
                var previous = ReadRelease(pointer.previous, token);
                SwitchPointer(pointer.previous, pointer.active, token);
                return Task.FromResult(previous);
            }
        }

        private ContentRelease ReadRelease(long version, CancellationToken token)
        {
            if (version < 1)
                throw new ArgumentOutOfRangeException(nameof(version));
            token.ThrowIfCancellationRequested();
            string path = SafePath(SafePath(store, "releases"), VersionName(version));
            var manifest = ParseVerified(ReadBounded(SafePath(path, "manifest.json"), MaxManifestBytes),
                ReadBounded(SafePath(path, "manifest.sig"), trustedKey.KeySize / 8));
            RequireCompatible(manifest);
            if (manifest.Version != version)
                throw new InvalidDataException("Installed directory and signed version disagree.");
            string payload = SafePath(path, "files");
            VerifyPayload(payload, manifest, token);
            return new ContentRelease(version, payload);
        }

        private static void VerifyPayload(string payload, ContentManifest manifest, CancellationToken token)
        {
            ValidateTree(payload, manifest.Files, token);
            foreach (var entry in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                if (!Matches(ResolveFile(payload, entry.Path, false), entry, token))
                    throw new InvalidDataException("Installed payload is corrupt: " + entry.Path);
            }

        }
    }
}
