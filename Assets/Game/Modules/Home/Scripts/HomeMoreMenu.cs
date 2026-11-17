using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using UnityEngine;

namespace AstralOath.Client.Home
{
    public sealed class HomeMoreMenu : UIBaseView
    {
        public const string Address = "home/more-menu";
        public override string PrefabPath => Address;
        internal GameButton Heroes => Bindings.Require<GameButton>("heroes");
        internal GameText HeroesLabel => Bindings.Require<GameText>("heroesLabel");
        internal GameButton Close => Bindings.Require<GameButton>("close");
        internal GameButton Outside => Bindings.Require<GameButton>("outside");
        private RectTransform Drawer => Bindings.Require<RectTransform>("drawer");
        private CanvasGroup Shade => Bindings.Require<CanvasGroup>("shade");

        public override void Validate()
        {
            _ = Heroes; _ = HeroesLabel; _ = Close; _ = Outside;
            _ = Drawer; _ = Shade;
        }

        internal async Task AnimateOpeningAsync(CancellationToken token)
        {
            const float duration = .16f;
            var drawer = Drawer;
            var shade = Shade;
            // The native drawer's travel is its 377-unit width in the 720-high layout.
            var hidden = new Vector2(drawer.rect.height * (377f / 720f), 0);
            double started = Time.realtimeSinceStartupAsDouble;
            float elapsed = 0;
            while (!token.IsCancellationRequested && drawer && shade)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float remaining = 1 - progress;
                drawer.anchoredPosition = hidden * (remaining * remaining * remaining);
                shade.alpha = progress;
                if (progress >= 1) return;
                await Task.Yield();
                elapsed = (float)(Time.realtimeSinceStartupAsDouble - started);
            }
        }
    }
}
