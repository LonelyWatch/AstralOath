using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // Independently clocked native CFX. Only an explicit owner can hold an authored loop.
    // Presentation lifetime never owns an action or delays a hit.
    internal sealed class BattleNativeFeedbackEffect : IDisposable
    {
        internal const string HealingSource = "heal_hp_01";
        internal sealed class Library
        {
            private readonly Dictionary<string, BattleSkillPresentationAsset> assets;
            private readonly Dictionary<(string, float), SkeletonPlayerSource> skeletons = new Dictionary<(string, float), SkeletonPlayerSource>();
            internal readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            internal readonly SkeletonPlayerFactory Factory = new SkeletonPlayerFactory();

            internal Library(BattleSkillPresentationAsset[] assets)
            {
                this.assets = (assets ?? Array.Empty<BattleSkillPresentationAsset>()).ToDictionary(a => a.source, StringComparer.Ordinal);
                foreach (var asset in this.assets.Values)
                    if (asset.texture) Textures[asset.source] = asset.texture;
            }

            internal bool Contains(string source) => assets.TryGetValue("effect/" + source + ".cfx", out var asset) && asset.document;
            internal JObject Document(string source)
            {
                if (!assets.TryGetValue(source, out var asset) || !asset.document)
                    throw new InvalidOperationException("Missing native action feedback: " + source);
                return JObject.Parse(asset.document.text);
            }
            internal TextAsset Particle(string source) => assets["effect/" + source + ".particle"].document;
            internal SkeletonPlayerSource Skeleton(string source, float pixelScale)
            {
                var key = (source, pixelScale);
                if (skeletons.TryGetValue(key, out var result)) return result;
                var asset = assets["effect/" + source + ".scsp"];
                var pages = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                if (!asset.document || !asset.atlas || asset.pages == null || asset.pageNames == null || asset.pages.Length != asset.pageNames.Length)
                    throw new InvalidOperationException("Incomplete native action feedback skeleton: " + source);
                for (int i = 0; i < asset.pages.Length; i++) pages.Add(asset.pageNames[i], asset.pages[i]);
                result = new SkeletonPlayerSource(asset.document.text, asset.atlas.text, pages, asset.premultipliedAlpha, pixelScale);
                Factory.Prepare(result);
                skeletons.Add(key, result);
                return result;
            }
        }

        private sealed class Part
        {
            internal ISkeletonPlayer Spine;
            internal BattleHudParticles Particles;
            internal GameObject Root;
            internal Renderer[] Renderers;
            internal string First, Loop;
            internal float Delay, Duration, IntroDuration, Speed;
            internal bool Started, LoopStarted;
        }

        private readonly List<Part> parts = new List<Part>();
        private readonly MaterialPropertyBlock opacity = new MaterialPropertyBlock();
        private readonly float duration, fadeSeconds;
        private float time, finishAt;
        private bool disposed, heldForOwner, finishRequested;
        internal readonly GameObject Root;
        private readonly Renderer healingCross;
        internal bool Active { get; private set; }
        internal float Duration => (heldForOwner ? finishAt : duration) + fadeSeconds;
        internal float Elapsed => time;

        internal BattleNativeFeedbackEffect(Library library, string source, Transform parent, float pixelScale, int sortingLayer, int sortingOrder)
        {
            Root = new GameObject("Native Feedback " + source);
            Root.transform.SetParent(parent, false);
            Root.SetActive(false);
            try
            {
                var primitives = library.Document("effect/" + source + ".cfx")["primitive"] as JArray;
                if (primitives == null || primitives.Count == 0) throw new FormatException("Native feedback CFX has no primitives: " + source);
                // Keep native z order within each CFX, including the explicit globalZ ordering.
                foreach (var data in primitives.OfType<JObject>().OrderBy(p => Number(p, "globalZ")).ThenBy(p => Number(p, "z")))
                {
                    var part = new Part { Delay = Number(data, "delay") / 1000f, Speed = Number(data, "speed", 1f) };
                    if (part.Delay < 0 || part.Speed <= 0) throw new FormatException("Invalid feedback timing: " + source);
                    string name = (string)data["source"];
                    float scale = Number(data, "scale", 1f);
                    if (scale <= 0) throw new FormatException("Invalid feedback scale: " + source);
                    var group = new GameObject(name).transform;
                    group.SetParent(Root.transform, false);
                    group.localPosition = new Vector3(Number(data, "x"), Number(data, "y"), 0) * pixelScale;
                    group.localRotation = Quaternion.Euler(0, 0, -Number(data, "rotate"));
                    group.localScale = Vector3.one * scale;
                    part.Root = group.gameObject;
                    parts.Add(part);
                    int order = sortingOrder + parts.Count;
                    if ((string)data["format"] == "spine")
                    {
                        part.Spine = library.Factory.Create(library.Skeleton(name, pixelScale), group);
                        var animations = part.Spine.Animations.ToDictionary(a => a.Name, a => a.Duration, StringComparer.Ordinal);
                        string[] clips = ((string)data["ani"] ?? (animations.ContainsKey("animation") ? "animation" : animations.Keys.First())).Split(',');
                        part.First = clips[0];
                        part.IntroDuration = animations[part.First] / part.Speed;
                        part.Duration = part.IntroDuration;
                        if (clips.Length == 2 && Number(data, "loop") != 0)
                        {
                            part.Loop = clips[1];
                            // Default one-shots remain bounded. The avatar opts into the
                            // source loop until its complete action presentation settles.
                            part.Duration += animations[part.Loop] / part.Speed;
                            fadeSeconds = .12f;
                        }
                        else if (clips.Length != 1) throw new FormatException("Unsupported feedback animation sequence: " + source);
                    }
                    else if ((string)data["format"] == "particle")
                    {
                        part.Particles = new BattleHudParticles(library.Particle(name), library.Textures, group,
                            Vector2.zero, pixelScale, order, true, true);
                        var emitters = library.Document("effect/" + name + ".particle")["emitters"].OfType<JObject>();
                        part.Duration = emitters.Where(e => (bool?)e["disable"] != true).Select(e =>
                            Number(e, "duration") < 0 && (int?)e["burningType"] != 1 ? 0f : Number(e, "startDelay") + Math.Abs(Number(e, "startDelayVar")) +
                            Math.Max(0, Number(e, "repeat") - 1) * Number(e, "interval") +
                            ((int?)e["burningType"] == 1 ? 0 : Number(e, "duration")) +
                            Number(e, "particleLifespan") + Math.Abs(Number(e, "particleLifespanVariance"))).DefaultIfEmpty(0).Max();
                    }
                    else throw new FormatException("Unsupported native feedback primitive: " + source);
                    part.Renderers = group.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in part.Renderers) { renderer.sortingLayerID = sortingLayer; renderer.sortingOrder = order; }
                    duration = Math.Max(duration, part.Delay + part.Duration);
                }
                if (duration <= 0) throw new FormatException("Native feedback requires a finite authored animation: " + source);
                foreach (var part in parts) if (part.Duration <= 0) part.Duration = duration - part.Delay;
                if (source == HealingSource)
                {
                    healingCross = parts.Single().Renderers.Single(renderer =>
                        renderer.sharedMaterial.GetInt("_DstBlend") == (int)BlendMode.OneMinusSrcAlpha);
                }
            }
            catch { Dispose(); throw; }
        }

        internal void Play() => Start(false);

        internal void PlayUntilFinished()
        {
            if (!parts.Any(part => part.Loop != null))
                throw new InvalidOperationException("Only an authored intro/loop effect can wait for its owner.");
            Start(true);
        }

        internal void Finish()
        {
            if (!Active || !heldForOwner || finishRequested) return;
            finishRequested = true;
            // A very short action still shows the authored intro; normal actions
            // fade at the existing settle boundary, including queued successors.
            finishAt = Math.Max(time, parts.Where(part => part.Loop != null)
                .Select(part => part.Delay + part.IntroDuration).DefaultIfEmpty(0).Max());
        }

        private void Start(bool hold)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BattleNativeFeedbackEffect));
            heldForOwner = hold; finishRequested = false; finishAt = float.PositiveInfinity;
            time = 0; Active = true;
            foreach (var part in parts) { part.Started = part.LoopStarted = false; part.Root.SetActive(false); part.Particles?.Stop(); }
            Root.SetActive(true);
            Tick(0);
        }

        internal void Tick(float delta)
        {
            if (!Active || disposed) return;
            if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            float previous = time;
            time += delta;
            if (!heldForOwner && time >= Duration) { Stop(); return; }
            float alpha = fadeSeconds > 0 ? Mathf.Clamp01((Duration - time) / fadeSeconds) : 1f;
            foreach (var part in parts)
            {
                float local = time - part.Delay;
                if (local < 0) continue;
                if (!part.Started)
                {
                    part.Started = true; part.Root.SetActive(true);
                    part.Spine?.Play(part.First, false);
                    part.Particles?.Play();
                }
                if (part.Spine != null)
                {
                    float before = Mathf.Max(0, previous - part.Delay);
                    if (part.Loop != null && local >= part.IntroDuration)
                    {
                        if (!part.LoopStarted)
                        {
                            part.LoopStarted = true;
                            part.Spine.Play(part.Loop, true);
                            before = part.IntroDuration;
                        }
                        part.Spine.Tick((local - before) * part.Speed);
                    }
                    else part.Spine.Tick((Mathf.Min(local, part.Duration) - Mathf.Min(before, part.Duration)) * part.Speed);
                }
                else
                {
                    float before = Mathf.Max(0, previous - part.Delay);
                    if (heldForOwner && finishRequested && time >= finishAt)
                    {
                        // Advance emission only to the owner's boundary, then age
                        // existing particles using their authored life/color curves.
                        float boundary = Mathf.Max(before, finishAt - part.Delay);
                        if (boundary > before) part.Particles.Tick(boundary - before);
                        part.Particles.StopEmission();
                        part.Particles.Tick(Mathf.Max(0, local - boundary));
                    }
                    else part.Particles.Tick(Mathf.Max(0, local) - before);
                }
                part.Particles?.SetOpacity(heldForOwner ? 1f : alpha);
                if (part.Spine != null)
                    foreach (var renderer in part.Renderers)
                    {
                        renderer.GetPropertyBlock(opacity);
                        opacity.SetFloat("_PresentationOpacity", alpha);
                        opacity.SetFloat("_SrgbVertexTint", 1f);
                        renderer.SetPropertyBlock(opacity);
                    }
            }
            if (heldForOwner && finishRequested && time >= Duration &&
                !parts.Any(part => part.Particles != null && part.Particles.HasLiveParticles)) Stop();
        }

        internal void Stop()
        {
            Active = false;
            foreach (var part in parts) part.Particles?.Stop();
            if (Root) Root.SetActive(false);
        }

        internal void SetActorSorting(Renderer body)
        {
            if (healingCross)
            {
                // The battle camera looks along +Z. Stay in the recipient's order so an adjacent
                // foreground hero covers every track. Soft light sits between body and filled cross.
                float bodyDepth = body.bounds.center.z;
                foreach (var renderer in parts[0].Renderers)
                {
                    renderer.sortingLayerID = body.sortingLayerID;
                    renderer.sortingOrder = body.sortingOrder;
                    var position = renderer.transform.position;
                    position.z = bodyDepth - (renderer == healingCross ? .04f : .02f);
                    renderer.transform.position = position;
                }
                return;
            }
            SetSorting(body.sortingLayerID, body.sortingOrder + 5);
        }

        internal void SetSorting(int layer, int order)
        {
            for (int i = 0; i < parts.Count; i++)
                foreach (var renderer in parts[i].Renderers)
                    if (renderer) { renderer.sortingLayerID = layer; renderer.sortingOrder = order + i + 1; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; Stop();
            foreach (var part in parts) { part.Spine?.Dispose(); part.Particles?.Dispose(); }
            parts.Clear();
            if (Root) { Root.transform.SetParent(null, false); if (Application.isPlaying) Object.Destroy(Root); else Object.DestroyImmediate(Root); }
        }

        private static float Number(JObject data, string key, float fallback = 0)
        {
            if (data[key] == null) return fallback;
            if (!float.TryParse(data[key].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new FormatException("Invalid CFX number: " + key);
            return value;
        }
    }
}
