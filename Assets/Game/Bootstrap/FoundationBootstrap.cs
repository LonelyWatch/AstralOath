using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralOath.Client.Bootstrap
{
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        [SerializeField] private Transform viewRoot;
        [SerializeField] private GameButton open;
        [SerializeField] private GameText openLabel;
        [SerializeField] private GameText startupStatus;
        [SerializeField] private AudioSource audioOutput;
        [SerializeField] private string baseUrl = "http://127.0.0.1:5080";
        [SerializeField] private string textLocale = "zh-Hans";
        [SerializeField] private string voiceLocale = "ja";
        [SerializeField] private string manifestUrl;
        [SerializeField] private string signatureUrl;
        [SerializeField] private TextAsset contentPublicKey;

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BootMessages =
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["zh-Hans"] = new Dictionary<string, string>
                {
                    ["foundation.loading"] = "正在加载演示资源…",
                    ["foundation.load.error"] = "地基初始化失败，请查看日志。"
                }
            };
        private BindingScope lifetime;
        private IHotUpdateEntry entry;

        private async void Start()
        {
            bool startupSmoke = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-astral-startup-smoke");
            bool automation = startupSmoke || Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-astral-demo-echo");
            var scope = new BindingScope(default);
            var bootLocale = new LocaleService(BootMessages, "zh-Hans", "ja", "ja");
            lifetime = scope;
            try
            {
                var services = new ServiceRegistry(scope.Token);
                scope.Add(services.Dispose);
                startupStatus.Bind(bootLocale, scope, "foundation.loading");
                bool recovered = false;
                var content = await StartupContent.OpenAsync(manifestUrl, signatureUrl, contentPublicKey,
                    scope.Token, message =>
                    {
                        recovered = true;
                        Debug.LogWarning("Foundation content update: " + message);
                    });
                if (scope.Token.IsCancellationRequested)
                {
                    content.Dispose();
                    scope.Token.ThrowIfCancellationRequested();
                }

                services.Add(content);
                services.Add<IAssetService>(content.Assets);
                services.Add<IConfigService>(new JsonConfigService(content.Assets));
                services.Add<IAnimationService>(new DotweenAnimations());
                string url = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
                services.Add<IEchoService>(new ProtobufEchoService(string.IsNullOrWhiteSpace(url) ? baseUrl : url));
                entry = content.CreateEntry();
                scope.Add(entry.Dispose);
                await entry.StartAsync(new GameLaunchContext(services, scope, viewRoot, open, openLabel,
                    startupStatus, textLocale, voiceLocale, recovered));
                scope.Token.ThrowIfCancellationRequested();
                services.Add<IAudioService>(new VoiceService(audioOutput, content.Assets, services.Get<ILocaleService>()));
                content.ConfirmStartup();
                Debug.Log($"ASTRAL_CODE_BOOT_OK release={content.ReleaseVersion?.ToString() ?? "builtin"} recovered={recovered}");
                if (content.ReleaseVersion.HasValue)
                    Debug.Log($"ASTRAL_CONTENT_ACTIVE version={content.ReleaseVersion.Value} recovered={recovered}");
                if (startupSmoke)
                {
                    Quit(0);
                    return;
                }
                if (automation && !scope.Token.IsCancellationRequested)
                {
                    EchoResult echo = await entry.EchoAsync();
                    if (echo == null)
                        throw new InvalidOperationException("Echo request failed.");
                    Debug.Log($"ASTRAL_ECHO_OK sequence={echo.Sequence}");
                    string[] arguments = Environment.GetCommandLineArgs();
                    int screenshot = Array.IndexOf(arguments, "-astral-demo-screenshot");
                    if (screenshot < 0)
                        Quit(0);
                    else if (screenshot + 1 < arguments.Length)
                        StartCoroutine(CaptureSmokeScreenshot(arguments[screenshot + 1]));
                    else
                        throw new ArgumentException("Provide a PNG path after -astral-demo-screenshot.");
                }
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (scope.Token.IsCancellationRequested)
                    return;
                startupStatus.gameObject.SetActive(true);
                startupStatus.Bind(bootLocale, scope, "foundation.load.error");
                Debug.LogException(error, this);
                if (automation)
                {
                    Debug.LogError("ASTRAL_ECHO_FAILED " + error.Message);
                    Quit(1);
                }
            }
        }

        private static IEnumerator CaptureSmokeScreenshot(string destination)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!SplashScreen.isFinished && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!SplashScreen.isFinished)
            {
                Debug.LogError("Foundation screenshot timed out waiting for the splash screen.");
                Quit(1);
                yield break;
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D frame = null;
            int exitCode = 0;
            try
            {
                string path = Path.GetFullPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                frame = ScreenCapture.CaptureScreenshotAsTexture();
                var pixels = frame.GetPixels32();
                Color32 first = pixels[0];
                if (!Array.Exists(pixels, pixel => pixel.r != first.r || pixel.g != first.g || pixel.b != first.b))
                    throw new InvalidDataException("Foundation screenshot has no visible detail.");
                File.WriteAllBytes(path, frame.EncodeToPNG());
                Debug.Log("ASTRAL_SCREENSHOT_OK " + path);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                exitCode = 1;
            }
            finally
            {
                if (frame)
                    Destroy(frame);
            }
            Quit(exitCode);
        }



        private void OnDestroy()
        {
            entry = null;
            try { lifetime?.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private static void Quit(int exitCode)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
