using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Home;
using AstralOath.Client.Skeletons;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    /// <summary>Original summon scene layers and rendered native FMOD events.</summary>
    public sealed class SummonPresentation : ScriptableObject
    {
        public const string Address = "summon/presentation";
        private const float PixelScale = .01f;
        private const int RenderLayer = 30;

        [Serializable]
        public sealed class Layer
        {
            public string animation;
            public TextAsset skeleton, atlas;
            public string[] pageNames;
            public Texture2D[] pages;
            public float x, y, delay, rotate, speed, scale;
            public bool loop;
        }

        [Serializable]
        public sealed class Phase
        {
            public string id;
            public Layer[] parts;
        }

        [Serializable]
        public sealed class Cue
        {
            public string id;
            public AudioClip clip;
        }

        [SerializeField] private Phase[] phases;
        [SerializeField] private Cue[] cues;
        [SerializeField] private Shader composite;

        public void Configure(Phase[] source, Cue[] audio, Shader shader)
        {
            phases = source;
            cues = audio;
            composite = shader;
        }

        public Player CreatePlayer(RawImage output, AudioSource audio) => new Player(this, output, audio);

        public sealed class Player : IDisposable
        {
            private sealed class Part
            {
                internal ISkeletonPlayer skeleton;
                internal GameObject stage;
                internal string id;
                internal int order;
                internal string[] clips;
                internal float delay, speed, introLength;
                internal bool hold, started, looping;
            }

            private readonly RawImage output;
            private readonly AudioSource audio, voice;
            private AudioSource soundtrack;
            private readonly GameObject scene;
            private readonly Camera camera;
            private readonly Material canvasMaterial;
            private readonly Material previousMaterial;
            private readonly Dictionary<string, Phase> phases = new Dictionary<string, Phase>(StringComparer.Ordinal);
            private readonly Dictionary<string, AudioClip> cues = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
            private readonly Dictionary<TextAsset, SkeletonPlayerSource> sources = new Dictionary<TextAsset, SkeletonPlayerSource>();
            private readonly SkeletonPlayerFactory factory = new SkeletonPlayerFactory();
            private readonly List<Part> active = new List<Part>();
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
            private CancellationTokenSource playback;
            private RenderTexture target;
            private ISkeletonPlayer rewardMiniature;
            private MeshFilter miniatureMesh;
            private Part miniatureTrack;
            private Vector2 miniatureRest;
            private float miniatureSize;
            private RectTransform rewardArt;
            private Part rewardTrack;
            private string rewardBone;
            private Vector2 rewardOrigin, rewardRest;
            private float elapsed;
            private int lastTick = -1;
            private bool disposed;

            internal Player(SummonPresentation assets, RawImage output, AudioSource audio)
            {
                if (!output || !audio || !assets.composite)
                    throw new ArgumentException("Summon scene requires its own display surface, audio and composite.");
                this.output = output;
                this.audio = audio;
                foreach (Phase phase in assets.phases) phases.Add(phase.id, phase);
                foreach (Cue cue in assets.cues) cues.Add(cue.id, cue.clip);

                scene = new GameObject("Native summon scene") { layer = RenderLayer, hideFlags = HideFlags.HideAndDontSave };
                scene.transform.position = new Vector3(32000, 20000, 0);
                voice = scene.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0;
                voice.outputAudioMixerGroup = audio.outputAudioMixerGroup;
                camera = new GameObject("Summon scene camera").AddComponent<Camera>();
                camera.transform.SetParent(scene.transform, false);
                camera.transform.localPosition = new Vector3(0, 0, -10f);
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 20f;
                camera.cullingMask = 1 << RenderLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.orthographic = true;
                camera.orthographicSize = 720f * PixelScale;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.depth = -110;
                camera.enabled = false;
                canvasMaterial = new Material(assets.composite) { hideFlags = HideFlags.HideAndDontSave };
                previousMaterial = output.material;
                output.material = canvasMaterial;
                output.enabled = false;
                Canvas.willRenderCanvases += Update;
            }

            public async Task PlayOpeningAsync(int rarity, CancellationToken token)
            {
                var operation = StartOperation(token);
                try
                {
                    string opening = rarity >= 5 ? "1_violet1" : rarity == 4 ? "1_yellow1" : "1_blue";
                    SetScenes(opening);
                    Play(rarity >= 5 ? "OpenViolet" : rarity == 4 ? "OpenGold" : "OpenBlue");
                    await WaitUntilAsync(.97f, operation.Token);
                    Play("Hall");
                    await WaitUntilAsync(4.1333f, operation.Token);
                }
                finally { FinishOperation(operation); }
            }

            public async Task PlayRevealAsync(int rarity, bool artifact, CancellationToken token, Action revealReady = null, RectTransform artwork = null, string heroClass = null, Action<float> captionOpacity = null)
            {
                var operation = StartOperation(token);
                try
                {
                    string grade = rarity >= 5 ? "violet" : rarity == 4 ? "yellow" : "blue";
                    SetHallScene(grade, artifact ? "artifact" : heroClass);
                    Play("Figure");
                    await WaitUntilAsync(2.36f, operation.Token);
                    Play(rarity >= 5 ? "BurstViolet" : rarity == 4 ? "BurstGold" : "BurstBlue");
                    await WaitUntilAsync(3.9f, operation.Token);
                    if (artifact)
                    {
                        SetScenes("3_b", "3_artifact_bone", "3_f");
                        Play("ArtifactReveal");
                    }
                    else SetScenes("3_b", "3_character_bone", "3_portrait_bone", "3_f");
                    AttachReward(artwork, artifact);
                    revealReady?.Invoke();
                    captionOpacity?.Invoke(0f);
                    await WaitUntilAsync(2.4f, operation.Token, captionOpacity);
                }
                finally { FinishOperation(operation); }
            }

            public void PlayTheme(AudioSource music, bool ceremony)
            {
                ThrowIfDisposed();
                if (!music || music == audio) throw new ArgumentException("Summon theme requires its own audio source.", nameof(music));
                music.Stop();
                music.clip = cues[ceremony ? "Ceremony" : "Theme"];
                music.loop = true;
                music.Play();
                soundtrack = music;
            }

            // Use while presenting the card overview. Reveal and NEW are separate original bank events.
            public void PlayCardFlip(bool rare, bool isNew)
            {
                ThrowIfDisposed();
                Play(rare ? "FlipRare" : "FlipCommon");
                if (isNew) Play("New");
            }

            public void PlaySummary(bool playCue = true)
            {
                ThrowIfDisposed();
                audio.Stop();
                StopHeroVoice();
                if (soundtrack && !soundtrack.isPlaying) soundtrack.Play();
                SetScenes("3_b");
                Advance(2.4f);
                camera.Render();
                if (playCue) Play("Summary");
            }

            public void ShowRewardDetail(bool artifact, RectTransform artwork, HomeHeroArt.Hero hero)
            {
                ThrowIfDisposed();
                if (artifact) SetScenes("3_b", "3_artifact_bone", "3_f");
                else SetScenes("3_b", "3_character_bone", "3_portrait_bone", "3_f");
                AttachReward(artwork, artifact);
                if (hero != null) ShowHeroMiniature(hero.modelSkeleton, hero.modelAtlas, hero.modelPage);
                Advance(2.4f);
                // A detail can open after this camera rendered. Publish the final hall and actor together.
                camera.Render();
            }

            public void PlayHeroVoice(string heroId)
            {
                ThrowIfDisposed();
                voice.Stop();
                if (cues.TryGetValue("Voice_" + heroId, out var clip) && clip)
                {
                    voice.clip = clip;
                    voice.Play();
                }
            }

            public void StopHeroVoice()
            {
                if (voice) voice.Stop();
            }

            public void PlayCardReady()
            {
                ThrowIfDisposed();
                Play("FlipReady");
            }

            public void Stop()
            {
                var pending = playback;
                playback = null;
                if (pending != null) pending.Cancel();
                ClearScene();
                if (output) output.enabled = false;
                if (camera) camera.enabled = false;
                if (audio) audio.Stop();
                StopHeroVoice();
                if (soundtrack) soundtrack.Stop();
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Canvas.willRenderCanvases -= Update;
                lifetime.Cancel();
                Stop();
                lifetime.Dispose();
                if (output)
                {
                    output.texture = null;
                    output.material = previousMaterial;
                }
                if (camera) camera.targetTexture = null;
                if (target) target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(canvasMaterial);
                UnityEngine.Object.Destroy(scene);
            }

            private CancellationTokenSource StartOperation(CancellationToken token)
            {
                ThrowIfDisposed();
                token.ThrowIfCancellationRequested();
                playback?.Cancel();
                var next = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
                playback = next;
                return next;
            }

            private void FinishOperation(CancellationTokenSource operation)
            {
                if (ReferenceEquals(playback, operation))
                {
                    playback = null;
                    if (operation.IsCancellationRequested) Stop();
                }
                operation.Dispose();
            }

            private void ThrowIfDisposed()
            {
                if (disposed) throw new ObjectDisposedException(nameof(Player));
            }

            private void Play(string id)
            {
                if (audio && cues.TryGetValue(id, out var clip) && clip) audio.PlayOneShot(clip);
            }

            private void SetHallScene(string grade, string heroClass)
            {
                string animation = heroClass switch
                {
                    "artifact" => "artifact",
                    "thief" or "assassin" => "assassin",
                    "soul_weaver" or "manauser" => "manauser",
                    "mage" or "knight" or "ranger" or "warrior" => heroClass,
                    _ => "warrior",
                };
                SetSceneLayers(new[] { "2_b_" + grade, "2_class", "2_" + grade }, animation);
            }

            private void SetScenes(params string[] names) => SetSceneLayers(names, null);

            private void SetSceneLayers(string[] names, string statueAnimation)
            {
                ClearScene();
                int order = 0;
                try
                {
                    foreach (string name in names)
                    {
                        foreach (Layer source in phases[name].parts)
                            AddLayer(name, source, ++order, name == "2_class" ? statueAnimation : null);
                    }
                    EnsureTarget();
                    camera.enabled = output.enabled = true;
                    elapsed = 0;
                    Advance(0);
                }
                catch
                {
                    Stop();
                    throw;
                }
            }

            private void AddLayer(string phase, Layer layer, int order, string animation)
            {
                var stage = new GameObject(layer.skeleton.name) { layer = RenderLayer };
                stage.transform.SetParent(scene.transform, false);
                var part = new Part { id = phase, order = order, stage = stage, speed = layer.speed, delay = layer.delay / 1000f,
                    clips = (animation ?? layer.animation).Split(','), hold = layer.loop };
                active.Add(part);
                stage.transform.localPosition = new Vector3(layer.x, layer.y, 0) * PixelScale;
                stage.transform.localScale = Vector3.one * layer.scale;
                stage.transform.localRotation = Quaternion.Euler(0, 0, -layer.rotate);
                if (!sources.TryGetValue(layer.skeleton, out var source))
                {
                    var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                    for (int index = 0; index < layer.pages.Length; ++index) textures.Add(layer.pageNames[index], layer.pages[index]);
                    source = new SkeletonPlayerSource(layer.skeleton.text, layer.atlas.text, textures, true, PixelScale);
                    sources.Add(layer.skeleton, source);
                }
                part.skeleton = factory.Create(source, stage.transform);
                foreach (Renderer renderer in stage.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.gameObject.layer = RenderLayer;
                    renderer.sortingOrder = order;
                }
                foreach (var clip in part.skeleton.Animations)
                    if (clip.Name == part.clips[0]) part.introLength = clip.Duration;
                stage.SetActive(false);
            }

            public void ShowHeroMiniature(TextAsset skeleton, TextAsset atlas, Texture2D page)
            {
                ThrowIfDisposed();
                if (!skeleton || !atlas || !page || rewardMiniature != null) return;
                miniatureTrack = active.Find(candidate => candidate.id == "3_character_bone");
                if (miniatureTrack == null) return;

                using var atlasText = new StringReader(atlas.text);
                string pageName;
                do { pageName = atlasText.ReadLine()?.Trim().TrimStart('\uFEFF'); }
                while (pageName != null && pageName.Length == 0);
                if (string.IsNullOrEmpty(pageName)) return;
                var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { [pageName] = page };
                var source = new SkeletonPlayerSource(skeleton.text, atlas.text, textures, true, PixelScale);
                rewardMiniature = factory.Create(source, scene.transform);
                if (rewardMiniature.Skins.Contains("normal")) rewardMiniature.SetSkin("normal");
                if (rewardMiniature.Animations.Any(clip => clip.Name == "idle")) rewardMiniature.Play("idle", true);
                foreach (Renderer renderer in rewardMiniature.Root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.gameObject.layer = RenderLayer;
                    renderer.sortingOrder = miniatureTrack.order;
                }
                miniatureMesh = rewardMiniature.Root.GetComponent<MeshFilter>();
                var mesh = miniatureMesh.sharedMesh;
                if (!mesh || mesh.bounds.size.y <= 0 ||
                    !TryGetRestPosition(miniatureTrack, "character_small", out miniatureRest))
                {
                    rewardMiniature.Dispose();
                    rewardMiniature = null;
                    return;
                }

                miniatureSize = camera.orthographicSize * .70f / mesh.bounds.size.y;
                rewardMiniature.Root.transform.localScale = miniatureSize * Vector3.one;
                UpdateHeroMiniature(0f);
            }

            private void UpdateHeroMiniature(float tick)
            {
                if (rewardMiniature == null ||
                    !miniatureTrack.skeleton.TryGetBoneSkeletonPosition("character_small", out Vector2 position)) return;
                rewardMiniature.Tick(tick);

                var bounds = miniatureMesh.sharedMesh.bounds;
                Vector3 offset = miniatureTrack.looping
                    ? Vector3.zero
                    : miniatureTrack.stage.transform.TransformVector(position - miniatureRest);
                Vector3 rest = new Vector3(-.23f * camera.aspect, -.77f, 0) * camera.orthographicSize;
                rewardMiniature.Root.transform.localPosition = rest + offset -
                    miniatureSize * new Vector3(bounds.center.x, bounds.min.y, 0);
            }

            private static bool TryGetRestPosition(Part part, string bone, out Vector2 position)
            {
                float begin = part.skeleton.Time;
                part.skeleton.Seek(part.introLength);
                bool found = part.skeleton.TryGetBoneSkeletonPosition(bone, out position);
                part.skeleton.Seek(begin);
                return found;
            }

            private void AttachReward(RectTransform artwork, bool artifact)
            {
                if (!artwork) return;
                string phase = artifact ? "3_artifact_bone" : "3_portrait_bone";
                string bone = artifact ? "portrait" : "character_big";
                Part part = active.Find(candidate => candidate.id == phase);
                if (part == null) throw new InvalidDataException("Missing native summon attachment " + phase);

                if (!TryGetRestPosition(part, bone, out rewardRest))
                    throw new InvalidDataException("Missing native summon bone " + bone);
                rewardArt = artwork;
                rewardTrack = part;
                rewardBone = bone;
                rewardOrigin = artwork.anchoredPosition;
                UpdateRewardPosition();
            }

            private void UpdateRewardPosition()
            {
                if (!rewardArt || !rewardTrack.skeleton.TryGetBoneSkeletonPosition(rewardBone, out Vector2 position))
                    return;

                // The authored UI anchor positions the artwork once the native entry has finished.
                // The native loop keeps the end anchor. Its sparse keys omit the intro's pivot setup.
                Vector3 relative = rewardTrack.looping
                    ? Vector3.zero
                    : rewardTrack.stage.transform.TransformVector(position - rewardRest);
                Rect rect = output.rectTransform.rect;
                float sceneHeight = camera.orthographicSize * 2f;
                Vector2 canvasOffset = new Vector2(relative.x * rect.width / (sceneHeight * camera.aspect),
                    relative.y * rect.height / sceneHeight);
                Vector3 worldOffset = output.rectTransform.TransformVector(canvasOffset);
                rewardArt.anchoredPosition = rewardOrigin + (Vector2)rewardArt.parent.InverseTransformVector(worldOffset);
            }

            private void ClearScene()
            {
                rewardMiniature?.Dispose();
                rewardMiniature = null;
                miniatureMesh = null;
                miniatureTrack = null;
                if (rewardArt) rewardArt.anchoredPosition = rewardOrigin;
                rewardArt = null;
                rewardTrack = null;
                rewardBone = null;
                foreach (Part part in active)
                {
                    part.stage.SetActive(false);
                    part.skeleton?.Dispose();
                    UnityEngine.Object.Destroy(part.stage);
                }
                active.Clear();
            }

            private void EnsureTarget()
            {
                Rect rect = output.rectTransform.rect;
                float ratio = rect.width > 0 && rect.height > 0 ? Mathf.Clamp(rect.width / rect.height, 1, 3) : 16f / 9f;
                int width = Mathf.RoundToInt(720 * ratio);
                if (target && target.width == width) return;
                var previous = target;
                var replacement = new RenderTexture(width, 720, 16, RenderTextureFormat.ARGB32) { name = "Original summon canvas" };
                // Canvas can resize after the scene camera has rendered this frame. Seed the replacement
                // before publishing it so the UI always samples a completed frame while dragging.
                var priorActive = RenderTexture.active;
                try
                {
                    if (previous && previous.IsCreated()) Graphics.Blit(previous, replacement);
                    else
                    {
                        RenderTexture.active = replacement;
                        GL.Clear(true, true, Color.black);
                    }
                }
                finally { RenderTexture.active = priorActive; }
                target = replacement;
                camera.targetTexture = replacement;
                camera.aspect = (float)width / replacement.height;
                output.texture = replacement;
                // This can run after Graphic.UpdateMaterial in willRenderCanvases. The current batch
                // needs the same texture as RawImage, and an already submitted batch can still use the old one.
                output.canvasRenderer.SetTexture(replacement);
                if (previous) UnityEngine.Object.Destroy(previous);
            }

            private void Update()
            {
                if (disposed || active.Count == 0 || lastTick == Time.frameCount) return;
                lastTick = Time.frameCount;
                EnsureTarget();
                Advance(Time.unscaledDeltaTime);
            }

            private void Advance(float tick)
            {
                float previous = elapsed;
                elapsed += tick;
                foreach (Part part in active)
                {
                    if (elapsed < part.delay) continue;
                    float now = (elapsed - part.delay) * part.speed;
                    float before = Mathf.Max(0, previous - part.delay) * part.speed;
                    if (!part.started)
                    {
                        part.stage.SetActive(true);
                        part.started = true;
                        part.skeleton.Play(part.clips[0], part.hold && part.clips.Length == 1);
                    }
                    if (!part.looping && part.clips.Length > 1 && now >= part.introLength)
                    {
                        part.looping = true;
                        part.skeleton.Play(part.clips[1], part.hold);
                        part.skeleton.Tick(now - part.introLength);
                    }
                    else part.skeleton.Tick(now - before);
                }
                UpdateRewardPosition();
                UpdateHeroMiniature(tick);
            }

            private async Task WaitUntilAsync(float seconds, CancellationToken token, Action<float> captionOpacity = null)
            {
                while (elapsed < seconds)
                {
                    token.ThrowIfCancellationRequested();
                    if (!output || !output.gameObject.activeInHierarchy)
                        throw new OperationCanceledException("The summon presentation was closed.", token);
                    Update();
                    captionOpacity?.Invoke(Mathf.InverseLerp(.45f, .80f, elapsed));
                    if (elapsed < seconds) await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
            }
        }
    }
}
