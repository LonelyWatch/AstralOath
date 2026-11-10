using System;
using System.Collections;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class GameControlsTests
    {
        [UnityTest]
        public IEnumerator ButtonDeduplicatesCancelsAndRebinds()
        {
            var owner = new GameObject("Button", typeof(RectTransform), typeof(Button), typeof(GameButton));
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