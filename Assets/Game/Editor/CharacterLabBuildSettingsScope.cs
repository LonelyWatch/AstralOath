using System;
using HybridCLR.Settings;
using UnityEditor;
namespace AstralOath.Editor
{
    // Each build owns a temporary assembly selection; it must not become the next profile's defaults.
    public sealed class CharacterLabBuildSettingsScope : IDisposable
    {
        private readonly HybridCLRSettings settings = HybridCLRSettings.instance;
        private readonly string snapshot;
        private bool disposed;
        public CharacterLabBuildSettingsScope() { snapshot = EditorJsonUtility.ToJson(settings); }
        public void Dispose()
        {
            if (disposed) return;
            EditorJsonUtility.FromJsonOverwrite(snapshot, settings);
            HybridCLRSettings.Save();
            disposed = true;
        }
    }
}
