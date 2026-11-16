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
    internal sealed class StartupContentUnavailableException : IOException
    {
        internal StartupContentUnavailableException(string message, Exception inner = null) : base(message, inner) { }
    }

    internal sealed class StartupContent : IDisposable
    {
        private const int SchemaVersion = 3;
        private const string MinimalContentResourceName = "AstralOathMinimalContent";
        private sealed class QuarantinedReleaseException : Exception
        {
            internal QuarantinedReleaseException() : base("该补丁上次启动失败，已隔离。") { }
        }
        [Serializable, Preserve]
        private sealed class ContentSource
        {
            public string manifestUrl;
            public string signatureUrl;
            public string modulus;
            public string exponent;
        }
        private readonly HotUpdateCodeLoader code;
        private readonly CodeBootJournal journal;
        private IDisposable startupLease;
        private StartupContent(YooAssetService assets, HotUpdateCodeLoader code, CodeBootJournal journal, long? version)
        {
            Assets = assets;
            this.code = code;
            this.journal = journal;
            ReleaseVersion = version;
        }
        internal YooAssetService Assets { get; }
        internal long? ReleaseVersion { get; }
        internal bool RestartRequired { get; private set; }
        internal string PackageVersion => Assets.PackageVersion;
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
            try
            {
                if (ReleaseVersion.HasValue) journal.Confirm(ReleaseVersion.Value);
            }
            finally { ReleaseStartupLease(); }
        }

        internal void ReleaseStartupLease()
        {
            var lease = startupLease;
            startupLease = null;
            lease?.Dispose();
        }
        internal static async Task<StartupContent> OpenAsync(string manifest, string signature, TextAsset key,
            CancellationToken lifetime, Action<string> failedUpdate,
            Func<long, CancellationToken, Task<bool>> approveDownload = null,
            Action<ContentDownloadProgress> downloadProgress = null)
        {
            bool remote = !string.IsNullOrWhiteSpace(manifest) || !string.IsNullOrWhiteSpace(signature) || key;
#if UNITY_EDITOR
            if (remote)
                throw new InvalidOperationException("远端 C# 热更必须使用 IL2CPP Player 验证；编辑器直接使用已编译的 C#，请清空演示场景更新地址。");
            return await LoadAsync(null, null, null, lifetime);
#else
            var asset = Resources.Load<TextAsset>(PlayerBaseline.ResourceName);
            if (!asset) throw new InvalidDataException("Missing player baseline. Build using Tools/强更包.");
            var baseline = JsonUtility.FromJson<PlayerBaseline>(asset.text);
            if (baseline == null || baseline.formatVersion != 1 || string.IsNullOrWhiteSpace(baseline.baselineId) ||
                baseline.baselineId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || baseline.baselineId == "." || baseline.baselineId == "..")
                throw new InvalidDataException("Invalid built-in player baseline.");
            string storePath = Path.Combine(Application.persistentDataPath, "FoundationContent", baseline.baselineId);
            var journal = new CodeBootJournal(Path.Combine(storePath, "code-boot.state"));
            IDisposable lease;
            try { lease = await journal.AcquireStartupLeaseAsync(lifetime); }
            catch (TimeoutException error)
            {
                throw new StartupContentUnavailableException("另一个客户端仍在检查或安装更新。请等它完成后再重试。", error);
            }

            try
            {
                var content = await OpenVersionedAsync(baseline, storePath, journal, remote, manifest, signature,
                    key, lifetime, failedUpdate, approveDownload, downloadProgress);
                if (content != null && !content.RestartRequired)
                {
                    content.startupLease = lease;
                    lease = null;
                }
                return content;
            }
            finally { lease?.Dispose(); }
#endif
        }

