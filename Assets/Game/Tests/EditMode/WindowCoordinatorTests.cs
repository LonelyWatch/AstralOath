using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class WindowCoordinatorTests
    {
        [Test]
        public void ReplacingPageClosesOverlayBeforePageAndCancelsTheirWork()
        {
            using var windows = new WindowCoordinator(CancellationToken.None);
            var closed = new List<string>();
            CancellationToken page = default, modal = default;
            windows.ShowPageAsync(scope =>
            {
                page = scope.Token;
                scope.Add(() => closed.Add("page"));
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            windows.ShowOverlay("create", scope =>
            {
                modal = scope.Token;
                scope.Add(() => closed.Add("modal"));
            });
            windows.ShowPageAsync(_ => Task.CompletedTask).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "modal", "page" }, closed);
            Assert.That(page.IsCancellationRequested && modal.IsCancellationRequested, Is.True);
            Assert.That(windows.IsOverlayOpen("create"), Is.False);
        }

        [Test]
        public void OnlyCurrentOverlayCanBeClosedByItsBinder()
        {
            using var windows = new WindowCoordinator(CancellationToken.None);
            windows.ShowPageAsync(_ => Task.CompletedTask).GetAwaiter().GetResult();
            CancellationToken first = default, second = default;
            windows.ShowOverlay("search", scope => first = scope.Token);
            windows.ShowOverlay("filter", scope => second = scope.Token);
            windows.CloseOverlay("search");
            Assert.That(first.IsCancellationRequested, Is.True);
            Assert.That(second.IsCancellationRequested, Is.False);
            Assert.That(windows.IsOverlayOpen("filter"), Is.True);
            windows.CloseOverlay("filter");
            Assert.That(second.IsCancellationRequested, Is.True);
        }

        [Test]
        public void LateFailureOfOldPageDoesNotCloseNewPage()
        {
            using var windows = new WindowCoordinator(CancellationToken.None);
            var oldWork = new TaskCompletionSource<bool>();
            Task oldPage = windows.ShowPageAsync(_ => oldWork.Task);
            CancellationToken current = default;
            windows.ShowPageAsync(scope => { current = scope.Token; return Task.CompletedTask; }).GetAwaiter().GetResult();
            oldWork.SetException(new InvalidOperationException("old work"));
            Assert.That(oldPage.IsCompleted, Is.True);
            Assert.Throws<InvalidOperationException>(() => oldPage.GetAwaiter().GetResult());
            Assert.That(current.IsCancellationRequested, Is.False);
        }
    }
}
