using System;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleTurnFocus : IDisposable
    {
        private const float WorldWidth = 3.08f;
        private const float Period = .96f;
        private readonly SpriteRenderer ring;
        private Transform shadow;
        private float elapsed;

        internal BattleTurnFocus(Transform units, Sprite sprite)
        {
            if (!sprite) return;
            var root = new GameObject("Active Turn Focus", typeof(SpriteRenderer));
            root.transform.SetParent(units, false);
            root.transform.localScale = Vector3.one * (WorldWidth / sprite.bounds.size.x);
            ring = root.GetComponent<SpriteRenderer>();
            ring.sprite = sprite;
            ring.enabled = false;
        }

        internal void Tick(BattlePreviewActor actor, float delta)
        {
            if (!ring) return;
            var next = actor ? actor.PresentationShadow : null;
            if (next != shadow)
            {
                shadow = next;
                elapsed = 0f;
                if (shadow && shadow.TryGetComponent<SpriteRenderer>(out var floor))
                    ring.sortingOrder = floor.sortingOrder + 1;
            }
            ring.enabled = shadow && shadow.gameObject.activeInHierarchy;
            if (!ring.enabled) return;
            ring.transform.position = shadow.position;
            elapsed = Mathf.Repeat(elapsed + Mathf.Max(0f, delta), Period);
            // The recorded highlight fades up, holds at full strength, then fades down.
            float pulse = Mathf.Clamp01(Mathf.Min(elapsed / .24f, (Period - elapsed) / .27f));
            ring.color = new Color(1f, 1f, 1f, Mathf.Lerp(.2f, 1f, pulse));
        }

        public void Dispose()
        {
            if (!ring) return;
            ring.enabled = false;
            UnityEngine.Object.Destroy(ring.gameObject);
        }
    }
}
