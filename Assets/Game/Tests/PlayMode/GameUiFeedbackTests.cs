using System;
using System.Collections;
using System.Reflection;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class GameUiFeedbackTests
    {
        [UnityTest]
        public IEnumerator PressDimsTheCompleteButtonAndRestoresEachTint()
        {
            var canvas = new GameObject("Button press", typeof(Canvas));
            var events = new GameObject("Press events", typeof(EventSystem));
            var item = new GameObject("Draw", typeof(RectTransform), typeof(Image), typeof(GameButton));
            item.transform.SetParent(canvas.transform, false);
            var price = new GameObject("Price", typeof(RectTransform), typeof(GameText)).GetComponent<GameText>();
            price.transform.SetParent(item.transform, false);
            var nested = new GameObject("Independent button", typeof(RectTransform), typeof(Image), typeof(GameButton));
            nested.transform.SetParent(item.transform, false);
            var thumbnail = new GameObject("Passive equipment card", typeof(RectTransform), typeof(Image), typeof(GameButton));
            thumbnail.transform.SetParent(item.transform, false);
            thumbnail.GetComponent<GameButton>().enabled = false;
            try
            {
                var button = item.GetComponent<GameButton>();
                button.targetGraphic = item.GetComponent<Image>();
                button.transition = Selectable.Transition.SpriteSwap;
                var tint = new Color(.9f, .72f, .37f, .64f);
                price.canvasRenderer.SetColor(tint);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>());
                button.OnPointerEnter(pointer);
                button.OnPointerDown(pointer);
                Assert.That(button.image.canvasRenderer.GetColor().r, Is.LessThan(.8f));
                var held = price.canvasRenderer.GetColor();
                Assert.That(held.r, Is.LessThan(tint.r * .8f));
                Assert.That(held.a, Is.EqualTo(tint.a).Within(.005f));
                Assert.That(nested.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(Color.white));
                Assert.That(thumbnail.GetComponent<Image>().canvasRenderer.GetColor().r, Is.LessThan(.8f));
                button.OnPointerUp(pointer);
                Assert.That(price.canvasRenderer.GetColor().r, Is.EqualTo(tint.r).Within(.005f));
                Assert.That(button.image.canvasRenderer.GetColor(), Is.EqualTo(Color.white));
                Assert.That(thumbnail.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(Color.white));

                button.OnSubmit(new BaseEventData(events.GetComponent<EventSystem>()));
                Assert.That(price.canvasRenderer.GetColor().r, Is.LessThan(tint.r * .8f));
                item.SetActive(false);
                item.SetActive(true);
                Assert.That(price.canvasRenderer.GetColor().g, Is.EqualTo(tint.g).Within(.005f));
                yield return null;
            }
            finally
            {
                Object.Destroy(canvas);
                Object.Destroy(events);
            }
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator AuthoredPulseBeginsFilledThenOpensIntoARing()
        {
            const string resources = "Assets/Game/Common/Res/";
            var atlas = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(resources + "Textures/UiTouchPulse.png");
            var blend = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(resources + "Materials/UiTouchPulse.mat");
            Assert.That(atlas, Is.Not.Null, "The native weighted attachment animation must be imported.");
            Assert.That(atlas.width, Is.EqualTo(1792));
            Assert.That(atlas.height, Is.EqualTo(896));
            Assert.That(blend.shader.isSupported, Is.True);
            var target = new GameObject("Native feedback canvas", typeof(Canvas), typeof(GameUiFeedback));
            var cameraObject = new GameObject("Native feedback test camera", typeof(Camera));
            var canvas = target.GetComponent<Canvas>();
            var camera = cameraObject.GetComponent<Camera>();
            var render = new RenderTexture(320, 240, 16, RenderTextureFormat.ARGB32);
            var frame = new Texture2D(320, 240, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.enabled = false;
                camera.orthographic = true;
                camera.targetTexture = render;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var feedback = target.GetComponent<GameUiFeedback>();
                feedback.Configure(null, null, null, null, null, null, null, atlas, blend);
                var type = typeof(GameUiFeedback);
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetMethod("ShowAt", Private).Invoke(feedback, new object[] { new Vector2(160, 120) });
                var graphic = target.GetComponentInChildren<RawImage>();
                Assert.That(graphic, Is.Not.Null);
                Assert.That(graphic.raycastTarget, Is.False);
                Assert.That(graphic.material, Is.EqualTo(blend));
                var pulse = ((Array)type.GetField("pulses", Private).GetValue(feedback)).GetValue(0);
                float start = (float)pulse.GetType().GetField("Start", Private).GetValue(pulse);
                var advance = type.GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic);

                void Capture(float at, string filename)
                {
                    advance.Invoke(null, new[] { pulse, (object)(start + at) });
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = render;
                    frame.ReadPixels(new Rect(0, 0, 320, 240), 0, 0);
                    frame.Apply();
                    string directory = Environment.GetEnvironmentVariable("ASTRAL_FEEDBACK_CAPTURE_DIR");
                    if (!string.IsNullOrEmpty(directory)) System.IO.File.WriteAllBytes(
                        System.IO.Path.Combine(directory, filename), frame.EncodeToPNG());
                }

                Capture(.035f, "native-click-disc.png");
                Assert.That(frame.GetPixel(162, 120).b, Is.GreaterThan(.30f), "Native weighted inner vertices fill the early disc.");
                Capture(.275f, "native-click-ring.png");
                Assert.That(frame.GetPixel(162, 120).b, Is.LessThan(.055f), "Weighted inner vertices open the ring center.");
                float peak = 0;
                for (int x = 170; x < 225; x++) peak = Mathf.Max(peak, frame.GetPixel(x, 120).b);
                Assert.That(peak, Is.GreaterThan(.30f), "The late ring keeps a visible luminous edge.");
                Capture(.44f, "native-click-particles.png");
                int litParticles = 0;
                for (int y = 68; y < 172; y++)
                    for (int x = 108; x < 212; x++)
                        if (frame.GetPixel(x, y).b > .04f)
                            litParticles++;
                Assert.That(litParticles, Is.InRange(4, 400), "Sparse particles stay visible briefly after the ring fades.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(frame);
                Object.Destroy(render);
                Object.Destroy(target);
                Object.Destroy(cameraObject);
            }
        }
#endif

        [UnityTest]
        public IEnumerator BlockedButtonPlaysDeniedSoundWithoutDispatchingClick()
        {
            var canvas = new GameObject("Blocked feedback test canvas", typeof(Canvas), typeof(GameUiFeedback), typeof(AudioListener));
            var events = new GameObject("Blocked feedback events", typeof(EventSystem));
            var item = new GameObject("Blocked button", typeof(RectTransform), typeof(Image), typeof(GameButton));
            var normal = AudioClip.Create("normal feedback", 24000, 1, 48000, false);
            var cancel = AudioClip.Create("cancel feedback", 24000, 1, 48000, false);
            var blocked = AudioClip.Create("blocked feedback", 24000, 1, 48000, false);
            try
            {
                item.transform.SetParent(canvas.transform, false);
                var feedback = canvas.GetComponent<GameUiFeedback>();
                feedback.Configure(null, null, normal, null, cancel, blocked: blocked);
                var resolve = typeof(GameUiFeedback).GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(resolve.Invoke(feedback, new object[] { GameUiFeedback.Sound.Back }), Is.SameAs(cancel));
                Assert.That(resolve.Invoke(feedback, new object[] { GameUiFeedback.Sound.Cancel }), Is.SameAs(cancel));
                Assert.That(resolve.Invoke(feedback, new object[] { GameUiFeedback.Sound.Confirm }), Is.SameAs(normal));
                var button = item.GetComponent<GameButton>();
                button.interactable = false;
                int activations = 0;
                button.onClick.AddListener(() => activations++);
                var mouse = new PointerEventData(events.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(item, mouse, ExecuteEvents.pointerClickHandler);
                Assert.That(canvas.GetComponent<AudioSource>(), Is.Null);

                button.BlockedFeedback = true;
                ExecuteEvents.Execute(item, mouse, ExecuteEvents.pointerClickHandler);
                Assert.That(activations, Is.Zero);
                Assert.That(canvas.GetComponent<AudioSource>(), Is.Not.Null);
                Assert.That(resolve.Invoke(feedback, new object[] { GameUiFeedback.Sound.Blocked }), Is.SameAs(blocked));
                yield return null;
                Assert.That(UiAudioLifetimeTests.Playing(blocked), Is.Not.Empty);
            }
            finally
            {
                Object.Destroy(item);
                Object.Destroy(events);
                Object.Destroy(canvas);
                Object.Destroy(normal);
                Object.Destroy(cancel);
                Object.Destroy(blocked);
            }
        }
        [UnityTest]
        public IEnumerator ACanvasReusesNonInteractiveClickGlows()
        {
            var target = new GameObject("Feedback test canvas", typeof(Canvas));
            target.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var texture = Texture2D.whiteTexture;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f);
            try
            {
                var feedback = target.AddComponent<GameUiFeedback>();
                feedback.Configure(sprite, sprite, null, null, null);
                var track = typeof(GameUiFeedback).GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(track, Is.Not.Null);
                var center = new Vector2(Screen.width * .5f, Screen.height * .5f);
                track.Invoke(feedback, new object[] { 9, center, true, false, false });
                track.Invoke(feedback, new object[] { 9, center + Vector2.right * 100, false, false, false });
                track.Invoke(feedback, new object[] { 9, center, false, true, false });
                Assert.That(target.GetComponentsInChildren<Image>(), Is.Empty, "A drag returning to its starting point cannot play a click pulse.");
                for (int i = 0; i < 20; i++)
                {
                    track.Invoke(feedback, new object[] { i, center, true, false, false });
                    track.Invoke(feedback, new object[] { i, center, false, true, false });
                }
                var images = target.GetComponentsInChildren<Image>();
                Assert.That(images.Length, Is.EqualTo(20), $"Canvas: {target.GetComponent<Canvas>().renderMode}, rect: {((RectTransform)target.transform).rect}, children: {target.transform.childCount}, rendered: {target.GetComponentsInChildren<Image>(true).Length}");
                foreach (var image in images) Assert.That(image.raycastTarget, Is.False);
                yield return new WaitForSecondsRealtime(.65f);
                Assert.That(target.GetComponentsInChildren<Image>(), Is.Empty);
            }
            finally
            {
                Object.Destroy(target);
                Object.Destroy(sprite);
            }
        }
    }
}
