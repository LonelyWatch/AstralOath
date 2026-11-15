using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattleSourceMotionTests
    {
        private const string Module = "AstralOath.Client.BattleScene";
        private const string Characters = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
        private GameObject root;

        [TearDown]
        public void Cleanup()
        {
            if (root) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void RasSecondSkillStartsApproachAtTheSourceJumpEvent()
        {
            using var playback = Begin(2, out var caster, out var shadow);
            Tick(playback, .79f);
            Assert.That(caster.localPosition, Is.EqualTo(Vector3.zero));

            Tick(playback, .11f);
            Assert.That(caster.localPosition.x, Is.GreaterThan(.05f),
                "The 350 ms MOVE starts when the source skill2 jump event fires at .8 s.");

            Tick(playback, .30f);
            Assert.That(caster.position.x, Is.InRange(1.5f, 3f),
                "The caster has reached the near side of the target by the landing event.");
            Assert.That(shadow.position.x, Is.EqualTo(caster.position.x).Within(.001f));
            var hits = (IReadOnlyList<float>)playback.GetType().GetProperty("HitTimes").GetValue(playback);
            Assert.That(hits, Is.EqualTo(new[] { 1.466f, 1.766f, 2.733f }).Within(.001f));
        }

        [TestCase(3f, false, 0f, 1.18f)]
        [TestCase(-3f, true, 0f, 1.18f)]
        [TestCase(3f, false, 80f, .9f)]
        [TestCase(-3f, true, 80f, .9f)]
        [TestCase(.6f, false, 0f, .6f)]
        public void ApproachLeavesSpaceOnEitherSideWithoutRetreatingFromCloseTargets(
            float targetX, bool facesLeft, float authoredX, float expectedGap)
        {
            using var playback = Begin(2, out var caster, out var shadow, graph =>
            {
                foreach (JObject node in (JArray)graph["source"]["entities"])
                    if ((string)node["etty"] == "MOVE" && (int?)node["location"] == 16)
                        node["x"] = authoredX;
            }, targetX, facesLeft);
            Tick(playback, 1.2f);
            Assert.That(Mathf.Abs(targetX - caster.position.x), Is.EqualTo(expectedGap).Within(.001f));
            Assert.That(Mathf.Sign(targetX) * (targetX - caster.position.x), Is.GreaterThan(0f));
            Assert.That(shadow.position.x, Is.EqualTo(caster.position.x).Within(.001f));
        }

        [Test]
        public void RasThirdSkillExecutesVerticalMoveAndSourceReturnWithoutExplicitX()
        {
            using var playback = Begin(3, out var caster, out var shadow);
            Tick(playback, 1.39f);
            Assert.That(caster.localPosition, Is.EqualTo(Vector3.zero));

            Tick(playback, .26f);
            Assert.That(caster.localPosition.y, Is.GreaterThan(.1f),
                "The style-1 MOVE after jump_end contains h and time, so it raises the caster without x.");
            Assert.That(shadow.localPosition, Is.EqualTo(Vector3.zero), "The shadow stays on the ground during the jump.");

            Tick(playback, .40f);
            Assert.That(caster.localPosition, Is.EqualTo(Vector3.zero),
                "The following style-3 MOVE returns the visual to its slot.");
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void KayronFirstSkillCrossesTheActualTargetAtTheAuthoredSlashMarkers(float facing)
        {
            using var playback = Begin(1, out var caster, out var shadow, targetX: 3f * facing,
                facesLeft: facing < 0f, hero: "c1023", model: "kylon");
            Tick(playback, .433f);
            Assert.That(caster.position.x, Is.Zero.Within(.001f));
            Tick(playback, .002f);
            Assert.That(caster.position.x * facing, Is.EqualTo(.5f).Within(.03f),
                "At 434 ms style 3 must place the caster 250 source pixels before the primary target.");
            Tick(playback, .10f);
            Assert.That(caster.position.x * facing, Is.EqualTo(5.5f).Within(.001f));
            Tick(playback, .47f);
            Assert.That(caster.position.x * facing, Is.EqualTo(.5f).Within(.001f));
            Assert.That(shadow.position.x, Is.EqualTo(caster.position.x).Within(.001f));
            Tick(playback, .37f);
            Assert.That(caster.position.x * facing, Is.EqualTo(5.5f).Within(.001f));
            Tick(playback, .86f);
            Assert.That(caster.position.x, Is.Zero.Within(.001f));
        }

        [Test]
        public void KayronThirdSkillKeepsCastingCameraInFrameWhileSourceParksTheActor()
        {
            var cameraObject = new GameObject("Parking camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = Vector3.back * 10f;
            camera.orthographic = true;
            try
            {
                using var playback = Begin(1, out var caster, out _, hero: "c1023", model: "kylon", camera: camera);
                var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(Characters + "c1023/Configs/stagept/c_kylon_skill_03.graph.json");
                var nodes = JObject.Parse(graph.text)["source"]["entities"].OfType<JObject>().ToDictionary(node => (int)node["guid"]);
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                var stage = playback.GetType().GetField("cinematic", Private).GetValue(playback);
                var stageType = stage.GetType();
                var playMove = playback.GetType().GetMethod("PlayMove", Private);
                stageType.GetMethod("Play").Invoke(stage, new object[] { nodes[42] });
                void AdvanceCamera(float delta) => stageType.GetMethod("Tick").Invoke(stage, new object[] { delta });
                void Move(int id) => playMove.Invoke(playback, new object[] { nodes[id], null, null, 0f });

                AdvanceCamera(1.76f);
                float beforeParking = camera.transform.position.x;
                Move(43);
                AdvanceCamera(.02f);
                Assert.That(caster.position.x, Is.EqualTo(-80f).Within(.001f));
                Assert.That(camera.transform.position.x, Is.EqualTo(beforeParking).Within(.1f),
                    "The still-running source camera should not move to the parked caster.");

                Move(44);
                caster.position += Vector3.right;
                AdvanceCamera(.50f);
                Assert.That(camera.transform.position.x, Is.EqualTo(3f).Within(.01f),
                    "A source return restores ordinary tracking of the visible caster.");
            }
            finally { UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        [Test]
        public void CharlesThirdSkillStartsOffscreenAndUsesTheSourceCurveTowardTheTargetGroup()
        {
            using var playback = Begin(3, out var caster, out var shadow, hero: "c1027", model: "charles", secondTargetX: 5f);
            // This motion-only fixture skips nondeterministic graphics dependencies. The
            // graph starts the slide after its initial 300 ms LAYOUT when no loaded CFX or
            // Cut-in interposes a separate visual duration.
            Tick(playback, .3001f);
            Assert.That(caster.position.x, Is.EqualTo(-11f).Within(.02f),
                "The authored style-3 displacement starts 1500 source pixels before the target group.");
            Tick(playback, .1334f);
            Assert.That(caster.position.x, Is.EqualTo(-4.296f).Within(.02f),
                "The 267 ms slide follows its own cubic Bézier, including the authored midpoint.");
            Assert.That(shadow.position.x, Is.EqualTo(caster.position.x).Within(.001f));
            Tick(playback, .14f);
            Assert.That(caster.position.x, Is.EqualTo(4f).Within(.001f));
        }

        [Test]
        public void UnsupportedSourceConnectionIsReportedOnceWithItsSkillAndEndpoints()
        {
            var messages = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Unsupported STG connection"))
                    messages.Add(message);
            }

            Application.logMessageReceived += Capture;
            try
            {
                using var playback = Begin(2, out _, out _, graph =>
                {
                    var edges = (JArray)graph["source"]["connections"];
                    for (int repeat = 0; repeat < 2; repeat++)
                        edges.Add(new JObject { ["from"] = 15, ["to"] = 11, ["when"] = 7 });
                });
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Does.Contain("c_ras_skill_02").And.Contain("when=7").And.Contain("15 -> 11"));
        }

        private IDisposable Begin(int skill, out Transform caster, out Transform shadow, Action<JObject> edit = null,
            float targetX = 3f, bool facesLeft = false, string hero = "c1001", string model = "ras",
            float? secondTargetX = null, Camera camera = null)
        {
            root = new GameObject("Source motion");
            root.SetActive(false);
            var actor = Type.GetType(Module + ".BattlePreviewActor, " + Module, true);
            var runtime = Type.GetType(Module + ".BattleSkillPresentation, " + Module, true);
            var asset = Type.GetType(Module + ".BattleSkillPresentationAsset, " + Module, true);
            string sourceId = "c_" + model + "_skill_0" + skill;
            string character = Characters + hero + "/";
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(character + "Configs/stagept/" + sourceId + ".graph.json");
            if (edit != null)
            {
                var document = JObject.Parse(graph.text);
                edit(document);
                graph = new TextAsset(document.ToString());
            }
            var skeleton = AssetDatabase.LoadAssetAtPath<TextAsset>(character + "Animations/model/" + model + ".spine.json");
            var source = CreateActor(Vector3.zero, out shadow);
            var target = CreateActor(Vector3.right * targetX, out _);
            using (var serialized = new SerializedObject(source))
            {
                serialized.FindProperty("skeleton").objectReferenceValue = skeleton;
                serialized.FindProperty("faceLeft").boolValue = facesLeft;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            caster = source.transform;
            var targets = Array.CreateInstance(actor, secondTargetX.HasValue ? 2 : 1);
            targets.SetValue(target, 0);
            if (secondTargetX.HasValue) targets.SetValue(CreateActor(Vector3.right * secondTargetX.Value, out _), 1);
            try
            {
                var presenter = (IDisposable)Activator.CreateInstance(runtime,
                    new object[] { root.transform, new[] { graph }, Array.CreateInstance(asset, 0), camera, null, null });
                runtime.GetMethod("Begin").Invoke(presenter, new object[] { sourceId, source, target, targets });
                return presenter;
            }
            finally
            {
                if (edit != null) UnityEngine.Object.DestroyImmediate(graph);
            }

            Component CreateActor(Vector3 position, out Transform groundShadow)
            {
                var slot = new GameObject("Slot").transform;
                slot.SetParent(root.transform, false);
                slot.localPosition = position;
                groundShadow = new GameObject("Shadow", typeof(SpriteRenderer)).transform;
                groundShadow.SetParent(slot, false);
                var body = new GameObject("Actor");
                body.transform.SetParent(slot, false);
                return body.AddComponent(actor);
            }
        }

        private static void Tick(IDisposable playback, float delta) =>
            playback.GetType().GetMethod("Tick").Invoke(playback, new object[] { delta });
    }
}
