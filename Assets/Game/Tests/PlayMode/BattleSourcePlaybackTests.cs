#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattleSourcePlaybackTests
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const double ActionTimeout = 30d;
        private static readonly Type PresentationType = typeof(BattleHudPreview).Assembly
            .GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
        private static readonly MethodInfo Begin = PresentationType.GetMethod("Begin");
        private static readonly MethodInfo Tick = PresentationType.GetMethod("Tick");
        private static readonly FieldInfo Facing = typeof(BattlePreviewActor).GetField("faceLeft", PrivateInstance);
        private Scene scene;
        private IDisposable playback;
        private Transform units;
        private Camera camera;
        private GameObject background;
        private TextAsset[] graphs;
        private BattleSkillPresentationAsset[] assets;
        private Action<bool> setCinematicVisible;
        private bool cinematicVisible;
        private string captureDirectory;

        [Flags]
        private enum Phase { None = 0, Approach = 1, Impact = 2, Wide = 4, Blur = 8, Cutin = 16 }

        [UnityTest]
        public IEnumerator AllNineSourceGraphsPlayInBothDirectionsAndRestoreTheScene()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("The source playback sweep requires a graphics device.");

            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;

            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            graphs = HostField<TextAsset[]>(host, "battleGraphs");
            assets = HostField<BattleSkillPresentationAsset[]>(host, "skillAssets");
            background = HostField<GameObject>(host, "stageBackground");
            camera = host.GetComponent<Canvas>().worldCamera;
            units = roots.Single(root => root.name == "Units").transform;
            var map = HostField<BindingMap>(host, "hud");
            var hudObjects = new[] { map.transform.Find("Skills").gameObject, map.Require<GameButton>("soulBurn").gameObject,
                map.transform.Find("Unit Status").gameObject };
            var hudVisibility = hudObjects.Select(item => item.activeSelf).ToArray();
            setCinematicVisible = visible =>
            {
                cinematicVisible = visible;
                for (int index = 0; index < hudObjects.Length; index++)
                    hudObjects[index].SetActive(!visible && hudVisibility[index]);
            };

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                captureDirectory = Path.Combine(output, "source-playback");
                Directory.CreateDirectory(captureDirectory);
            }

            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>();
            var target = enemies.Single(actor => actor.name == "Ice Slime");
            var secondEnemy = enemies.Single(actor => actor.name == "Fire Slime");
            var ras = allies.Single(actor => actor.name == "Ras [c1001]");
            var mercedes = allies.Single(actor => actor.name == "Mercedes [c1005]");
            var allEnemyTargets = new[] { target }.Concat(enemies.Where(actor => actor != target)).ToArray();
            var allAllyTargets = new[] { ras }.Concat(allies.Where(actor => actor != ras)).ToArray();
            var roster = new[]
            {
                (Name: "Ras [c1001]", Graph: "ras"),
                (Name: "Mercedes [c1005]", Graph: "mercedes"),
                (Name: "Angelica [c1062]", Graph: "angelica")
            };
            var slots = allies.Concat(enemies).Select(actor => actor.transform.parent).Distinct().ToArray();
            var positions = slots.Select(slot => slot.position).ToArray();
            var expectedIds = roster.SelectMany(hero => Enumerable.Range(1, 3)
                .Select(skill => $"c_{hero.Graph}_skill_{skill:D2}")).ToArray();
            Assert.That(graphs.Select(graph => JsonUtility.FromJson<GraphId>(graph.text).id), Is.EquivalentTo(expectedIds));
            Assert.That(assets, Is.Not.Empty);

            foreach (bool faceLeft in new[] { false, true })
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    var position = positions[index];
                    if (faceLeft) position.x = -position.x;
                    slots[index].position = position;
                }
                foreach (var actor in allies) SetFacing(actor, faceLeft);
                foreach (var actor in enemies) SetFacing(actor, !faceLeft);
                yield return null;

                foreach (var hero in roster)
                {
                    var actor = allies.Single(item => item.name == hero.Name);
                    for (int skill = 1; skill <= 3; skill++)
                    {
                        string id = $"c_{hero.Graph}_skill_{skill:D2}";
                        string sample = $"{hero.Graph}-s{skill}-{(faceLeft ? "left" : "right")}";
                        Phase focus = id switch
                        {
                            "c_ras_skill_02" => Phase.Approach | Phase.Impact | Phase.Blur,
                            "c_mercedes_skill_03" => Phase.Wide,
                            "c_angelica_skill_03" => Phase.Blur | Phase.Cutin,
                            _ => Phase.None
                        };
                        var recipients = id switch
                        {
                            "c_ras_skill_03" or "c_mercedes_skill_02" or "c_mercedes_skill_03" => allEnemyTargets,
                            "c_mercedes_skill_01" => new[] { target, secondEnemy },
                            "c_angelica_skill_02" => new[] { ras, mercedes },
                            "c_angelica_skill_03" => allAllyTargets,
                            _ => new[] { target }
                        };
                        var primary = recipients[0];
                        yield return Play(id, sample, actor, primary, recipients, focus, Phase.None);
                        if (focus != Phase.None)
                        {
                            Phase interruptAt = focus.HasFlag(Phase.Approach) ? Phase.Approach :
                                focus.HasFlag(Phase.Wide) ? Phase.Wide : Phase.Blur;
                            yield return Play(id, sample + "-interrupted", actor, primary, recipients, Phase.None, interruptAt);
                        }
                    }
                }
            }
        }

        private IEnumerator Play(string id, string sample, BattlePreviewActor actor, BattlePreviewActor target,
            BattlePreviewActor[] recipients, Phase capturePhases, Phase interruptAt)
        {
            playback ??= (IDisposable)Activator.CreateInstance(PresentationType,
                new object[] { units, graphs, assets, camera, setCinematicVisible, background });
            var effects = units.Find("Battle Skill Effects");
            var stage = PresentationType.GetField("cinematic", PrivateInstance).GetValue(playback);
            var requestedSize = stage.GetType().GetField("requestedSize", PrivateInstance);
            var shadow = actor.transform.parent.GetComponentInChildren<SpriteRenderer>().transform;
            var baseline = new SceneBaseline(camera, background, units, actor.transform, shadow);
            var visualMeshes = new HashSet<Mesh>();
            var visualTargets = new HashSet<RenderTexture>();
            var frameCaptures = new List<string>();
            var timing = ((float Impact, float Duration))Begin.Invoke(playback, new object[] { id, actor, target, recipients });
            Assert.That(float.IsFinite(timing.Duration) && timing.Duration > 0f && timing.Duration < ActionTimeout, Is.True, sample);
            Assert.That(timing.Impact, Is.InRange(0f, timing.Duration), sample);
            double deadline = Time.realtimeSinceStartupAsDouble + ActionTimeout;
            float elapsed = 0f;
            Phase observed = Phase.None;
            do
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), sample + " exceeded the real-time deadline");
                Assert.That(IsFinite(camera.transform.position) && float.IsFinite(camera.orthographicSize) &&
                    camera.orthographicSize > 0f, Is.True, sample + " produced an invalid camera pose");
                Assert.That(IsFinite(actor.transform.position) && IsFinite(shadow.position), Is.True, sample);

                var blur = camera.transform.Find("Battle post-effect camera");
                var postCamera = blur && blur.gameObject.activeInHierarchy ? blur.GetComponent<Camera>() : null;
                if (postCamera && postCamera.enabled && postCamera.targetTexture)
                {
                    visualTargets.Add(postCamera.targetTexture);
                    Observe(Phase.Blur);
                }
                if (camera.transform.Cast<Transform>().Any(child => child.gameObject.activeInHierarchy &&
                    child.name.StartsWith("CUTIN ", StringComparison.Ordinal)))
                    Observe(Phase.Cutin);
                if ((float)requestedSize.GetValue(stage) > baseline.CameraSize + 0.1f)
                    Observe(Phase.Wide);
                if (elapsed < timing.Impact && Mathf.Abs(actor.transform.localPosition.x - baseline.ActorHome.x) > 0.25f &&
                    Mathf.Abs(actor.transform.position.x - target.transform.position.x) < Mathf.Abs(baseline.ActorWorldX - target.transform.position.x))
                    Observe(Phase.Approach);
                if (elapsed >= timing.Impact)
                    Observe(Phase.Impact);

                if (frameCaptures.Count > 0)
                {
                    bool blurred = postCamera && postCamera.enabled && postCamera.targetTexture;
                    var frame = RenderFrame(stage, blurred ? postCamera : null, visualTargets);
                    Assert.That(frame.Composite.Length, Is.GreaterThan(8), sample + " did not produce a PNG");
                    foreach (string path in frameCaptures)
                    {
                        File.WriteAllBytes(path, frame.Composite);
                        if (frame.World != null)
                            File.WriteAllBytes(Path.ChangeExtension(path, null) + "-world.png", frame.World);
                    }
                    frameCaptures.Clear();
                }
                TrackMeshes(camera.transform, visualMeshes);
                TrackMeshes(effects, visualMeshes);
                if (interruptAt != Phase.None && (observed & interruptAt) != 0)
                {
                    playback.Dispose();
                    playback = null;
                    break;
                }

                yield return null;
                float delta = Time.unscaledDeltaTime;
                Tick.Invoke(playback, new object[] { delta });
                elapsed += delta;
            }
            while (elapsed < timing.Duration + 0.05f);

            Assert.That((observed & capturePhases) == capturePhases, Is.True,
                $"{sample} observed {observed}; expected {capturePhases}.");
            Assert.That(interruptAt == Phase.None || playback == null, Is.True, sample + " never reached the interruption phase");
            yield return null;
            yield return null;
            baseline.AssertRestored(sample);
            Assert.That(cinematicVisible, Is.False, sample + " left the gameplay HUD hidden");
            Assert.That(!effects || effects.childCount == 0, Is.True, sample + " left effect objects in the scene");
            Assert.That(visualMeshes.All(mesh => !mesh), Is.True, sample + " leaked a transient mesh");
            Assert.That(visualTargets.All(texture => !texture), Is.True, sample + " leaked a post-effect render target");

            void Observe(Phase phase)
            {
                if ((observed & phase) != 0)
                    return;
                observed |= phase;
                if ((capturePhases & phase) == 0 || string.IsNullOrEmpty(captureDirectory))
                    return;
                frameCaptures.Add(Path.Combine(captureDirectory, sample + "-" + phase.ToString().ToLowerInvariant() + ".png"));
            }
        }

        private (byte[] Composite, byte[] World) RenderFrame(object stage, Camera postCamera, HashSet<RenderTexture> visualTargets)
        {
            const int Width = 1920;
            const int Height = 1080;
            var target = RenderTexture.GetTemporary(Width, Height, 24);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousAspect = camera.aspect;
            var previousActive = RenderTexture.active;
            object postEffect = null;
            MethodInfo refreshPostEffect = null;
            if (postCamera)
            {
                postEffect = stage.GetType().GetField("postEffects", PrivateInstance).GetValue(stage);
                refreshPostEffect = postEffect.GetType().GetMethod("Tick");
            }

            try
            {
                camera.targetTexture = target;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)Width / Height;
                Canvas.ForceUpdateCanvases();
                byte[] world = null;
                if (postCamera)
                {
                    // Update the blur target and projection for this resolution before drawing either pass.
                    refreshPostEffect.Invoke(postEffect, new object[] { 0f });
                    var blurTarget = postCamera.targetTexture;
                    Assert.That(blurTarget && blurTarget.IsCreated() && blurTarget.width == Width &&
                        blurTarget.height == Height, Is.True, "The blur camera must draw the capture frame at the capture resolution.");
                    visualTargets.Add(blurTarget);
                    postCamera.Render();
                    world = ReadPng(blurTarget, image);
                }
                camera.Render();
                return (ReadPng(target, image), world);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                try
                {
                    if (postCamera)
                    {
                        refreshPostEffect.Invoke(postEffect, new object[] { 0f });
                        if (postCamera.targetTexture)
                            visualTargets.Add(postCamera.targetTexture);
                    }
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target);
                    UnityEngine.Object.Destroy(image);
                }
            }
        }

        private static byte[] ReadPng(RenderTexture source, Texture2D image)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                image.Apply(false, false);
                return image.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void TrackMeshes(Transform parent, HashSet<Mesh> output)
        {
            foreach (var filter in parent.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh && (mesh.name == "Skeleton dynamic mesh" || mesh.name == "Battle cinematic quad" ||
                    mesh.name == "Battle post-effect quad"))
                    output.Add(mesh);
            }
        }

        private static T HostField<T>(BattleHudPreview host, string name) =>
            (T)typeof(BattleHudPreview).GetField(name, PrivateInstance).GetValue(host);

        private static void SetFacing(BattlePreviewActor actor, bool faceLeft)
        {
            actor.enabled = false;
            Facing.SetValue(actor, faceLeft);
            actor.enabled = true;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        [Serializable]
        private sealed class GraphId { public string id = ""; }

        private sealed class SceneBaseline
        {
            private readonly Camera camera;
            private readonly GameObject background;
            private readonly Transform actor;
            private readonly Transform shadow;
            private readonly Vector3 cameraPosition;
            private readonly Color cameraClear;
            private readonly Rect cameraRect;
            private readonly int cameraMask;
            private readonly RenderTexture cameraTarget;
            private readonly int cameraChildren;
            private readonly bool backgroundActive;
            private readonly Vector3 shadowHome;
            private readonly (Renderer Renderer, bool Enabled)[] visibility;
            private readonly (Transform Transform, Vector3 Position, Vector3 Scale)[] backdrop;

            internal SceneBaseline(Camera camera, GameObject background, Transform units, Transform actor, Transform shadow)
            {
                this.camera = camera;
                this.background = background;
                this.actor = actor;
                this.shadow = shadow;
                cameraPosition = camera.transform.position;
                CameraSize = camera.orthographicSize;
                cameraClear = camera.backgroundColor;
                cameraRect = camera.rect;
                cameraMask = camera.cullingMask;
                cameraTarget = camera.targetTexture;
                cameraChildren = camera.transform.childCount;
                backgroundActive = background.activeSelf;
                ActorHome = actor.localPosition;
                ActorWorldX = actor.position.x;
                shadowHome = shadow.localPosition;
                visibility = units.GetComponentsInChildren<Renderer>(true).Select(renderer => (renderer, renderer.enabled)).ToArray();
                backdrop = background.GetComponentsInChildren<SpriteRenderer>(true).Select(item =>
                    (item.transform, item.transform.localPosition, item.transform.localScale)).ToArray();
            }

            internal float CameraSize { get; }
            internal Vector3 ActorHome { get; }
            internal float ActorWorldX { get; }

            internal void AssertRestored(string sample)
            {
                Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(0.001f), sample);
                Assert.That(camera.orthographicSize, Is.EqualTo(CameraSize).Within(0.001f), sample);
                Assert.That(camera.backgroundColor, Is.EqualTo(cameraClear), sample);
                Assert.That(camera.rect, Is.EqualTo(cameraRect), sample);
                Assert.That(camera.cullingMask, Is.EqualTo(cameraMask), sample);
                Assert.That(camera.targetTexture, Is.EqualTo(cameraTarget), sample);
                Assert.That(camera.transform.childCount, Is.EqualTo(cameraChildren), sample + " left camera children behind");
                Assert.That(background.activeSelf, Is.EqualTo(backgroundActive), sample);
                Assert.That(Vector3.Distance(actor.localPosition, ActorHome), Is.LessThan(0.001f), sample);
                Assert.That(Vector3.Distance(shadow.localPosition, shadowHome), Is.LessThan(0.001f), sample);
                foreach (var item in visibility)
                    Assert.That(item.Renderer.enabled, Is.EqualTo(item.Enabled), sample + " changed slot renderer visibility");
                foreach (var item in backdrop)
                {
                    Assert.That(Vector3.Distance(item.Transform.localPosition, item.Position), Is.LessThan(0.001f), sample);
                    Assert.That(Vector3.Distance(item.Transform.localScale, item.Scale), Is.LessThan(0.001f), sample);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            playback?.Dispose();
            playback = null;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
