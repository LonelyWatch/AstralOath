using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaDraftPortrait : IDisposable
    {
        private const int PreviewLayer = 30;
        private static readonly HashSet<int> ActiveStages = new HashSet<int>();
        private readonly RawImage output;
        private readonly bool miniature;
        private readonly Material originalMaterial;
        private readonly ISkeletonPlayerFactory factory = new SkeletonPlayerFactory();

        private SkeletonPlayerSource source;
        private string skin;
        private string animation;
        private ISkeletonPlayer player;
        private GameObject stage;
        private Camera camera;
        private RenderTexture target;
        private Material material;
        private Bounds framingBounds;
        private int stageIndex = -1;
        private bool disposed;

        internal ArenaDraftPortrait(RawImage output, bool miniature = false)
        {
            this.output = output ? output : throw new ArgumentNullException(nameof(output));
            this.miniature = miniature;
            originalMaterial = output.material;
            output.raycastTarget = false;
            output.enabled = false;
        }

        internal static SkeletonPlayerSource CreateSource(TextAsset skeleton, TextAsset atlas, Texture2D page)
        {
            if (!skeleton || !atlas || !page) return null;
            using var reader = new System.IO.StringReader(atlas.text);
            string name;
            do { name = reader.ReadLine()?.Trim().TrimStart('\uFEFF'); }
            while (name != null && name.Length == 0);
            if (string.IsNullOrEmpty(name)) return null;
            return new SkeletonPlayerSource(skeleton.text, atlas.text,
                new Dictionary<string, Texture2D>(StringComparer.Ordinal) { [name] = page }, premultipliedAlpha: true);
        }

        internal bool Show(SkeletonPlayerSource source, string skin = null, string animation = null)
        {
            if (disposed || !output) return false;
            if (source == null) { Clear(); return false; }
            bool same = ReferenceEquals(this.source, source) && this.skin == skin && this.animation == animation;
            if (same && player != null) return true;
            this.source = source;
            this.skin = skin;
            this.animation = animation;
            if (!output.gameObject.activeInHierarchy)
            {
                ReleaseStage();
                return true;
            }
            return StartPlayer();
        }

        internal void Tick(float unscaledDeltaTime)
        {
            if (disposed || !output || !output.gameObject.activeInHierarchy)
            {
                if (stage) ReleaseStage();
                return;
            }
            if (source == null || (player == null && !StartPlayer())) return;
            FrameOutput();
            if (unscaledDeltaTime > 0) player.Tick(unscaledDeltaTime);
        }

        internal void Clear()
        {
            source = null;
            skin = null;
            animation = null;
            ReleaseStage();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }

        private bool StartPlayer()
        {
            try
            {
                if (!stage) CreateStage();
                player?.Dispose();
                player = factory.Create(source, stage.transform);
                var chosenSkin = skin ?? (miniature ? null : "normal");
                if (chosenSkin != null && HasSkin(chosenSkin)) player.SetSkin(chosenSkin);

                string clip = animation ?? (miniature && HasAnimation("b_idle") ? "b_idle" : "idle");
                if (!HasAnimation(clip) && animation == null)
                    foreach (var candidate in player.Animations)
                        if (candidate.Duration > 0)
                        {
                            clip = candidate.Name;
                            break;
                        }

                if (HasAnimation(clip)) player.Play(clip, true);
                else if (animation != null) throw new ArgumentException("未知骨骼动画 " + clip);

                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || mesh.vertexCount == 0) throw new InvalidOperationException("骨骼预览没有可见网格。");
                framingBounds = mesh.bounds;
                FrameOutput();
                output.texture = target;
                output.material = material;
                output.enabled = true;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error, output);
                Clear();
                return false;
            }
        }

        private bool HasSkin(string name)
        {
            foreach (var value in player.Skins)
                if (value == name) return true;
            return false;
        }

        private bool HasAnimation(string name)
        {
            foreach (var value in player.Animations)
                if (value.Name == name) return true;
            return false;
        }

        private void CreateStage()
        {
            var shader = Resources.Load<Shader>("AstralOathArenaDraftPortrait");
            if (!shader) throw new InvalidOperationException("缺少竞技场预览的透明合成 Shader。");

            stageIndex = 0;
            while (!ActiveStages.Add(stageIndex)) stageIndex++;
            stage = new GameObject("Arena draft preview") { layer = PreviewLayer, hideFlags = HideFlags.HideAndDontSave };
            // Distinct camera frustums also isolate simultaneous preview slots on this layer.
            stage.transform.position = new Vector3(16384 + stageIndex * 256, 16384, stageIndex * 48);
            var cameraObject = new GameObject("Preview camera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(stage.transform, false);
            camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 20;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -100;
            int size = miniature ? 256 : 768;
            target = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "Arena draft preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            camera.targetTexture = target;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void FrameOutput()
        {
            var rect = output.rectTransform.rect;
            float aspect = Mathf.Clamp(rect.width / Mathf.Max(rect.height, 1), .1f, 10f);
            var size = framingBounds.extents;
            float halfHeight = Mathf.Max(.05f, Mathf.Max(size.y, size.x / aspect) * (miniature ? 1.24f : 1.10f));
            camera.aspect = aspect;
            camera.orthographicSize = halfHeight;
            var center = framingBounds.center;
            float verticalCenter = miniature ? framingBounds.min.y + halfHeight * .84f : center.y;
            camera.transform.localPosition = new Vector3(center.x, verticalCenter, -10);
        }

        private void ReleaseStage()
        {
            if (output)
            {
                output.texture = null;
                output.material = originalMaterial;
                output.enabled = false;
            }
            if (camera)
            {
                camera.enabled = false;
                camera.targetTexture = null;
                camera = null;
            }
            player?.Dispose();
            player = null;
            if (stage)
            {
                stage.SetActive(false);
                Object.Destroy(stage);
                stage = null;
            }
            if (stageIndex >= 0)
            {
                ActiveStages.Remove(stageIndex);
                stageIndex = -1;
            }
            if (target)
            {
                target.Release();
                Object.Destroy(target);
                target = null;
            }
            if (material)
            {
                Object.Destroy(material);
                material = null;
            }
        }
    }
}
