using System.Threading.Tasks;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class UIPageLayersTests
    {
        [Test]
        public void ReplacingAnExclusivePageClosesItsOverlaysAndDeactivatesEveryUnderlyingPage()
        {
            var window = new GameObject("Window");
            try
            {
                GameObject main = Child(window, "Main"), result = Child(window, "Results");
                GameObject info = Child(window, "Info"), confirm = Child(window, "Confirm");
                var layers = new UIPageLayers(new[] { main, result }, new[] { info, confirm });
                layers.ShowPage(result);
                layers.ShowOverlay(info, () => { layers.CloseOverlay(); return Task.CompletedTask; });
                layers.ShowOverlay(confirm, () => { layers.CloseOverlay(); return Task.CompletedTask; });
                Assert.That((main.activeInHierarchy, result.activeInHierarchy), Is.EqualTo((false, true)));
                Assert.That(confirm.transform.GetSiblingIndex(), Is.GreaterThan(info.transform.GetSiblingIndex()));
                layers.BackAsync(null).GetAwaiter().GetResult();
                Assert.That((info.activeInHierarchy, confirm.activeInHierarchy), Is.EqualTo((true, false)));
                layers.ShowPage(main);
                Assert.That((main.activeInHierarchy, result.activeInHierarchy, info.activeInHierarchy), Is.EqualTo((true, false, false)));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void DuplicateBackWhileAnAsyncCloseIsPendingCannotPopTheParentPage()
        {
            var window = new GameObject("Window");
            try
            {
                var page = Child(window, "Page");
                var modal = Child(window, "Modal");
                var layers = new UIPageLayers(new[] { page }, new[] { modal });
                var close = new TaskCompletionSource<bool>();
                int dispatched = 0;
                layers.ShowPage(page);
                layers.ShowOverlay(modal, () => { dispatched++; layers.CloseOverlay(); return close.Task; });
                Task first = layers.BackAsync(() => AssertPageBack());
                Task second = layers.BackAsync(() => AssertPageBack());
                Assert.That(second.IsCompleted, Is.True);
                Assert.That(first.IsCompleted, Is.False);
                close.SetResult(true);
                Assert.That(dispatched, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static Task AssertPageBack()
        {
            Assert.Fail("The second back was dispatched to the parent page.");
            return Task.CompletedTask;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
    }
}
