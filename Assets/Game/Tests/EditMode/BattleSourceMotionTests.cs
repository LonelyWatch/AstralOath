using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattleSourceMotionTests
    {
        private const string Module = "AstralOath.Client.BattleScene";
        private const string Ras = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/";
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

        private IDisposable Begin(int skill, out Transform caster, out Transform shadow, Action<JObject> edit = null)
        {
            root = new GameObject("Source motion");
            root.SetActive(false);
            var actor = Type.GetType(Module + ".BattlePreviewActor, " + Module, true);
            var runtime = Type.GetType(Module + ".BattleSkillPresentation, " + Module, true);
            var asset = Type.GetType(Module + ".BattleSkillPresentationAsset, " + Module, true);
            var sourceId = "c_ras_skill_0" + skill;
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(Ras + "Configs/stagept/" + sourceId + ".graph.json");
            if (edit != null)
            {
                var document = JObject.Parse(graph.text);
                edit(document);
                graph = new TextAsset(document.ToString());
            }
            var skeleton = AssetDatabase.LoadAssetAtPath<TextAsset>(Ras + "Animations/model/ras.spine.json");
            var source = CreateActor(Vector3.zero, out shadow);
            var target = CreateActor(Vector3.right * 3f, out _);
            using (var serialized = new SerializedObject(source))
            {
                serialized.FindProperty("skeleton").objectReferenceValue = skeleton;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            caster = source.transform;
            var targets = Array.CreateInstance(actor, 1);
            targets.SetValue(target, 0);
            try
            {
                var presenter = (IDisposable)Activator.CreateInstance(runtime,
                    new object[] { root.transform, new[] { graph }, Array.CreateInstance(asset, 0), null, null, null });
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
