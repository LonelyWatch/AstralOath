using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using YooAsset;

namespace AstralOath.Client.Bootstrap
{
    public sealed class YooAssetService : IAssetService, IDisposable
    {
        public const string PackageName = "Foundation";
        private readonly ResourcePackage package;
        private readonly string packageId;
        private bool disposed;

        public YooAssetService(string packageName = PackageName)
        {
            if (YooAssets.Initialized)
                throw new InvalidOperationException("YooAsset already has an owner.");
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("Package name is required.", nameof(packageName));
            packageId = packageName;
            YooAssets.Initialize();
            package = YooAssets.CreatePackage(packageId);
        }

        public async Task InitializeAsync(CancellationToken token, string verifiedPackageRoot)
        {
            using var deadline = WithDeadline(token);
            InitializeParameters parameters;
            if (verifiedPackageRoot != null)
            {
                var files = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(packageRoot: verifiedPackageRoot);
                files.AddParameter(FileSystemParametersDefine.DISABLE_CATALOG_FILE, true);
                parameters = new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = files,
                    AutoUnloadBundleWhenUnused = true
                };
            }
            else
            {
#if UNITY_EDITOR
                var build = EditorSimulateModeHelper.SimulateBuild(packageId);
                parameters = new EditorSimulateModeParameters
                {
                    EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory),
                    AutoUnloadBundleWhenUnused = true
                };
#else
                parameters = new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                    AutoUnloadBundleWhenUnused = true
                };
#endif
            }

            await WaitAsync(package.InitializeAsync(parameters), deadline.Token);
            var version = package.RequestPackageVersionAsync(timeout: 15);
            await WaitAsync(version, deadline.Token);
            await WaitAsync(package.UpdatePackageManifestAsync(version.PackageVersion, 15), deadline.Token);
        }

        public async Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
        {
            using var deadline = WithDeadline(scope.Token);
            deadline.Token.ThrowIfCancellationRequested();
            var handle = package.LoadAssetAsync<T>(address);
            try
            {
                while (handle.IsValid && !handle.IsDone)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                deadline.Token.ThrowIfCancellationRequested();
                if (handle.Status != EOperationStatus.Succeed || !(handle.AssetObject is T asset))
                    throw new InvalidOperationException($"YooAsset {address}: {handle.LastError}");
                scope.Add(handle.Release);
                return asset;
            }
            catch
            {
                if (handle.IsValid)
                    handle.Release();
                throw;
            }
        }

        private static CancellationTokenSource WithDeadline(CancellationToken parent)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(parent);
            source.CancelAfter(TimeSpan.FromSeconds(20));
            return source;
        }

        private static async Task WaitAsync(AsyncOperationBase operation, CancellationToken token)
        {
            while (!operation.IsDone)
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            if (operation.Status != EOperationStatus.Succeed)
                throw new InvalidOperationException(operation.Error);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            YooAssets.Destroy();
        }
    }
}
