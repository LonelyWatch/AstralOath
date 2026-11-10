#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using AstralOath.Client.FoundationDemo;
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
        private const string ScenePath = "Assets/Game/Modules/FoundationDemo/Scenes/FoundationDemo.unity";

        [UnityTest]
        public IEnumerator DemoLoadsBundledContentAndReopens()
        {
            var scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                yield return null;
                var view = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<FoundationView>(true)).Single();
                var status = view.Status.GetComponent<TextMeshProUGUI>();
                yield return WaitFor(() => status.text.StartsWith("已加载 ", StringComparison.Ordinal));
                Assert.That(status.text, Does.Contain("AstralOath"));
                Assert.That(view.Title.GetComponent<TextMeshProUGUI>().text, Does.Contain("客户端地基"));
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

                var title = view.Title.GetComponent<TextMeshProUGUI>();
                title.ForceMeshUpdate();
                Assert.That(title.textInfo.characterCount, Is.EqualTo(title.text.Length));
                Assert.That(title.textInfo.characterInfo.Take(title.textInfo.characterCount)
                    .Select(character => character.character).ToArray(), Is.EqualTo(title.text.ToCharArray()));

                if (Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_LIVE_TEST") == "1")
                {
                    view.Echo.GetComponent<Button>().onClick.Invoke();
                    yield return WaitFor(() => status.text.StartsWith("服务端返回 ", StringComparison.Ordinal));
                    Assert.That(status.text, Does.Contain("序号 "));
                    Debug.Log("ASTRAL_EDITOR_ECHO_OK " + status.text);
                }

                view.Close.GetComponent<Button>().onClick.Invoke();
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
                    status.text.StartsWith("已加载 ", StringComparison.Ordinal));
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