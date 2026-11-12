#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class BattleCutinTimelineTests
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type PresentationType = typeof(BattleHudPreview).Assembly
            .GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
        private static readonly MethodInfo Begin = PresentationType.GetMethod("Begin");
        private static readonly MethodInfo Tick = PresentationType.GetMethod("Tick");
        private static readonly PropertyInfo StageClock = PresentationType.GetProperty("StageTime");
        private static readonly MethodInfo Control = typeof(BattlePreviewActor).GetMethod("BeginPresentationControl", PrivateInstance);
        private static readonly MethodInfo ReleaseControl = typeof(BattlePreviewActor).GetMethod("EndPresentationControl", PrivateInstance);
        private static readonly MethodInfo AdvanceActor = typeof(BattlePreviewActor).GetMethod("AdvancePresentation", PrivateInstance);
        private Scene scene;
        private IDisposable playback;
        private BattlePreviewActor[] controlledActors = Array.Empty<BattlePreviewActor>();
        private BattlePreviewActor caster;
        private Camera camera;
        private object stage;
        private Transform effects;
        private float wall;
        private string captureDirectory;
        private readonly List<string> samples = new List<string>();

        [UnityTest]
        public IEnumerator OriginalAngelicaCutinPausesSourceMotionAndDefersTargetEffectAndHit() => Run(false);

        [UnityTest]
        public IEnumerator SoloAngelicaCutinUsesTheSameClockWithOneRecipient() => Run(true);

        private IEnumerator Run(bool solo)
        {
            captureDirectory = null;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Angelica timing capture requires a real graphics device.");
            Assert.That(StageClock, Is.Not.Null, "The source clock must be observable independently of Cut-in wall time.");
            Assert.That(Control, Is.Not.Null, "The direct source test needs to disable actors' autonomous Update clocks.");

            SceneManager.sceneLoaded += DisableAutomaticHost;
            try
            {
                scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return null;
            }
            finally { SceneManager.sceneLoaded -= DisableAutomaticHost; }
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            host.AutoStartBattle = false;
            host.enabled = false;
            yield return null;
            Assert.That(host.SimulationSnapshot, Is.Null, "Source playback must be isolated from the match Update loop.");
            camera = host.GetComponent<Canvas>().worldCamera;
            var units = roots.Single(root => root.name == "Units").transform;
            var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>();
            caster = allies.Single(actor => actor.name == "Angelica [c1062]");
            controlledActors = units.GetComponentsInChildren<BattlePreviewActor>();
            foreach (var actor in controlledActors) Control.Invoke(actor, null);
            if (solo)
            {
                // The reference clip has only Angelica on the friendly side. Hide the other authored
                // allies without disabling their skeletons or changing the shared production graph.
                foreach (var ally in allies.Where(actor => actor != caster))
                    foreach (var renderer in ally.transform.parent.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = false;
            }
            var recipients = solo ? new[] { caster } : allies;
            var player = (ISkeletonPlayer)typeof(BattlePreviewActor).GetField("player", PrivateInstance).GetValue(caster);
            Assert.That(player, Is.Not.Null, "The real imported Angelica Spine must be active.");
            var map = HostField<BindingMap>(host, "hud");
            var hudObjects = new[] { map.transform.Find("Skills").gameObject,
                map.Require<GameButton>("soulBurn").gameObject, map.transform.Find("Unit Status").gameObject };
            var hudVisibility = hudObjects.Select(item => item.activeSelf).ToArray();
            Action<bool> visibility = hidden =>
            {
                for (int i = 0; i < hudObjects.Length; i++) hudObjects[i].SetActive(!hidden && hudVisibility[i]);
            };
            playback = (IDisposable)Activator.CreateInstance(PresentationType, new object[]
            {
                units, HostField<TextAsset[]>(host, "battleGraphs"), HostField<BattleSkillPresentationAsset[]>(host, "skillAssets"),
                camera, visibility, HostField<GameObject>(host, "stageBackground")
            });
            stage = PresentationType.GetField("cinematic", PrivateInstance).GetValue(playback);
            effects = units.Find("Battle Skill Effects");
            var timing = ((float Impact, float Duration))Begin.Invoke(playback,
                new object[] { "c_angelica_skill_03", caster, caster, recipients });
            var hits = (IReadOnlyList<float>)PresentationType.GetProperty("HitTimes").GetValue(playback);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0], Is.EqualTo(2.39f).Within(.0001f), "Use the real .73 + 1.66 source HIT connection.");
            Assert.That(timing.Impact, Is.EqualTo(hits[0]).Within(.0001f));

            string output = Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(output))
            {
                captureDirectory = Path.Combine(output, solo ? "angelica-solo-clock" : "angelica-clock");
                Directory.CreateDirectory(captureDirectory);
            }
            samples.Clear();
            samples.Add("wallSeconds,sourceSeconds,cutinRemainingSeconds,actorSeconds,targetEffectCount,cameraX,cameraY,cameraSize");
            wall = 0f;
            Vector3 frozenCamera = default;
            float frozenSize = 0f;
            float frozenActorTime = 0f;
            Vector3[] frozenVertices = null;
            var actorMesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            double deadline = Time.realtimeSinceStartupAsDouble + 120d;

            for (int frame = 0; frame <= 52; frame++)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "The bounded source capture exceeded 120 seconds.");
                float nextWall = frame / 10f;
                if (frame == 21)
                {
                    AdvanceTo(2.009f);
                    Assert.That(SourceTime, Is.EqualTo(.729f).Within(.0001f));
                    Assert.That(TargetEffectCount(), Is.Zero, "Target CFX must not precede source .73 seconds.");
                    AdvanceTo(2.0102f);
                    Assert.That(SourceTime, Is.EqualTo(.7302f).Within(.0001f));
                    Assert.That(TargetEffectCount(), Is.GreaterThan(0), "Target CFX must appear immediately after source .73 seconds.");
                }
                if (frame == 37)
                {
                    AdvanceTo(3.669f);
                    Assert.That(SourceTime, Is.LessThan(hits[0]), "The source HIT must wait for the full Cut-in wall time.");
                    AdvanceTo(3.6702f);
                    Assert.That(SourceTime, Is.GreaterThanOrEqualTo(hits[0]));
                    Assert.That(SourceTime, Is.EqualTo(2.3902f).Within(.0002f),
                        "The .6/1.28 Cut-in must shift the real 2.39 source HIT to wall 3.67 seconds.");
                }
                AdvanceTo(nextWall);
                float expectedSource = wall <= .6f ? wall : wall <= 1.88f ? .6f : wall - 1.28f;
                Assert.That(SourceTime, Is.EqualTo(expectedSource).Within(.0002f), "wall=" + wall.ToString("F1", CultureInfo.InvariantCulture));
                if (frame == 6)
                {
                    Assert.That(CutinRemaining, Is.EqualTo(1.28f).Within(.0002f));
                    Assert.That(player.CurrentAnimation, Is.EqualTo("skill3"));
                    frozenActorTime = player.Time;
                    Assert.That(frozenActorTime, Is.EqualTo(.6f).Within(.0002f));
                    frozenCamera = camera.transform.position;
                    frozenSize = camera.orthographicSize;
                    frozenVertices = actorMesh.vertices;
                }
                if (frame >= 7 && frame <= 18)
                {
                    Assert.That(CutinRemaining, Is.GreaterThan(0f));
                    Assert.That(player.Time, Is.EqualTo(frozenActorTime), "The actor animation must remain frozen behind the Cut-in.");
                    Assert.That(actorMesh.vertices, Is.EqualTo(frozenVertices), "The visible Spine pose must not advance behind the Cut-in.");
                    Assert.That(camera.transform.position, Is.EqualTo(frozenCamera));
                    Assert.That(camera.orthographicSize, Is.EqualTo(frozenSize));
                    Assert.That(TargetEffectCount(), Is.Zero, "The target CFX must not develop unseen under the overlay.");
                }
                if (frame == 11)
                    Assert.That(CutinRemaining, Is.EqualTo(.78f).Within(.0002f), "Half a second of wall time must only advance the Cut-in.");
                if (frame == 19)
                {
                    Assert.That(CutinRemaining, Is.EqualTo(0f));
                    Assert.That(player.Time, Is.EqualTo(.62f).Within(.0002f));
                }
                if (frame == 20) Assert.That(TargetEffectCount(), Is.Zero);
                RecordFrame(frame, player.Time);
                yield return null;
            }
            if (captureDirectory != null) File.WriteAllLines(Path.Combine(captureDirectory, "timeline.csv"), samples);
            Assert.That(wall, Is.EqualTo(5.2f).Within(.0001f));
        }

        private void AdvanceTo(float targetWall)
        {
            float before = SourceTime;
            float delta = targetWall - wall;
            Assert.That(delta, Is.GreaterThanOrEqualTo(0f));
            Tick.Invoke(playback, new object[] { delta });
            float stageDelta = SourceTime - before;
            foreach (var actor in controlledActors)
                if (actor && actor != caster) AdvanceActor.Invoke(actor, new object[] { stageDelta });
            wall = targetWall;
        }

        private float SourceTime => (float)StageClock.GetValue(playback);
        private float CutinRemaining => (float)stage.GetType().GetProperty("CutinRemainingSeconds").GetValue(stage);
        private int TargetEffectCount() => effects.Cast<Transform>().Count(child => child.gameObject.activeInHierarchy &&
            child.name == "Skill CFX angelica_skill_03_target.cfx");

        private void RecordFrame(int frame, float actorTime)
        {
            Vector3 position = camera.transform.position;
            samples.Add(string.Join(",", new[] { wall, SourceTime, CutinRemaining, actorTime, TargetEffectCount(),
                position.x, position.y, camera.orthographicSize }.Select(value => value.ToString("F5", CultureInfo.InvariantCulture))));
            if (captureDirectory == null) return;
            File.WriteAllBytes(Path.Combine(captureDirectory, $"frame-{frame:D3}-wall-{wall:F1}.png"), RenderFrame());
            File.WriteAllLines(Path.Combine(captureDirectory, "timeline.csv"), samples);
        }

        private byte[] RenderFrame()
        {
            const int Width = 1280;
            const int Height = 720;
            var target = RenderTexture.GetTemporary(Width, Height, 24);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture;
            var oldRect = camera.rect;
            float oldAspect = camera.aspect;
            var oldActive = RenderTexture.active;
            var blur = camera.transform.Find("Battle post-effect camera");
            var postCamera = blur ? blur.GetComponent<Camera>() : null;
            // ReleaseDisplay disables the capture immediately, but Unity destroys its GameObject
            // at the end of the frame. A retiring capture is no longer a render pass.
            if (postCamera && !postCamera.isActiveAndEnabled) postCamera = null;
            object post = postCamera ? stage.GetType().GetField("postEffects", PrivateInstance).GetValue(stage) : null;
            MethodInfo refresh = post?.GetType().GetMethod("Tick");
            try
            {
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = (float)Width / Height;
                Canvas.ForceUpdateCanvases();
                if (postCamera)
                {
                    refresh.Invoke(post, new object[] { 0f });
                    Assert.That(postCamera.targetTexture && postCamera.targetTexture.IsCreated(), Is.True);
                    postCamera.Render();
                }
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply(false, false);
                return image.EncodeToPNG();
            }
            finally
            {
                camera.targetTexture = oldTarget;
                camera.rect = oldRect;
                camera.aspect = oldAspect;
                try { if (postCamera) refresh.Invoke(post, new object[] { 0f }); }
                finally
                {
                    RenderTexture.active = oldActive;
                    RenderTexture.ReleaseTemporary(target);
                    UnityEngine.Object.Destroy(image);
                }
            }
        }

        private static void DisableAutomaticHost(Scene loaded, LoadSceneMode mode)
        {
            if (loaded.path != ScenePath) return;
            foreach (var root in loaded.GetRootGameObjects())
            {
                var host = root.GetComponent<BattleHudPreview>();
                if (!host) continue;
                host.AutoStartBattle = false;
                host.enabled = false;
            }
        }

        private static T HostField<T>(BattleHudPreview host, string name) =>
            (T)typeof(BattleHudPreview).GetField(name, PrivateInstance).GetValue(host);

        [UnityTearDown]
        public IEnumerator Unload()
        {
            playback?.Dispose();
            playback = null;
            foreach (var actor in controlledActors) if (actor) ReleaseControl.Invoke(actor, null);
            controlledActors = Array.Empty<BattlePreviewActor>();
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
