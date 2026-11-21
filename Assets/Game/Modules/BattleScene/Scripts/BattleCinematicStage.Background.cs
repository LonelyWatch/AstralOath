using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    public sealed partial class BattleCinematicStage
    {
        private BackgroundTransition backgroundTransition;

        private sealed class BackgroundTransition
        {
            private readonly Camera camera;
            private readonly GameObject map;
            private readonly bool originalActive;
            private readonly Color originalClear;
            private SpriteRenderer[] layers;
            private Color[] colors;
            private float amount = 1f, from, to, duration, elapsed;

            internal BackgroundTransition(Camera camera, GameObject map, bool originalActive, Color originalClear)
            {
                this.camera = camera;
                this.map = map;
                this.originalActive = originalActive;
                this.originalClear = originalClear;
            }

            internal void Set(bool show, float seconds)
            {
                from = amount;
                to = show ? 1f : 0f;
                duration = Mathf.Max(0f, seconds);
                elapsed = 0f;
                if (duration <= 0f || from == to)
                {
                    Finish();
                    return;
                }
                if (map && originalActive)
                {
                    if (layers == null)
                    {
                        layers = map.GetComponentsInChildren<SpriteRenderer>(true);
                        colors = new Color[layers.Length];
                        for (int i = 0; i < layers.Length; i++) colors[i] = layers[i].color;
                    }
                    map.SetActive(true);
                }
                Apply();
            }

            internal void Tick(float delta)
            {
                if (duration <= 0f) return;
                elapsed += delta;
                if (elapsed >= duration) Finish();
                else
                {
                    amount = Mathf.Lerp(from, to, elapsed / duration);
                    Apply();
                }
            }

            private void Apply()
            {
                camera.backgroundColor = Color.Lerp(Color.black, originalClear, amount);
                if (layers == null) return;
                for (int i = 0; i < layers.Length; i++)
                {
                    var color = colors[i];
                    if (layers[i]) layers[i].color = new Color(color.r * amount, color.g * amount, color.b * amount, color.a);
                }
            }

            private void Finish()
            {
                duration = 0f;
                amount = to;
                if (map) map.SetActive(originalActive && to > 0f);
                camera.backgroundColor = to > 0f ? originalClear : Color.black;
                RestoreColors();
            }

            internal void RestoreColors()
            {
                if (layers == null) return;
                for (int i = 0; i < layers.Length; i++)
                    if (layers[i]) layers[i].color = colors[i];
                layers = null;
                colors = null;
            }
        }
    }
}
