using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Updates;
using HybridCLR;
using UnityEngine;

namespace AstralOath.Client.Bootstrap
{
    // This object only holds verified bytes. Construction/preflight never executes candidate code.
    internal sealed class HotUpdateCodeLoader : IDisposable
    {
        internal const string ManifestAddress = "code/manifest";
        private readonly HotUpdateManifest manifest;
        private readonly Dictionary<string, byte[]> bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private static bool loadAttempted;
        private HotUpdateCodeLoader(HotUpdateManifest manifest)
        {
            this.manifest = manifest;
        }

        internal static async Task<HotUpdateCodeLoader> PrepareAsync(IAssetService assets, PlayerBaseline baseline,
            BindingScope scope)
        {
            var asset = await assets.LoadAsync<TextAsset>(ManifestAddress, scope);
            byte[] data = asset.bytes;
            var manifest = HotUpdateManifest.Parse(data);
            manifest.Validate(baseline.baselineId, baseline.platform, baseline.architecture);
            var prepared = new HotUpdateCodeLoader(manifest);
            try
            {
                foreach (var file in manifest.aotMetadata.Concat<HotUpdateFile>(manifest.assemblies))
                {
                    scope.Token.ThrowIfCancellationRequested();
                    var dll = await assets.LoadAsync<TextAsset>(file.address, scope);
                    var bytes = dll.bytes;
                    HotUpdateManifest.VerifyFile(file, bytes);
                    prepared.bytes.Add(file.name, bytes);
                }
                return prepared;
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }

        internal IHotUpdateEntry CreateEntry()
        {
            if (loadAttempted)
                throw new InvalidOperationException("C# code was already loaded or failed in this process. Restart is required.");
            loadAttempted = true;
            Assembly loadedEntry = null;
            try
            {
                foreach (var file in manifest.aotMetadata)
                {
                    var error = RuntimeApi.LoadMetadataForAOTAssembly(bytes[file.name], HomologousImageMode.SuperSet);
                    if (error != LoadImageErrorCode.OK)
                        throw new InvalidDataException($"AOT metadata {file.name} failed: {error}.");
                }
                foreach (var file in manifest.GetLoadOrder())
                {
                    var assembly = Assembly.Load(bytes[file.name]);
                    if (assembly.GetName().Name != file.name)
                        throw new InvalidDataException("Loaded assembly identity disagrees with its signed code manifest.");
                    if (file.name == manifest.entryAssembly)
                        loadedEntry = assembly;
                }
                Debug.Log($"ASTRAL_CODE_LOADED baseline={manifest.baselineId} entry={manifest.entryAssembly}");
                return InstantiateEntry(loadedEntry, manifest.entryType);
            }
            finally { Dispose(); }
        }

        internal static IHotUpdateEntry CreateEditorEntry()
        {
            const string name = "AstralOath.Client.FoundationDemo";
            var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == name)
                ?? throw new InvalidOperationException("The FoundationDemo assembly was not compiled by the Editor.");
            return InstantiateEntry(assembly, name + ".FoundationEntry");
        }

        private static IHotUpdateEntry InstantiateEntry(Assembly assembly, string typeName)
        {
            var type = assembly?.GetType(typeName, true);
            if (type == null || !typeof(IHotUpdateEntry).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidDataException("Code entry does not implement IHotUpdateEntry.");
            return (IHotUpdateEntry)Activator.CreateInstance(type);
        }

        public void Dispose() => bytes.Clear();
    }
}
