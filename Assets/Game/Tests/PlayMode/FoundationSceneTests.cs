#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class FoundationSceneTests
    {
        private const string Module = "Assets/Game/Modules/FoundationDemo/";
        private const string ScenePath = Module + "Scenes/FoundationDemo.unity";

        [Serializable]
        private sealed class Rows<T>
        {
            public T[] rows;
        }

        [Serializable]
        private sealed class DemoRow
        {
            public int id;
            public string titleKey;
            public string messageKey;
            public int displaySeconds;
        }

        [Serializable]
        private sealed class MessageRow
        {
            public string key;
            public string zhCn;
        }

        private static T[] ReadRows<T>(string path)
        {
            string json = AssetDatabase.LoadAssetAtPath<TextAsset>(Module + path).text;
            return JsonUtility.FromJson<Rows<T>>("{\"rows\":" + json + "}").rows;
        }

        [UnityTest]
        public IEnumerator DemoLoadsBundledContentAndReopens()
        {
            var demo = ReadRows<DemoRow>("Configs/demo-config.json").Single(row => row.id == 1);
            var locale = ReadRows<MessageRow>("Configs/demo-localization.json").ToDictionary(row => row.key, row => row.zhCn);
            string sample = AssetDatabase.LoadAssetAtPath<TextAsset>(Module + "Res/Text/demo-resource.txt").text.Trim();
            string ready = string.Format(locale["foundation.ready"], sample, demo.displaySeconds);
            var scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                BindingMap view = null;
                yield return WaitFor(() => view = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<BindingMap>(true)).SingleOrDefault());
                var status = view.Require<GameText>("status").GetComponent<TextMeshProUGUI>();
                yield return WaitFor(() => status.text == ready);
                var title = view.Require<GameText>("title").GetComponent<TextMeshProUGUI>();
                Assert.That(title.text, Is.EqualTo(locale[demo.titleKey]));
                Assert.That(view.Require<GameText>("description").GetComponent<TextMeshProUGUI>().text, Is.EqualTo(locale[demo.messageKey]));
                Assert.That(scene.GetRootGameObjects().Select(go => go.GetComponent<Camera>())
                    .Count(camera => camera && camera.isActiveAndEnabled), Is.EqualTo(1));
                const string fontPath = "Assets/Game/Modules/FoundationDemo/Res/Fonts/FoundationFont.asset";
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                Assert.That(font, Is.Not.Null);
                var labels = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<TextMeshProUGUI>(true)).ToArray();
                Assert.That(labels.Length, Is.EqualTo(7));
                foreach (var label in labels)
                {
                    Assert.That(label.font, Is.EqualTo(font), label.name);
                    Assert.That(label.fontSharedMaterial, Is.EqualTo(font.material), label.name);
                }

                title.ForceMeshUpdate();
                Assert.That(title.textInfo.characterCount, Is.EqualTo(title.text.Length));
                Assert.That(title.textInfo.characterInfo.Take(title.textInfo.characterCount)
                    .Select(character => character.character).ToArray(), Is.EqualTo(title.text.ToCharArray()));

                if (Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_LIVE_TEST") == "1")
                {
                    view.Require<GameButton>("echo").GetComponent<Button>().onClick.Invoke();
                    yield return WaitFor(() => status.text != ready && status.text != locale["foundation.pending"]);
                    Assert.That(status.text, Is.Not.EqualTo(locale["foundation.echo.error"]));
                    Debug.Log("ASTRAL_EDITOR_ECHO_OK " + status.text);
                }

                view.Require<GameButton>("close").GetComponent<Button>().onClick.Invoke();
                Assert.That(view.gameObject.activeSelf, Is.False);
                Button open = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Button>(true)).Single(button => button.name == "Open");
                var startup = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<RectTransform>(true))
                    .Single(rect => rect.name == "Startup Status");
                Assert.That(WorldBottom(startup), Is.GreaterThan(WorldTop((RectTransform)view.transform)));
                Assert.That(WorldBottom((RectTransform)open.transform), Is.GreaterThan(WorldTop(startup)));
                open.onClick.Invoke();
                yield return WaitFor(() => view.gameObject.activeSelf &&
                    status.text == ready);
            }
            finally
            {
                SceneManager.UnloadSceneAsync(scene);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EntryLoadsWindowAndCloseCancelsOnePendingEcho()
        {
            const string entryType = "AstralOath.Client.FoundationDemo.FoundationEntry, AstralOath.Client.FoundationDemo";
            var type = Type.GetType(entryType);
            Assert.That(type, Is.Not.Null, "The bootstrap must be able to resolve the C# entry by name.");
            using var entry = (IHotUpdateEntry)Activator.CreateInstance(type);
            using var lifetime = new BindingScope(default);
            using var services = new ServiceRegistry(lifetime.Token);
            var owner = new GameObject("Foundation entry test", typeof(RectTransform));
            try
            {
                var open = new GameObject("Open", typeof(RectTransform), typeof(Button), typeof(GameButton));
                var label = new GameObject("Open label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(GameText));
                var startup = new GameObject("Startup status", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(GameText));
                foreach (var item in new[] { open, label, startup })
                    item.transform.SetParent(owner.transform, false);

                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Module + "Res/Fonts/FoundationFont.asset");
                label.GetComponent<TMP_Text>().font = font;
                startup.GetComponent<TMP_Text>().font = font;
                var echo = new PendingEcho();
                services.Add<IAssetService>(new LocalAssets());
                services.Add<IConfigService>(new LocalConfig());
                services.Add<IAnimationService>(new DotweenAnimations());
                services.Add<IEchoService>(echo);

                var started = entry.StartAsync(new GameLaunchContext(services, lifetime, owner.transform,
                    open.GetComponent<GameButton>(), label.GetComponent<GameText>(), startup.GetComponent<GameText>(),
                    "zh-Hans", "ja", true));
                yield return WaitFor(() => started.IsCompleted);
                started.GetAwaiter().GetResult();

                var view = owner.GetComponentInChildren<BindingMap>();
                Assert.That(view.Require<GameText>("title").GetComponent<TMP_Text>().text, Is.EqualTo("基础设施演示"));
                Assert.That(label.GetComponent<TMP_Text>().text, Is.EqualTo("打开演示"));
                Assert.That(startup.GetComponent<TMP_Text>().text, Is.EqualTo("更新失败，已使用上个有效版本。"));
                Assert.That(services.Get<ILocaleService>().VoiceLocale, Is.EqualTo("ja"));
                var status = view.Require<GameText>("status").GetComponent<TMP_Text>();
                string ready = status.text;

                var echoButton = view.Require<GameButton>("echo").GetComponent<Button>();
                echoButton.onClick.Invoke();
                echoButton.onClick.Invoke();
                var request = entry.EchoAsync();
                Assert.That(echo.Calls, Is.EqualTo(1));
                Assert.That(echo.Message, Is.EqualTo("AstralOath"));
                Assert.That(status.text, Is.EqualTo("正在请求服务端…"));
                Assert.That(echoButton.interactable, Is.False);

                view.Require<GameButton>("close").GetComponent<Button>().onClick.Invoke();
                Assert.That(view.gameObject.activeSelf, Is.False);
                Assert.That(echo.Token.IsCancellationRequested, Is.True);
                Assert.That(echoButton.interactable, Is.True);
                open.GetComponent<Button>().onClick.Invoke();
                yield return WaitFor(() => view.gameObject.activeSelf && status.text == ready);
                echo.Complete();
                yield return WaitFor(() => request.IsCompleted);
                Assert.That(request.IsCanceled, Is.True);
                Assert.That(status.text, Is.EqualTo(ready));

                var next = entry.EchoAsync();
                yield return WaitFor(() => next.IsCompleted);
                Assert.That(next.GetAwaiter().GetResult().Sequence, Is.EqualTo(7));
                Assert.That(echo.Calls, Is.EqualTo(2));
                Assert.That(status.text, Is.EqualTo("服务端返回 AstralOath · 序号 7"));

                entry.Dispose();
                open.GetComponent<Button>().onClick.Invoke();
                Assert.That(view.gameObject.activeSelf, Is.False);
            }
            finally
            {
                entry.Dispose();
                UnityEngine.Object.Destroy(owner);
            }
            yield return null;
        }

        private sealed class LocalAssets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                scope.Token.ThrowIfCancellationRequested();
                string path = address switch
                {
                    "foundation/foundation-window" => "Res/Prefab/FoundationWindow.prefab",
                    "foundation/demo-resource" => "Res/Text/demo-resource.txt",
                    _ => throw new ArgumentOutOfRangeException(nameof(address), address, null)
                };
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>(Module + path));
            }
        }

        private sealed class LocalConfig : IConfigService
        {
            public Task<T> ReadAsync<T>(string address, BindingScope scope) where T : class
            {
                scope.Token.ThrowIfCancellationRequested();
                string path = address switch
                {
                    "foundation/demo-localization" => "Configs/demo-localization.json",
                    "foundation/demo-config" => "Configs/demo-config.json",
                    _ => throw new ArgumentOutOfRangeException(nameof(address), address, null)
                };
                string json = AssetDatabase.LoadAssetAtPath<TextAsset>(Module + path).text.Trim();
                return Task.FromResult(JsonUtility.FromJson<T>("{\"rows\":" + json + "}"));
            }
        }

        private sealed class PendingEcho : IEchoService
        {
            private readonly TaskCompletionSource<EchoResult> pending = new TaskCompletionSource<EchoResult>();

            public int Calls { get; private set; }
            public CancellationToken Token { get; private set; }
            public string Message { get; private set; }

            public Task<EchoResult> EchoAsync(string message, CancellationToken cancellation)
            {
                Calls++;
                Token = cancellation;
                Message = message;
                return pending.Task;
            }

            public void Complete() => pending.SetResult(new EchoResult("AstralOath", 7));
        }

        private static float WorldBottom(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[0].y;
        }

        private static float WorldTop(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[1].y;
        }

        private static IEnumerator WaitFor(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (!ready() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(ready(), Is.True, "Foundation demo did not reach the expected visible state.");
        }
    }
}
#endif
