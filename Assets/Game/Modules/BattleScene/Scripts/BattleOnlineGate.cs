using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleOnlineGate : IDisposable
    {
        private const float SourceHeight = 720f;
        private const float SourceWidth = 1280f;
        private const float InverseScale = .01f;
        private const float DesignAspect = SourceWidth / SourceHeight;
        private const float IgnitionViewScale = 1.35f;
        private const int MaxRenderWidth = 2560;
        private readonly BattleOnlineTransitionAssets assets;
        private readonly GameObject stage, overlay;
        private readonly Camera camera;
        private readonly Canvas canvas;
        private readonly RawImage picture;
        private RenderTexture texture;
        private readonly AudioSource audio;
        private readonly Dictionary<string, ISkeletonPlayer> players = new Dictionary<string, ISkeletonPlayer>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures;
        private readonly Dictionary<string, TextAsset> particles;
        private readonly List<SpineLayer> layers = new List<SpineLayer>();
        private readonly List<Particle> visuals = new List<Particle>();
        private bool disposed, showingIgnition;

        private sealed class SpineLayer
        {
            internal ISkeletonPlayer Player;
            internal string Intro, Loop;
            internal float Delay;
            internal bool Started, FinishedIntro;
        }

        private sealed class Particle
        {
            internal Transform Anchor;
            internal ISkeletonPlayer Player;
            internal string Bone;
            internal Vector3 Offset;
            internal float Delay;
            internal BattleHudParticles View;
            internal bool Started;
        }

        internal BattleOnlineGate(BattleOnlineTransitionAssets assets)
        {
            this.assets = assets;
            textures = assets.textures.ToDictionary(asset => asset.name, StringComparer.Ordinal);
            particles = assets.particles.ToDictionary(asset => asset.name, StringComparer.Ordinal);
            stage = new GameObject("Online Battle Transition Render Stage");
            // Only this camera can see these meshes. Render them on top of the old or new scene using their own canvas.
            stage.transform.position = new Vector3(4000f, 0f, 0f);
            overlay = new GameObject("Online Battle Gate", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            try
            {
                audio = overlay.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 0f;

                camera = new GameObject("Transition Camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.transform.localPosition = new Vector3(0f, 0f, -10f);
                camera.depth = -100f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.orthographic = true;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 30f;
                camera.allowHDR = false;
                camera.allowMSAA = false;

                canvas = overlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32700;
                picture = new GameObject("Native Gate Frame", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                var rect = picture.rectTransform;
                rect.SetParent(overlay.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                UpdateProjection();

                var factory = new SkeletonPlayerFactory();
                foreach (var effect in assets.skeletons)
                {
                    var player = factory.Create(effect.CreateSource(InverseScale), stage.transform);
                    player.Root.name = effect.page.name;
                    player.Root.SetActive(false);
                    players.Add(effect.page.name, player);
                }
                Object.DontDestroyOnLoad(stage);
                Object.DontDestroyOnLoad(overlay);
            }
            catch { Dispose(); throw; }
        }

        internal Task CloseAsync(CancellationToken token) => PlayAsync(assets.closeCfx, assets.doorClose, true, token);
        internal Task OpenAsync(CancellationToken token) => PlayAsync(assets.openCfx, assets.doorOpen, false, token);
        internal Task IgniteAsync(CancellationToken token) => PlayAsync(assets.ignitionCfx, null, false, token);

        // The middle gate layer has an authored .5s closed loop. The door panels
        // use zero-duration loop poses and remain visible without redrawing them.
        internal void TickClosed()
        {
            UpdateProjection();
            float delta = FrameDelta();
            foreach (var layer in layers)
                if (layer.FinishedIntro && layer.Loop != null && layer.Player.Duration > 0f)
                    layer.Player.Tick(delta);
        }

        private async Task PlayAsync(TextAsset effect, AudioClip sound, bool hold, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ClearEffects();
            showingIgnition = effect == assets.ignitionCfx;
            UpdateProjection();
            var parts = (JArray)JObject.Parse(effect.text)["primitive"];
            foreach (var row in parts.OfType<JObject>())
            {
                string source = (string)row["source"];
                switch ((string)row["format"])
                {
                    case "spine":
                        AddLayer(row, players[source]);
                        break;
                    case "particle":
                        AddParticle(row, particles[source + ".particle"]);
                        break;
                    default:
                        throw new FormatException($"Unsupported online battle transition layer '{source}'.");
                }
            }
            if (sound) audio.PlayOneShot(sound);

            float elapsed = 0f;
            bool playing = Advance(elapsed, 0f);
            while (playing)
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                UpdateProjection();
                float delta = FrameDelta();
                elapsed += delta;
                playing = Advance(elapsed, delta);
            }
            if (!hold) ClearEffects();
        }

        private void AddLayer(JObject row, ISkeletonPlayer player)
        {
            string[] clips = ((string)row["ani"] ?? "animation").Split(',');
            player.Root.transform.localPosition = new Vector3(Number(row, "x"), Number(row, "y"), 0f) * InverseScale;
            player.Root.transform.localScale = Vector3.one * Number(row, "scale", 1f);
            player.Root.GetComponent<MeshRenderer>().sortingOrder = (int)Number(row, "z");
            layers.Add(new SpineLayer
            {
                Player = player,
                Intro = clips[0],
                Loop = clips.Length > 1 ? clips[1] : null,
                Delay = Number(row, "delay") * .001f
            });
        }

        private void AddParticle(JObject row, TextAsset source)
        {
            var anchor = new GameObject("Native transition particle anchor").transform;
            anchor.SetParent(stage.transform, false);
            string[] scale = ((string)row["scale"] ?? "1,1").Split(',');
            anchor.localScale = new Vector3(Parse(scale[0], 1f), scale.Length > 1 ? Parse(scale[1], 1f) : 1f, 1f);
            string[] attachment = ((string)row["attach"])?.Split('/');
            var visual = new Particle
            {
                Anchor = anchor,
                Delay = Number(row, "delay") * .001f,
                Player = attachment?.Length == 2 ? players[attachment[0]] : null,
                Bone = attachment?.Length == 2 ? attachment[1] : null,
                Offset = new Vector3(Number(row, "x"), Number(row, "y"), 0f) * InverseScale
            };
            visuals.Add(visual);
            visual.View = new BattleHudParticles(source, textures, anchor, Vector2.zero, InverseScale, (int)Number(row, "z"));
        }

        private bool Advance(float elapsed, float delta)
        {
            bool playingIntro = false;
            foreach (var layer in layers)
            {
                if (elapsed < layer.Delay) { playingIntro = true; continue; }
                var player = layer.Player;
                if (!layer.Started)
                {
                    layer.Started = true;
                    player.Root.SetActive(true);
                    player.Play(layer.Intro, false);
                }
                if (layer.FinishedIntro)
                {
                    if (layer.Loop != null && player.Duration > 0f) player.Tick(delta);
                    continue;
                }
                player.Tick(delta);
                if (player.Time < player.Duration) { playingIntro = true; continue; }

                layer.FinishedIntro = true;
                if (layer.Loop != null) player.Play(layer.Loop, true);
                else player.Root.SetActive(false);
            }
            foreach (var particle in visuals)
            {
                if (elapsed < particle.Delay) continue;
                if (particle.Player != null && particle.Player.TryGetBoneSkeletonPosition(particle.Bone, out var bone))
                    particle.Anchor.position = particle.Player.Root.transform.TransformPoint((Vector3)bone + particle.Offset);
                else particle.Anchor.localPosition = particle.Offset;
                if (!particle.Started)
                {
                    particle.Started = true;
                    particle.View.Play();
                }
                particle.View.Tick(delta);
            }
            return playingIntro;
        }

        private void UpdateProjection()
        {
            Rect output = canvas.pixelRect;
            // An offscreen camera can display this canvas at a size different from the Game view.
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera)
                output = canvas.worldCamera.pixelRect;
            if (output.width <= 1f || output.height <= 1f)
                output = new Rect(0f, 0f, Screen.width, Screen.height);
            float aspect = output.height > 1f && output.width > 1f ? output.width / output.height : DesignAspect;
            int height = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(SourceHeight, MaxRenderWidth / aspect)));
            int width = Mathf.Max(1, Mathf.RoundToInt(height * aspect));

            // The gate covers the output. IGNITION retains its original fit framing, but its
            // translucent effects can extend through the full output instead of a fitted rectangle.
            float frameScale = showingIgnition ? IgnitionViewScale * Mathf.Max(1f, DesignAspect / aspect) :
                Mathf.Min(1f, DesignAspect / aspect);
            camera.aspect = aspect;
            camera.orthographicSize = .5f * SourceHeight * InverseScale * frameScale;
            if (texture && texture.width == width && texture.height == height) return;

            ReleaseTarget();
            texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            { name = "Online Battle Gate Frame", useMipMap = false, wrapMode = TextureWrapMode.Clamp };
            texture.Create();
            camera.targetTexture = texture;
            picture.texture = texture;
        }

        private void ReleaseTarget()
        {
            if (!texture) return;
            if (camera) camera.targetTexture = null;
            if (picture) picture.texture = null;
            texture.Release();
            Object.Destroy(texture);
            texture = null;
        }

        private static float FrameDelta() => Mathf.Clamp(Time.unscaledDeltaTime, 0f, .05f);
        private static float Number(JObject row, string key, float fallback = 0) => Parse((string)row[key], fallback);
        private static float Parse(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number : fallback;

        private void ClearEffects()
        {
            foreach (var layer in layers) layer.Player.Root.SetActive(false);
            layers.Clear();
            foreach (var item in visuals)
            {
                item.View?.Dispose();
                if (item.Anchor)
                {
                    item.Anchor.gameObject.SetActive(false);
                    Object.Destroy(item.Anchor.gameObject);
                }
            }
            visuals.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearEffects();
            foreach (var player in players.Values) player.Dispose();
            players.Clear();
            ReleaseTarget();
            if (stage) { stage.SetActive(false); Object.Destroy(stage); }
            if (overlay) { overlay.SetActive(false); Object.Destroy(overlay); }
        }
    }
}
