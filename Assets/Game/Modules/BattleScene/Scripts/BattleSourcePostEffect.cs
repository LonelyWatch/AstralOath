using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // A short-lived composite of the world camera. The battle HUD keeps its original camera.
    internal sealed class BattleSourcePostEffect : IDisposable
    {
        private const int CompositeLayer = 5;
        private const int CompositeSortingOrder = 90;

        private readonly struct SineRange
        {
            public readonly float Duration, Power, Start, End;

            public SineRange(float duration, float power, float start, float end)
            {
                Duration = duration;
                Power = power;
                Start = start;
                End = end;
            }

            // Source fractions .5..1 fit a half-sine decay. This additive envelope
            // is a reconstruction of the binding, not an original engine formula.
            public float Evaluate(float elapsed) =>
                Power * Mathf.Sin(Mathf.PI * Mathf.Lerp(Start, End, Mathf.Clamp01(elapsed / Duration)));
        }

        private sealed class Blur
        {
            public string Id;
            public float Start, Duration, Range, Samples, Ratio;
            public Vector2 Direction;
            public SineRange[] Modulation;
        }

        private readonly Camera camera;
        private readonly List<Blur> active = new List<Blur>();
        private Camera capture;
        private GameObject composite;
        private Mesh mesh;
        private Material material;
        private RenderTexture target;
        private float time;

        public BattleSourcePostEffect(Camera camera) => this.camera = camera;

        internal float? ActiveRange
        {
            get
            {
                if (active.Count == 0) return null;
                var blur = active[active.Count - 1];
                float range = blur.Range;
                foreach (var sine in blur.Modulation)
                    range += sine.Evaluate(time - blur.Start);
                return Mathf.Max(0f, range);
            }
        }

        public bool Play(JObject node, JObject[] modulators)
        {
            if ((string)node["program"] != "sprite_blur" ||
                !TryNumber(node, "u_range", out float range) ||
                !TryNumber(node, "u_sample", out float samples) || samples <= .5f ||
                !TryNumber(node, "duration", out float duration) || duration <= 0f)
                return false;

            var random = modulators.FirstOrDefault(item => (string)item["etty"] == "RANDOM_VALUE" &&
                (string)item["name"] == "u_range" && item["value"] != null);
            if (random != null) range = Number(random, "value", range);
            var sineNodes = modulators.Where(item => item != random).ToArray();
            var modulation = new SineRange[sineNodes.Length];
            for (int index = 0; index < sineNodes.Length; index++)
            {
                var item = sineNodes[index];
                if ((string)item["etty"] != "SINE_VALUE" || (string)item["name"] != "u_range" ||
                    !TryNumber(item, "duration", out float period) || period <= 0f ||
                    !TryNumber(item, "power", out float power))
                    return false;
                modulation[index] = new SineRange(period / 1000f, power,
                    Number(item, "start_rad"), Number(item, "finish_rad", 1f));
            }

            var effect = new Blur
            {
                Id = (string)node["id"] ?? "pp:" + node["guid"],
                Start = time,
                Duration = duration / 1000f,
                Range = range,
                Samples = Mathf.Min(5f, samples),
                Ratio = Mathf.Clamp01(Number(node, "u_ratio")),
                Direction = new Vector2(Number(node, "direction_x"), Number(node, "direction_y")),
                Modulation = modulation
            };
            active.Add(effect);
            if (UpdateDisplay()) return true;
            active.Remove(effect);
            ReleaseDisplay();
            return false;
        }

        public void Tick(float delta)
        {
            time += Mathf.Max(0f, delta);
            active.RemoveAll(effect => time >= effect.Start + effect.Duration);
            if (!UpdateDisplay())
            {
                Debug.LogWarning("Could not update the battle sprite_blur render target.");
                Reset();
            }
        }

        public void Stop(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            active.RemoveAll(effect => effect.Id == id);
            if (!UpdateDisplay()) Reset();
        }

        public void Reset()
        {
            active.Clear();
            time = 0f;
            ReleaseDisplay();
        }

        public void Dispose() => Reset();

        private bool UpdateDisplay()
        {
            float? range = ActiveRange;
            if (!range.HasValue)
            {
                ReleaseDisplay();
                return true;
            }
            if (!camera) return true;
            if (!camera.orthographic || (camera.cullingMask & (1 << CompositeLayer)) == 0) return false;
            if (!capture && !CreateDisplay()) return false;

            int width = Mathf.Max(1, camera.pixelWidth);
            int height = Mathf.Max(1, camera.pixelHeight);
            if (!target || target.width != width || target.height != height)
            {
                var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Battle post-effect image",
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!texture.Create())
                {
                    Destroy(texture);
                    return false;
                }
                var previous = target;
                target = texture;
                material.mainTexture = target;
                capture.targetTexture = target;
                if (previous) { previous.Release(); Destroy(previous); }
            }

            // Enabled offscreen cameras are rendered by URP. The original screen camera
            // retains its viewport, target, and screen-space Canvas bindings.
            capture.CopyFrom(camera);
            capture.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            capture.targetTexture = target;
            capture.rect = new Rect(0f, 0f, 1f, 1f);
            capture.aspect = camera.aspect;
            capture.cullingMask = camera.cullingMask & ~(1 << CompositeLayer);
            capture.depth = camera.depth - 1f;
            capture.enabled = true;

            float viewHeight = camera.orthographicSize * 2f;
            composite.transform.localScale = new Vector3(viewHeight * camera.aspect, viewHeight, 1f);
            composite.transform.localPosition = Vector3.forward * (camera.nearClipPlane + .5f);
            var current = active[active.Count - 1];
            material.SetVector("u_resolution", new Vector4(width, height, 0f, 0f));
            material.SetVector("u_direction", new Vector4(current.Direction.x, current.Direction.y, 0f, 0f));
            material.SetFloat("u_range", range.Value);
            material.SetFloat("u_sample", current.Samples);
            material.SetFloat("u_ratio", current.Ratio);
            return true;
        }

        private bool CreateDisplay()
        {
            var shader = Resources.Load<Shader>("BattleSourcePostEffect");
            if (!shader || !shader.isSupported) return false;
            var captureObject = new GameObject("Battle post-effect camera");
            captureObject.transform.SetParent(camera.transform, false);
            capture = captureObject.AddComponent<Camera>();
            capture.enabled = false;

            composite = new GameObject("Battle post-effect composite", typeof(MeshFilter), typeof(MeshRenderer));
            composite.layer = CompositeLayer;
            composite.transform.SetParent(camera.transform, false);
            mesh = new Mesh
            {
                name = "Battle post-effect quad",
                vertices = new[]
                {
                    new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f),
                    new Vector3(.5f, .5f), new Vector3(-.5f, .5f)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            composite.GetComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(shader) { name = "Battle sprite blur" };
            var renderer = composite.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = CompositeSortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        private void ReleaseDisplay()
        {
            if (capture)
            {
                capture.enabled = false;
                capture.targetTexture = null;
                Destroy(capture.gameObject);
            }
            if (composite)
            {
                composite.SetActive(false);
                Destroy(composite);
            }
            if (target) { target.Release(); Destroy(target); }
            Destroy(mesh);
            Destroy(material);
            capture = null;
            composite = null;
            target = null;
            mesh = null;
            material = null;
        }

        private static float Number(JObject node, string key, float fallback = 0f) =>
            TryNumber(node, key, out float value) ? value : fallback;

        private static bool TryNumber(JObject node, string key, out float value) =>
            float.TryParse(node[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            float.IsFinite(value);

        private static void Destroy(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
