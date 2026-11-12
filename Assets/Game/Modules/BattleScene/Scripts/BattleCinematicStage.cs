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
    public sealed class BattleCinematicStage : IDisposable
    {
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
        }

        private readonly Camera camera;
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
        private (Vector3 From, Vector3 To, float Start, float Duration, string Curve)? pan;
        private (float From, float To, float Start, float Duration, string Curve)? zoom;
        private Texture2D[] hatchTextures;

        public BattleCinematicStage(Camera camera, Action<bool> cinematicChanged, GameObject background)
        {
            this.camera = camera;
            this.cinematicChanged = cinematicChanged;
            this.background = background;
        }

        public void SetHatchTextures(Texture2D[] textures) => hatchTextures = textures;
        public void SetParticipants(Transform[] actors) => participants = actors;

        public void Begin(Transform actor, Transform[] recipients, bool hideHud)
        {
            Reset();
            if (!camera) return;

            caster = actor;
            targets = recipients ?? Array.Empty<Transform>();
            originalPosition = camera.transform.position;
            originalSize = camera.orthographicSize;
            originalClear = camera.backgroundColor;
            originalBackground = background && background.activeSelf;
            time = 0f;
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
                    Vector3 target = Anchor((int?)node["location"] ?? 0);
                    target += new Vector3(Number(node, "x") * .01f, Number(node, "y") * .01f, 0f);
                    target.z = originalPosition.z;
                    if (Number(node, "time") <= 0f)
                    {
                        camera.transform.position = target;
                        pan = null;
                    }
                    else
                    {
                        pan = (camera.transform.position, target, time,
                            Number(node, "time") / 1000f, (string)node["curve"]);
                    }
                    break;
                case "ZOOM":
                    float targetSize = originalSize / Mathf.Max(.01f, Number(node, "scale", 1f));
                    if (Number(node, "time") <= 0f)
                    {
                        camera.orthographicSize = targetSize;
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
                    var dark = AddLayer("DARK " + ((string)node["id"] ?? (string)node["guid"]), -20, "CharacterLabUnlit");
                    dark.Node = node;
                    dark.Duration = Number(node, "duration", -1f) / 1000f;
                    dark.Material.mainTexture = Texture2D.whiteTexture;
                    break;
                case "STOP":
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

        private static bool ContainsActor(Transform participant, Transform actor) =>
            actor && (actor == participant || actor.IsChildOf(participant));

        public void PlayCutin(BattleSkillPresentationAsset asset)
        {
            if (!running || asset?.document == null) return;
            var descriptor = JObject.Parse(asset.document.text);
            if (asset.pages == null || asset.pages.Length != ((JArray)descriptor["frames"]).Count)
                throw new InvalidOperationException("Incomplete original CUTIN frames: " + asset.source);

            var layer = AddLayer("CUTIN " + asset.source, 30000, "CharacterLabUnlit");
            layer.Cutin = asset;
            layer.Node = descriptor;
            layer.Duration = CutinDuration(descriptor);
            UpdateLayers();
        }

        public static float CutinDuration(JObject descriptor) =>
            descriptor?["frames"]?.Sum(frame => (float?)frame["durationSeconds"] ?? 0f) ?? 0f;

        public void Tick(float delta)
        {
            if (!running || !camera) return;
            time += Mathf.Max(0f, delta);
            if (pan is { } move)
            {
                float progress = Progress(move.Start, move.Duration);
                camera.transform.position = Vector3.LerpUnclamped(move.From, move.To, Curve(progress, move.Curve));
                if (progress >= 1f) pan = null;
            }
            if (zoom is { } scale)
            {
                float progress = Progress(scale.Start, scale.Duration);
                camera.orthographicSize = Mathf.LerpUnclamped(scale.From, scale.To, Curve(progress, scale.Curve));
                if (progress >= 1f) zoom = null;
            }
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
                float elapsed = time - layer.Start;
                float fadeOut = Number(layer.Node, "fadeOut") / 1000f;
                if ((layer.Duration >= 0f && elapsed >= layer.Duration) ||
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
                if (fadeOut > 0f && layer.Duration >= 0f)
                    alpha *= Mathf.Clamp01((layer.Duration - elapsed) / fadeOut);
                if (layer.Stop >= 0f)
                    alpha *= fadeOut > 0f ? Mathf.Clamp01(1f - (time - layer.Stop) / fadeOut) : 0f;
                Color color = (string)layer.Node?["etty"] == "DARK"
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
            RestoreLayout();
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
