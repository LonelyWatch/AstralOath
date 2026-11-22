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

        [Test]
        public void DieneS3RestoresTheBattlefieldUnderWhiteBeforeBuffsAndFadesIntoIdle()
        {
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1076", "c_diene_skill_03", null, null, false, true, true });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            float reveal = cues.Single(c => (string)c.Node["etty"] == "BGSHOW" && (bool?)c.Node["show"] == true).At;
            float buff = cues.Single(c => (string)c.Node["etty"] == "HIT").At;
            var camera = (Camera)type.GetField("camera", Instance).GetValue(session);
            float WhiteAt(float at)
            {
                type.GetMethod("SeekStage", Instance).Invoke(session, new object[] { at });
                var overlay = camera.transform.Find("DARK wh");
                return overlay ? overlay.GetComponent<MeshFilter>().sharedMesh.colors[0].a : 0f;
            }
            Assert.That(WhiteAt(reveal - .017f), Is.GreaterThan(.98f), "Cover the old backdrop before changing the camera.");
            Assert.That(WhiteAt(reveal + .017f), Is.GreaterThan(.98f), "The battlefield must return while fully covered.");
            Assert.That(buff - reveal, Is.EqualTo(.3f).Within(.001f), "The original return-to-buff cue spacing is retained.");
            Assert.That(WhiteAt(buff), Is.GreaterThan(.93f), "In the reference, the buff text appears over white.");
            Assert.That(WhiteAt(buff + .25f), Is.InRange(.43f, .55f));
            Assert.That(WhiteAt(buff + .52f), Is.Zero);
        }

        [Test]
        public void BaalAnimationPositionsSeparateTheCutinFromTheExplosion()
        {
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1015", "c_baal_skill_03", null, "c1001", false, false, false });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            float At(int id) => cues.Single(cue => (int?)cue.Node["guid"] == id).At;
            float bodyStart = At(6);
            Assert.That(At(55) - bodyStart, Is.EqualTo(1.1666f).Within(.001f), "The native body marker starts the CUT.");
            Assert.That(At(8) - bodyStart, Is.EqualTo(1.188f).Within(.001f), "Horizontal movement starts after the CUT returns.");
            Assert.That(At(12) - bodyStart, Is.EqualTo(1.980f).Within(.001f), "The target explosion follows the horizontal flight.");
        }

        [TestCase(false, 960)]
        [TestCase(true, 960)]
        [TestCase(false, 1280)]
        [TestCase(true, 1280)]
        public void IseriaGateFillsTheCameraBeforeTheCutin(bool casterOnRight, int width)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("剑门验收需要图形设备。");
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1024", "c_iseria_skill_03", null, "c1001", casterOnRight, false, false });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            var gate = cues.Single(cue => (int?)cue.Node["guid"] == 33);
            var cutin = cues.Single(cue => (int?)cue.Node["guid"] == 25);
            var camera = (Camera)type.GetField("camera", Instance).GetValue(session);
            var scene = (Scene)type.GetField("scene", Instance).GetValue(session);
            void Seek(float at) => type.GetMethod("SeekStage", Instance).Invoke(session, new object[] { at });
            bool HasGate() => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>())
                .Any(renderer => renderer.name == "iseria_skill_03_gateopen");
            var original = camera.targetTexture;
            var target = RenderTexture.GetTemporary(width, 540, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                camera.targetTexture = target;
                camera.aspect = width / 540f;
                Seek(gate.At - .05f);
                Assert.That(HasGate(), Is.False, "The gate should be absent before the sky shot.");
                Seek(gate.At + .22f);
                Assert.That(HasGate(), Is.True);
                type.GetMethod("Render", Instance).Invoke(session, null);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                var pixels = readback.GetPixels32();
                for (int half = 0; half < 2; half++)
                {
                    int lit = 0;
                    for (int y = 100; y < 415; y++)
                        for (int x = width / 2 - 350 + 350 * half; x < width / 2 + 350 * half; x++)
                        {
                            var pixel = pixels[y * width + x];
                            if (pixel.b > 100 && pixel.b > pixel.r + 15) lit++;
                        }
                    TestContext.WriteLine($"Gate half {half}, {width} × 540: {lit} lit pixels.");
                    Assert.That(lit, Is.GreaterThan(5000), "Blue sword trails must cross both halves of the shot.");
                }
                Seek(gate.At + .63f);
                type.GetMethod("Render", Instance).Invoke(session, null);
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                var peak = readback.GetPixels32();
                int white = 0;
                for (int y = 148; y < 520; y++)
                    for (int x = width / 2 - 304; x < width / 2 + 304; x++)
                    {
                        var pixel = peak[y * width + x];
                        if (pixel.r > 209 && pixel.g > 209 && pixel.b > 209) white++;
                    }
                float whiteCoverage = white / (608f * 372f);
                TestContext.WriteLine($"Gate bright cores: {whiteCoverage:P2} white.");
                Assert.That(whiteCoverage, Is.InRange(.10f, .20f), "Overlapping sword light has a white core.");
                Seek(cutin.At + .1f);
                Assert.That(HasGate(), Is.False, "After the CUT resumes the stage, subsequent hits use their own effects.");
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = original;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
            }
        }

        [Test]
        public void IseriaOpeningDarkensTheBattlefieldOverTheSourceTwoHundredMilliseconds()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备。");
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1024", "c_iseria_skill_03", null, "c1001", false, false, false });
            var target = (RenderTexture)type.GetProperty("Texture", Instance).GetValue(session);
            var previous = RenderTexture.active;
            var readback = new Texture2D(48, 32, TextureFormat.RGB24, false);
            try
            {
                float Brightness(float seconds)
                {
                    type.GetMethod("Seek", Instance).Invoke(session, new object[] { seconds });
                    type.GetMethod("Render", Instance).Invoke(session, null);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(target.width * .8f, target.height * .65f, 48, 32), 0, 0);
                    return (float)readback.GetPixels32().Average(p => (p.r + p.g + p.b) / (3f * 255f));
                }
                float initial = Brightness(0f), middle = Brightness(.1f), final = Brightness(.21f);
                TestContext.WriteLine($"Map opening luminance at 0, 100, 210 ms: {initial:F3}, {middle:F3}, {final:F3}.");
                Assert.That(initial, Is.GreaterThan(.12f));
                Assert.That(middle, Is.GreaterThan(final + .04f));
                Assert.That(middle, Is.LessThan(initial - .03f));
                Assert.That(final, Is.LessThan(.05f));
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IseriaFinalSwordTravelsWithTheAuthoredEffectObject(bool casterOnRight)
        {
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1024", "c_iseria_skill_03", null, "c1001", casterOnRight, false, false });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            var cutin = cues.Single(cue => (int?)cue.Node["guid"] == 25);
            var world = (Scene)type.GetField("scene", Instance).GetValue(session);
            var seek = type.GetMethod("SeekStage", Instance);
            seek.Invoke(session, new object[] { cutin.At + .015f });
            var sword = world.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<MeshRenderer>())
                .Single(r => r.name == "iseria_skill_03_final_front").transform;
            float initialY = sword.position.y, initialScale = Mathf.Abs(sword.lossyScale.x);
            seek.Invoke(session, new object[] { cutin.At + .68f });
            float settledScale = Mathf.Abs(sword.lossyScale.x);
            Assert.That(initialScale / settledScale, Is.EqualTo(2.4f).Within(.05f),
                "The 650 ms scale track has to update the entire sword, even after a particle starts.");
            Assert.That(sword.position.y - initialY, Is.GreaterThan(7f),
                "The authored 900-unit lift must bring the blade back into the hit shot.");
            seek.Invoke(session, new object[] { cutin.At + 2f });
            Assert.That(Mathf.Abs(sword.lossyScale.x), Is.EqualTo(settledScale).Within(.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IseriaVinesFillBothSidesOfTheImpactShotAndReleaseTheCamera(bool casterOnRight)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备。");
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1024", "c_iseria_skill_03", null, "c1001", casterOnRight, false, false });
            var camera = (Camera)type.GetField("camera", Instance).GetValue(session);
            float originalSize = camera.orthographicSize;
            var seek = type.GetMethod("Seek", Instance);
            seek.Invoke(session, new object[] { 8.8f });
            var caster = (Component)type.GetField("caster", Instance).GetValue(session);
            var recipient = (Component)type.GetField("primaryTarget", Instance).GetValue(session);
            var casterBody = caster.GetComponentsInChildren<MeshRenderer>(true).First();
            Assert.That(casterBody.enabled, Is.False, "Only the recipient is visible in the reverse impact shot.");
            Assert.That(recipient.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.enabled), Is.True);
            type.GetMethod("Render", Instance).Invoke(session, null);
            var target = camera.targetTexture;
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                var pixels = image.GetPixels32();
                int brightCores = 0;
                for (int y = target.height * 25 / 135; y < target.height * 132 / 135; y++)
                    for (int x = target.width * 55 / 240; x < target.width * 182 / 240; x++)
                    {
                        var pixel = pixels[y * target.width + x];
                        if (pixel.r > 209 && pixel.g > 209 && pixel.b > 209) brightCores++;
                    }
                float brightArea = brightCores / (target.width * (127f / 240f) * target.height * (107f / 135f));
                TestContext.WriteLine($"Impact white cores: {brightArea:P2}.");
                Assert.That(brightArea, Is.InRange(.08f, .35f), "The final crest and vines have white centres.");
                for (int side = 0; side < 2; side++)
                {
                    int start = target.width * (side == 0 ? 55 : 365) / 480;
                    int end = start + target.width / 8;
                    int cyan = 0;
                    for (int y = target.height / 6; y < target.height * 4 / 5; y++)
                        for (int x = start; x < end; x++)
                        {
                            var p = pixels[y * target.width + x];
                            if (p.b > 170 && p.g > 155 && p.b > p.r * 1.25f && p.g > p.r * 1.24f) cyan++;
                        }
                    float coverage = cyan / (float)((end - start) * target.height * 19 / 30);
                    TestContext.WriteLine($"Sword vine side {side}: {coverage:P2} cyan coverage.");
                    Assert.That(coverage, Is.GreaterThan(.065f), "The native vines reach the outer impact lanes.");
                }
                seek.Invoke(session, new object[] { 11f });
                Assert.That(camera.orthographicSize, Is.EqualTo(originalSize).Within(.01f));
                Assert.That(casterBody.enabled, Is.True, "Returning to the battlefield restores the caster.");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

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

        [TestCase(false)]
        [TestCase(true)]
        public void VioletS3ModelLayersStayWithBothAnimatedBodiesAtOnlineScale(bool casterOnRight)
        {
            const string graphId = "c_violetto_skill_03";
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(HeroDirectory +
                "c1074/Configs/stagept/" + graphId + ".graph.json");
            var type = Type.GetType(PreviewTypeName, true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1074", graphId, graph.text, "c1001", casterOnRight, false, false });
            var actor = (Component)type.GetField("caster", Instance).GetValue(session);
            actor.GetType().GetMethod("SetBattleDisplayScale", Instance).Invoke(actor, new object[] { 1.17f });
            var body = (Renderer)actor.GetType().GetProperty("PresentationRenderer", Instance).GetValue(actor);
            var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)actor.GetType().GetField("player", Instance).GetValue(actor);
            var playback = type.GetField("playback", Instance).GetValue(session);
            var effects = (Transform)playback.GetType().GetField("root", Instance).GetValue(playback);
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);

            foreach (int phase in new[] { 1, 2 })
            {
                string animation = "skill3_" + phase;
                var cue = cues.Single(item => (string)item.Node["ani"] == animation);
                type.GetMethod("SeekStage", Instance).Invoke(session, new object[] { cue.At + .48f });
                Assert.That(player.CurrentAnimation, Is.EqualTo(animation));
                Assert.That(player.Time, Is.EqualTo(.48f).Within(.025f));
                var sword = effects.GetComponentsInChildren<MeshRenderer>()
                    .Single(layer => layer.name == "violetto_eff_skill_3_" + phase);
                Assert.That(sword.transform.lossyScale.x, Is.EqualTo(body.transform.lossyScale.x).Within(.001f),
                    "The rapier arc and caster have independent native motion tracks in one coordinate system.");
                Assert.That(sword.transform.lossyScale.y, Is.EqualTo(body.transform.lossyScale.y).Within(.001f));
                if (phase == 2)
                {
                    Vector3 origin = sword.transform.position;
                    type.GetMethod("SeekStage", Instance).Invoke(session, new object[] { cue.At + 1.6f });
                    Assert.That(Vector3.Distance(actor.transform.position, origin), Is.GreaterThan(20f),
                        "The source deliberately parks the caster offscreen for the finishing slash.");
                    Assert.That(Vector3.Distance(sword.transform.position, origin), Is.LessThan(.01f),
                        "Location 5 keeps the rapier sweep at its emission point through the final hits.");
                }
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

        [Test]
        public void LotsUsesRigLaunchAnchorWhenTheSourceOmitsSelfBone()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(HeroDirectory +
                "c1031/Configs/stagept/c_lots_skill_01.graph.json");
            Assert.That(asset, Is.Not.Null);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1031", "c_lots_skill_01", asset.text, "c1001", false, false, false });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            var shot = cues.Single(cue => (string)cue.Node["etty"] == "SHOT");
            Assert.That(shot.Node["selfBone"], Is.Null);
            Assert.That(shot.Duration, Is.EqualTo(.2f).Within(.001f));
            var seek = type.GetMethod("Seek", Instance);

            Transform Ball()
            {
                var playback = type.GetField("playback", Instance).GetValue(session);
                var root = (Transform)playback.GetType().GetField("root", Instance).GetValue(playback);
                return root.Find("Skill CFX genie_light_skill_01_ball.cfx");
            }

            seek.Invoke(session, new object[] { shot.At + .04f });
            Assert.That(Ball(), Is.Not.Null);
            float first = Ball().position.x;
            seek.Invoke(session, new object[] { shot.At + .15f });
            Assert.That(Ball(), Is.Not.Null);
            Assert.That(Mathf.Abs(Ball().position.x - first), Is.GreaterThan(.25f));
            seek.Invoke(session, new object[] { shot.At + .23f });
            Assert.That(Ball(), Is.Null);
        }

        [Test]
        public void RaviTerminalCutinHoldsOnceAndScopeEndsAtTheLandingMarker()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(HeroDirectory +
                "c1019/Configs/stagept/c_ravi_skill_03.graph.json");
            Assert.That(graph, Is.Not.Null);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session, new object[] { "c1019", "c_ravi_skill_03", graph.text, null, false, true, false });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            var cutins = (IReadOnlyList<(float At, float Duration)>)type.GetProperty("Cutins", Instance).GetValue(session);
            var first = cues.Single(cue => (int?)cue.Node["guid"] == 5);
            Assert.That(cutins, Has.Count.EqualTo(1));
            Assert.That(cutins[0].At, Is.EqualTo(first.At + 1.8666f).Within(.001f));
            Assert.That(cutins[0].Duration, Is.EqualTo(1.8f).Within(.001f));
            var last = cues.Single(cue => (int?)cue.Node["guid"] == 16);
            var home = cues.Single(cue => (int?)cue.Node["guid"] == 19);
            Assert.That(home.At, Is.EqualTo(last.At + .9333f).Within(.001f));
            var hits = (IReadOnlyList<float>)type.GetProperty("HitTimes", Instance).GetValue(session);
            Assert.That(hits.Count, Is.EqualTo(8));
            Assert.That((float)type.GetProperty("StageDuration", Instance).GetValue(session), Is.GreaterThan(hits.Last()));
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
