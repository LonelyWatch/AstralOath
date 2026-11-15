using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests.PlayMode
{
    public sealed class EntryScreenTests
    {
        [UnityTest]
        public IEnumerator VerifiedUpdateWaitsForRestartConfirmation()
        {
            var canvas = new GameObject("Restart test canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var type = typeof(FoundationBootstrap).Assembly.GetType("AstralOath.Client.Bootstrap.EntryScreen");
                Assert.That(type, Is.Not.Null);
                var showRestart = type.GetMethod("ShowRestartAsync");
                Assert.That(showRestart, Is.Not.Null);
                using var screen = (IDisposable)Activator.CreateInstance(type,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { canvas.transform, null }, null);
                var prompt = (Task)showRestart.Invoke(screen, new object[] { CancellationToken.None });
                Assert.That(prompt.IsCompleted, Is.False);
                var message = Array.Find(canvas.GetComponentsInChildren<GameText>(),
                    candidate => candidate.name == "Restart instructions");
                Assert.That(message?.text, Does.Contain("重新打开"));
                var confirm = Array.Find(canvas.GetComponentsInChildren<GameButton>(),
                    candidate => candidate.name == "Quit after hot update");
                Assert.That(confirm, Is.Not.Null);
                confirm.onClick.Invoke();
                Assert.That(prompt.IsCompletedSuccessfully, Is.True);
                Assert.That(confirm.IsInteractable(), Is.False);
                yield return null;
            }
            finally { UnityEngine.Object.Destroy(canvas); }
        }

        [UnityTest]
        public IEnumerator PatchCancellationDoesNotAcceptDownloadAndHiddenScreenCancelsPendingPrompt()
        {
            var canvas = new GameObject("Test canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var type = typeof(FoundationBootstrap).Assembly.GetType(
                    "AstralOath.Client.Bootstrap.EntryScreen");
                Assert.That(type, Is.Not.Null, "The update prompt belongs in the stable startup assembly.");
                using var screen = (IDisposable)Activator.CreateInstance(type,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { canvas.transform, null }, null);
                var consent = type.GetMethod("ConfirmDownloadAsync");
                var prompt = (Task<bool>)consent.Invoke(screen, new object[] { 1048576L, CancellationToken.None });
                Assert.That(prompt.IsCompleted, Is.False);

                GameButton cancel = Array.Find(canvas.GetComponentsInChildren<GameButton>(),
                    candidate => candidate.name == "Cancel download");
                Assert.That(cancel, Is.Not.Null);
                cancel.onClick.Invoke();
                cancel.onClick.Invoke();
                Assert.That(prompt.Result, Is.False);
                yield return null;

                var next = (Task<bool>)consent.Invoke(screen, new object[] { 1048576L, CancellationToken.None });
                Assert.That(next.IsCompleted, Is.False);
                type.GetMethod("Hide").Invoke(screen, null);
                Assert.That(next.IsCanceled, Is.True);
                Assert.That(canvas.GetComponentsInChildren<GameButton>(), Is.Empty);
            }
            finally { UnityEngine.Object.Destroy(canvas); }
        }
    }
}