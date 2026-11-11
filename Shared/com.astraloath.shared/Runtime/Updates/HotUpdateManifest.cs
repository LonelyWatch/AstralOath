using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AstralOath.Shared.Updates
{
    [Serializable, DataContract]
    public class HotUpdateFile
    {
        [DataMember(IsRequired = true)] public string name;
        [DataMember(IsRequired = true)] public string address;
        [DataMember(IsRequired = true)] public string sha256;
        [DataMember(IsRequired = true)] public long size;
    }

    [Serializable, DataContract]
    public sealed class HotUpdateAssembly : HotUpdateFile
    {
        [DataMember(IsRequired = true)] public string[] dependencies;
    }

    [Serializable, DataContract]
    public sealed class HotUpdateManifest
    {
        private const int MaxManifestBytes = 1024 * 1024;
        private const int MaxFiles = 4096;
        private static readonly Regex Identifier = new Regex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z");
        private static readonly Regex TypeName = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*(?:[.+][A-Za-z_][A-Za-z0-9_]*)*\z");
        private static readonly Regex HashText = new Regex(@"\A[a-f0-9]{64}\z");

        [DataMember(IsRequired = true)] public int formatVersion = 1;
        [DataMember(IsRequired = true)] public string baselineId;
        [DataMember(IsRequired = true)] public string platform;
        [DataMember(IsRequired = true)] public string architecture;
        [DataMember(IsRequired = true)] public string entryAssembly;
        [DataMember(IsRequired = true)] public string entryType;
        [DataMember(IsRequired = true)] public HotUpdateAssembly[] assemblies;
        [DataMember(IsRequired = true)] public HotUpdateFile[] aotMetadata;

        public static HotUpdateManifest Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxManifestBytes)
                throw new InvalidDataException("Code manifest size is invalid.");

            try
            {
                using (var stream = new MemoryStream(bytes, false))
                    return (HotUpdateManifest)new DataContractJsonSerializer(typeof(HotUpdateManifest)).ReadObject(stream)
                        ?? throw new InvalidDataException("Code manifest is empty.");
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException("Code manifest is malformed.", exception);
            }
        }

        public void Validate(string baselineId, string platform, string architecture)
        {
            if (formatVersion != 1 || !Identifier.IsMatch(this.baselineId ?? ""))
                throw new InvalidDataException("Code manifest format or player baseline ID is invalid.");
            if (!(this.platform == "StandaloneWindows64" && this.architecture == "x64") &&
                !(this.platform == "Android" && this.architecture == "ARM64"))
                throw new InvalidDataException("Code manifest platform or architecture is unsupported.");
            if (this.baselineId != baselineId || this.platform != platform || this.architecture != architecture)
                throw new InvalidDataException("Code manifest is incompatible with this player baseline.");
            if (!IsAssemblyName(entryAssembly) || !TypeName.IsMatch(entryType ?? ""))
                throw new InvalidDataException("Code manifest entry point is invalid.");
            if (assemblies == null || assemblies.Length == 0 || assemblies.Length > MaxFiles ||
                aotMetadata == null || aotMetadata.Length > MaxFiles - assemblies.Length)
                throw new InvalidDataException("Code manifest file count is invalid.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in assemblies)
                RequireUniqueFile(file, "code/hot/", names, addresses);
            foreach (var file in aotMetadata)
                RequireUniqueFile(file, "code/aot/", names, addresses);

            var hotByName = GetAssembliesByName();
            if (!hotByName.ContainsKey(entryAssembly))
                throw new InvalidDataException("Code manifest entry assembly is missing.");
            GetLoadOrder(hotByName);
        }

        public IReadOnlyList<HotUpdateAssembly> GetLoadOrder() => GetLoadOrder(GetAssembliesByName());

        public static void VerifyFile(HotUpdateFile file, byte[] bytes)
        {
            RequireFileDigest(file);
            if (bytes == null || bytes.LongLength != file.size)
                throw new InvalidDataException("Code payload size mismatch: " + file.name);
            using (var hash = SHA256.Create())
            {
                string digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (digest != file.sha256)
                    throw new InvalidDataException("Code payload hash mismatch: " + file.name);
            }
        }

        private static bool IsAssemblyName(string name) =>
            Identifier.IsMatch(name ?? "") && !name.EndsWith(".", StringComparison.Ordinal) &&
            !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        private static void RequireFileDigest(HotUpdateFile file)
        {
            if (file == null || file.size <= 0 || file.size > int.MaxValue || !HashText.IsMatch(file.sha256 ?? ""))
                throw new InvalidDataException("Code manifest file size or hash is invalid.");
        }

        private static void RequireUniqueFile(HotUpdateFile file, string prefix, HashSet<string> names, HashSet<string> addresses)
        {
            RequireFileDigest(file);
            if (!IsAssemblyName(file.name) || file.address != prefix + file.name)
                throw new InvalidDataException("Code manifest file name or address is invalid.");
            if (!names.Add(file.name) || !addresses.Add(file.address))
                throw new InvalidDataException("Duplicate code manifest file: " + file.name);
        }

        private Dictionary<string, HotUpdateAssembly> GetAssembliesByName()
        {
            if (assemblies == null || assemblies.Length == 0 || assemblies.Length > MaxFiles)
                throw new InvalidDataException("Code manifest assembly count is invalid.");

            var byName = new Dictionary<string, HotUpdateAssembly>(StringComparer.Ordinal);
            foreach (var file in assemblies)
            {
                if (file == null || !IsAssemblyName(file.name) || !byName.TryAdd(file.name, file))
                    throw new InvalidDataException("Code manifest assembly name is invalid or duplicated.");
            }
            return byName;
        }

        private IReadOnlyList<HotUpdateAssembly> GetLoadOrder(Dictionary<string, HotUpdateAssembly> byName)
        {
            var consumers = new Dictionary<string, List<HotUpdateAssembly>>(StringComparer.Ordinal);
            var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
            var ready = new Queue<HotUpdateAssembly>();
            foreach (var file in assemblies)
            {
                if (file.dependencies == null || file.dependencies.Length > byName.Count)
                    throw new InvalidDataException("Code manifest dependency count is invalid: " + file.name);
                remaining.Add(file.name, file.dependencies.Length);
                if (file.dependencies.Length == 0)
                    ready.Enqueue(file);

                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (string dependency in file.dependencies)
                {
                    if (dependency == null || !byName.ContainsKey(dependency) || !unique.Add(dependency))
                        throw new InvalidDataException("Missing or duplicate dependency for " + file.name + ": " + dependency);
                    if (!consumers.TryGetValue(dependency, out var dependents))
                        consumers.Add(dependency, dependents = new List<HotUpdateAssembly>());
                    dependents.Add(file);
                }
            }

            var ordered = new List<HotUpdateAssembly>(byName.Count);
            while (ready.Count > 0)
            {
                var file = ready.Dequeue();
                ordered.Add(file);
                if (!consumers.TryGetValue(file.name, out var dependents))
                    continue;
                foreach (var consumer in dependents)
                {
                    if (--remaining[consumer.name] == 0)
                        ready.Enqueue(consumer);
                }
            }
            if (ordered.Count != byName.Count)
                throw new InvalidDataException("Code manifest contains a dependency cycle.");
            return ordered;
        }
    }
}
