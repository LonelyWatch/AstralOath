#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using AstralOath.Client.Common;
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
