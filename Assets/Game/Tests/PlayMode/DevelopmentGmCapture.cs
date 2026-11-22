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
        internal static void DragAndTapEntry(BindingMap gm)
        {
            var button = gm.Require<GameButton>("entry");
            var entry = (RectTransform)button.transform;
            var parent = (RectTransform)entry.parent;
            var raycaster = gm.GetComponent<GraphicRaycaster>();
            var camera = raycaster.eventCamera;
            Canvas.ForceUpdateCanvases();
            Vector2 Center() => RectTransformUtility.WorldToScreenPoint(camera, entry.TransformPoint(entry.rect.center));
            var center = Center();
            var press = center + new Vector2(-5, 3);
            var desired = RectTransformUtility.WorldToScreenPoint(camera, parent.TransformPoint(parent.rect.center));
            var pointer = new PointerEventData(EventSystem.current)
            {
                pointerId = -1, position = press, pressPosition = press, eligibleForClick = true,
                pointerPressRaycast = new RaycastResult { module = raycaster }
            };
            var target = button.gameObject;
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
            pointer.position = press + desired - center;
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.dragHandler);
            Assert.That(Vector2.Distance(Center(), desired), Is.LessThan(2f), "Dragging should preserve the pointer's offset on the badge.");
            pointer.position += new Vector2(-Screen.width * 2f, Screen.height * 2f);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.dragHandler);
            var corners = new Vector3[4];
            entry.GetWorldCorners(corners);
            var bounds = parent.rect;
            foreach (var point in corners)
            {
                var local = parent.InverseTransformPoint(point);
                Assert.That(local.x, Is.InRange(bounds.xMin - .1f, bounds.xMax + .1f));
                Assert.That(local.y, Is.InRange(bounds.yMin - .1f, bounds.yMax + .1f));
            }
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            if (pointer.eligibleForClick) ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.endDragHandler);
            Assert.That(gm.Require<GameObject>("panel").activeSelf, Is.False, "Releasing a drag should leave the page visible.");
            Assert.That(Vector2.Distance(Center(), center), Is.GreaterThan(10f));
            pointer.position = pointer.pressPosition = Center();
            pointer.eligibleForClick = true;
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(gm.Require<GameObject>("panel").activeSelf, Is.True, "Tapping the relocated badge should open GM.");
        }

        internal static IEnumerator BattleRoundTrip(GameObject home, BindingMap gm)
        {
            var previous = SceneManager.GetActiveScene();
            var entry = (RectTransform)gm.Require<GameButton>("entry").transform;
            Vector2 movedAnchor = entry.anchorMin;
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
                Assert.That(entry.anchorMin, Is.EqualTo(movedAnchor), "The relocated entry should keep its position in battle.");
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
            Assert.That(entry.anchorMin, Is.EqualTo(movedAnchor));
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
