using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    public sealed class BattleSkillPreviewSession : IDisposable
    {
        private const int PreviewLayer = 29;
        // CUTIN and post-effect overlays use layer 5 in the battle renderer.
        private const int OverlayLayer = 5;
        private static readonly HashSet<int> LiveStages = new();
        private readonly RawImage output;
        private readonly Texture previousTexture;
        private readonly GameObject stage;
        private readonly int stageIndex;
        private readonly List<Renderer> renderers = new();
        private readonly List<Camera> cameras = new();
        private Camera camera;
        private RenderTexture surface;
        private BattlePreviewActor caster, target;
        private BattleSkillPresentation playback;
        private int nextHit;
        private bool disposed;

        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsComplete => Elapsed >= Duration;

        internal BattleSkillPreviewSession(BattleSkillPreviewDefinition definition, RawImage output)
        {
            this.output = output ? output : throw new ArgumentNullException(nameof(output));
            if (!definition.Graph || string.IsNullOrEmpty(definition.GraphId) || !definition.Background)
                throw new ArgumentException("The skill preview requires a graph, its source ID and a backdrop.", nameof(definition));
            previousTexture = output.texture;
            while (!LiveStages.Add(stageIndex)) stageIndex++;
            stage = new GameObject("Offscreen skill preview " + definition.GraphId) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                // World-space battle cameras look towards +Z. A separate depth band per
                // preview keeps its full-screen planes out of the lobby and sibling previews.
                stage.transform.position = Vector3.back * (4096f + stageIndex * 128f);
                var background = Object.Instantiate(definition.Background, stage.transform, false);
                var units = Child("Units", stage.transform);
                caster = CreateActor(units, definition, false);
                target = CreateActor(units, definition, true);
                camera = Child("Skill preview camera", stage.transform).gameObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.localPosition = Vector3.back * 10f;
                camera.orthographic = true;
                camera.orthographicSize = 5.4f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 40f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(143, 180, 192, 255);
                camera.cullingMask = (1 << PreviewLayer) | (1 << OverlayLayer);
                camera.allowHDR = camera.allowMSAA = false;
                FitSurface();
                playback = new BattleSkillPresentation(units, new[] { definition.Graph }, definition.SkillAssets,
                    camera, null, background, definition.TimelineOverrides);
                float stageDuration = playback.Begin(definition.GraphId, caster, target, new[] { target }).Duration;
                Duration = playback.WallDuration(caster, definition.GraphId, stageDuration);
                Render();
            }
            catch { Dispose(); throw; }
        }

        private static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static BattlePreviewActor CreateActor(Transform units, BattleSkillPreviewDefinition definition, bool enemy)
        {
            // Same front slots, model scale and shadow footprint as the 1280x720 authored battle.
            var slot = Child("01 Front", Child(enemy ? "Enemies" : "Allies", units));
            slot.localPosition = new Vector3(enemy ? 2.805f : -2.805f, -1.995f);
            if (definition.Shadow)
            {
                var shadow = Child("Shadow", slot);
                shadow.localPosition = Vector3.up * .045f;
                var size = definition.Shadow.bounds.size;
                shadow.localScale = new Vector3(1.275f / size.x, .345f / size.y, 1f);
                var sprite = shadow.gameObject.AddComponent<SpriteRenderer>();
                sprite.sprite = definition.Shadow;
                sprite.sortingOrder = -50;
            }
            var body = Child(enemy ? "Slime" : definition.HeroId, slot);
            body.localScale = Vector3.one * .6f;
            var actor = body.gameObject.AddComponent<BattlePreviewActor>();
            actor.ConfigureOffscreenPreview(enemy ? definition.TargetModel : definition.CasterModel,
                enemy ? definition.TargetSkin : definition.CasterSkin, body.name, enemy ? null : definition.HeroId);
            return actor;
        }

        public void Step(float deltaSeconds)
        {
            if (disposed) return;
            if (!output) { Dispose(); return; }
            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            bool resized = FitSurface();
            float end = Mathf.Min(Duration, Elapsed + deltaSeconds);
            float afterSkill = Mathf.Max(0f, deltaSeconds - (end - Elapsed));
            if (end <= Elapsed && afterSkill <= 0f && !resized) return;
            // Advance simulation clocks without rendering intermediate catch-up frames.
            while (Elapsed < end)
            {
                float dt = Mathf.Min(end - Elapsed, 1f / 120f);
                float before = playback.StageTime;
                playback.Tick(dt);
                float stageDelta = playback.StageTime - before;
                caster.StepPreviewAmbient(stageDelta);
                target.StepPreviewAmbient(stageDelta);
                while (nextHit < playback.HitTimes.Count && playback.HitTimes[nextHit] <= playback.StageTime + .000001f)
                {
                    if (!playback.OwnsNodeAnimationPose(target)) target.PlayBattleAnimation("attacked");
                    nextHit++;
                }
                Elapsed += dt;
            }
            // The action clock ends with choreography. Its surviving emitters and the
            // actors' idle poses still run while the poster covers the battlefield.
            if (afterSkill > 0f)
            {
                playback.Tick(afterSkill);
                caster.StepPreviewAmbient(afterSkill);
                target.StepPreviewAmbient(afterSkill);
            }
            Render();
        }

        private bool FitSurface()
        {
            var rect = output.rectTransform;
            var bounds = rect.rect;
            var canvas = output.canvas ? output.canvas.rootCanvas : null;
            var view = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 Project(float x, float y) =>
                RectTransformUtility.WorldToScreenPoint(view, rect.TransformPoint(new Vector3(x, y)));
            var origin = Project(bounds.xMin, bounds.yMin);
            float w = Mathf.Max(1f, Vector2.Distance(origin, Project(bounds.xMax, bounds.yMin)));
            float h = Mathf.Max(1f, Vector2.Distance(origin, Project(bounds.xMin, bounds.yMax)));
            float scale = Mathf.Min(1f, Mathf.Min(4096, SystemInfo.maxTextureSize) / Mathf.Max(w, h));
            scale = Mathf.Min(scale, Mathf.Sqrt(3840f * 2160f / (w * h)));
            int width = Mathf.Max(1, Mathf.RoundToInt(w * scale)), height = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            if (surface && surface.width == width && surface.height == height) return false;
            var replacement = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Battle skill preview", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            if (!replacement.Create())
            {
                Object.Destroy(replacement);
                throw new InvalidOperationException("Cannot allocate the battle skill preview.");
            }
            var previous = surface;
            surface = replacement;
            camera.targetTexture = surface;
            camera.aspect = width / (float)height;
            output.texture = surface;
            if (previous) { previous.Release(); Object.Destroy(previous); }
            return true;
        }

        private void Render()
        {
            // New meshes appear throughout authored skills. Keep their world and overlay
            // distinction while excluding gameplay objects from the offscreen camera.
            stage.GetComponentsInChildren(false, renderers);
            foreach (var renderer in renderers)
                if (renderer.gameObject.layer != OverlayLayer && renderer.gameObject.layer != PreviewLayer)
                    renderer.gameObject.layer = PreviewLayer;
            camera.GetComponentsInChildren(false, cameras);
            foreach (var child in cameras)
                if (child != camera && child.targetTexture)
                {
                    child.enabled = false;
                    child.Render();
                }
            camera.Render();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { playback?.Dispose(); }
            finally
            {
                if (camera) camera.targetTexture = null;
                if (output && output.texture == surface) output.texture = previousTexture;
                if (surface) { surface.Release(); Object.Destroy(surface); }
                if (stage) { stage.SetActive(false); Object.Destroy(stage); }
                LiveStages.Remove(stageIndex);
            }
        }
    }
}
