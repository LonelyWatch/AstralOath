using System;
using System.Collections;
using System.IO;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using UnityEngine;

namespace AstralOath.Client.Bootstrap
{
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        [SerializeField] private FoundationView view;
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

        private const string Demo = "foundation";
        private BindingScope lifetime;
        private FoundationLua lua;

        private async void Start()
        {
            bool automation = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-astral-demo-echo");
            var scope = new BindingScope(default);
            lifetime = scope;
            try
            {
                var services = new ServiceRegistry(scope.Token);
                scope.Add(services.Dispose);
                services.Add<ILocaleService>(new LocaleService(DemoMessages.Text, textLocale, voiceLocale, "ja", "zh-Hans"));
                var locale = services.Get<ILocaleService>();
                startupStatus.Bind(locale, scope, "foundation.loading");
                bool recovered = false;
                var content = await StartupContent.OpenAsync(manifestUrl, signatureUrl, contentPublicKey, locale,
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
                if (content.ReleaseVersion.HasValue)
                    Debug.Log($"ASTRAL_CONTENT_ACTIVE version={content.ReleaseVersion.Value} recovered={recovered}");
                lua = content.Lua;
                services.Add<IConfigService>(new JsonConfigService(content.Assets));
                services.Add<IAnimationService>(new DotweenAnimations());
                services.Add<IAudioService>(new VoiceService(audioOutput, content.Assets, locale));
                string url = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
                services.Add<IEchoService>(new ProtobufEchoService(string.IsNullOrWhiteSpace(url) ? baseUrl : url));
                var windows = new WindowCoordinator(scope.Token);
                services.Add(windows);
                var window = new DemoWindow(view, lua, content.Assets, services.Get<IConfigService>(), locale,
                    services.Get<IEchoService>(), services.Get<IAnimationService>(), scope.Token);
                windows.Register(Demo, window);
                openLabel.Bind(locale, scope, "foundation.open");
                open.Bind(scope, _ => windows.OpenAsync(Demo));
                if (recovered)
                    startupStatus.Bind(locale, scope, "foundation.update.recovered");
                else
                    startupStatus.gameObject.SetActive(false);
                await windows.OpenAsync(Demo);

                if (automation && !scope.Token.IsCancellationRequested)
                {
                    EchoResult echo = await window.EchoAsync();
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
                var locale = new LocaleService(DemoMessages.Text, "zh-Hans", "ja", "ja");
                startupStatus.Bind(locale, scope, "foundation.load.error");
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
            yield return new WaitForEndOfFrame();
            Texture2D frame = null;
            int exitCode = 0;
            try
            {
                string path = Path.GetFullPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                frame = ScreenCapture.CaptureScreenshotAsTexture();
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

        private void Update() => lua?.Tick();

        private void OnDestroy()
        {
            lua = null;
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