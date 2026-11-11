using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared;
using AstralOath.Shared.Updates;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Bootstrap
{
    internal sealed class StartupContent : IDisposable
    {
        private const int SchemaVersion = 3;
        [Serializable, Preserve]
        private sealed class PublicKey { public string modulus; public string exponent; }
        private readonly HotUpdateCodeLoader code;
        private readonly CodeBootJournal journal;
        private StartupContent(YooAssetService assets, HotUpdateCodeLoader code, CodeBootJournal journal, long? version)
        {
            Assets = assets;
            this.code = code;
            this.journal = journal;
            ReleaseVersion = version;
        }
        internal YooAssetService Assets { get; }
        internal long? ReleaseVersion { get; }
        internal IHotUpdateEntry CreateEntry()
        {
#if UNITY_EDITOR
            return HotUpdateCodeLoader.CreateEditorEntry();
#else
            if (ReleaseVersion.HasValue) journal.Begin(ReleaseVersion.Value);
            return code.CreateEntry();
#endif
        }
        internal void ConfirmStartup()
        {
            if (ReleaseVersion.HasValue) journal.Confirm(ReleaseVersion.Value);
        }
        internal static async Task<StartupContent> OpenAsync(string manifest, string signature, TextAsset key,
            CancellationToken lifetime, Action<string> failedUpdate)
        {
            bool remote = !string.IsNullOrWhiteSpace(manifest) || !string.IsNullOrWhiteSpace(signature) || key;
#if UNITY_EDITOR
            if (remote)
                throw new InvalidOperationException("远端 C# 热更必须使用 IL2CPP Player 验证；编辑器直接使用已编译的 C#，请清空演示场景更新地址。");
            return await LoadAsync(null, null, null, lifetime);
#else
            var asset = Resources.Load<TextAsset>(PlayerBaseline.ResourceName);
            if (!asset) throw new InvalidDataException("Missing player baseline. Build using Tools/AstralOath/强更包.");
            var baseline = JsonUtility.FromJson<PlayerBaseline>(asset.text);
            if (baseline == null || baseline.formatVersion != 1 || string.IsNullOrWhiteSpace(baseline.baselineId) ||
                baseline.baselineId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || baseline.baselineId == "." || baseline.baselineId == "..")
                throw new InvalidDataException("Invalid built-in player baseline.");
            string storePath = Path.Combine(Application.persistentDataPath, "FoundationContent", baseline.baselineId);
            var journal = new CodeBootJournal(Path.Combine(storePath, "code-boot.state"));
            long? interrupted = journal.RecoverInterruptedBoot();
            if (interrupted.HasValue)
                failedUpdate("上次 C# 启动未完成，已隔离版本 " + interrupted.Value);
            if (!remote)
                return await LoadAsync(null, baseline, journal, lifetime);
            if (!Uri.TryCreate(manifest, UriKind.Absolute, out var manifestUri) ||
                !Uri.TryCreate(signature, UriKind.Absolute, out var signatureUri) ||
                !IsTrustedTransport(manifestUri) || !IsTrustedTransport(signatureUri) || !key)
                throw new InvalidDataException("Content update requires HTTPS URLs and a pinned RSA public key.");
            var parameters = JsonUtility.FromJson<PublicKey>(key.text);
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(parameters.modulus),
                Exponent = Convert.FromBase64String(parameters.exponent) });
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var unityContext = SynchronizationContext.Current ?? throw new InvalidOperationException("Start content on the Unity main thread.");
            var updater = new ContentUpdater(storePath, http, rsa, (int)FoundationProtocol.Version, SchemaVersion,
                () => AvailableBytes(storePath, unityContext));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            StartupContent candidate = null;
            try
            {
                if (interrupted.HasValue && journal.LastHealthyVersion.HasValue)
                    await Task.Run(() => updater.ActivateReleaseAsync(journal.LastHealthyVersion.Value, deadline.Token), deadline.Token);
                await Task.Run(() => updater.InstallAsync(manifestUri, signatureUri, deadline.Token,
                    (release, token) => OnUnityThread(unityContext, async () =>
                    {
                        if (journal.IsRejected(release.Version))
                            throw new InvalidDataException("This release previously failed startup and is quarantined. Publish a new version.");
                        candidate = await LoadAsync(release, baseline, journal, token);
                    })), deadline.Token);
                lifetime.ThrowIfCancellationRequested();
                return candidate ?? throw new InvalidOperationException("Candidate was not preflighted.");
            }
            catch (Exception error)
            {
                candidate?.Dispose();
                lifetime.ThrowIfCancellationRequested();
                failedUpdate(error.Message);
                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                recovery.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    long? healthy = journal.LastHealthyVersion;
                    var active = healthy.HasValue
                        ? await Task.Run(() => updater.ActivateReleaseAsync(healthy.Value, recovery.Token), recovery.Token)
                        : await Task.Run(() => updater.GetActiveRelease(recovery.Token), recovery.Token);
                    if (active != null && !journal.IsRejected(active.Version))
                        return await LoadAsync(active, baseline, journal, recovery.Token);
                }
                catch (Exception recoveryError) when (!(recoveryError is OperationCanceledException))
                {
                    failedUpdate("本地更新不可用，回到安装包内容：" + recoveryError.Message);
                }
                return await LoadAsync(null, baseline, journal, recovery.Token);
            }
#endif
        }
        private static bool IsTrustedTransport(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps || (Debug.isDebugBuild && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

        private static long AvailableBytes(string directory, SynchronizationContext unityContext)
        {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
            if (!GetDiskFreeSpaceEx(directory, out ulong available, IntPtr.Zero, IntPtr.Zero))
                throw new IOException("Unable to read content disk space. Windows error " + Marshal.GetLastWin32Error());
            return checked((long)available);
#elif UNITY_ANDROID && !UNITY_EDITOR
            // The updater invokes this callback from a worker. Create/use the JNI object on Unity's main thread.
            long available = 0;
            OnUnityThread(unityContext, () =>
            {
                using var stat = new AndroidJavaObject("android.os.StatFs", directory);
                available = stat.Call<long>("getAvailableBytes");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            return available;
#else
            return new DriveInfo(Path.GetPathRoot(directory)).AvailableFreeSpace;
#endif
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode,
            ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available,
            IntPtr totalBytes, IntPtr totalFreeBytes);
#endif

        private static Task OnUnityThread(SynchronizationContext context, Func<Task> work)
        {
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(async _ =>
            {
                try { await work(); result.SetResult(true); }
                catch (Exception error) { result.SetException(error); }
            }, null);
            return result.Task;
        }

        private static async Task<StartupContent> LoadAsync(ContentRelease release, PlayerBaseline baseline,
            CodeBootJournal journal, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string root = release == null ? null : Path.Combine(release.DirectoryPath, "yooasset", YooAssetService.PackageName);
            var assets = new YooAssetService();
            HotUpdateCodeLoader code = null;
            try
            {
                await assets.InitializeAsync(token, root);
#if !UNITY_EDITOR
                using var scope = new BindingScope(token);
                code = await HotUpdateCodeLoader.PrepareAsync(assets, baseline, scope);
#endif
                return new StartupContent(assets, code, journal, release?.Version);
            }
            catch
            {
                code?.Dispose();
                assets.Dispose();
                throw;
            }
        }
        public void Dispose()
        {
            code?.Dispose();
            Assets.Dispose();
        }
    }
}