#if !UNITY_EDITOR
        private static async Task<StartupContent> OpenVersionedAsync(PlayerBaseline baseline, string storePath,
            CodeBootJournal journal, bool remote, string manifest, string signature, TextAsset key,
            CancellationToken lifetime, Action<string> failedUpdate,
            Func<long, CancellationToken, Task<bool>> approveDownload, Action<ContentDownloadProgress> downloadProgress)
        {
            long? interrupted = journal.RecoverInterruptedBoot();
            if (interrupted.HasValue)
                failedUpdate("上次 C# 启动未完成，已隔离版本 " + interrupted.Value);
            bool minimal = baseline.requiresDownloadedContent;
            if (!remote && !minimal)
                return await LoadAsync(null, baseline, journal, lifetime);
            using var rsa = RSA.Create();
            var (manifestUri, signatureUri) = ConfigureSource(rsa, minimal, manifest, signature, key);
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            var unityContext = SynchronizationContext.Current ?? throw new InvalidOperationException("Start content on the Unity main thread.");
            var updater = new ContentUpdater(storePath, http, rsa, (int)FoundationProtocol.Version, SchemaVersion,
                () => AvailableBytes(storePath, unityContext));
            bool declined = false;
            StartupContent candidate = null;
            try
            {
                if (interrupted.HasValue && journal.LastHealthyVersion.HasValue)
                    await Task.Run(() => updater.ActivateReleaseAsync(journal.LastHealthyVersion.Value, lifetime), lifetime);
                var installed = await Task.Run(() => updater.InstallAsync(manifestUri, signatureUri, lifetime,
                    (release, token) => OnUnityThread(unityContext, async () =>
                    {
                        if (journal.IsRejected(release.Version))
                            throw new QuarantinedReleaseException();
                        candidate = await LoadAsync(release, baseline, journal, token);
                    }), approveDownload == null ? null : ConfirmAsync, downloadProgress,
                    minimal ? 0 : baseline.contentVersion), lifetime);
                lifetime.ThrowIfCancellationRequested();
                if (declined)
                    return null;
                if (installed == null)
                {
                    if (minimal)
                        throw new StartupContentUnavailableException("服务器未提供适用于此客户端的启动资源，请稍后重试。");
                    return await LoadAsync(null, baseline, journal, lifetime);
                }
                if (candidate == null)
                    throw new InvalidOperationException("Candidate was not preflighted.");
                candidate.RestartRequired = approveDownload != null && installed.WasActivated;
                return candidate;
            }
            catch (Exception error)
            {
                candidate?.Dispose();
                lifetime.ThrowIfCancellationRequested();
                bool quarantined = error is QuarantinedReleaseException;
                bool remoteUnavailable = error is HttpRequestException || error is OperationCanceledException;
                if (approveDownload != null && !quarantined && !(minimal && remoteUnavailable))
                {
                    if (minimal && !(error is StartupContentUnavailableException))
                        throw new StartupContentUnavailableException("启动资源更新失败，请检查网络和存储空间后重试。", error);
                    throw;
                }
                failedUpdate(error.Message);
                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                recovery.CancelAfter(TimeSpan.FromSeconds(30));
                if (minimal && remoteUnavailable)
                {
                    // After a completed update, the active release may not yet have a successful boot.
                    try
                    {
                        var current = await Task.Run(() => updater.GetActiveRelease(recovery.Token), recovery.Token);
                        if (current != null && !journal.IsRejected(current.Version))
                            return await LoadAsync(current, baseline, journal, recovery.Token);
                    }
                    catch (Exception recoveryError) when (!(recoveryError is OperationCanceledException))
                    {
                        failedUpdate("已安装资源无法启动：" + recoveryError.Message);
                    }
                }
                try
                {
                    long? healthy = journal.LastHealthyVersion;
                    var active = healthy.HasValue
                        ? await Task.Run(() => updater.ActivateReleaseAsync(healthy.Value, recovery.Token), recovery.Token)
                        : !minimal ? await Task.Run(() => updater.GetActiveRelease(recovery.Token), recovery.Token) : null;
                    if (active != null && !journal.IsRejected(active.Version))
                        return await LoadAsync(active, baseline, journal, recovery.Token);
                }
                catch (Exception recoveryError) when (!(recoveryError is OperationCanceledException))
                {
                    failedUpdate("本地回退资源不可用：" + recoveryError.Message);
                }
                if (minimal)
                    throw new StartupContentUnavailableException("此安装包需要已验证的启动资源，请检查网络后重试。", error);
                return await LoadAsync(null, baseline, journal, recovery.Token);
            }

            async Task<bool> ConfirmAsync(long bytes, CancellationToken token)
            {
                bool approved = false;
                await OnUnityThread(unityContext, async () =>
                {
                    token.ThrowIfCancellationRequested();
                    approved = await approveDownload(bytes, token);
                }).ConfigureAwait(false);
                declined = !approved;
                return approved;
            }
        }
#endif
        private static (Uri manifest, Uri signature) ConfigureSource(RSA rsa, bool minimal,
            string manifest, string signature, TextAsset key)
        {
            try
            {
                var sourceAsset = minimal ? Resources.Load<TextAsset>(MinimalContentResourceName) : key;
                var source = sourceAsset ? JsonUtility.FromJson<ContentSource>(sourceAsset.text) : null;
                if (minimal)
                {
                    manifest = source?.manifestUrl;
                    signature = source?.signatureUrl;
                }
                if (string.IsNullOrWhiteSpace(source?.modulus) || string.IsNullOrWhiteSpace(source.exponent) ||
                    !Uri.TryCreate(manifest, UriKind.Absolute, out var manifestUri) ||
                    !Uri.TryCreate(signature, UriKind.Absolute, out var signatureUri) ||
                    !IsTrustedTransport(manifestUri) || !IsTrustedTransport(signatureUri))
                {
                    throw new InvalidDataException("Content update requires HTTPS URLs and a pinned RSA public key.");
                }

                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Convert.FromBase64String(source.modulus),
                    Exponent = Convert.FromBase64String(source.exponent)
                });
                return (manifestUri, signatureUri);
            }
            catch (Exception error) when (minimal && (error is InvalidDataException || error is ArgumentException ||
                error is FormatException || error is CryptographicException))
            {
                throw new StartupContentUnavailableException("安装包缺少可信的资源下载配置，请重新获取客户端。", error);
            }
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
            try
            {
                code?.Dispose();
                Assets.Dispose();
            }
            finally { ReleaseStartupLease(); }
        }
    }
}