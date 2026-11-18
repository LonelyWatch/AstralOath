using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Skeletons;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Home
{
    /// <summary>One isolated source portrait camera. The inventory page owns its lifetime.</summary>
    public sealed class HeroRosterPortrait : IDisposable
    {
        private const int Layer = 30;
        private readonly RawImage output;
        private readonly int renderSize;
        private readonly int positionOffset;
        private readonly bool animated;
        private readonly float framing, headFocus;
        private GameObject stage;
        private RenderTexture target;
        private Material material;
        private ISkeletonPlayer player;
        private HomeHeroArt.Hero source;
        private bool visible;
        private int lastFrame = -1;

        public HeroRosterPortrait(RawImage output, int renderSize = 768, bool animated = true,
            int positionOffset = 0, float framing = 1.07f, float headFocus = 0f)
        {
            this.output = output;
            this.renderSize = renderSize;
            this.animated = animated;
            this.positionOffset = positionOffset;
            this.framing = framing;
            this.headFocus = headFocus;
        }

        public bool Show(HomeHeroArt.Hero next)
        {
            if (ReferenceEquals(next, source) && player != null)
                return SetVisible(true);
            Clear();
            if (next == null || !next.skeleton || !next.atlas || !next.page || !output)
                return false;
            try
            {
                using var atlas = new StringReader(next.atlas.text);
                string pageName;
                do { pageName = atlas.ReadLine()?.Trim().TrimStart('\uFEFF'); }
                while (pageName != null && pageName.Length == 0);
                if (string.IsNullOrEmpty(pageName)) return false;
                var shader = Resources.Load<Shader>("AstralOathArenaDraftPortrait");
                if (!shader) return false;
                stage = new GameObject("Inventory hero portrait") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                stage.transform.position = new Vector3(24000 + positionOffset, 16000, 0);
                player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(next.skeleton.text, next.atlas.text,
                    new Dictionary<string, Texture2D>(StringComparer.Ordinal) { [pageName] = next.page },
                    premultipliedAlpha: true), stage.transform);
                if (player.Skins.Contains("normal")) player.SetSkin("normal");
                if (player.Animations.Any(item => item.Name == "idle")) player.Play("idle", true);
                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || mesh.vertexCount == 0) { Clear(); return false; }

                var camera = new GameObject("Portrait camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << Layer;
                camera.orthographic = true;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 20f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.depth = -100f;
                var bounds = mesh.bounds;
                var center = bounds.center;
                // Keep the full authored idle pose inside a square UI surface with breathing room.
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * framing;
                camera.transform.localPosition = new Vector3(center.x, center.y + headFocus * bounds.extents.y, -10f);
                target = new RenderTexture(renderSize, renderSize, 16, RenderTextureFormat.ARGB32) { name = "Owned hero portrait" };
                camera.targetTexture = target;
                if (!animated)
                {
                    camera.enabled = false;
                    camera.Render();
                }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                output.texture = target;
                output.material = material;
                source = next;
                return SetVisible(true);
            }
            catch (Exception error)
            {
                Debug.LogWarning("Unable to show owned hero portrait: " + error.GetType().Name);
                Clear();
                return false;
            }
        }

        public bool SetVisible(bool active)
        {
            visible = active && player != null;
            if (stage) stage.SetActive(visible);
            if (output) output.enabled = visible;
            return visible;
        }

        public void Tick()
        {
            if (!visible || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            player?.Tick(Time.unscaledDeltaTime);
        }

        private void Clear()
        {
            SetVisible(false);
            player?.Dispose();
            player = null;
            source = null;
            if (output)
            {
                output.material = null;
                output.texture = null;
            }
            if (stage) { Object.Destroy(stage); stage = null; }
            if (target) { target.Release(); Object.Destroy(target); target = null; }
            if (material) { Object.Destroy(material); material = null; }
        }

        public void Dispose() => Clear();
    }
}
