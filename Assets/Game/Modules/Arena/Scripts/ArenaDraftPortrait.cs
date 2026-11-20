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
        // Unlit gold at the point of the native draft rails (left/right midpoint).
        private static readonly Color GuideRailColor = new Color32(200, 173, 136, 255);
        private static readonly Color GuideRailLinear = GuideRailColor.linear;
        private static readonly HashSet<int> ActiveStages = new HashSet<int>();
        private readonly RawImage output;
        private readonly bool miniature;
        private readonly Vector2? nativeEffectSize;
        private readonly Vector2? effectRightFade;
        private readonly bool blendCardLight, centralBanLight, turnGuide;
        private readonly List<Color> guideTints;
        private Mesh guideMesh;
        private RectTransform effectPlacement;
        private float effectScale = 1f;
        private bool BanLight => centralBanLight || effectPlacement != null;
        private readonly Material originalMaterial;
        private readonly ISkeletonPlayerFactory factory = new SkeletonPlayerFactory();

        private SkeletonPlayerSource source;
        private string skin;
        private string animation, completionClip;
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
            Vector2? effectRightFade = null, bool blendCardLight = false, bool centralBanLight = false,
            bool turnGuide = false)
        {
            this.output = output ? output : throw new ArgumentNullException(nameof(output));
            this.miniature = miniature;
            if (nativeEffectSize.HasValue && (nativeEffectSize.Value.x <= 0 || nativeEffectSize.Value.y <= 0))
                throw new ArgumentOutOfRangeException(nameof(nativeEffectSize));
            this.nativeEffectSize = nativeEffectSize;
            this.effectRightFade = effectRightFade;
            this.blendCardLight = blendCardLight;
            this.centralBanLight = centralBanLight;
            this.turnGuide = turnGuide;
            if (turnGuide && QualitySettings.activeColorSpace == ColorSpace.Linear)
                guideTints = new List<Color>(16); // Four authored additive quads.
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
            completionClip = null;
            effectPlacement = null;
            effectScale = 1f;
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

        internal bool PlayOneShot(SkeletonPlayerSource source, string animation, RectTransform placement = null,
            float scale = 1f, string loopAfter = null)
        {
            if (disposed || !output) return false;
            if (source == null || string.IsNullOrWhiteSpace(animation)) { Clear(); return false; }
            ResetFeedback();
            this.source = source;
            this.skin = null;
            this.animation = animation;
            oneShot = true;
            completionClip = loopAfter;
            effectPlacement = placement;
            effectScale = scale;
            if (!output.gameObject.activeInHierarchy)
            {
                ReleaseStage();
                return true;
            }
            return StartPlayer();
        }

        internal bool IsVisible => output && output.enabled;

        internal void AddParticles(TextAsset definition, IReadOnlyDictionary<string, Texture2D> textures,
            float delay, float angle)
        {
            if (!definition || !stage) return;
            ReleaseParticles();
            particleAnchor = new GameObject("Draft effect particles") { layer = PreviewLayer };
            particleAnchor.transform.SetParent(stage.transform, false);
            particleAnchor.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            particleAnchor.transform.localScale = Vector3.one * effectScale;
            particleDelay = delay;
            particles = new BattleHudParticles(definition, textures, particleAnchor.transform, Vector2.zero, .01f, 5,
                premultiplied: true, sourceColorsSrgb: true);
            if (blendCardLight || BanLight)
                foreach (var renderer in particleAnchor.GetComponentsInChildren<MeshRenderer>(true))
                    ConfigureEffectLight(renderer, particle: true);
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
            // Formation can load four previews in the frame that starts the ban reveal.
            // Keep the stamp's impact visible instead of consuming its whole clip in that frame.
            if (oneShot && BanLight) unscaledDeltaTime = Mathf.Min(unscaledDeltaTime, .10f);
            float before = player.Time;
            if (unscaledDeltaTime > 0)
            {
                player.Tick(unscaledDeltaTime);
                if (guideMesh) ApplyGuideOpacity();
            }
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
                if (completionClip == null)
                {
                    Clear();
                    return;
                }
                // Keep the authored particles and the render target across a requested intro-to-loop handoff.
                float overflow = Mathf.Max(0f, before + unscaledDeltaTime - player.Duration);
                animation = completionClip;
                completionClip = null;
                oneShot = false;
                player.Play(animation, true);
                if (overflow > 0f) player.Tick(overflow);
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
            completionClip = null;
            effectPlacement = null;
            effectScale = 1f;
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
                material.SetFloat("_PreserveBanLight", BanLight || turnGuide ? 1f : 0f);
                player?.Dispose();
                player = factory.Create(source, stage.transform);
                player.Root.transform.localScale *= effectScale;
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

                if (completionClip != null && !HasAnimation(completionClip))
                    throw new ArgumentException("未知骨骼动画 " + completionClip);
                if (HasAnimation(clip)) player.Play(clip, !oneShot);
                else if (animation != null) throw new ArgumentException("未知骨骼动画 " + clip);

                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || mesh.vertexCount == 0) throw new InvalidOperationException("骨骼预览没有可见网格。");
                if (blendCardLight || BanLight || turnGuide)
                    ConfigureEffectLight(player.Root.GetComponent<MeshRenderer>(), particle: false);
                if (guideTints != null)
                {
                    guideMesh = mesh;
                    ApplyGuideOpacity();
                }
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

        private void ApplyGuideOpacity()
        {
            // The source varies additive light intensity in display space. The UI composite
            // applies its node alpha once more after the linear render target.
            float nodeAlpha = output.color.a;
            if (nodeAlpha <= 0f) return;
            guideMesh.GetColors(guideTints);
            // Both source skeletons draw two identical arrow quads over the gold edge,
            // followed by arrow_glow and the wide flash. Display-space addition on the
            // lit rail emits more light than adding the same colours against black.
            for (int i = 0; i < guideTints.Count; i += 4)
            {
                var tint = guideTints[i];
                if (i < 8)
                {
                    float combinedAlpha = 2f * nodeAlpha;
                    // The recorded edge stays cool-white through the 0x47 trough. Match
                    // its measured contrast on gold while retaining the source timing.
                    float pulse = Mathf.InverseLerp(71f / 255f, 1f, tint.a);
                    float lineIntensity = Mathf.Lerp(.40f, .90f, pulse);
                    var litRail = GuideRailColor + tint * (combinedAlpha * lineIntensity);
                    tint = ((litRail.linear - GuideRailLinear) / combinedAlpha).gamma;
                    tint.a = 1f;
                }
                else tint.a = Mathf.GammaToLinearSpace(tint.a * nodeAlpha) / nodeAlpha;
                for (int vertex = i; vertex < i + 4; vertex++) guideTints[vertex] = tint;
            }
            guideMesh.SetColors(guideTints);
        }

        private void ConfigureEffectLight(MeshRenderer renderer, bool particle)
        {
            // Additive RGB must not contribute to the offscreen coverage mask.
            foreach (var layer in renderer.sharedMaterials)
            {
                // Particle vertices already use linear colors. The Spine keys are sRGB.
                if (!particle && QualitySettings.activeColorSpace == ColorSpace.Linear)
                    layer.SetFloat("_SrgbVertexTint", 1f);
                if (layer.GetInt("_DstBlend") != (int)UnityEngine.Rendering.BlendMode.One) continue;
                layer.SetInt("_SrcAlphaBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                layer.SetInt("_DstAlphaBlend", (int)UnityEngine.Rendering.BlendMode.One);
                // The guide atlas stores the emitted light in its premultiplied RGB.
                // Let the sRGB sampler decode it directly, including the dim outer halo.
                if (turnGuide) layer.SetFloat("_PmaSrgb", 0f);
                if (!particle)
                {
                    if (effectPlacement)
                    {
                        // The small card reveal keeps a concentrated flash at the stamp.
                        // Reduce the low-alpha red Spine halo before compositing it over the card.
                        layer.SetFloat("_SoftEmissionFalloff", 1f);
                        layer.SetFloat("_PresentationOpacity", .68f);
                    }
                    continue;
                }
                // The broad native halo and compact sparks use different textures.
                // Keep the spark cores bright while letting the halo fall away quickly.
                bool banHalo = BanLight && layer.mainTexture && layer.mainTexture.name == "pati_03";
                if (!BanLight || banHalo) layer.SetFloat("_SoftEmissionFalloff", 1f);
                if (banHalo) layer.SetFloat("_PresentationOpacity", .34f);
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
            int size = miniature ? 384 : 768;
            var format = camera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            target = new RenderTexture(turnGuide ? 512 : size, turnGuide ? 1280 : size, 16, format)
            {
                name = "Arena draft preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            camera.targetTexture = target;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (nativeEffectSize.HasValue && !turnGuide) material.SetFloat("_SoftEffectEdges", 1f);
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
                float halfHeight = Mathf.Max(size.y, size.x / aspect) * .005f;
                camera.orthographicSize = halfHeight;
                var center = new Vector3(0, 0, -10);
                if (effectPlacement && rect.width > 0f && rect.height > 0f)
                {
                    var stamp = effectPlacement.TransformPoint(effectPlacement.rect.center);
                    Vector2 local = (Vector2)output.rectTransform.InverseTransformPoint(stamp) - rect.center;
                    center.x = -local.x / rect.width * halfHeight * 2f * aspect;
                    center.y = -local.y / rect.height * halfHeight * 2f;
                }
                camera.transform.localPosition = center;
            }
            else if (miniature)
            {
                // Enlarge only the model surface about its authored floor point. The input,
                // floor ring and change control stay on the prefab slot. Keeping the wider
                // render area preserves the existing framing allowance for large weapons.
                const float addedScale = .5f;
                const float floorY = 50f / 240f;
                var surface = output.rectTransform;
                surface.anchorMin = new Vector2(-addedScale * .5f, -addedScale * floorY);
                surface.anchorMax = new Vector2(1f + addedScale * .5f, 1f + addedScale * (1f - floorY));
                surface.offsetMin = surface.offsetMax = Vector2.zero;

                // Size the camera from the initial pose once; subsequent animation cannot zoom or shift the floor.
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
            guideMesh = null;
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
