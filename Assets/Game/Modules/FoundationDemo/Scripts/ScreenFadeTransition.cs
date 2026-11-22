using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    // One curtain per navigation lifetime. Page binders continue receiving normal Unity updates.
    internal sealed class ScreenFadeTransition
    {
        private readonly CancellationToken lifetime;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CanvasGroup curtain;

        internal ScreenFadeTransition(BindingScope owner)
        {
            lifetime = owner.Token;
            var root = new GameObject("Home loading curtain", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(Image));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            root.GetComponent<Image>().color = Color.black;
            curtain = root.GetComponent<CanvasGroup>();
            root.SetActive(false);
            owner.Add(() =>
            {
                if (!root) return;
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
            });
        }

        internal async Task RunAsync(Func<Task> swap, CancellationToken request)
        {
            using var beforeSwap = CancellationTokenSource.CreateLinkedTokenSource(request, lifetime);
            await gate.WaitAsync(beforeSwap.Token);
            var input = EventSystem.current;
            var previous = input ? input.currentSelectedGameObject : null;
            try
            {
                beforeSwap.Token.ThrowIfCancellationRequested();
                curtain.alpha = 0f;
                curtain.gameObject.SetActive(true);
                if (input) input.SetSelectedGameObject(curtain.gameObject);
                await FadeAsync(0f, 1f, .24f, beforeSwap.Token);
                // Draw the opaque frame before entering potentially synchronous view construction.
                await Task.Yield();
                beforeSwap.Token.ThrowIfCancellationRequested();
                await swap();
                // Swapping pages normally cancels the outgoing button's request token.
                await FadeAsync(1f, 0f, .20f, lifetime);
            }
            finally
            {
                if (curtain)
                {
                    if (input && input.currentSelectedGameObject == curtain.gameObject)
                        input.SetSelectedGameObject(previous && previous.activeInHierarchy ? previous : null);
                    curtain.gameObject.SetActive(false);
                }
                gate.Release();
            }
        }

        private async Task FadeAsync(float from, float to, float seconds, CancellationToken token)
        {
            double start = Time.unscaledTimeAsDouble;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                float progress = (float)((Time.unscaledTimeAsDouble - start) / seconds);
                curtain.alpha = Mathf.Lerp(from, to, progress);
                if (progress >= 1f) return;
                await Task.Yield();
            }
        }
    }
}
