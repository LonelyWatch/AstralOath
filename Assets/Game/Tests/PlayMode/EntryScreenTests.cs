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