using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine.SceneManagement;
using YooAsset;

namespace AstralOath.Client.Bootstrap
{
    public sealed class YooAssetService : IAssetService, ISceneAssetService, IDisposable
    {
        public const string PackageName = "Foundation";
        internal string PackageVersion { get; private set; }
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
            PackageVersion = version.PackageVersion;
            if (packageId == PackageName) UiAudioPlayback.AcquireClipLease = RetainUiClip;
        }

        private Action RetainUiClip(UnityEngine.AudioClip clip)
        {
            string address = UiAudioPlayback.ClipAddress(clip.name);
            if (!package.CheckLocationValid(address)) return null; // Runtime clips and built-in entry sounds can have no package entry.
            if (package.IsNeedDownloadFromRemote(address))
                throw new InvalidOperationException("UI audio was not prepared locally: " + address);
            var handle = package.LoadAssetSync<UnityEngine.AudioClip>(address);
            if (handle.Status != EOperationStatus.Succeed)
            {
                string error = handle.LastError;
                handle.Release();
                throw new InvalidOperationException($"Cannot retain UI audio {address}: {error}");
            }
            if (handle.AssetObject == clip) return () => { if (handle.IsValid) handle.Release(); };
            // A Resources clip can share its name with a packaged clip, e.g. the startup confirm button.
            handle.Release();
            return null;
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

        public async Task<ILoadedScene> LoadAdditiveAsync(string address, CancellationToken token)
        {
            using var deadline = WithDeadline(token);
            deadline.Token.ThrowIfCancellationRequested();
            var handle = package.LoadSceneAsync(address, LoadSceneMode.Additive);
            // Unity cannot cancel a scene load in flight. Finish it before rolling back.
            while (handle.IsValid && !handle.IsDone)
                await Task.Yield();

            if (!handle.IsValid || handle.Status != EOperationStatus.Succeed)
            {
                string error = handle.IsValid ? handle.LastError : "invalid scene handle";
                if (handle.IsValid) handle.Release();
                deadline.Token.ThrowIfCancellationRequested();
                throw new InvalidOperationException($"YooAsset scene {address}: {error}");
            }

            var loaded = new LoadedScene(handle);
            if (deadline.IsCancellationRequested)
            {
                await loaded.UnloadAsync();
                deadline.Token.ThrowIfCancellationRequested();
            }
            return loaded;
        }

        private sealed class LoadedScene : ILoadedScene
        {
            private readonly SceneHandle handle;
            public Scene Scene { get; }

            internal LoadedScene(SceneHandle handle)
            {
                this.handle = handle;
                Scene = handle.SceneObject;
            }

            public async Task UnloadAsync()
            {
                if (handle.IsValid)
                    await WaitAsync(handle.UnloadAsync(), CancellationToken.None);
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
            if (packageId == PackageName) UiAudioPlayback.AcquireClipLease = null;
            YooAssets.Destroy();
        }
    }
}
