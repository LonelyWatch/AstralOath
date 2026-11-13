using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleLayoutTargetVisibilityTests
    {
        const string Assembly = "AstralOath.Client.BattleScene";
        GameObject root; Camera camera; Transform[] slots, actors; MeshRenderer[] bodies;
        SpriteRenderer targetShadow; object stage;
        [SetUp] public void Create()
        {
            root = new GameObject("Layout target visibility test");
            camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
            slots = new Transform[6]; actors = new Transform[6]; bodies = new MeshRenderer[6];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new GameObject("Slot " + i).transform; slots[i].SetParent(root.transform);
                slots[i].position = new Vector3(i * 2 - 6, i % 2, 0);
                actors[i] = new GameObject("Actor " + i, typeof(MeshRenderer)).transform;
                actors[i].SetParent(slots[i], false); bodies[i] = actors[i].GetComponent<MeshRenderer>();
            }
            targetShadow = new GameObject("Target shadow", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            targetShadow.transform.SetParent(slots[1], false);
            stage = Activator.CreateInstance(Type.GetType(Assembly + ".BattleCinematicStage, " + Assembly, true),
                new object[] { camera, null, null });
            Call("SetParticipants", (object)slots);
            Call("Begin", actors[0], actors.Skip(1).Take(4).ToArray(), true);
        }
        [TearDown] public void Release() { ((IDisposable)stage)?.Dispose(); Object.DestroyImmediate(root); }
        void Call(string name, params object[] values) => stage.GetType().GetMethod(name).Invoke(stage, values);
        static JObject SourceNode(string character, string source, int guid)
        {
            string path = "Assets/Game/Modules/CharacterPresentation/Res/Characters/" + character + "/Configs/stagept/" + source + ".graph.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            return JObject.Parse(asset.text)["source"]["entities"].OfType<JObject>().Single(node => (int)node["guid"] == guid);
        }
        [Test] public void OriginalRasTargetCameraRevealsAllFourRecipientsBeforeTheImpactSequence()
        {
            var positions = slots.Select(x => x.position).ToArray();
            Call("Play", SourceNode("c1001", "c_ras_skill_03", 5));
            Assert.That(bodies.Skip(1).All(x => !x.enabled), Is.True, "Caster close-up hides recipients and spectators.");
            Call("Play", SourceNode("c1001", "c_ras_skill_03", 26));
            for (int i = 1; i <= 4; i++)
                Assert.That(bodies[i].enabled, Is.True, "Target camera must reveal recipient " + i + " before hit feedback.");
            Assert.That(targetShadow.enabled, Is.True);
            Assert.That(bodies[5].enabled, Is.False, "Uninvolved spectators remain hidden until LAYOUT restore.");
            Call("Play", SourceNode("c1001", "c_ras_skill_03", 16)); Call("Tick", .4f);
            Assert.That(bodies.Take(5).All(x => x.enabled), Is.True);
            Assert.That(slots.Select(x => x.position), Is.EqualTo(positions));
            Call("Play", SourceNode("c1001", "c_ras_skill_03", 22));
            Assert.That(bodies.All(x => x.enabled), Is.True);
        }
        [Test] public void TargetCameraDoesNotOverrideOriginallyHiddenBodiesAndRepeatedLayoutsRestoreCleanly()
        {
            bodies[2].enabled = false;
            var away = SourceNode("c1001", "c_ras_skill_03", 5);
            var targetCamera = SourceNode("c1001", "c_ras_skill_03", 26);
            Call("Play", away); Call("Play", targetCamera);
            Assert.That(bodies[1].enabled, Is.True); Assert.That(bodies[2].enabled, Is.False);
            Call("Play", away);
            Assert.That(bodies.Skip(1).All(x => !x.enabled), Is.True);
            Call("Play", targetCamera); Call("Reset");
            Assert.That(bodies[1].enabled, Is.True); Assert.That(bodies[2].enabled, Is.False);
            Assert.That(bodies[5].enabled, Is.True); Assert.That(targetShadow.enabled, Is.True);
        }
        [TestCase(5)][TestCase(12)][TestCase(16)][TestCase(31)][TestCase(32)][TestCase(41)][TestCase(42)]
        public void SharedTargetFocusLocationsRevealRecipientsWithoutRevealingSpectators(int location)
        {
            Call("Play", SourceNode("c1001", "c_ras_skill_03", 5));
            Call("Play", new JObject { ["etty"] = "CAM", ["location"] = location, ["time"] = 100 });
            Assert.That(bodies.Skip(1).Take(4).All(x => x.enabled), Is.True);
            Assert.That(bodies[5].enabled, Is.False);
            Call("Dispose"); Assert.That(bodies.All(x => x.enabled), Is.True);
        }
        [Test] public void OriginalMercedesFocusLayoutKeepsAllTargetsAndHidesOnlySpectators()
        {
            Call("Play", SourceNode("c1005", "c_mercedes_skill_03", 4));
            Call("Play", SourceNode("c1005", "c_mercedes_skill_03", 3));
            Assert.That(bodies.Take(5).All(x => x.enabled), Is.True);
            Assert.That(bodies[5].enabled, Is.False);
        }
    }
}
