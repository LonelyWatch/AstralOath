#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleScenePreviewTests
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private Scene scene;

        [UnityTest]
        public IEnumerator AuthoredSceneShowsOriginalActorsAndHud()
        {
            Assert.That(File.Exists(ScenePath), Is.True, "The authored battle scene must be saved in the project.");
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var actors = roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>())
                .Where(mesh => mesh.sharedMesh && mesh.sharedMesh.name == "Skeleton dynamic mesh").ToArray();
            Assert.That(actors.Length, Is.GreaterThanOrEqualTo(5));
            foreach (var actor in actors)
                Assert.That(actor.sharedMesh.vertexCount, Is.GreaterThan(0), actor.transform.parent.name);

            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            map.Validate();
            foreach (string key in new[] { "skill1", "skill2", "skill3", "auto", "speed", "pause", "soulBurn" })
                Assert.That(map.Require<GameButton>(key).targetGraphic, Is.Not.Null, key);
            Assert.That(map.Require<Image>("activePortrait").sprite, Is.Not.Null);
            Assert.That(map.Require<GameText>("wave").text, Is.Not.Empty);

            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Single(item => item.name == "Battle Camera");
            Assert.That(camera.pixelRect.width / camera.pixelRect.height, Is.EqualTo(16f / 9f).Within(0.01f));
            foreach (var actor in actors)
            {
                Vector3 point = camera.WorldToViewportPoint(actor.transform.TransformPoint(actor.sharedMesh.bounds.center));
                Assert.That(point.x, Is.InRange(0f, 1f), actor.transform.parent.name);
                Assert.That(point.y, Is.InRange(0f, 1f), actor.transform.parent.name);
            }

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
                Capture(camera, output);

            var first = actors[0];
            first.transform.parent.gameObject.SetActive(false);
            yield return null;
            Assert.That(!first, Is.True, "Disabling an actor must dispose its Spine mesh object.");
        }

        private static void Capture(Camera camera, string directory)
        {
            Directory.CreateDirectory(directory);
            var target = RenderTexture.GetTemporary(1920, 1080, 24);
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousRect = camera.rect;
            try
            {
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, "battle-scene-1920x1080.png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(texture);
            }
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
