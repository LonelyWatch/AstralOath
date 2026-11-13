using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // Presentation only. The caller owns the accepted death event and paused battle clock.
    internal sealed class BattleDeathPresentation : IDisposable
    {
        private static readonly int Brightness = Shader.PropertyToID("_PresentationBrightness");
        private static readonly int Opacity = Shader.PropertyToID("_PresentationOpacity");
        private readonly Renderer[] renderers;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly float[] originalBrightness, originalOpacity;
        private readonly SpriteRenderer shadow;
        private readonly Color originalShadow;
        private readonly BattleHudParticles particles;
        private readonly GameObject effects;
        private readonly float particleLifetime;
        private float elapsed;
        private bool started, particlesStarted, disposed;
        // Presentation timings are configurable until original frame intervals are measured.
        internal const float DefaultDarkenSeconds = .2f;
        internal const float DefaultHoldSeconds = .12f;
        internal const float DefaultFadeSeconds = .28f;
        public float DarkenSeconds { get; private set; } = DefaultDarkenSeconds;
        public float HoldSeconds { get; private set; } = DefaultHoldSeconds;
        public float FadeSeconds { get; private set; } = DefaultFadeSeconds;
        private float FadeStart => DarkenSeconds + HoldSeconds;
        private float BodyEnd => FadeStart + FadeSeconds;
        public bool IsRunning => started && !IsComplete;
        public bool BodyComplete => started && elapsed >= BodyEnd;
        public float RemainingBodySeconds => started ? Mathf.Max(0f, BodyEnd - elapsed) : 0f;
        public float BodyOpacity => started ? 1f - Mathf.Clamp01((elapsed - FadeStart) / FadeSeconds) : 1f;
        public bool IsComplete => started && elapsed >= FadeStart + Mathf.Max(FadeSeconds, particleLifetime);

        public BattleDeathPresentation(Transform visual, TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures)
            : this(visual, definition, textures, null) { }

        public BattleDeathPresentation(Transform visual, TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures, Renderer shadow)
            : this(visual, definition, textures, shadow, null) { }

        public BattleDeathPresentation(Transform visual, TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures,
            Renderer shadow, Renderer body)
        {
            if (!visual) throw new ArgumentNullException(nameof(visual));
            if (body && body.transform != visual && !body.transform.IsChildOf(visual))
                throw new ArgumentException("The presentation body must belong to its visual owner.", nameof(body));
            // Explicit ownership avoids capturing retired or auxiliary renderers during same-frame resurrection.
            renderers = body ? new[] { body } : visual.GetComponentsInChildren<Renderer>(true);
            originalBrightness = new float[renderers.Length]; originalOpacity = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                originalBrightness[i] = block.HasFloat(Brightness) ? block.GetFloat(Brightness) : 1f;
                originalOpacity[i] = block.HasFloat(Opacity) ? block.GetFloat(Opacity) : 1f;
            }
            this.shadow = shadow as SpriteRenderer;
            if (this.shadow) originalShadow = this.shadow.color;
            if (definition)
            {
                particleLifetime = Lifetime(definition);
                effects = new GameObject("Battle death particles"); effects.transform.SetParent(visual, false);
                effects.layer = visual.gameObject.layer;
                int order = 0;
                foreach (var renderer in renderers) order = Mathf.Max(order, renderer.sortingOrder);
                try { particles = new BattleHudParticles(definition, textures, effects.transform, Vector2.zero, .012f, order + 1, true); }
                catch { Destroy(effects); throw; }
            }
        }

        public void ConfigureTimings(float darken, float hold, float fade)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleDeathPresentation));
            if (started) throw new InvalidOperationException("Death timings cannot change after Begin; Reset first.");
            if (!float.IsFinite(darken) || darken <= 0) throw new ArgumentOutOfRangeException(nameof(darken));
            if (!float.IsFinite(hold) || hold < 0) throw new ArgumentOutOfRangeException(nameof(hold));
            if (!float.IsFinite(fade) || fade <= 0) throw new ArgumentOutOfRangeException(nameof(fade));
            DarkenSeconds = darken;
            HoldSeconds = hold;
            FadeSeconds = fade;
        }

        public void Begin()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleDeathPresentation));
            if (started) return;
            started = true; elapsed = 0;
            if (effects) PositionParticles();
            Apply(1f, 1f);
        }
        public void Tick(float delta)
        {
            if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (disposed || !IsRunning) return;
            float previous = elapsed;
            elapsed += delta;
            Apply(1f - Mathf.Clamp01(elapsed / DarkenSeconds), BodyOpacity);
            if (particles != null && elapsed >= FadeStart)
            {
                if (!particlesStarted)
                {
                    particles.Play();
                    particlesStarted = true;
                }
                particles.Tick(Mathf.Max(0f, elapsed - FadeStart) - Mathf.Max(0f, previous - FadeStart));
                if (IsComplete) particles.Stop();
            }
        }
        private void PositionParticles()
        {
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            if (found) effects.transform.position = bounds.center;
        }

        private void Apply(float brightness, float opacity)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i]; if (!renderer) continue;
                renderer.GetPropertyBlock(block);
                block.SetFloat(Brightness, originalBrightness[i] * brightness);
                block.SetFloat(Opacity, originalOpacity[i] * opacity);
                renderer.SetPropertyBlock(block);
            }
            if (shadow) { var color = originalShadow; color.a *= opacity; shadow.color = color; }
        }
        public void Reset()
        {
            started = false; particlesStarted = false; elapsed = 0;
            particles?.Stop(); Apply(1f, 1f);
        }
        public void Dispose()
        {
            if (disposed) return;
            Reset(); disposed = true; particles?.Dispose(); if (effects) Destroy(effects);
        }
        private static float Lifetime(TextAsset definition)
        {
            float end = 0;
            foreach (var item in (JArray)JObject.Parse(definition.text)["emitters"])
            {
                if ((bool?)item["disable"] == true) continue;
                float duration = (float?)item["duration"] ?? 0;
                int burning = (int?)item["burningType"] ?? 0;
                if (burning != 1 && duration < 0) throw new FormatException("Death effects require a finite source emitter.");
                float interval = (float?)item["interval"] ?? 0;
                int repeat = interval > 0 ? Mathf.Max(1, (int?)item["repeat"] ?? 1) : 1;
                end = Mathf.Max(end, Mathf.Max(0, (float?)item["startDelay"] ?? 0) +
                    Mathf.Abs((float?)item["startDelayVar"] ?? 0) + interval * (repeat - 1) +
                    (burning == 1 ? 0 : Mathf.Max(0, duration)) +
                    Mathf.Max(.01f, (float?)item["particleLifespan"] ?? 0) + Mathf.Abs((float?)item["particleLifespanVariance"] ?? 0));
            }
            return end;
        }
        private static void Destroy(Object value)
        {
            if (value is GameObject root) { root.SetActive(false); root.transform.SetParent(null, false); }
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
