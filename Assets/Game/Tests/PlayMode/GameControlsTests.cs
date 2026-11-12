using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class GameControlsTests
    {
        private sealed class Locale : ILocaleService
        {
            public string TextLocale { get; private set; } = "zh-Hans";
            public string VoiceLocale => "ja";
            public event Action TextChanged;
            public void SelectText(string value) { TextLocale = value; TextChanged?.Invoke(); }
            public void SelectVoice(string value) { }
            public string Resolve(string key) => TextLocale == "zh-Hans" ? "Alpha" : "Beta";
        }

        [UnityTest]
        public IEnumerator TextIsTheGraphicAndKeepsAuthoredTypographyDuringBinding()
        {
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            using var scope = new BindingScope(default);
            try
            {
                var child = new GameObject("Title", typeof(RectTransform), typeof(GameText));
                child.transform.SetParent(root.transform, false);
                var control = child.GetComponent<GameText>();
                var label = child.GetComponent<TextMeshProUGUI>();
                Assert.That(label, Is.SameAs(control), "Adding GameText must create the actual TMP graphic, not a companion.");
                Assert.That(child.GetComponents<TMP_Text>(), Has.Length.EqualTo(1));
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                label.richText = true;
                label.fontSize = 31;
                label.color = Color.cyan;
                var locale = new Locale();
                control.Bind(locale, scope, "label");
                Assert.That(label.enableWordWrapping, Is.False);
                Assert.That(label.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
                Assert.That(label.richText, Is.True);
                Assert.That(label.fontSize, Is.EqualTo(31));
                Assert.That(label.color, Is.EqualTo(Color.cyan));
                Assert.That(label.text, Is.EqualTo("Alpha"));
                locale.SelectText("en");
                Assert.That(label.text, Is.EqualTo("Beta"));
                yield return null;
                label.ForceMeshUpdate();
                Assert.That(label.textInfo.characterCount, Is.EqualTo(4));
                var canvas = root.GetComponent<Canvas>();
                Assert.That(GraphicRegistry.GetGraphicsForCanvas(canvas).Contains(label), Is.True);
                child.SetActive(false);
                Assert.That(GraphicRegistry.GetGraphicsForCanvas(canvas).Contains(label), Is.False);
                locale.SelectText("zh-Hans");
                Assert.That(label.text, Is.EqualTo("Beta"), "Disable must unsubscribe the old locale binding.");
                child.SetActive(true);
                control.Bind(locale, scope, "label");
                yield return null;
                label.ForceMeshUpdate();
                Assert.That(label.textInfo.characterCount, Is.EqualTo(5));
                Assert.That(GraphicRegistry.GetGraphicsForCanvas(canvas).Contains(label), Is.True);
                scope.Dispose();
                locale.SelectText("en");
                Assert.That(label.text, Is.EqualTo("Alpha"));
            }
            finally { UnityEngine.Object.Destroy(root); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ButtonIsTheSelectableAndCancelsPendingClickOnDisable()
        {
            var go = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(GameButton));
            var eventSystemObject = new GameObject("Events", typeof(EventSystem));
            using var scope = new BindingScope(default);
            var pending = new TaskCompletionSource<bool>();
            try
            {
                var control = go.GetComponent<GameButton>();
                var button = go.GetComponent<Button>();
                Assert.That(button, Is.SameAs(control), "Adding GameButton must create the actual Selectable, not a companion.");
                Assert.That(go.GetComponents<Button>(), Has.Length.EqualTo(1));
                button.targetGraphic = go.GetComponent<Image>();
                int calls = 0;
                CancellationToken request = default;
                control.Bind(scope, token => { calls++; request = token; return pending.Task; });
                var pointer = new PointerEventData(eventSystemObject.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
                button.OnPointerClick(pointer);
                button.OnPointerClick(pointer);
                button.OnSubmit(new BaseEventData(eventSystemObject.GetComponent<EventSystem>()));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(button.interactable, Is.False);
                Assert.That(Selectable.allSelectablesArray, Has.Member(button));
                go.SetActive(false);
                Assert.That(request.IsCancellationRequested, Is.True);
                Assert.That(button.interactable, Is.True);
                Assert.That(Selectable.allSelectablesArray, Has.No.Member(button));
                go.SetActive(true);
                int reboundCalls = 0;
                control.Bind(scope, _ => { reboundCalls++; return Task.CompletedTask; });
                button.OnSubmit(new BaseEventData(eventSystemObject.GetComponent<EventSystem>()));
                Assert.That(reboundCalls, Is.EqualTo(1), "Keyboard submission must continue to use Button semantics.");
                pending.SetResult(true);
                yield return null;
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(button.interactable, Is.True);
                scope.Dispose();
                button.onClick.Invoke();
                Assert.That(reboundCalls, Is.EqualTo(1), "Disposing the owner must remove its click listener.");
            }
            finally
            {
                pending.TrySetResult(true);
                UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(eventSystemObject);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator ButtonDeduplicatesCancelsAndRebinds()
        {
            var owner = new GameObject("Button", typeof(RectTransform), typeof(GameButton));
            try
            {
                var button = owner.GetComponent<Button>();
                var control = owner.GetComponent<GameButton>();
                var scope = new BindingScope(default);
                var finished = new TaskCompletionSource<bool>();
                int clicks = 0;
                bool cancelled = false;
                control.Bind(scope, async token =>
                {
                    ++clicks;
                    using var registration = token.Register(() =>
                    {
                        cancelled = true;
                        finished.TrySetCanceled();
                    });
                    await finished.Task;
                });
                button.onClick.Invoke();
                button.onClick.Invoke();
                Assert.That(clicks, Is.EqualTo(1));
                Assert.That(button.interactable, Is.False);
                owner.SetActive(false);
                Assert.That(cancelled, Is.True);
                scope.Dispose();
                owner.SetActive(true);
                using var reopen = new BindingScope(default);
                control.Bind(reopen, _ =>
                {
                    ++clicks;
                    return Task.CompletedTask;
                });
                button.onClick.Invoke();
                yield return null;
                Assert.That(clicks, Is.EqualTo(2));
                Assert.That(button.interactable, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(owner);
            }
        }

        [UnityTest]
        public IEnumerator TweenUsesUnscaledTimeAndRestoresInitialScaleOnClose()
        {
            var owner = new GameObject("Tween");
            owner.transform.localScale = Vector3.one * 2;
            float previousTimeScale = Time.timeScale;
            var animations = new DotweenAnimations();
            var scope = new BindingScope(default);
            try
            {
                Time.timeScale = 0;
                animations.Pulse(owner.transform, 1f, scope);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(owner.transform.localScale, Is.Not.EqualTo(Vector3.one * 2));
                scope.Dispose();
                Assert.That(owner.transform.localScale, Is.EqualTo(Vector3.one * 2));
            }
            finally
            {
                scope.Dispose();
                animations.Dispose();
                Time.timeScale = previousTimeScale;
                UnityEngine.Object.Destroy(owner);
            }
        }
    }
}
