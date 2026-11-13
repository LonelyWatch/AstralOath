using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class BattleActorReturnTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type ActorType = Type.GetType(
            "AstralOath.Client.BattleScene.BattlePreviewActor, AstralOath.Client.BattleScene", true);
        private GameObject root;
        private Texture2D page;
        private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        private readonly List<Component> actors = new List<Component>();

        [SetUp] public void SetUp()
        {
            root = new GameObject("Battle actor recovery regression"); root.SetActive(false);
            page = new Texture2D(2, 2);
        }
        [TearDown] public void TearDown()
        {
            foreach (var actor in actors) Invoke(actor, "OnDisable");
            actors.Clear(); UnityEngine.Object.DestroyImmediate(root);
            foreach (var asset in assets) UnityEngine.Object.DestroyImmediate(asset);
            assets.Clear(); UnityEngine.Object.DestroyImmediate(page);
        }

        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void ReturnToIdleKeepsTheDisplayedPoseThenConvergesWithoutMovingTheSlot(string version)
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json(version));
            json["animations"]["b_idle"] = JObject.Parse("{\"bones\":{\"root\":{\"translate\":[{\"time\":0},{\"time\":1}]}}}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor);
            actor.transform.localPosition = new Vector3(4, 5, 0);
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1);
            var before = Position(player, "root");
            Invoke(actor, "PlayBattleIdle");
            Assert.That(Position(player, "root"), Is.EqualTo(before), "Starting recovery must not snap the visible root to idle.");
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "root").x, Is.InRange(.001f, before.x - .001f), "Recovery must progress between the two poses.");
            Invoke(actor, "AdvancePresentation", .95f);
            Assert.That(Position(player, "root").x, Is.Zero.Within(.00001f));
            Assert.That(actor.transform.localPosition, Is.EqualTo(new Vector3(4, 5, 0)), "The authoritative slot is not a bone-recovery offset.");
        }

        [TestCase("ras", "attacked", "ras_center")]
        [TestCase("ras", "jump_back", "ras_center")]
        [TestCase("ras", "skill3", "ras_center")]
        [TestCase("mercedes", "attacked", "quarter_center")]
        [TestCase("mercedes", "skill3", "quarter_center")]
        [TestCase("angelica", "attacked", "center")]
        [TestCase("angelica", "skill3", "center")]
        public void RealSourceTailDoesNotSnapAndFinalIdleMatchesIndependentBonesAndMesh(string model, string animation, string bone)
        {
            var source = ReadModel(model);
            var actor = Create(source.json, source.atlas, source.page);
            var expectedActor = Create(source.json, source.atlas, source.page);
            var player = Player(actor); var expected = Player(expectedActor);
            Invoke(actor, "PlayBattleAnimation", animation); player.Tick(player.Duration);
            var before = Position(player, bone);
            Invoke(actor, "PlayBattleIdle");
            Assert.That(Vector2.Distance(Position(player, bone), before), Is.LessThan(.00001f), model + " " + animation + " recovery must begin at the displayed tail pose.");
            for (int frame = 0; frame < 5; frame++)
            { Invoke(actor, "AdvancePresentation", .05f); expected.Tick(.05f); }
            foreach (var item in (JArray)JObject.Parse(source.json)["bones"])
            {
                string name = (string)item["name"];
                Assert.That(player.TryGetBonePose(name, out var actualBone), Is.True);
                Assert.That(expected.TryGetBonePose(name, out var expectedBone), Is.True);
                Assert.That(Vector2.Distance(actualBone.LocalPosition, expectedBone.LocalPosition), Is.LessThan(.0001f), model + " bone position " + name);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(actualBone.LocalRotation, expectedBone.LocalRotation)), Is.LessThan(.001f), model + " bone rotation " + name);
                Assert.That(Vector2.Distance(actualBone.LocalScale, expectedBone.LocalScale), Is.LessThan(.0001f), model + " bone scale " + name);
            }
            var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            var expectedMesh = expected.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.triangles, Is.EqualTo(expectedMesh.triangles), "Attachments and draw order must finish at independent idle. " + SlotDifferences(player, expected));
            Assert.That(mesh.uv, Is.EqualTo(expectedMesh.uv));
            var actualVertices = mesh.vertices; var expectedVertices = expectedMesh.vertices;
            Assert.That(actualVertices.Length, Is.EqualTo(expectedVertices.Length));
            for (int i = 0; i < actualVertices.Length; i++)
                Assert.That(Vector3.Distance(actualVertices[i], expectedVertices[i]), Is.LessThan(.0001f), model + " mesh/FFD vertex " + i);
        }

        [Test]
        public void SourceZeroSecondReactionCanHoldAndReleaseWithoutStealingANewerTrack()
        {
            var source = ReadModel("ras");
            var actor = Create(source.json, source.atlas, source.page);
            var player = Player(actor);
            var hold = ActorType.GetMethod("HoldSourceReaction", Private);
            Assert.That(hold, Is.Not.Null);
            var release = (Action)hold.Invoke(actor, new object[] { "flying_damage" });
            Assert.That(release, Is.Not.Null);
            Assert.That(player.CurrentAnimation, Is.EqualTo("flying_damage"));
            Assert.That(player.Duration, Is.Zero);
            Invoke(actor, "Update");
            Assert.That(player.CurrentAnimation, Is.EqualTo("flying_damage"));
            release();
            Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"));

            release = (Action)hold.Invoke(actor, new object[] { "flying_damage" });
            Invoke(actor, "PlayBattleAnimation", "attacked");
            release();
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"));
        }

        [TestCase("jump_back")]
        [TestCase("attacked")]
        [TestCase("skill2")]
        [TestCase("skill3")]
        public void RoseChangesBodyViewWithoutBlendingThroughParkedBones(string animation)
        {
            var source = ReadModel("rose");
            var actor = Create(source.json, source.atlas, source.page);
            var expectedActor = Create(source.json, source.atlas, source.page);
            var player = Player(actor); var expected = Player(expectedActor);
            var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            var expectedMesh = expected.Root.GetComponent<MeshFilter>().sharedMesh;
            actor.transform.localPosition = new Vector3(4, 5, 0);
            Invoke(actor, "PlayBattleAnimation", animation); player.Tick(player.Duration);
            Assert.That(Position(player, "quarter_view").x, Is.Zero.Within(.0001f));
            Assert.That(Position(player, "front_view").x, Is.GreaterThan(6f));
            Invoke(actor, "PlayBattleIdle");
            for (int frame = 0; frame < 6; frame++)
            {
                Assert.That(Position(player, "front_view").x, Is.EqualTo(Position(expected, "front_view").x).Within(.0001f),
                    "The active body view should enter idle at its authored position on the first frame.");
                Assert.That(Position(player, "quarter_view").x, Is.EqualTo(Position(expected, "quarter_view").x).Within(.0001f));
                Assert.That(Vector3.Distance(mesh.bounds.center, expectedMesh.bounds.center), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(mesh.bounds.size, expectedMesh.bounds.size), Is.LessThan(.0001f));
                Assert.That(actor.transform.localPosition, Is.EqualTo(new Vector3(4, 5, 0)));
                Invoke(actor, "AdvancePresentation", .025f); expected.Tick(.025f);
            }
        }

        [Test]
        public void PausedReturnDoesNotAdvanceAndRepeatedIdleOrOtherActorsCannotRestartIt()
        {
            var source = ReadModel("ras");
            var actor = Create(source.json, source.atlas, source.page);
            var other = Create(source.json, source.atlas, source.page);
            Invoke(other, "OnDisable");
            ActorType.GetField("skeleton", Private).SetValue(other, ActorType.GetField("skeleton", Private).GetValue(actor));
            ActorType.GetField("atlas", Private).SetValue(other, ActorType.GetField("atlas", Private).GetValue(actor));
            Invoke(other, "OnEnable");
            var player = Player(actor);
            Invoke(actor, "PlayBattleAnimation", "attacked"); player.Tick(player.Duration);
            Invoke(actor, "PlayBattleIdle"); Invoke(actor, "AdvancePresentation", .05f);
            var before = Position(player, "ras_center"); float clock = player.Time;
            Invoke(actor, "PlayBattleIdle");
            Assert.That(player.Time, Is.EqualTo(clock));
            Invoke(actor, "SetPaused", true); Invoke(actor, "AdvancePresentation", 1f);
            Assert.That(Position(player, "ras_center"), Is.EqualTo(before));
            Invoke(actor, "SetPaused", false); Invoke(actor, "SetPresentationPaused", true);
            Invoke(actor, "AdvancePresentation", 1f);
            Assert.That(Position(player, "ras_center"), Is.EqualTo(before));
            Invoke(actor, "SetPresentationPaused", false); Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "ras_center"), Is.Not.EqualTo(before));
            Assert.That(Player(other).Time, Is.Zero);
            Invoke(actor, "PlayBattleAnimation", "attacked");
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked")); Assert.That(player.Time, Is.Zero);
            Invoke(actor, "PlayBattleIdle"); Invoke(actor, "AdvancePresentation", .3f);
            Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"));
        }

        [TestCase(false)][TestCase(true)]
        public void PartialIncomingClipBlendsMissingBoneChannelsToSetupDuringRecovery(bool delayedFirstKey)
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["b_idle"] = JObject.Parse("{\"events\":[{\"time\":1,\"name\":\"hit\"}]}");
            if (delayedFirstKey)
                json["animations"]["b_idle"]["bones"] = JObject.Parse("{\"root\":{\"translate\":[{\"time\":0.1},{\"time\":1}]}}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor);
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            float tail = Position(player, "root").x;
            Invoke(actor, "PlayBattleIdle");
            Assert.That(Position(player, "root").x, Is.EqualTo(tail).Within(.00001f));
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "root").x, Is.InRange(.001f, tail - .001f));
            float first = Position(player, "root").x;
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "root").x, Is.LessThan(first));
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "root").x, Is.Zero.Within(.00001f));
        }

        [TestCase(false)][TestCase(true)]
        public void CompletedClipHeldAcrossFramesCanStillBlendThroughAutomaticUpdate(bool automatic)
        {
            var source = ReadModel("ras");
            var actor = Create(source.json, source.atlas, source.page);
            var player = Player(actor);
            Invoke(actor, "PlayBattleAnimation", "attacked"); player.Tick(player.Duration);
            player.Tick(.02f); player.Tick(.02f);
            var tail = Position(player, "ras_center");
            Invoke(actor, automatic ? "Update" : "PlayBattleIdle");
            Assert.That(Vector2.Distance(Position(player, "ras_center"), tail), Is.LessThan(.00001f), "Held source tracks must remain available for an eventual idle mix.");
            Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"));
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(Position(player, "ras_center"), Is.Not.EqualTo(tail));
        }

        [Test]
        public void HoldingTheLastPoseDoesNotReplayCompletionAndSeekDoesNotSynthesizeIt()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["b_idle"] = json["animations"]["move"].DeepClone();
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); int completions = 0;
            player.Event += value => { if (value.Kind == SkeletonPlaybackEventKind.Complete) completions++; };
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            Assert.That(completions, Is.EqualTo(1));
            for (int i = 0; i < 10; i++) player.Tick(1f);
            Assert.That(completions, Is.EqualTo(1));
            Invoke(actor, "PlayBattleAnimation", "move"); player.Seek(player.Duration); player.Tick(.1f);
            Assert.That(completions, Is.EqualTo(1), "A diagnostic seek to the end must not manufacture completion callbacks.");
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            Assert.That(completions, Is.EqualTo(2));
        }

        [Test]
        public void MissingMeshDeformationFadesToSetupInsteadOfPoppingAtTheEnd()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["skins"]["default"]["body"]["quad"] = JObject.Parse("{\"type\":\"mesh\",\"uvs\":[0,0,1,0,1,1,0,1],\"triangles\":[0,1,2,2,3,0],\"vertices\":[0,0,2,0,2,2,0,2],\"hull\":4}");
            json["animations"]["deformed"] = JObject.Parse("{\"ffd\":{\"default\":{\"body\":{\"quad\":[{\"time\":0,\"vertices\":[10,0,10,0,10,0,10,0]},{\"time\":1,\"vertices\":[10,0,10,0,10,0,10,0]}]}}}}");
            json["animations"]["b_idle"] = JObject.Parse("{\"events\":[{\"time\":1,\"name\":\"hit\"}]}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            float setup = mesh.vertices[0].x;
            Invoke(actor, "PlayBattleAnimation", "deformed"); player.Tick(1f);
            float tail = mesh.vertices[0].x;
            Invoke(actor, "PlayBattleIdle");
            Assert.That(mesh.vertices[0].x, Is.EqualTo(tail).Within(.00001f));
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(mesh.vertices[0].x, Is.InRange(setup + .001f, tail - .001f));
            Invoke(actor, "AdvancePresentation", .05f);
            Invoke(actor, "AdvancePresentation", .05f);
            Assert.That(mesh.vertices[0].x, Is.EqualTo(setup).Within(.00001f));
        }

        [Test]
        public void DelayedFirstKeyKeepsTheSourceHoldBehaviorAfterTheFirstLoop()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["b_idle"] = JObject.Parse("{\"bones\":{\"root\":{\"translate\":[{\"time\":0.5,\"x\":20},{\"time\":1,\"x\":30}]}}}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var expectedActor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); var expected = Player(expectedActor);
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            Invoke(actor, "PlayBattleIdle");
            for (int frame = 0; frame < 24; frame++)
            { Invoke(actor, "AdvancePresentation", .05f); expected.Tick(.05f); }
            Assert.That(Position(expected, "root").x, Is.GreaterThan(.25f), "The independent source keeps the previous cycle's tail before its first key.");
            Assert.That(Position(player, "root").x, Is.EqualTo(Position(expected, "root").x).Within(.00001f));
        }

        [Test]
        public void ReenteringAMixedLoopRestartsItsDefaultsWithoutDuplicatingSourceEvents()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["b_idle"] = JObject.Parse("{\"bones\":{\"root\":{\"translate\":[{\"time\":0.5,\"x\":20},{\"time\":1,\"x\":30}]}},\"events\":[{\"time\":0.25,\"name\":\"hit\"},{\"time\":0.8,\"name\":\"hit\"}]}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var expectedActor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); var expected = Player(expectedActor);
            int events = 0, expectedEvents = 0;
            player.Event += value => { if (value.Kind == SkeletonPlaybackEventKind.Event) events++; };
            expected.Event += value => { if (value.Kind == SkeletonPlaybackEventKind.Event) expectedEvents++; };
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
                events = 0; expectedEvents = 0;
                Invoke(actor, "PlayBattleIdle"); expected.Play("b_idle", true);
                for (int frame = 0; frame < 45; frame++)
                {
                    Invoke(actor, "AdvancePresentation", .05f); expected.Tick(.05f);
                    if (frame >= 3)
                        Assert.That(Position(player, "root").x, Is.EqualTo(Position(expected, "root").x).Within(.00001f));
                }
                Assert.That(events, Is.EqualTo(expectedEvents));
                Assert.That(events, Is.GreaterThanOrEqualTo(4), "The source loop must actually emit multiple cycles of events.");
            }
        }

        [Test]
        public void OutgoingTerminalAttachmentCannotOverwriteTheIncomingHeldAttachment()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["move"]["slots"] = JObject.Parse("{\"body\":{\"attachment\":[{\"time\":0,\"name\":\"quad\"},{\"time\":1,\"name\":null}]}}");
            json["animations"]["b_idle"] = JObject.Parse("{\"slots\":{\"body\":{\"attachment\":[{\"time\":0,\"name\":\"quad\"}]}},\"events\":[{\"time\":1,\"name\":\"hit\"}]}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            Assert.That(mesh.vertexCount, Is.Zero);
            Invoke(actor, "PlayBattleIdle");
            for (int frame = 0; frame < 5; frame++)
            {
                Invoke(actor, "AdvancePresentation", .05f);
                Assert.That(mesh.vertexCount, Is.EqualTo(4), "The target's held attachment must win over a previous terminal key at every frame.");
            }
        }

        [Test]
        public void LongMixRetainsTheTargetAttachmentAcrossALoopBeforeItsFirstKey()
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json("2.1.25"));
            json["animations"]["move"]["slots"] = JObject.Parse("{\"body\":{\"attachment\":[{\"time\":0,\"name\":\"quad\"},{\"time\":1,\"name\":null}]}}");
            json["animations"]["b_idle"] = JObject.Parse("{\"slots\":{\"body\":{\"attachment\":[{\"time\":0.5,\"name\":\"quad\"},{\"time\":1,\"name\":\"quad\"}]}}}");
            var actor = Create(json.ToString(), SkeletonPlayerTests.Atlas, "page.png");
            var player = Player(actor); var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            Invoke(actor, "PlayBattleAnimation", "move"); player.Tick(1f);
            player.Play("b_idle", true, 2f);
            for (int frame = 0; frame < 24; frame++)
            {
                player.Tick(.05f);
                if (frame >= 10)
                    Assert.That(mesh.vertexCount, Is.EqualTo(4), "A later loop holds its own last key while the previous track is still mixing out.");
            }
        }

        private Component Create(string json, string atlasText, string pageName)
        {
            var go = new GameObject("Actor"); go.transform.SetParent(root.transform, false);
            var actor = go.AddComponent(ActorType);
            var skeleton = new TextAsset(json); var atlas = new TextAsset(atlasText);
            assets.Add(skeleton); assets.Add(atlas);
            ActorType.GetField("skeleton", Private).SetValue(actor, skeleton);
            ActorType.GetField("atlas", Private).SetValue(actor, atlas);
            ActorType.GetField("page", Private).SetValue(actor, page);
            ActorType.GetField("pageName", Private).SetValue(actor, pageName);
            actors.Add(actor); Invoke(actor, "OnEnable"); return actor;
        }
        private static (string json, string atlas, string page) ReadModel(string model)
        {
            var manifest = JObject.Parse(File.ReadAllText("Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json"));
            var resources = ((JArray)manifest["entries"]).Cast<JObject>().ToDictionary(row => (string)row["address"], row => (string)row["path"]);
            string Read(string address) => File.ReadAllText(resources[address]);
            var source = JObject.Parse(Read("characterlab/source/model/" + model + ".scsp"));
            return (Read((string)source["jsonAddress"]), Read((string)source["atlasAddress"]), (string)source["pages"][0]["name"]);
        }
        private static string SlotDifferences(ISkeletonPlayer actual, ISkeletonPlayer expected)
        {
            Dictionary<string, string> Read(ISkeletonPlayer value)
            {
                var runtime = value.GetType().GetField("runtime", Private).GetValue(value);
                var skeleton = runtime.GetType().GetField("skeleton", Private).GetValue(runtime);
                var slots = (System.Collections.IEnumerable)skeleton.GetType().GetProperty("Slots").GetValue(skeleton);
                var values = new Dictionary<string, string>();
                foreach (var slot in slots)
                {
                    var data = slot.GetType().GetProperty("Data").GetValue(slot);
                    string name = (string)data.GetType().GetProperty("Name").GetValue(data);
                    var attachment = slot.GetType().GetProperty("Attachment").GetValue(slot);
                    values[name] = attachment == null ? "<null>" : attachment.GetType().Name + ":" + attachment.GetType().GetProperty("Name").GetValue(attachment);
                }
                return values;
            }
            var left = Read(actual); var right = Read(expected);
            return string.Join("; ", left.Where(row => row.Value != right[row.Key]).Select(row => row.Key + " actual=" + row.Value + " expected=" + right[row.Key]));
        }
        private static Vector2 Position(ISkeletonPlayer player, string bone)
        { Assert.That(player.TryGetBonePose(bone, out var pose), Is.True); return pose.LocalPosition; }
        private static ISkeletonPlayer Player(Component actor) => (ISkeletonPlayer)ActorType.GetField("player", Private).GetValue(actor);
        private static object Invoke(Component actor, string name, params object[] args) =>
            ActorType.GetMethod(name, Private | BindingFlags.Public).Invoke(actor, args);
    }
}
