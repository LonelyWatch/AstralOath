using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using AstralOath.Shared;
using AstralOath.Shared.Updates;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Bootstrap
{
    internal sealed class StartupContent : IDisposable
    {
        private const int SchemaVersion = 1;

        [Serializable, Preserve]
        private sealed class PublicKey
        {
            public string modulus;
            public string exponent;
        }

        private StartupContent(YooAssetService assets, FoundationLua lua, long? releaseVersion)
        {
            Assets = assets;
            Lua = lua;
            ReleaseVersion = releaseVersion;
        }

        internal YooAssetService Assets { get; }
        internal FoundationLua Lua { get; }
        internal long? ReleaseVersion { get; }

        internal static async Task<StartupContent> OpenAsync(string manifest, string signature, TextAsset key,
            ILocaleService locale, CancellationToken lifetime, Action<string> failedUpdate)
        {
            if (string.IsNullOrWhiteSpace(manifest) && string.IsNullOrWhiteSpace(signature) && !key)
                return await LoadAsync(null, locale, lifetime);
            if (!Uri.TryCreate(manifest, UriKind.Absolute, out var manifestUri) ||
                !Uri.TryCreate(signature, UriKind.Absolute, out var signatureUri) ||
                !IsTrustedTransport(manifestUri) || !IsTrustedTransport(signatureUri) || !key)
                throw new InvalidDataException("Content update requires HTTPS manifest and signature URLs and a pinned RSA public key.");

            var parameters = JsonUtility.FromJson<PublicKey>(key.text);
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(parameters.modulus),
                Exponent = Convert.FromBase64String(parameters.exponent)
            });
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            var updater = new ContentUpdater(Path.Combine(Application.persistentDataPath, "FoundationContent"),
                http, rsa, (int)FoundationProtocol.Version, SchemaVersion);
            var unityContext = SynchronizationContext.Current ??
                throw new InvalidOperationException("Content must start from the Unity main thread.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            StartupContent loaded = null;
            try
            {
                await Task.Run(() => updater.InstallAsync(manifestUri, signatureUri, deadline.Token,
                    (release, token) => OnUnityThread(unityContext, async () =>
                    {
                        loaded = await LoadAsync(release, locale, token);
                    })), deadline.Token);
                lifetime.ThrowIfCancellationRequested();
                return loaded ?? throw new InvalidOperationException("Candidate content was not validated.");
            }
            catch (Exception error)
            {
                loaded?.Dispose();
                lifetime.ThrowIfCancellationRequested();
                failedUpdate(error.Message);

                using var recovery = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                recovery.CancelAfter(TimeSpan.FromSeconds(30));
                ContentRelease active = await Task.Run(() => updater.GetActiveRelease(recovery.Token), recovery.Token);
                if (active == null)
                    throw new InvalidOperationException("No verified content release is available.", error);
                return await LoadAsync(active, locale, recovery.Token);
            }
        }

        private static bool IsTrustedTransport(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps || (Debug.isDebugBuild && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

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

        private static async Task<StartupContent> LoadAsync(ContentRelease release, ILocaleService locale,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string root = release == null ? null : Path.Combine(release.DirectoryPath, "yooasset", YooAssetService.PackageName);
            var assets = new YooAssetService();
            FoundationLua lua = null;
            try
            {
                await assets.InitializeAsync(token, root);
                using var scope = new BindingScope(token);
                var binder = await assets.LoadAsync<TextAsset>(DemoWindow.BinderAddress, scope);
                string source = binder.text;
                using (var validation = new FoundationLua(source))
                    await DemoWindow.ValidateContentAsync(assets, new JsonConfigService(assets), validation, locale, scope);
                lua = new FoundationLua(source);
                return new StartupContent(assets, lua, release?.Version);
            }
            catch
            {
                try { lua?.Dispose(); }
                finally { assets.Dispose(); }
                throw;
            }
        }

        public void Dispose()
        {
            try { Lua.Dispose(); }
            finally { Assets.Dispose(); }
        }
    }
}