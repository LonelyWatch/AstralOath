using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    // Each opening owns its own coordinator. A notice or player card can therefore cover
    // another window without replacing that window's internal overlay or bindings.
    internal sealed class SocialPanel : IDisposable
    {
        private readonly BindingScope lifetime;
        private readonly WindowCoordinator windows;
        private readonly GameObject portal;
        private readonly SynchronizationContext context;
        private CancellationTokenRegistration cancelled;
        private bool disposed;
        internal bool IsOpen => !disposed;
        internal CancellationToken Token => lifetime.Token;
        internal Transform Parent => portal.transform;

        internal SocialPanel(Transform parent, int sortingOrder, CancellationToken owner)
        {
            owner.ThrowIfCancellationRequested();
            context = SynchronizationContext.Current;
            lifetime = new BindingScope(owner);
            windows = new WindowCoordinator(lifetime.Token);
            lifetime.Add(windows.Dispose);
            portal = new GameObject("Social window", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var rect = (RectTransform)portal.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = portal.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            cancelled = owner.Register(() =>
            {
                if (context == null || context == SynchronizationContext.Current) Dispose();
                else context.Post(_ => Dispose(), null);
            });
        }

        internal async Task OpenAsync(Func<BindingScope, Task> open)
        {
            try { await windows.ShowPageAsync(open); }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancelled.Dispose();
            try { lifetime.Dispose(); }
            finally
            {
                // Remove input immediately; Destroy itself is deferred until the frame ends.
                if (portal) { portal.SetActive(false); UnityEngine.Object.Destroy(portal); }
            }
        }
    }
}
