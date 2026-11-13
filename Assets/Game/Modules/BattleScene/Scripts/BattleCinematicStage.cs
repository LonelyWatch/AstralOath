using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // Owns presentation only. Source descriptors use milliseconds; Tick receives seconds.
    public sealed partial class BattleCinematicStage : IDisposable
    {
        private const float CutinClockTolerance = .000001f;

        private sealed class Layer
        {
            public GameObject Root;
            public Mesh Mesh;
            public Material Material;
            public JObject Node;
            public float Start;
            public float Duration;
            public float Stop = -1f;
            public BattleSkillPresentationAsset Cutin;
            public bool IsCutinCurtain;
        }

        private readonly Camera camera;
        private readonly BattleSourcePostEffect postEffects;
        private readonly Action<bool> cinematicChanged;
        private readonly GameObject background;
        private readonly List<Layer> layers = new List<Layer>();
        private readonly Dictionary<Renderer, bool> layoutVisibility = new Dictionary<Renderer, bool>();
        private Transform[] participants;
        private Transform caster;
        private Transform[] targets;
        private Vector3 originalPosition;
        private float originalSize;
        private Color originalClear;
        private bool originalBackground;
        private bool running;
        private bool hidden;
        private float time;
        private float cutinTime;
        private (Vector3 From, int Location, Vector3 Offset, float GroundOffset, float Start, float Duration, string Curve)? pan;
        private Vector3 basePosition;
        private int activeFocusLocation;
        private Vector3 activeFocusOffset;
        private float requestedSize;
        private float activeGroundOffset;
        private bool hasFocus;
        private readonly Dictionary<Transform, float> groundOffsets = new Dictionary<Transform, float>();
        private Bounds mapBounds;
        private bool hasMapBounds;
        private (float From, float To, float Start, float Duration, string Curve)? zoom;
        private Texture2D[] hatchTextures;

        public BattleCinematicStage(Camera camera, Action<bool> cinematicChanged, GameObject background)
        {
            this.camera = camera;
            postEffects = new BattleSourcePostEffect(camera);
            this.cinematicChanged = cinematicChanged;
            this.background = background;
        }

        public bool PlayPostEffect(JObject node, JObject[] modulators) => running && postEffects.Play(node, modulators);

        public void SetHatchTextures(Texture2D[] textures) => hatchTextures = textures;
        public void SetParticipants(Transform[] actors) => participants = actors;

        public void Begin(Transform actor, Transform[] recipients, bool hideHud)
        {
            var inherited = pendingHandoff;
            pendingHandoff = null;
            Reset();
            if (!camera) return;

            caster = actor;
            targets = recipients ?? Array.Empty<Transform>();
            originalPosition = inherited?.Position ?? camera.transform.position;
            originalSize = inherited?.Size ?? camera.orthographicSize;
            originalClear = inherited?.Clear ?? camera.backgroundColor;
            originalBackground = inherited?.Background ?? (background && background.activeSelf);
            time = 0f;
            cutinTime = 0f;
            basePosition = camera.transform.position;
            requestedSize = camera.orthographicSize;
            hasFocus = false;
            groundOffsets.Clear();
            foreach (var participant in targets.Concat(new[] { caster }).Where(item => item).Distinct())
                groundOffsets[participant] = originalPosition.y - participant.position.y;
            if (inherited == null) CaptureBackdrop();
            else AdoptBackdrop(inherited);
            running = true;
            hidden = hideHud;
            if (hidden) cinematicChanged?.Invoke(true);
        }

        public void Play(JObject node)
        {
            if (!running) return;
            switch ((string)node["etty"])
            {
                case "CAM":
                    int location = (int?)node["location"] ?? 0;
                    if (location is 5 or 12 or 16 or 31 or 32 or 41 or 42) RevealLayoutTargets();

                    float ground = GroundOffset(location);
                    float facing = caster && caster.TryGetComponent<BattlePreviewActor>(out var actor) ? actor.FacingDirection : 1f;
                    Vector3 offset = new Vector3(Number(node, "x") * .01f * facing, Number(node, "y") * .01f, 0f);
                    hasFocus = false;
                    if (Number(node, "time") <= 0f)
                    {
                        activeFocusLocation = location;
                        activeFocusOffset = offset;
                        activeGroundOffset = ground;
                        hasFocus = true;
                        basePosition = Framed(FocusPoint(location, offset), ground);
                        pan = null;
                    }
                    else
                        pan = (camera.transform.position - shakeOffset, location, offset, ground, time,
                            Number(node, "time") / 1000f, (string)node["curve"]);
                    break;
                case "ZOOM":
                    float targetSize = originalSize / Mathf.Max(.01f, Number(node, "scale", 1f));
                    if (Number(node, "time") <= 0f)
                    {
                        requestedSize = targetSize;
                        zoom = null;
                    }
                    else
                    {
                        zoom = (camera.orthographicSize, targetSize, time,
                            Number(node, "time") / 1000f, (string)node["curve"]);
                    }
                    break;
                case "LAYOUT":
                    ApplyLayout(node);
                    break;
                case "BGSHOW":
                    bool show = (bool?)node["show"] ?? false;
                    if (background) background.SetActive(show && originalBackground);
                    camera.backgroundColor = show ? originalClear : Color.black;
                    break;
                case "DARK":
                    int darkOrder = Number(node, "globalZ", -1f) > 0f ? 91 : -20;
                    var dark = AddLayer("DARK " + ((string)node["id"] ?? (string)node["guid"]), darkOrder, "CharacterLabUnlit");
                    dark.Node = node;
                    dark.Duration = Number(node, "duration", -1f) / 1000f;
                    dark.Material.mainTexture = Texture2D.whiteTexture;
                    break;
                case "STOP":
                    postEffects.Stop((string)node["targetId"]);
                    foreach (var layer in layers.Where(item => (string)item.Node?["id"] == (string)node["targetId"]))
                        layer.Stop = time;
                    break;
                case "BGHATCH":
                    if (hatchTextures == null || hatchTextures.Length != 3 || hatchTextures.Any(item => !item))
                    {
                        Debug.LogWarning("Missing original BGHATCH textures.");
                        break;
                    }
                    var hatch = AddLayer("BGHATCH", -19, "BattleCinematicHatch");
                    hatch.Node = node;
                    hatch.Duration = Number(node, "time") / 1000f;
                    hatch.Material.SetTexture("_MainTex", hatchTextures[0]);
                    hatch.Material.SetTexture("_Texture1", hatchTextures[1]);
                    hatch.Material.SetTexture("_Texture2", hatchTextures[2]);
                    for (int index = 0; index < 3; index++)
                        hatch.Material.SetColor("_FragColor" + index, ColorValue((string)node["color" + index]));
                    break;
            }
            ApplyCameraPose();
            UpdateLayers();
        }

        private void ApplyLayout(JObject node)
        {
            int mode = (int?)node["mode"] ?? 0;
            if (mode == 1)
            {
                RestoreLayout();
                return;
            }
            if (mode != 0) return;

            // This is the project's source-guided focus rule. Exact source layout coordinates and
            // fade/movement curves are not established. Keep slots and skeleton lifetimes intact.
            bool offscreenPhase = Mathf.Abs(Number(node, "away_x")) >= 1280f;
            RestoreLayout();
            foreach (var participant in participants ?? targets)
            {
                if (!participant || ContainsActor(participant, caster)) continue;
                if (!offscreenPhase && targets.Any(target => ContainsActor(participant, target))) continue;

                foreach (var renderer in participant.GetComponentsInChildren<Renderer>(true))
                {
                    if (!layoutVisibility.ContainsKey(renderer))
                        layoutVisibility.Add(renderer, renderer.enabled);
                    renderer.enabled = false;
                }
            }
        }

        internal void RevealLayoutTargets()
        {
            // away_x is currently represented by a temporary offscreen visibility mask,
            // not by moving authoritative slots. A target-focused shot must release that
            // mask before drawing impacts; the source LAYOUT reset comes only at the end.
            if (layoutVisibility.Count == 0) return;
            foreach (var participant in participants ?? targets)
            {
                if (!participant || !targets.Any(target => ContainsActor(participant, target))) continue;
                foreach (var renderer in participant.GetComponentsInChildren<Renderer>(true))
                {
                    if (!layoutVisibility.TryGetValue(renderer, out bool original)) continue;
                    renderer.enabled = original;
                    layoutVisibility.Remove(renderer); // Release only what this stage hid.
                }
            }
        }

        private static bool ContainsActor(Transform participant, Transform actor) =>
            actor && (actor == participant || actor.IsChildOf(participant));

        public void PlayCutin(BattleSkillPresentationAsset asset) => PlayCutinWithNode(asset, null);

        public void PlayCutinWithNode(BattleSkillPresentationAsset asset, JObject node)
        {
            if (!running || asset?.document == null) return;
            var descriptor = JObject.Parse(asset.document.text);
            if (asset.pages == null || asset.pages.Length != ((JArray)descriptor["frames"]).Count)
                throw new InvalidOperationException("Incomplete original CUTIN frames: " + asset.source);

            float duration = CutinDuration(descriptor);
            string curtainColor = (string)node?["curtain_color"];
            if (string.IsNullOrEmpty(curtainColor)) curtainColor = "0,0,0,255";
            float curtainOpacity = Mathf.Clamp01(Number(node, "curtain_opacity", 1f));
            if (ColorValue(curtainColor).a * curtainOpacity > 0f)
            {
                // Source curtain composites behind transparent Cut-in pixels, including ordinary
                // HUD. Pause controls remain above both at 31000; world actors retain visibility.
                var curtain = AddLayer("Cut-in curtain " + asset.source, 29999, "CharacterLabUnlit");
                curtain.Root.layer = 5;
                curtain.IsCutinCurtain = true;
                curtain.Start = cutinTime;
                curtain.Duration = duration;
                curtain.Node = new JObject { ["color"] = curtainColor, ["opacity"] = curtainOpacity };
                curtain.Material.mainTexture = Texture2D.whiteTexture;
            }
            var layer = AddLayer("CUTIN " + asset.source, 30000, "CharacterLabUnlit");
            // CUTIN remains a single overlay above the captured world and is excluded from sprite blur.
            layer.Root.layer = 5;
            layer.Cutin = asset;
            layer.Start = cutinTime;
            layer.Node = descriptor;
            layer.Duration = duration;
            UpdateLayers();
        }

        public static float CutinDuration(JObject descriptor) =>
            descriptor?["frames"]?.Sum(frame => (float?)frame["durationSeconds"] ?? 0f) ?? 0f;

        public float CutinRemainingSeconds
        {
            get
            {
                float remaining = 0f;
                foreach (var layer in layers)
                    if (layer.Cutin != null)
                        remaining = Mathf.Max(remaining, layer.Duration - (cutinTime - layer.Start));
                return remaining > CutinClockTolerance ? remaining : 0f;
            }
        }

        // Cut-in consumes wall time while source-clock motion, DARK and world effects are paused.
        public void TickCutin(float delta)
        {
            if (!running || !camera) return;
            cutinTime += Mathf.Max(0f, delta);
            UpdateLayers();
        }

        public void Tick(float delta)
        {
            if (!running || !camera) return;
            time += Mathf.Max(0f, delta);
            cutinTime += Mathf.Max(0f, delta);
            if (zoom is { } scale)
            {
                float progress = Progress(scale.Start, scale.Duration);
                requestedSize = Mathf.LerpUnclamped(scale.From, scale.To, Curve(progress, scale.Curve));
                if (progress >= 1f) zoom = null;
            }
            camera.orthographicSize = EffectiveSize();
            if (pan is { } move)
            {
                float progress = Progress(move.Start, move.Duration);
                basePosition = Vector3.LerpUnclamped(move.From, Framed(FocusPoint(move.Location, move.Offset), move.GroundOffset), Curve(progress, move.Curve));
                if (progress >= 1f)
                {
                    activeFocusLocation = move.Location; activeFocusOffset = move.Offset; activeGroundOffset = move.GroundOffset; hasFocus = true; pan = null;
                }
            }
            else if (hasFocus) basePosition = Framed(FocusPoint(activeFocusLocation, activeFocusOffset), activeGroundOffset);
            TickShakes(delta);
            ApplyCameraPose();
            postEffects.Tick(delta);
            UpdateLayers();
        }

        private Vector3 Anchor(int location)
        {
            if (location == 0) return originalPosition;
            if ((location == 1 || location == 11) && caster) return caster.position;
            var live = targets.Where(item => item).ToArray();
            if (live.Length == 0) return caster ? caster.position : originalPosition;
            if (location == 31 && caster) return (caster.position + live[0].position) * .5f;
            if (location == 32 || location == 41 || location == 42)
                return live.Aggregate(Vector3.zero, (sum, item) => sum + item.position) / live.Length;
            if (location == 5 || location == 12 || location == 16) return live[0].position;

            Debug.LogWarning("Unmapped cinematic CAM location " + location);
            return originalPosition;
        }

        private Layer AddLayer(string name, int order, string shaderName)
        {
            var shader = Resources.Load<Shader>(shaderName);
            if (!shader) throw new InvalidOperationException("Missing cinematic shader " + shaderName);

            var item = new Layer
            {
                Root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)),
                Start = time,
                Duration = -1f,
                Mesh = new Mesh { name = "Battle cinematic quad" },
                Material = new Material(shader)
            };
            item.Root.transform.SetParent(camera.transform, false);
            item.Root.transform.localPosition = new Vector3(0f, 0f, 2f);
            item.Mesh.vertices = new[]
            {
                new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f),
                new Vector3(.5f, .5f), new Vector3(-.5f, .5f)
            };
            item.Mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            item.Mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            item.Mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            item.Root.GetComponent<MeshFilter>().sharedMesh = item.Mesh;
            var renderer = item.Root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = item.Material;
            renderer.sortingOrder = order;
            layers.Add(item);
            return item;
        }

        private void UpdateLayers()
        {
            foreach (var layer in layers.ToArray())
            {
                float elapsed = (layer.Cutin != null || layer.IsCutinCurtain ? cutinTime : time) - layer.Start;
                float fadeOut = Mathf.Max(0f, Number(layer.Node, "fadeOut") / 1000f);
                bool dark = (string)layer.Node?["etty"] == "DARK";
                // DARK duration ends its hold; fadeOut is a separate interval. CUTIN and
                // BGHATCH retain their source total durations and their own clock semantics.
                float lifetime = layer.Duration + (dark ? fadeOut : 0f);
                // Compare against the same tolerance as the wall-clock boundary. A residue
                // smaller than one float step at this timestamp cannot hold the source clock.
                float tolerance = layer.Cutin != null || layer.IsCutinCurtain ? CutinClockTolerance : 0f;
                if ((layer.Duration >= 0f && elapsed >= lifetime - tolerance) ||
                    (layer.Stop >= 0f && time >= layer.Stop + fadeOut))
                {
                    Release(layer);
                    layers.Remove(layer);
                    continue;
                }

                float height = camera.orthographicSize * 2f;
                float aspect = layer.Cutin != null
                    ? Number(layer.Node, "width", 1280f) / Number(layer.Node, "height", 720f)
                    : camera.aspect;
                if (layer.Cutin != null) height *= Mathf.Max(1f, camera.aspect / aspect);
                layer.Root.transform.localScale = new Vector3(height * aspect, height, 1f);
                if (layer.Cutin != null)
                {
                    int frame = 0;
                    foreach (var entry in layer.Node["frames"])
                    {
                        float duration = (float)entry["durationSeconds"];
                        if (elapsed < duration) break;
                        elapsed -= duration;
                        frame++;
                    }
                    layer.Material.mainTexture = layer.Cutin.pages[Mathf.Min(frame, layer.Cutin.pages.Length - 1)];
                    continue;
                }

                float alpha = Number(layer.Node, "opacity", 1f);
                float fadeIn = Number(layer.Node, "fadeIn") / 1000f;
                if (fadeIn > 0f) alpha *= Mathf.Clamp01(elapsed / fadeIn);
                float fadeStart = layer.Duration >= 0f
                    ? layer.Duration - (dark ? 0f : fadeOut) : float.PositiveInfinity;
                if (layer.Stop >= 0f) fadeStart = Mathf.Min(fadeStart, layer.Stop - layer.Start);
                if (fadeOut > 0f)
                    alpha *= Mathf.Clamp01(1f - (elapsed - fadeStart) / fadeOut);
                else if (elapsed >= fadeStart) alpha = 0f;
                Color color = dark || layer.IsCutinCurtain
                    ? ColorValue((string)layer.Node["color"])
                    : Color.white;
                color.a *= alpha;
                layer.Mesh.colors = new[] { color, color, color, color };
                if ((string)layer.Node?["etty"] == "BGHATCH")
                {
                    layer.Material.SetVector("_Option", new Vector4(
                        elapsed * Number(layer.Node, "speed0"),
                        elapsed * Number(layer.Node, "speed1"),
                        elapsed * Number(layer.Node, "speed2"), 0f));
                }
            }
        }

        // Solve x(t) for the source cubic Bezier, then evaluate y(t); preserve overshoot in y.
        public static float Curve(float progress, string curve)
        {
            if (string.IsNullOrEmpty(curve) || progress <= 0f || progress >= 1f) return progress;
            var values = curve.Split(',');
            if (values.Length != 4) return progress;
            float[] points = values.Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            float low = 0f;
            float high = 1f;
            for (int index = 0; index < 20; index++)
            {
                float t = (low + high) * .5f;
                if (Bezier(t, points[0], points[2]) < progress) low = t;
                else high = t;
            }
            return Bezier((low + high) * .5f, points[1], points[3]);
        }

        private static float Bezier(float t, float a, float b) =>
            3f * (1f - t) * (1f - t) * t * a + 3f * (1f - t) * t * t * b + t * t * t;

        private float Progress(float start, float duration) =>
            duration <= 0f ? 1f : Mathf.Clamp01((time - start) / duration);

        private static float Number(JObject node, string key, float fallback = 0f) =>
            float.TryParse(node?[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            float.IsFinite(value) ? value : fallback;

        private static Color ColorValue(string text)
        {
            if (string.IsNullOrEmpty(text)) return Color.white;
            var components = text.Split(',')
                .Select(value => float.Parse(value, CultureInfo.InvariantCulture) / 255f).ToArray();
            return components.Length == 4
                ? new Color(components[0], components[1], components[2], components[3])
                : Color.white;
        }

        private static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }

        private static void Release(Layer layer)
        {
            if (layer.Root) layer.Root.SetActive(false);
            Destroy(layer.Root);
            Destroy(layer.Mesh);
            Destroy(layer.Material);
        }

        private void RestoreLayout()
        {
            foreach (var pair in layoutVisibility)
                if (pair.Key) pair.Key.enabled = pair.Value;
            layoutVisibility.Clear();
        }

        public void Reset()
        {
            if (pendingHandoff != null)
            {
                originalPosition = pendingHandoff.Position;
                originalSize = pendingHandoff.Size;
                originalClear = pendingHandoff.Clear;
                originalBackground = pendingHandoff.Background;
                AdoptBackdrop(pendingHandoff);
                pendingHandoff = null;
                running = true;
            }
            RestoreLayout();
            postEffects.Reset();
            ReleaseCameraMotion();
            foreach (var layer in layers) Release(layer);
            layers.Clear();
            pan = null;
            zoom = null;
            if (running && camera)
            {
                camera.transform.position = originalPosition;
                camera.orthographicSize = originalSize;
                camera.backgroundColor = originalClear;
            }
            if (running && background) background.SetActive(originalBackground);
            if (hidden) cinematicChanged?.Invoke(false);
            running = false;
            hidden = false;
            caster = null;
            targets = Array.Empty<Transform>();
        }

        public void Dispose() => Reset();
    }
}
