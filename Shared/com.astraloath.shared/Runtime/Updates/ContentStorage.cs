using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AstralOath.Shared.Updates
{
    [DataContract]
    internal sealed class ContentManifest
    {
        [DataMember(Name = "version", IsRequired = true)] public long Version { get; set; }
        [DataMember(Name = "protocolVersion", IsRequired = true)] public int ProtocolVersion { get; set; }
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "files", IsRequired = true)] public ContentEntry[] Files { get; set; }
    }

    [DataContract]
    internal sealed class ContentEntry
    {
        [DataMember(Name = "path", IsRequired = true)] public string Path { get; set; }
        [DataMember(Name = "size", IsRequired = true)] public long Size { get; set; }
        [DataMember(Name = "sha256", IsRequired = true)] public string Sha256 { get; set; }
    }

    public sealed partial class ContentUpdater
    {
        private static readonly Regex FileSegment = new Regex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z");
        private static readonly Regex ReservedSegment = new Regex(@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HashText = new Regex(@"\A[a-f0-9]{64}\z");

        private ContentManifest ParseVerified(byte[] bytes, byte[] signature)
        {
            if (signature.Length != trustedKey.KeySize / 8 ||
                !trustedKey.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new InvalidDataException("Content manifest signature is invalid.");

            ContentManifest manifest;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                    manifest = (ContentManifest)new DataContractJsonSerializer(typeof(ContentManifest)).ReadObject(stream);
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException("Content manifest is malformed.", exception);
            }

            if (manifest == null || manifest.Version < 1 || manifest.ProtocolVersion < 1 ||
                manifest.SchemaVersion < 1 || manifest.Files == null || manifest.Files.Length == 0 || manifest.Files.Length > 4096)
                throw new InvalidDataException("Content manifest fields are invalid.");

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in manifest.Files)
            {
                if (file == null || file.Path == null || file.Path.Length > 240 || file.Size < 0 ||
                    file.Size > 8L * 1024 * 1024 * 1024 || !HashText.IsMatch(file.Sha256 ?? ""))
                    throw new InvalidDataException("Invalid manifest payload entry.");
                var parts = file.Path.Split('/');
                string directory = "";
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    if (!FileSegment.IsMatch(part) || ReservedSegment.IsMatch(part) || part.EndsWith(".", StringComparison.Ordinal))
                        throw new InvalidDataException("Unsafe manifest path: " + file.Path);
                    if (i == parts.Length - 1)
                        continue;
                    directory = directory.Length == 0 ? part : directory + "/" + part;
                    if (directories.TryGetValue(directory, out string existing) && existing != directory)
                        throw new InvalidDataException("Inconsistent directory casing: " + directory);
                    directories[directory] = directory;
                }
                if (file.Path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    file.Path.Equals("manifest.sig", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Manifest metadata paths are reserved.");
                if (!files.Add(file.Path))
                    throw new InvalidDataException("Duplicate manifest path: " + file.Path);
                total += file.Size;
                if (total > 64L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Content release exceeds the size limit.");
            }

            foreach (var file in files)
            {
                if (directories.ContainsKey(file))
                    throw new InvalidDataException("A payload path is both a file and a directory: " + file);
            }
            return manifest;
        }

        private void RequireCompatible(ContentManifest manifest)
        {
            if (manifest.ProtocolVersion != protocolVersion || manifest.SchemaVersion != schemaVersion)
                throw new InvalidDataException("Content is incompatible with this client protocol or config schema.");
        }

        private long AvailableSpace()
        {
            string drive = Path.GetPathRoot(store);
            string location = store.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var candidate in DriveInfo.GetDrives())
            {
                string root = candidate.RootDirectory.FullName;
                if (root.Length > drive.Length && location.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    drive = root;
            }
            return new DriveInfo(drive).AvailableFreeSpace;
        }

        private static void ValidateTree(string payload, ContentEntry[] entries, CancellationToken token)
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            var directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                files.Add(entry.Path);
                string path = entry.Path;
                int slash;
                while ((slash = path.LastIndexOf('/')) >= 0)
                {
                    path = path.Substring(0, slash);
                    directories.Add(path);
                }
            }

            var pending = new Stack<(string local, string prefix)>();
            pending.Push((payload, ""));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (string item in Directory.EnumerateFileSystemEntries(directory.local))
                {
                    token.ThrowIfCancellationRequested();
                    RejectReparsePoint(item);
                    string relative = directory.prefix + Path.GetFileName(item);
                    if (Directory.Exists(item) && directories.Contains(relative))
                        pending.Push((item, relative + "/"));
                    else if (Directory.Exists(item) || !files.Contains(relative))
                        throw new InvalidDataException("Unexpected installed payload path: " + relative);
                }
            }
        }

        private FileStream AcquireStore()
        {
            Directory.CreateDirectory(store);
            RejectReparsePoint(store);
            return new FileStream(SafePath(store, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private (long active, long previous) ReadPointer()
        {
            string path = SafePath(store, "active-release");
            if (!File.Exists(path))
                return (0, 0);
            var fields = Encoding.ASCII.GetString(ReadBounded(path, 64)).Split('\n');
            if (fields.Length != 2 ||
                !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long active) || active < 1 ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long previous) || previous == active)
                throw new InvalidDataException("Content release pointer is invalid.");
            return (active, previous);
        }

        private void SwitchPointer(long active, long previous, CancellationToken token)
        {
            string pointer = SafePath(store, "active-release");
            string temporary = SafePath(store, "active-release.pending");
            WriteDurable(temporary, Encoding.ASCII.GetBytes(VersionName(active) + "\n" + VersionName(previous)));
            token.ThrowIfCancellationRequested();
            if (File.Exists(pointer))
                File.Replace(temporary, pointer, null);
            else
                File.Move(temporary, pointer);
        }

        private static string VersionName(long version) => version.ToString(CultureInfo.InvariantCulture);

        private static string SafePath(string parent, string name)
        {
            string path = Path.Combine(parent, name);
            RejectReparsePoint(path);
            return path;
        }

        private static void RejectReparsePoint(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Content store contains a symbolic link or reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static string SafeDirectory(string parent, string name)
        {
            string path = SafePath(parent, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static string ResolveFile(string payload, string relative, bool createDirectories)
        {
            string[] parts = relative.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
                payload = createDirectories ? SafeDirectory(payload, parts[i]) : SafePath(payload, parts[i]);
            return SafePath(payload, parts[parts.Length - 1]);
        }

        private static byte[] ReadBounded(string path, int maximum)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > maximum)
                    throw new InvalidDataException("Stored content metadata exceeds its size limit.");
                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                        throw new InvalidDataException("Stored content metadata was truncated.");
                    offset += read;
                }
                return bytes;
            }
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        private static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return Hex(hash.ComputeHash(bytes));
        }

        private static bool Matches(string path, ContentEntry entry, CancellationToken token)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Size)
                return false;
            using (var stream = File.OpenRead(path))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                }
                return Hex(hash.GetHashAndReset()) == entry.Sha256;
            }
        }
    }
}
