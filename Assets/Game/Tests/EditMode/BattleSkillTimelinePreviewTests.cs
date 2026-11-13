using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleSkillTimelinePreviewTests
    {
        private const string PreviewTypeName =
            "AstralOath.Client.BattleScene.Editor.SkillTimelinePreviewSession, AstralOath.Client.BattleScene.Editor";
        private const string HeroDirectory = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase("c1001", "c_ras_skill_02", false)]
        [TestCase("c1005", "c_mercedes_skill_03", true)]
        public void IsolatedPreviewUsesAuthoredClocksAndReplaysTheSameFrame(string heroId, string skillId, bool hasCutin)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("预览像素验证需要图形设备。");

            var sourceScene = SceneManager.GetActiveScene();
            bool wasDirty = sourceScene.isDirty;
            int originalPreviewSceneCount = EditorSceneManager.previewSceneCount;
            string path = HeroDirectory + heroId + "/Configs/stagept/" + skillId + ".graph.json";
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(graph, Is.Not.Null, path);
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using (var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true))
            {
                T Read<T>(string name) => (T)type.GetProperty(name, Instance).GetValue(session);
                object Invoke(string method, params object[] args) => type.GetMethod(method, Instance).Invoke(session, args);
                Invoke("Load", heroId, skillId, graph.text, null, false, hasCutin, false);

                var hitTimes = Read<IReadOnlyList<float>>("HitTimes");
                float stageDuration = Read<float>("StageDuration");
                float wallDuration = Read<float>("Duration");
                var cutins = Read<IReadOnlyList<(float At, float Duration)>>("Cutins");
                Assert.That(hitTimes.Count, Is.GreaterThan(0));
                Assert.That(hitTimes[0], Is.InRange(0, stageDuration));
                Assert.That(wallDuration, Is.EqualTo(stageDuration + cutins.Sum(item => item.Duration)).Within(.0001f));

                float sample;
                if (hasCutin)
                {
                    Assert.That(cutins.Count, Is.GreaterThan(0));
                    var firstCutin = cutins[0];
                    sample = firstCutin.At + firstCutin.Duration * .4f;
                    Assert.That((float)Invoke("WallToStageTime", sample), Is.EqualTo(firstCutin.At).Within(.0001f));
                    float after = firstCutin.At + firstCutin.Duration + .15f;
                    Assert.That((float)Invoke("WallToStageTime", after), Is.EqualTo(firstCutin.At + .15f).Within(.0001f));
                    Invoke("Seek", sample);
                    Assert.That(Read<float>("StageTime"), Is.EqualTo(firstCutin.At).Within(.015f), "Cut-in freezes stage time.");
                }
                else
                {
                    Assert.That(cutins, Is.Empty);
                    sample = hitTimes[0] + .035f;
                    Invoke("Seek", sample);
                    Assert.That(Read<float>("StageTime"), Is.EqualTo(Read<float>("Time")).Within(.015f));
                }

                var texture = Read<RenderTexture>("Texture");
                Assert.That(texture && texture.IsCreated(), Is.True);
                Invoke("Render");
                Color32[] pixels = ReadPixels(texture);
                Assert.That(pixels.Select(pixel => (pixel.r, pixel.g, pixel.b)).Distinct().Take(8).Count(),
                    Is.EqualTo(8), "The preview must render scene geometry rather than a single clear color.");

                var camera = (Camera)type.GetField("camera", Instance).GetValue(session);
                var caster = (Component)type.GetField("caster", Instance).GetValue(session);
                var cameraPosition = camera.transform.position;
                var casterPosition = caster.transform.position;
                float cameraSize = camera.orthographicSize;
                float stageTime = Read<float>("StageTime");

                Invoke("Seek", wallDuration);
                Invoke("Seek", sample);
                Invoke("Render");
                Color32[] replay = ReadPixels(texture);
                Assert.That(Read<float>("StageTime"), Is.EqualTo(stageTime).Within(.00001f));
                Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(caster.transform.position, casterPosition), Is.LessThan(.0001f));
                Assert.That(camera.orthographicSize, Is.EqualTo(cameraSize).Within(.0001f));
                var differences = replay.Zip(pixels, (first, second) =>
                    (Math.Abs(first.r - second.r) + Math.Abs(first.g - second.g) + Math.Abs(first.b - second.b)) / 3f).ToArray();
                Assert.That(differences.Average(), Is.LessThan(3f), "Rewind must retain the frame's composition and colors.");
                Assert.That(differences.Count(difference => difference <= 4f) / (float)differences.Length,
                    Is.GreaterThan(.97f), "Most pixels should reproduce within four RGB levels after rewind.");
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(sourceScene));
                Assert.That(sourceScene.isDirty, Is.EqualTo(wasDirty));
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(originalPreviewSceneCount + 1));
            }
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(originalPreviewSceneCount));
            using (var probe = new SceneAllocationProbe())
                Assert.That(EditorSceneManager.IsPreviewScene(probe.Object.scene), Is.False);
        }

        [Test]
        public void MovedHitMarkerChangesThePreparedTrackAndTheRealPlaybackClock()
        {
            const string graphId = "c_ras_skill_02";
            const int hitGuid = 20;
            const float movedHitSeconds = 3.125f;
            string path = HeroDirectory + "c1001/Configs/stagept/" + graphId + ".graph.json";
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(graph, Is.Not.Null, path);
            var source = JObject.Parse(graph.text);
            var nodes = (JArray)source["source"]["entities"];
            var hit = nodes.OfType<JObject>().Single(node => (int)node["guid"] == hitGuid);
            Assert.That((string)hit["etty"], Is.EqualTo("HIT"));

            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            var load = type.GetMethod("Load", Instance);
            var cuesProperty = type.GetProperty("Cues", Instance);
            void Load(string json) => load.Invoke(session, new object[] { "c1001", graphId, json, null, false, false, false });
            float TrackTime() => ((IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                cuesProperty.GetValue(session)).Single(cue => (int)cue.Node["guid"] == hitGuid).At;

            Load(graph.text);
            float originalTime = TrackTime();
            Assert.That(originalTime, Is.Not.EqualTo(movedHitSeconds).Within(.0001f));
            hit["_astralAtMs"] = 3125;
            Load(source.ToString());
            Assert.That(TrackTime(), Is.EqualTo(movedHitSeconds).Within(.0001f));
            var hitTimes = (IReadOnlyList<float>)type.GetProperty("HitTimes", Instance).GetValue(session);
            Assert.That(hitTimes.Any(time => Mathf.Abs(time - movedHitSeconds) < .0001f), Is.True);
            Assert.That(JObject.Parse(graph.text)["source"]["entities"].OfType<JObject>()
                .Single(node => (int)node["guid"] == hitGuid)["_astralAtMs"], Is.Null);
        }

        [Test]
        public void OverlappingClipsAndMinimumWidthMarkersOccupySeparateLanes()
        {
            var type = Type.GetType(
                "AstralOath.Client.BattleScene.Editor.SkillTimelineWindow, AstralOath.Client.BattleScene.Editor",
                throwOnError: true);
            var assign = type.GetMethod("AssignLane", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(assign, Is.Not.Null);
            var ends = new List<float>();
            int Add(float left, float right) => (int)assign.Invoke(null, new object[] { ends, left, right });

            Assert.That(Add(10, 40), Is.Zero);
            Assert.That(Add(14, 22), Is.EqualTo(1));
            Assert.That(Add(20, 28), Is.EqualTo(2));
            Assert.That(Add(24, 32), Is.EqualTo(1));
            Assert.That(Add(42, 50), Is.Zero);
            Assert.That(ends, Has.Count.EqualTo(3));
        }

        [Test]
        public void TwoTargetSkillPreviewsBothEnemiesOnEitherSide()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(HeroDirectory +
                "c1015/Configs/stagept/c_baal_skill_01.graph.json");
            Assert.That(graph, Is.Not.Null);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            var method = type.GetMethod("LoadScenario", Instance);
            foreach (bool flip in new[] { false, true })
            {
                method.Invoke(session, new object[] { "c1015", "c_baal_skill_01", graph.text, "c1001", flip, false, false, false, true });
                var recipients = (Component[])type.GetField("recipients", Instance).GetValue(session);
                Assert.That(recipients, Has.Length.EqualTo(2));
                Assert.That(recipients.All(actor => actor.transform.parent.parent.name == (flip ? "Allies" : "Enemies")), Is.True);
                Assert.That(recipients.Select(actor => actor.transform.parent.GetSiblingIndex()), Is.EquivalentTo(new[] { 0, 1 }));
            }
        }

        [Test]
        public void AitherShotUsesTheOriginalLaunchWindowAndExpiresAtHitOnEitherSide()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(HeroDirectory +
                "c1018/Configs/stagept/c_iterre_skill_01.graph.json");
            Assert.That(asset, Is.Not.Null);
            var raw = JObject.Parse(asset.text);
            Assert.That((string)raw["source"]["entities"].OfType<JObject>().Single(node => (int)node["guid"] == 3)["ani"],
                Is.EqualTo("skill{idx}"));
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            var load = type.GetMethod("Load", Instance);
            var seek = type.GetMethod("Seek", Instance);
            foreach (bool flip in new[] { false, true })
            {
                load.Invoke(session, new object[] { "c1018", "c_iterre_skill_01", asset.text, "c1001", flip, false, false });
                var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                    type.GetProperty("Cues", Instance).GetValue(session);
                var shot = cues.Single(cue => (string)cue.Node["etty"] == "SHOT");
                Assert.That(shot.At, Is.EqualTo(1.4f).Within(.001f));
                Assert.That(shot.Duration, Is.EqualTo(.3f).Within(.001f));
                var hits = (IReadOnlyList<float>)type.GetProperty("HitTimes", Instance).GetValue(session);
                Assert.That(hits[0], Is.EqualTo(1.7f).Within(.001f));

                Transform Ball()
                {
                    var playback = type.GetField("playback", Instance).GetValue(session);
                    var root = (Transform)playback.GetType().GetField("root", Instance).GetValue(playback);
                    return root.Find("Skill CFX i_terre_skill_01_ball.cfx");
                }

                seek.Invoke(session, new object[] { 1.38f });
                Assert.That(Ball(), Is.Null);
                seek.Invoke(session, new object[] { 1.43f });
                Assert.That(Ball(), Is.Not.Null);
                var mesh = Ball().GetComponentInChildren<MeshFilter>()?.sharedMesh;
                Assert.That(mesh && mesh.vertexCount > 0, Is.True, "The moving layer includes the original Spine projectile.");
                float firstX = Ball().position.x;
                seek.Invoke(session, new object[] { 1.60f });
                Assert.That(Ball(), Is.Not.Null);
                Assert.That((Ball().position.x - firstX) * (flip ? -1f : 1f), Is.GreaterThan(.25f));
                seek.Invoke(session, new object[] { 1.73f });
                Assert.That(Ball(), Is.Null);
                seek.Invoke(session, new object[] { 1.43f });
                Assert.That(Ball().position.x, Is.EqualTo(firstX).Within(.001f));
            }
        }

        private sealed class SceneAllocationProbe : IDisposable
        {
            internal readonly GameObject Object = new GameObject("Preview allocation probe") { hideFlags = HideFlags.HideAndDontSave };

            public void Dispose() => UnityEngine.Object.DestroyImmediate(Object);
        }

        private static Color32[] ReadPixels(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }
    }
}
