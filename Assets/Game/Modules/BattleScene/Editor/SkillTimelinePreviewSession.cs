using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene.Editor
{
    internal sealed class SkillTimelinePreviewSession : IDisposable
    {
        private const int PreviewStepsPerSecond = 120;
        private const int Width = 960;
        private const int Height = 540;
        private const string PostEffectCamera = "Battle post-effect camera";

        // Unity 2022.3 exposes the preview scene lifetime API, but its scene-asset loader is internal.
        private const BindingFlags SceneMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo OpenPreviewScene = typeof(EditorSceneManager).GetMethod("OpenPreviewScene",
            SceneMethods, null, new[] { typeof(string) }, null);
        private static readonly MethodInfo SetTargetScene = typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects",
            SceneMethods, null, new[] { typeof(Scene) }, null);
        private static readonly MethodInfo GetTargetScene = typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects",
            SceneMethods, null, Type.EmptyTypes, null);
        private static readonly MethodInfo ClearTargetScene = typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects",
            SceneMethods, null, Type.EmptyTypes, null);

        // The runtime creates some effect objects before parenting them. Direct that allocation
        // into the preview scene so even transient objects cannot mark an open user scene dirty.
        private readonly struct PreviewAllocationScope : IDisposable
        {
            private readonly int previousHandle;

            public PreviewAllocationScope(Scene previewScene)
            {
                previousHandle = GetTargetScene.Invoke(null, null) switch
                {
                    Scene target => target.handle,
                    int handle => handle,
                    _ => throw new InvalidOperationException("无法读取当前 Unity 预览分配范围。")
                };
                SetTargetScene.Invoke(null, new object[] { previewScene });
            }

            public void Dispose()
            {
                if (previousHandle == 0)
                    ClearTargetScene.Invoke(null, null);
                else
                    SetTargetSceneByHandle.Invoke(null, new object[] { previousHandle });
            }
        }
        private static readonly MethodInfo SetTargetSceneByHandle = typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects",
            SceneMethods, null, new[] { typeof(int) }, null);

        private readonly List<BattlePreviewActor> initializedActors = new List<BattlePreviewActor>();
        private Scene scene;
        private Transform world;
        private Camera camera;
        private GameObject background;
        private BattleHudPreview host;
        private BattlePreviewActor caster;
        private BattlePreviewActor primaryTarget;
        private BattlePreviewActor[] recipients = Array.Empty<BattlePreviewActor>();
        private BattleSkillPresentationAsset[] assets;
        private BattleSkillPresentation playback;
        private BattleVoice voice;
        private TextAsset graphAsset;
        private string graphId;
        private bool useHitReactions;
        private bool disposed;
        private int step;
        private float requestedTime;
        private int nextHit;
        private bool dirty;

        internal RenderTexture Texture { get; private set; }
        internal float Duration { get; private set; }
        internal float StageDuration { get; private set; }
        internal float Time => requestedTime;
        internal float StageTime => playback?.StageTime ?? 0f;
        internal IReadOnlyList<float> HitTimes => playback?.HitTimes ?? Array.Empty<float>();
        internal IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)> Cues { get; private set; } =
            Array.Empty<(float, JObject, float, JObject[])>();
        internal IReadOnlyList<(float At, float Duration)> Cutins { get; private set; } =
            Array.Empty<(float, float)>();

        internal void Load(string heroId, string graphId, string graphJson, string targetHeroId = null,
            bool casterOnRight = false, bool targetAll = false, bool targetAllies = false)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SkillTimelinePreviewSession));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("技能演出预览请在编辑模式使用。");
            if (string.IsNullOrWhiteSpace(heroId)) throw new ArgumentException("请选择演出角色。", nameof(heroId));
            if (string.IsNullOrWhiteSpace(graphId)) throw new ArgumentException("请选择演出图。", nameof(graphId));

            StopAudio();
            ResetPlayback();
            ReleaseActors();
            try
            {
                EnsureScene();
                using var scope = new PreviewAllocationScope(scene);
                var serializedHost = new SerializedObject(host);
                var modelRows = serializedHost.FindProperty("heroModels");
                var castingSide = world.Find(casterOnRight ? "Enemies" : "Allies");
                var opposingSide = world.Find(casterOnRight ? "Allies" : "Enemies");
                caster = ActorAt(castingSide, 0);
                primaryTarget = ActorAt(targetAllies ? castingSide : opposingSide, targetAllies ? 1 : 0);
                recipients = targetAll
                    ? (targetAllies ? castingSide : opposingSide).GetComponentsInChildren<BattlePreviewActor>(true)
                    : new[] { primaryTarget };
                useHitReactions = !targetAllies;
                var active = new HashSet<BattlePreviewActor>(recipients) { caster };
                foreach (var actor in world.GetComponentsInChildren<BattlePreviewActor>(true))
                {
                    actor.transform.parent.gameObject.SetActive(active.Contains(actor));
                    if (active.Contains(actor)) BindActor(actor, modelRows, actor == caster ? heroId : targetHeroId ?? "c1001");
                }
                this.graphId = graphId;
                string json = graphJson;
                if (string.IsNullOrWhiteSpace(json))
                {
                    var original = ReadAssetArray<TextAsset>(serializedHost.FindProperty("battleGraphs"))
                        .FirstOrDefault(item => (string)JObject.Parse(item.text)["id"] == graphId);
                    if (!original) throw new InvalidOperationException("场景未引用演出图 " + graphId);
                    json = original.text;
                }
                if ((string)JObject.Parse(json)["id"] != graphId)
                    throw new ArgumentException("预览图 ID 与当前技能不一致。", nameof(graphJson));
                graphAsset = new TextAsset(json) { name = graphId, hideFlags = HideFlags.HideAndDontSave };
                Begin();
            }
            catch
            {
                ReleaseScene();
                throw;
            }
        }

        internal void Seek(float wallSeconds)
        {
            if (playback == null || !float.IsFinite(wallSeconds)) return;
            float clamped = Mathf.Clamp(wallSeconds, 0f, Duration);
            int target = Mathf.CeilToInt(clamped * PreviewStepsPerSecond - .00001f);
            if (target == step)
            {
                requestedTime = clamped;
                voice?.Tick();
                return;
            }
            using var scope = new PreviewAllocationScope(scene);
            if (target < step)
            {
                StopAudio();
                playback.Dispose();
                playback = null;
                foreach (var actor in initializedActors)
                {
                    actor.DisableEditorPreview();
                    actor.EnableEditorPreview();
                }
                Begin();
            }
            while (step < target)
            {
                float before = playback.StageTime;
                playback.Tick(1f / PreviewStepsPerSecond);
                float advanced = playback.StageTime - before;
                foreach (var actor in initializedActors)
                    if (actor != caster) actor.AdvanceEditorPreview(advanced);
                ApplyHitReactions();
                step++;
                dirty = true;
            }
            requestedTime = clamped;
            voice?.Tick();
        }

        internal float StageToWallTime(float stageSeconds)
        {
            float stage = Mathf.Clamp(stageSeconds, 0f, StageDuration);
            return stage + Cutins.Where(hold => hold.At < stage - .00001f).Sum(hold => hold.Duration);
        }

        internal void SeekStage(float stageSeconds) => Seek(StageToWallTime(stageSeconds));

        internal void Render()
        {
            if (!dirty || !camera || !Texture) return;
            // The preview scene's cameras never join the normal URP camera loop.
            var post = camera.transform.Find(PostEffectCamera);
            if (post && post.TryGetComponent<Camera>(out var capture) && capture.targetTexture)
            {
                capture.scene = scene;
                capture.Render();
            }
            camera.Render();
            dirty = false;
        }

        internal bool StartAudio()
        {
            if (step != 0 || playback == null) return false;
            StopAudio();
            var serializedHost = new SerializedObject(host);
            voice = new BattleVoice(Reference<TextAsset>(serializedHost.FindProperty("audioCatalog")),
                ReadAssetArray<TextAsset>(serializedHost.FindProperty("audioBanks")),
                Reference<TextAsset>(serializedHost.FindProperty("impactBank")));
            if (!voice.IsAvailable)
            {
                StopAudio();
                return false;
            }
            voice.Play(graphId);
            return true;
        }

        internal void SetAudioPaused(bool paused) => voice?.SetPaused(paused);

        internal void StopAudio()
        {
            var current = voice;
            voice = null;
            current?.Dispose();
        }

        private void EnsureScene()
        {
            if (scene.IsValid()) return;
            if (OpenPreviewScene == null || SetTargetScene == null || SetTargetSceneByHandle == null ||
                GetTargetScene == null || ClearTargetScene == null)
                throw new InvalidOperationException("当前 Unity 缺少独立预览场景所需接口。");
            try { scene = (Scene)OpenPreviewScene.Invoke(null, new object[] { BattleSceneBuilder.ScenePath }); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            {
                throw new InvalidOperationException("无法打开独立战斗预览场景。", error.InnerException);
            }
            var roots = scene.GetRootGameObjects();
            world = roots.Single(root => root.name == "Units").transform;
            host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var canvas = host.GetComponent<Canvas>();
            camera = canvas.worldCamera;
            canvas.enabled = false;
            host.enabled = false;
            var data = new SerializedObject(host);
            background = Reference<GameObject>(data.FindProperty("stageBackground"));
            var rows = data.FindProperty("skillAssets");
            assets = Enumerable.Range(0, rows.arraySize).Select(index => ReadAsset(rows.GetArrayElementAtIndex(index))).ToArray();

            foreach (var root in roots)
            foreach (var other in root.GetComponentsInChildren<Camera>(true))
                other.enabled = false;
            camera.scene = scene;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = Width / (float)Height;
            Texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "AstralOath skill timeline preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!Texture.Create()) throw new InvalidOperationException("无法创建技能演出预览纹理。");
            camera.targetTexture = Texture;
        }

        private void Begin()
        {
            playback = new BattleSkillPresentation(world, new[] { graphAsset }, assets, camera,
                background: background);
            Cues = playback.GetPreparedCues(caster, graphId);
            var cutins = new List<(float At, float Duration)>();
            foreach (var cue in Cues)
            {
                if ((string)cue.Node["etty"] != "CUTIN") continue;
                string key = "cut/fhd/" + (string)cue.Node["args"] + ".webp";
                var asset = Array.Find(assets, item => item.source == key);
                if (asset?.document)
                    cutins.Add((cue.At, BattleCinematicStage.CutinDuration(JObject.Parse(asset.document.text))));
            }
            Cutins = cutins.AsReadOnly();
            StageDuration = playback.Begin(graphId, caster, primaryTarget, recipients).Duration;
            Duration = StageDuration + Cutins.Sum(hold => hold.Duration);
            step = nextHit = 0;
            requestedTime = 0f;
            ApplyHitReactions();
            dirty = true;
        }

        private void ApplyHitReactions()
        {
            if (!useHitReactions) return;
            var hits = playback.HitTimes;
            while (nextHit < hits.Count && hits[nextHit] <= playback.StageTime + .000001f)
            {
                foreach (var actor in recipients)
                    if (!playback.OwnsNodeAnimationPose(actor)) actor.PlayBattleAnimation("attacked");
                nextHit++;
            }
        }

        private void BindActor(BattlePreviewActor actor, SerializedProperty modelRows, string heroId)
        {
            for (int index = 0; index < modelRows.arraySize; index++)
            {
                var model = modelRows.GetArrayElementAtIndex(index);
                if (model.FindPropertyRelative("id").stringValue != heroId) continue;
                var asset = ReadAsset(model.FindPropertyRelative("skeleton"));
                actor.UseModel(asset, model.FindPropertyRelative("skin").stringValue,
                    $"{model.FindPropertyRelative("label").stringValue} [{heroId}]");
                actor.EnableEditorPreview();
                initializedActors.Add(actor);
                return;
            }
            throw new InvalidOperationException("当前战斗场景未安装角色模型 " + heroId);
        }

        private static BattlePreviewActor ActorAt(Transform team, int index) =>
            team.GetChild(index).GetComponentInChildren<BattlePreviewActor>(true);

        private static T Reference<T>(SerializedProperty property) where T : Object =>
            property.objectReferenceValue as T;

        private static T[] ReadAssetArray<T>(SerializedProperty property) where T : Object =>
            Enumerable.Range(0, property.arraySize).Select(index => Reference<T>(property.GetArrayElementAtIndex(index))).ToArray();

        private static BattleSkillPresentationAsset ReadAsset(SerializedProperty property) =>
            new BattleSkillPresentationAsset
            {
                source = property.FindPropertyRelative("source").stringValue,
                document = Reference<TextAsset>(property.FindPropertyRelative("document")),
                atlas = Reference<TextAsset>(property.FindPropertyRelative("atlas")),
                pageNames = Enumerable.Range(0, property.FindPropertyRelative("pageNames").arraySize)
                    .Select(index => property.FindPropertyRelative("pageNames").GetArrayElementAtIndex(index).stringValue).ToArray(),
                pages = ReadAssetArray<Texture2D>(property.FindPropertyRelative("pages")),
                texture = Reference<Texture2D>(property.FindPropertyRelative("texture")),
                premultipliedAlpha = property.FindPropertyRelative("premultipliedAlpha").boolValue,
                scale = property.FindPropertyRelative("scale").floatValue
            };

        private void ResetPlayback()
        {
            playback?.Dispose();
            playback = null;
            if (graphAsset) Object.DestroyImmediate(graphAsset);
            graphAsset = null;
            Cues = Array.Empty<(float, JObject, float, JObject[])>();
            Cutins = Array.Empty<(float, float)>();
            StageDuration = Duration = requestedTime = 0f;
            step = 0;
        }

        private void ReleaseActors()
        {
            foreach (var actor in initializedActors)
                if (actor) actor.DisableEditorPreview();
            initializedActors.Clear();
        }

        private void ReleaseScene()
        {
            ResetPlayback();
            ReleaseActors();
            if (camera) camera.targetTexture = null;
            if (Texture)
            {
                Texture.Release();
                Object.DestroyImmediate(Texture);
            }
            Texture = null;
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
            camera = null;
            world = null;
            host = null;
            background = null;
            assets = null;
            caster = null;
            primaryTarget = null;
            recipients = Array.Empty<BattlePreviewActor>();
            dirty = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopAudio();
            ReleaseScene();
        }
    }
}
