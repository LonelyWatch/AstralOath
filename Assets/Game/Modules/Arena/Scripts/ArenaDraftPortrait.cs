using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using AstralOath.Client.BattleScene;
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
        private readonly Vector2? nativeEffectSize;
        private readonly Vector2? effectRightFade;
        private readonly bool blendCardLight;
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
        private BattleHudParticles particles;
        private GameObject particleAnchor;
        private float particleDelay;
        private Bounds framingBounds;
        private int stageIndex = -1;
        private bool disposed;
        private bool oneShot, feedbackActive;
        private float feedbackElapsed;
        private Vector2 feedbackOrigin;
        private static readonly int WhiteFlashId = Shader.PropertyToID("_WhiteFlash");
        // Visual feedback parameters; the source CSB does not author the portrait's runtime tween.
        private const float FeedbackDuration = .30f;
        private const float FeedbackRise = .08f;
        private const float FeedbackHeight = 20f;

        internal ArenaDraftPortrait(RawImage output, bool miniature)
            : this(output, miniature, null) { }

        internal ArenaDraftPortrait(RawImage output, bool miniature = false, Vector2? nativeEffectSize = null,
            Vector2? effectRightFade = null, bool blendCardLight = false)
        {
            this.output = output ? output : throw new ArgumentNullException(nameof(output));
            this.miniature = miniature;
            if (nativeEffectSize.HasValue && (nativeEffectSize.Value.x <= 0 || nativeEffectSize.Value.y <= 0))
                throw new ArgumentOutOfRangeException(nameof(nativeEffectSize));
            this.nativeEffectSize = nativeEffectSize;
            this.effectRightFade = effectRightFade;
            this.blendCardLight = blendCardLight;
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
            bool same = !oneShot && ReferenceEquals(this.source, source) && this.skin == skin && this.animation == animation;
            if (same && player != null) return true;
            oneShot = false;
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

        internal bool PlayOneShot(SkeletonPlayerSource source, string animation)
        {
            if (disposed || !output) return false;
            if (source == null || string.IsNullOrWhiteSpace(animation)) { Clear(); return false; }
            ResetFeedback();
            this.source = source;
            this.skin = null;
            this.animation = animation;
            oneShot = true;
            if (!output.gameObject.activeInHierarchy)
            {
                ReleaseStage();
                return true;
            }
            return StartPlayer();
        }

        internal bool IsVisible => output && output.enabled;

        internal void AddParticles(TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures,
            float delay, float angle, float opacity)
        {
            if (!definition || !stage) return;
            ReleaseParticles();
            particleAnchor = new GameObject("Draft effect particles") { layer = PreviewLayer };
            particleAnchor.transform.SetParent(stage.transform, false);
            particleAnchor.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            particleDelay = delay;
            particles = new BattleHudParticles(definition, textures, particleAnchor.transform, Vector2.zero, .01f, 5,
                premultiplied: true, sourceColorsSrgb: true);
            particles.SetOpacity(opacity);
            if (blendCardLight)
                foreach (var renderer in particleAnchor.GetComponentsInChildren<MeshRenderer>(true))
                    ConfigurePickLight(renderer, particle: true);
            if (delay == 0f) particles.Play();
        }

        internal void SetDesaturation(float amount)
        {
            if (material) material.SetFloat("_Desaturation", amount);
        }

        internal void PlaySelectionFeedback()
        {
            if (disposed || !output || !output.enabled || !output.gameObject.activeInHierarchy) return;
            ResetFeedback();
            feedbackOrigin = output.rectTransform.anchoredPosition;
            feedbackElapsed = 0;
            feedbackActive = true;
            if (material) material.SetFloat(WhiteFlashId, 1f);
        }

        internal void ResetFeedback()
        {
            if (feedbackActive && output) output.rectTransform.anchoredPosition = feedbackOrigin;
            feedbackActive = false;
            feedbackElapsed = 0;
            if (material) material.SetFloat(WhiteFlashId, 0f);
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
            if (particles != null && unscaledDeltaTime > 0f)
            {
                float activeTime = unscaledDeltaTime;
                if (particleDelay > 0f)
                {
                    activeTime = Mathf.Max(0f, unscaledDeltaTime - particleDelay);
                    particleDelay = Mathf.Max(0f, particleDelay - unscaledDeltaTime);
                    if (particleDelay == 0f) particles.Play();
                }
                particles.Tick(activeTime);
            }
            if (oneShot && player.Time >= player.Duration)
            {
                Clear();
                return;
            }
            if (feedbackActive)
            {
                feedbackElapsed += Mathf.Max(0, unscaledDeltaTime);
                if (feedbackElapsed >= FeedbackDuration) ResetFeedback();
                else
                {
                    float rise = feedbackElapsed / FeedbackRise;
                    float fall = (feedbackElapsed - FeedbackRise) / (FeedbackDuration - FeedbackRise);
                    float height = feedbackElapsed <= FeedbackRise
                        ? Mathf.SmoothStep(0, 1, rise) : 1f - Mathf.SmoothStep(0, 1, fall);
                    output.rectTransform.anchoredPosition = feedbackOrigin + Vector2.up * (FeedbackHeight * height);
                    if (material) material.SetFloat(WhiteFlashId,
                        1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .18f, feedbackElapsed)));
                }
            }
        }

        internal void Clear()
        {
            source = null;
            skin = null;
            animation = null;
            oneShot = false;
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
                ResetFeedback();
                ReleaseParticles();
                if (!stage) CreateStage();
                player?.Dispose();
                player = factory.Create(source, stage.transform);
                var chosenSkin = skin ?? (miniature || nativeEffectSize.HasValue ? null : "normal");
                if (chosenSkin != null && HasSkin(chosenSkin)) player.SetSkin(chosenSkin);

                string clip = animation ?? (miniature && !HasAnimation("idle") && HasAnimation("b_idle") ? "b_idle" : "idle");
                if (!HasAnimation(clip) && animation == null)
                    foreach (var candidate in player.Animations)
                        if (candidate.Duration > 0)
                        {
                            clip = candidate.Name;
                            break;
                        }

                if (HasAnimation(clip)) player.Play(clip, !oneShot);
                else if (animation != null) throw new ArgumentException("未知骨骼动画 " + clip);

                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || mesh.vertexCount == 0) throw new InvalidOperationException("骨骼预览没有可见网格。");
                if (blendCardLight)
                    ConfigurePickLight(player.Root.GetComponent<MeshRenderer>(), particle: false);
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

        private static void ConfigurePickLight(MeshRenderer renderer, bool particle)
        {
            // The five pick rigs have a single atlas. Their two blend materials are
            // created at the first frame and owned by this player instance.
            foreach (var layer in renderer.sharedMaterials)
            {
                // Particle vertices already use linear colors. The Spine keys are sRGB.
                if (!particle && QualitySettings.activeColorSpace == ColorSpace.Linear)
                    layer.SetFloat("_SrgbVertexTint", 1f);
                if (layer.GetInt("_DstBlend") != (int)UnityEngine.Rendering.BlendMode.One) continue;
                layer.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                layer.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (particle) layer.SetFloat("_SoftEmissionFalloff", 1f);
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
            camera.allowHDR = nativeEffectSize.HasValue && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
            camera.allowMSAA = false;
            camera.depth = -100;
            int size = miniature ? 256 : 768;
            var format = camera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            target = new RenderTexture(size, size, 16, format)
            {
                name = "Arena draft preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            camera.targetTexture = target;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (nativeEffectSize.HasValue) material.SetFloat("_SoftEffectEdges", 1f);
            if (blendCardLight) material.SetFloat("_BlendCardLight", 1f);
            if (effectRightFade.HasValue)
            {
                var fade = effectRightFade.Value;
                material.SetVector("_RightFade", new Vector4(fade.x, fade.y, 0f, 0f));
            }
        }

        private void FrameOutput()
        {
            var rect = output.rectTransform.rect;
            float aspect = Mathf.Clamp(rect.width / Mathf.Max(rect.height, 1), .1f, 10f);
            camera.aspect = aspect;
            if (nativeEffectSize.HasValue)
            {
                // Effect sources use .01 world units per native pixel and an authored origin.
                var size = nativeEffectSize.Value;
                camera.orthographicSize = Mathf.Max(size.y, size.x / aspect) * .005f;
                camera.transform.localPosition = new Vector3(0, 0, -10);
            }
            else if (miniature)
            {
                // CSB specifies the floor anchors, not the runtime model scale. Use one
                // display scale for the roster; only oversized meshes are reduced once.
                // Keep the rig origin on the floor (120,50), never recenter on animated bounds.
                float pixelsPerUnit = MiniaturePixelsPerUnit(framingBounds);
                camera.orthographicSize = 120f / pixelsPerUnit;
                camera.transform.localPosition = new Vector3(0, 70f / pixelsPerUnit, -10);
            }
            else
            {
                // pvplive_ready: n_portrait=(500,-375), scale=1.2, visible canvas=998x720.
                // The player uses .01 units/source pixel; retaining this origin preserves
                // each hero's authored face position instead of normalizing their bounds.
                camera.orthographicSize = 3f;
                camera.transform.localPosition = new Vector3(-1f / 120f, 735f / 120f, -10);
            }
        }

        private static float MiniaturePixelsPerUnit(Bounds bounds)
        {
            const float nominal = 29f;
            // Eight native pixels of breathing room on all four sides.
            float scale = nominal;
            if (bounds.min.x < 0) scale = Mathf.Min(scale, 112f / -bounds.min.x);
            if (bounds.max.x > 0) scale = Mathf.Min(scale, 112f / bounds.max.x);
            if (bounds.min.y < 0) scale = Mathf.Min(scale, 42f / -bounds.min.y);
            if (bounds.max.y > 0) scale = Mathf.Min(scale, 182f / bounds.max.y);
            return Mathf.Max(.01f, scale);
        }

        private void ReleaseParticles()
        {
            particles?.Dispose();
            particles = null;
            if (!particleAnchor) return;
            particleAnchor.SetActive(false);
            Object.Destroy(particleAnchor);
            particleAnchor = null;
        }

        private void ReleaseStage()
        {
            ResetFeedback();
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
            ReleaseParticles();
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
