using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Updates;
using UnityEngine;

namespace AstralOath.Client.Bootstrap
{
    public sealed class CharacterLabBootstrap : MonoBehaviour
    {
        public const string PackageName = "CharacterLab";
        [SerializeField] private Transform viewRoot;
        [SerializeField] private Transform worldRoot;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private GameText startupStatus;
        private BindingScope lifetime;
        private BindingScope diagnosticLifetime;
        private ICharacterLabEntry entry;
        private new FmodCharacterAudioService audio;
        private bool ready;
        private static readonly ILocaleService BootLocale = new LocaleService(
            new Dictionary<string, IReadOnlyDictionary<string, string>> {
                ["zh-Hans"] = new Dictionary<string, string> {
                    ["characterlab.boot.loading"] = "正在加载角色实验室…",
                    ["characterlab.boot.error"] = "角色实验室启动失败，请查看 Console：{0}",
                    ["characterlab.runtime.error"] = "角色实验室运行失败，请查看 Console：{0}"
                }
            }, "zh-Hans", "ja", "ja");
        public bool IsReady => ready;
        public bool IsAudioAvailable => audio?.IsAvailable == true;
        public string AudioDiagnostic => audio?.Diagnostic ?? "FMOD 尚未创建";
        public string StartupError { get; private set; }

        [Serializable] private sealed class BankCatalog { public string[] sharedBanks; }

        private async void Start()
        {
            var scope = new BindingScope(default);
            lifetime = scope;
            try
            {
                if (!viewRoot || !worldRoot || !previewCamera) throw new InvalidOperationException("角色实验室场景缺少启动引用");
                if (startupStatus) startupStatus.Bind(BootLocale, scope, "characterlab.boot.loading");
                var services = new ServiceRegistry(scope.Token);
                scope.Add(services.Dispose);
                var assets = new YooAssetService(PackageName);
                services.Add<IAssetService>(assets);
                await assets.InitializeAsync(scope.Token, null);
                scope.Token.ThrowIfCancellationRequested();
                services.Add<IConfigService>(new JsonConfigService(assets));
                services.Add<IAnimationService>(new DotweenAnimations());
                var catalogAsset = await assets.LoadAsync<TextAsset>("characterlab/catalog", scope);
                var catalog = JsonUtility.FromJson<BankCatalog>(catalogAsset.text);
                if (catalog?.sharedBanks == null) throw new InvalidOperationException("角色目录缺少声音库清单");
                audio = new FmodCharacterAudioService(scope.Token);
                services.Add<ICharacterAudioService>(audio);
                try { await audio.InitializeAsync(assets, catalog.sharedBanks); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { Debug.LogWarning("ASTRAL_CHARACTERLAB_AUDIO_UNAVAILABLE " + error.Message); }
                scope.Token.ThrowIfCancellationRequested();
#if UNITY_EDITOR
                entry = HotUpdateCodeLoader.CreateEditorCharacterLabEntry();
#else
                var baselineAsset = Resources.Load<TextAsset>(PlayerBaseline.ResourceName);
                if (!baselineAsset) throw new InvalidOperationException("缺少角色实验室母包身份，请使用实验室构建入口");
                var baseline = JsonUtility.FromJson<PlayerBaseline>(baselineAsset.text);
                if (baseline == null || baseline.formatVersion != 1 || !Guid.TryParseExact(baseline.baselineId, "N", out _))
                    throw new InvalidOperationException("角色实验室母包身份损坏");
                using (var loader = await HotUpdateCodeLoader.PrepareAsync(assets, baseline, scope))
                    entry = loader.CreateCharacterLabEntry();
#endif
                scope.Add(entry.Dispose);
                await entry.StartAsync(new CharacterLabLaunchContext(services, scope, viewRoot, worldRoot,
                    previewCamera, new SkeletonPlayerFactory(), audio));
                scope.Token.ThrowIfCancellationRequested();
                ready = true;
                if (startupStatus) startupStatus.gameObject.SetActive(false);
                Debug.Log("ASTRAL_CHARACTERLAB_READY audio=" + audio.IsAvailable);
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception error)
            {
                HandleFailure(error, "ASTRAL_CHARACTERLAB_FAILED", "characterlab.boot.error");
            }
        }

        private void Update()
        {
            if (!ready) return;
            try { audio?.Tick(); }
            catch (Exception error) { HandleAudioTickFailure(error); }
            // Audio is optional for browsing. Its failure must not cancel the entry lifetime.
            try { entry?.Tick(Time.unscaledDeltaTime); }
            catch (Exception error) { HandleFailure(error, "ASTRAL_CHARACTERLAB_TICK_FAILED", "characterlab.runtime.error"); }
        }

        private void HandleAudioTickFailure(Exception error)
        {
            Debug.LogWarning("ASTRAL_CHARACTERLAB_AUDIO_RUNTIME_UNAVAILABLE " + error);
            try { audio?.DisableAfterRuntimeFailure(error); }
            catch (Exception cleanup) { Debug.LogError("ASTRAL_CHARACTERLAB_AUDIO_CLEANUP_FAILED " + cleanup); }
        }

        private void HandleFailure(Exception error, string logTag, string messageKey)
        {
            ready = false;
            StartupError = error.ToString();
            Debug.LogError(logTag + " " + error);
            var previous = lifetime; lifetime = null; entry = null;
            DisposeScope(previous, logTag + "_CLEANUP_FAILED");
            DisposeScope(diagnosticLifetime, "ASTRAL_CHARACTERLAB_DIAGNOSTIC_CLEANUP_FAILED");
            diagnosticLifetime = new BindingScope(default);
            if (startupStatus)
            {
                startupStatus.gameObject.SetActive(true);
                startupStatus.Bind(BootLocale, diagnosticLifetime, messageKey, error.Message);
            }
        }

        private static void DisposeScope(BindingScope scope, string logTag)
        {
            try { scope?.Dispose(); }
            catch (Exception error) { Debug.LogError(logTag + " " + error); }
        }

        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();
        private void Shutdown()
        {
            ready = false;
            var previous = lifetime; lifetime = null;
            var diagnostic = diagnosticLifetime; diagnosticLifetime = null;
            entry = null; audio = null;
            // TMP submeshes read their parent's font material during OnDisable.
            // Disable the UI while those parents and assets are still alive.
            try { if (viewRoot) viewRoot.gameObject.SetActive(false); }
            catch (Exception error) { Debug.LogError("ASTRAL_CHARACTERLAB_UI_SHUTDOWN_FAILED " + error); }
            DisposeScope(previous, "ASTRAL_CHARACTERLAB_DESTROY_CLEANUP_FAILED");
            DisposeScope(diagnostic, "ASTRAL_CHARACTERLAB_DIAGNOSTIC_CLEANUP_FAILED");
        }
    }
}
