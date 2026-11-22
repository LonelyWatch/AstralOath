#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    internal static class DevelopmentGmCapture
    {
        internal static IEnumerator BattleRoundTrip(GameObject home, BindingMap gm)
        {
            var previous = SceneManager.GetActiveScene();
            var battle = EditorSceneManager.LoadSceneInPlayMode(BattleOnlineLaunch.ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                yield return null;
                var host = battle.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>(true)).Single();
                home.GetComponent<Canvas>().enabled = false;
                gm.GetComponent<Canvas>().enabled = false; // The online launch suspends every home Canvas.
                SceneManager.SetActiveScene(battle);
                yield return null;
                Assert.That(gm.GetComponent<Canvas>().isActiveAndEnabled, Is.True);
                Assert.That(gm.gameObject.scene, Is.EqualTo(battle));
                bool paused = host.IsPaused;
                gm.Require<GameButton>("entry").onClick.Invoke();
                gm.Require<GameButton>("diagnostics").onClick.Invoke();
                yield return new WaitUntil(() => gm.Require<GameText>("status").text.Contains("下次开战"));
                yield return new WaitUntil(() => !gm.Require<GameText>("report").text.Contains("— FPS"));
                Canvas.ForceUpdateCanvases();
                var events = EventSystem.current;
                var hits = new List<RaycastResult>();
                events.RaycastAll(new PointerEventData(events) { position = new Vector2(Screen.width / 2f, Screen.height / 2f) }, hits);
                Assert.That(hits.Count, Is.GreaterThan(0));
                Assert.That(hits[0].gameObject.transform.IsChildOf(gm.transform), Is.True, "Modal touches must hit GM ahead of the battle HUD.");
                Assert.That(host.IsPaused, Is.EqualTo(paused));
                yield return Frame(host.gameObject, "battle-diagnostics");
                gm.Require<GameButton>("close").onClick.Invoke();
                Assert.That(gm.Require<GameObject>("panel").activeSelf, Is.False);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                home.GetComponent<Canvas>().enabled = true;
                SceneManager.UnloadSceneAsync(battle);
            }
            Assert.That(gm.transform.IsChildOf(home.transform), Is.True);
        }

        internal static IEnumerator Frame(GameObject root, string name)
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_GM_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) yield break;
            var canvas = root.GetComponent<Canvas>();
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var camera = oldCamera ? oldCamera : new GameObject("GM capture", typeof(Camera)).GetComponent<Camera>();
            var oldTarget = camera.targetTexture;
            var oldDistance = canvas.planeDistance;
            var target = RenderTexture.GetTemporary(1440, 810, 24);
            var frame = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var scaler = root.GetComponent<CanvasScaler>();
            bool addScaler = !scaler;
            try
            {
                if (addScaler)
                {
                    scaler = root.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920,1080);
                    scaler.matchWidthOrHeight = 1;
                }
                camera.targetTexture = target;
                if (!oldCamera) camera.clearFlags = CameraClearFlags.SolidColor;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                if (!oldCamera) canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                frame.Apply();
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                camera.targetTexture = oldTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(frame);
                if (!oldCamera) Object.Destroy(camera.gameObject);
                if (addScaler) Object.Destroy(scaler);
            }
            yield return null;
        }
    }
}
#endif
