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

        [TestCase("ras")]
        [TestCase("mercedes")]
        public void IncapacitatedActorLoopsNativeGroggyThroughHitsUntilStatusClears(string model)
        {
            var source = ReadModel(model);
            var actor = Create(source.json, source.atlas, source.page);
            var player = Player(actor);
            Invoke(actor, "SetTurnReady", true);
            var actionBlocked = ActorType.GetMethod("SetActionBlocked", Private);
            Assert.That(actionBlocked, Is.Not.Null, "Active control status must reach the actor's ambient animation.");
            actionBlocked.Invoke(actor, new object[] { true });
            Assert.That(player.CurrentAnimation, Is.EqualTo("groggy"));
            float duration = player.Duration;
            Invoke(actor, "AdvanceAmbient", duration + .22f, duration + .22f);
            Assert.That(player.CurrentAnimation, Is.EqualTo("groggy"));
            Assert.That(player.Time, Is.EqualTo(.22f).Within(.01f), "The source clock must wrap instead of stopping on the last frame.");
            actionBlocked.Invoke(actor, new object[] { true });
            Assert.That(player.Time, Is.EqualTo(.22f).Within(.01f), "Applying the same status again must not restart the loop.");

            Invoke(actor, "PlayBattleAnimation", "attacked");
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"));
            actionBlocked.Invoke(actor, new object[] { true });
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"), "Status changes must let hit reactions finish.");
            Invoke(actor, "AdvanceAmbient", player.Duration + .05f, player.Duration + .05f);
            Assert.That(player.CurrentAnimation, Is.EqualTo("groggy"));

            Invoke(actor, "SetTurnReady", false);
            Assert.That(player.CurrentAnimation, Is.EqualTo("groggy"));
            actionBlocked.Invoke(actor, new object[] { false });
            Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));
        }

        [Test]
        public void ModelBodyReplacesTheCasterAtItsSourceCueAndKeepsThePresentationClock()
        {
            var initial = ReadModel("ras");
            var other = ReadModel("mercedes");
            var caster = Create(initial.json, initial.atlas, initial.page);
            var target = Create(initial.json, initial.atlas, initial.page);
            var originalPlayer = Player(caster);
            var graph = new TextAsset(@"{
                'id':'form-test','assets':[{'type':'skeleton','source':'model/mercedes.scsp'}],
                'source':{'entrypoint':{'start':1},'entities':[
                    {'guid':1,'etty':'ANI','ani':'skill1'},
                    {'guid':2,'etty':'MODEL_BODY','model_id':'mercedes'}],
                    'connections':[{'from':1,'to':2,'when':1,'delay':200}]}}");
            var model = new TextAsset(other.json);
            var atlas = new TextAsset(other.atlas);
            assets.AddRange(new UnityEngine.Object[] { graph, model, atlas });
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            var asset = Activator.CreateInstance(assetType);
            void Set(string key, object value) => assetType.GetField(key).SetValue(asset, value);
            Set("source", "model/mercedes.scsp"); Set("document", model); Set("atlas", atlas);
            Set("scale", .01f); Set("pageNames", new[] { other.page }); Set("pages", new[] { page });
            var sources = Array.CreateInstance(assetType, 1); sources.SetValue(asset, 0);
            var recipients = Array.CreateInstance(ActorType, 1); recipients.SetValue(target, 0);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform, new[] { graph }, sources, null, null, null);
            type.GetMethod("Prepare").Invoke(playback, new object[] { caster, "form-test" });
            type.GetMethod("Begin").Invoke(playback, new object[] { "form-test", caster, target, recipients });
            type.GetMethod("Tick").Invoke(playback, new object[] { .19f });
            Assert.That(Player(caster), Is.SameAs(originalPlayer), "Loading the graph and starting its skill must retain the base body.");
            type.GetMethod("Tick").Invoke(playback, new object[] { .02f });
            Assert.That(ReferenceEquals(ActorType.GetField("skeleton", Private).GetValue(caster), model), Is.True,
                "The scheduled MODEL_BODY must install the named skeleton.");
            Assert.That(Player(caster), Is.Not.SameAs(originalPlayer));
            Assert.That((int)ActorType.GetField("presentationControlCount", Private).GetValue(caster), Is.EqualTo(1));
            Assert.That(Player(target).CurrentAnimation, Does.Contain("idle"));
            playback.Dispose();
            Assert.That((int)ActorType.GetField("presentationControlCount", Private).GetValue(caster), Is.Zero);
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
            Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));

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
            // This regression exercises recovery to the battle stance during the current turn.
            EnterBattleIdle(actor, player);
            EnterBattleIdle(expectedActor, expected);
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
            Assert.That(player.CurrentAnimation, Is.EqualTo("idle"));
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
            EnterBattleIdle(actor, player);
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

        [TestCase(false)]
        [TestCase(true)]
        public void SezS3MovesTheTargetThroughItsFourReactionTracksAndRestoresTheBody(bool interrupt)
        {
            const string directory = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1038/";
            var graph = JObject.Parse(File.ReadAllText(directory + "Configs/stagept/c_sez_gaien_skill_03.graph.json"));
            var node = graph["source"]["entities"].OfType<JObject>().Single(row => (int)row["guid"] == 48);
            Assert.That(node["bone"], Is.Null);
            var targetSource = ReadModel("ras");
            var actor = Create(targetSource.json, targetSource.atlas, targetSource.page);
            var player = Player(actor);
            EnterBattleIdle(actor, player);
            player.Play("attacked", false);
            actor.transform.position = new Vector3(3f, -2f, 0f);
            var slot = actor.transform.position;
            var body = player.Root.transform;
            var start = body.position;
            var rotation = body.localRotation;
            var scale = body.localScale;

            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            var asset = Activator.CreateInstance(assetType);
            var rig = new TextAsset(File.ReadAllText(directory + "Animations/spani/sez_gaien_sk3eff_node.spine.json"));
            assets.Add(rig);
            assetType.GetField("document").SetValue(asset, rig);
            assetType.GetField("source").SetValue(asset, "spani/" + (string)node["source"]);
            assetType.GetField("scale").SetValue(asset, .01f);
            var sources = Array.CreateInstance(assetType, 1);
            sources.SetValue(asset, 0);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform,
                Array.Empty<TextAsset>(), sources, null, null, null);
            var targets = Array.CreateInstance(ActorType, 1);
            targets.SetValue(actor, 0);
            type.GetMethod("PlayNodeAnimation", Private).Invoke(playback, new object[] { node, 0f, targets });

            float previous = 0f;
            void At(float seconds, string reaction)
            {
                type.GetMethod("Tick").Invoke(playback, new object[] { seconds - previous });
                previous = seconds;
                Assert.That(player.CurrentAnimation, Is.EqualTo(reaction), "Source target reaction at " + seconds);
                Assert.That(actor.transform.position, Is.EqualTo(slot), "Target displacement belongs to its visible body.");
            }
            At(0f, "b_idle");
            At(.428f, "b_idle");
            At(.430f, "attacked_damage");
            At(1.187f, "attacked_damage");
            At(1.189f, "flying");
            At(1.4f, "flying");
            var displacement = body.position - start;
            Assert.That(displacement.x, Is.EqualTo(-8.386f).Within(.001f));
            Assert.That(displacement.y, Is.EqualTo(12.0597f).Within(.001f));
            At(2.099f, "flying");
            At(2.101f, "attacked_damage");
            if (interrupt) playback.Dispose();
            else At(8.14f, "b_idle");
            Assert.That(type.GetMethod("OwnsNodeAnimationPose", Private).Invoke(playback, new object[] { actor }), Is.False);
            Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"));
            Assert.That(body.position, Is.EqualTo(start));
            Assert.That(body.localRotation, Is.EqualTo(rotation));
            Assert.That(body.localScale, Is.EqualTo(scale));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CeriseMovesOnlyTheAcceptedTargetsAcrossTheBeamAndReleasesTheirBodies(bool mirrored)
        {
            const string directory = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1081/";
            var graph = JObject.Parse(File.ReadAllText(directory + "Configs/stagept/c_ceris_skill_03.graph.json"));
            var node = graph["source"]["entities"].OfType<JObject>().Single(row => (int)row["guid"] == 26);
            Assert.That((int)node["target_node"], Is.EqualTo(76));
            var model = ReadModel("ras");
            Component MakeActor(float x)
            {
                var actor = Create(model.json, model.atlas, model.page);
                actor.transform.position = new Vector3(x, 0, 0);
                return actor;
            }
            var caster = MakeActor(mirrored ? 4 : -4);
            ActorType.GetField("faceLeft", Private).SetValue(caster, mirrored);
            var targets = new[] { MakeActor(3), MakeActor(5) };
            foreach (var target in targets) EnterBattleIdle(target, Player(target));
            var bystander = MakeActor(1);
            var bodies = targets.Select(actor => Player(actor).Root.transform).ToArray();
            var starts = bodies.Select(body => body.position).ToArray();
            var casterStart = Player(caster).Root.transform.position;
            var bystanderStart = Player(bystander).Root.transform.position;
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            var asset = Activator.CreateInstance(assetType);
            var rig = new TextAsset(File.ReadAllText(directory + "Animations/spani/ceris_sk3_2eff_node_target.spine.json"));
            assets.Add(rig);
            assetType.GetField("document").SetValue(asset, rig);
            assetType.GetField("source").SetValue(asset, "spani/" + (string)node["source"]);
            assetType.GetField("scale").SetValue(asset, .01f);
            var sources = Array.CreateInstance(assetType, 1);
            sources.SetValue(asset, 0);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform,
                Array.Empty<TextAsset>(), sources, null, null, null);
            type.GetField("caster", Private).SetValue(playback, caster);
            var accepted = Array.CreateInstance(ActorType, targets.Length);
            Array.Copy(targets, accepted, targets.Length);
            type.GetMethod("PlayNodeAnimation", Private).Invoke(playback, new object[] { node, 0f, accepted });

            float direction = mirrored ? -1f : 1f;
            // Source setup x=-1300 plus its first key x=3096.93; at 600 ms the key is -62.77.
            Assert.That(bodies.Average(body => body.position.x) - starts.Average(p => p.x),
                Is.EqualTo(17.9693f * direction).Within(.002f));
            type.GetMethod("Tick").Invoke(playback, new object[] { .566f });
            Assert.That(targets.Select(actor => Player(actor).CurrentAnimation), Is.All.EqualTo("b_idle"));
            type.GetMethod("Tick").Invoke(playback, new object[] { .034f });
            Assert.That(targets.Select(actor => Player(actor).CurrentAnimation), Is.All.EqualTo("attacked_damage"),
                "Both Ras targets have this source reaction as a single setup pose at 567 ms.");
            Assert.That(bodies.Average(body => body.position.x) - starts.Average(p => p.x),
                Is.EqualTo(-13.6277f * direction).Within(.002f));
            Assert.That(bodies.Select(body => body.localScale.x), Is.All.EqualTo(.5f).Within(.001f));
            Assert.That(bodies[1].position.x - bodies[0].position.x,
                Is.EqualTo((starts[1].x - starts[0].x) * .5f).Within(.001f));
            Assert.That(Player(caster).Root.transform.position, Is.EqualTo(casterStart));
            Assert.That(Player(bystander).Root.transform.position, Is.EqualTo(bystanderStart));
            playback.Dispose();
            Assert.That(bodies.Select(body => body.position), Is.EqualTo(starts));
            Assert.That(targets.Select(actor => actor.transform.position.x), Is.EqualTo(new[] { 3f, 5f }));
        }

        [TestCase("stop", true)]
        [TestCase("interrupt", true)]
        [TestCase("end", true)]
        [TestCase("handoff", true)]
        [TestCase("nonzero", false)]
        [TestCase("unspecified", false)]
        [TestCase("timed-stop", false)]
        public void StrazeTransparentTargetLayerOwnsOnlyItsRecipientsUntilRelease(string variant, bool hides)
        {
            const string directory = "Assets/Game/Modules/CharacterPresentation/Res/Characters/";
            var graph = JObject.Parse(File.ReadAllText(directory + "c1034/Configs/stagept/c_strage_skill_03.graph.json"));
            var original = graph["source"]["entities"].OfType<JObject>().ToDictionary(row => (int)row["guid"]);
            var blend = (JObject)original[10].DeepClone();
            if (variant == "nonzero") blend["alpha_channel"] = .5f;
            if (variant == "unspecified") blend.Remove("alpha_channel");
            // Isolate the actual #10, the same-ID finite tint #20, and STOP #26.
            // The six Spine layers in the first CFX all end at 2.9333 s before the cut-in.
            var stop = (JObject)original[26].DeepClone();
            if (variant == "end") stop["targetId"] = "other-layer";
            if (variant == "timed-stop") stop["time"] = 200;
            JObject Edge(int to, float delay) => new JObject { ["from"] = 2, ["to"] = to, ["when"] = 1, ["delay"] = delay };
            graph["assets"] = new JArray();
            graph["source"]["entrypoint"]["start"] = 2;
            graph["source"]["entities"] = new JArray(original[2], blend, original[20], stop, original[9]);
            graph["source"]["connections"] = new JArray(Edge(10, 0), Edge(20, 233), Edge(26, 2933.3f), Edge(9, 3400));
            var text = new TextAsset(graph.ToString());
            assets.Add(text);
            var model = ReadModel("ras");
            Component Unit() => Create(model.json, model.atlas, model.page);
            var caster = Unit(); var target = Unit(); var alreadyHidden = Unit(); var spectator = Unit();
            Renderer Body(Component actor) => Player(actor).Root.GetComponent<Renderer>();
            Body(alreadyHidden).forceRenderingOff = true;
            target.transform.localPosition = new Vector3(4f, -2f, 0f);
            var slot = target.transform.localPosition;
            var roster = Array.CreateInstance(ActorType, 2);
            roster.SetValue(target, 0); roster.SetValue(alreadyHidden, 1);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform, new[] { text },
                Array.CreateInstance(assetType, 0), null, null, null);
            Assert.That(Body(target).forceRenderingOff, Is.False);
            type.GetMethod("Begin").Invoke(playback, new object[] { (string)graph["id"], caster, target, roster });
            void Tick(float delta) => type.GetMethod("Tick").Invoke(playback, new object[] { delta });
            Tick(.8f);
            Assert.That(Body(target).forceRenderingOff, Is.EqualTo(hides), "Target draw at 0.8 stage seconds");
            Tick(1f);
            Assert.That(Body(target).forceRenderingOff, Is.EqualTo(hides), "Target draw at 1.8 stage seconds");
            Assert.That(Body(caster).forceRenderingOff || Body(spectator).forceRenderingOff, Is.False);
            Assert.That(target.transform.localPosition, Is.EqualTo(slot));
            if (variant == "interrupt") playback.Dispose();
            else if (variant == "handoff")
            {
                using var successor = (IDisposable)type.GetMethod("CreateSuccessor").Invoke(playback, null);
                type.GetMethod("Begin").Invoke(successor, new object[] { (string)graph["id"], caster, target, roster });
                playback.Dispose();
                Assert.That(Body(target).forceRenderingOff, Is.True, "The previous action cannot release the successor's hold.");
            }
            else
            {
                Tick(1.14f);
                Assert.That(Body(target).forceRenderingOff, Is.EqualTo(variant == "end"), "Only the matching STOP releases the hold.");
                Tick(10f);
            }
            Assert.That(Body(target).forceRenderingOff, Is.False);
            Assert.That(Body(alreadyHidden).forceRenderingOff, Is.True);
            Assert.That(target.transform.localPosition, Is.EqualTo(slot));
        }

        [TestCase("c1076", "diene", 9, 12, .25f, 0f)]
        [TestCase("c1087", "furious", 22, 23, .2f, .2f)]
        [TestCase("c1088", "vivian", 16, 0, .2f, .2f)]
        public void SourceWithoutSelfLayersHideBothFormationsExceptTheCaster(
            string hero, string modelName, int guid, int stopGuid, float fadeIn, float fadeOut)
        {
            var graph = JObject.Parse(File.ReadAllText($"Assets/Game/Modules/CharacterPresentation/Res/Characters/{hero}/Configs/stagept/c_{modelName}_skill_03.graph.json"));
            var source = graph["source"];
            var nodes = source["entities"].ToDictionary(n => (int)n["guid"]);
            Assert.That((int)nodes[guid]["blend_target"], Is.EqualTo(55));
            var links = new JArray(new JObject { ["from"] = 900, ["to"] = guid, ["when"] = 1 },
                new JObject { ["from"] = 900, ["to"] = 901, ["when"] = 1, ["delay"] = 6000 });
            var cues = new JArray(nodes[guid], new JObject { ["guid"] = 900, ["etty"] = "ANI", ["ani"] = "skill1" },
                new JObject { ["guid"] = 901, ["etty"] = "IDLE" });
            float releaseAt = (float)nodes[guid]["duration"] / 1000f;
            if (stopGuid != 0)
            {
                cues.Add(nodes[stopGuid]);
                links.Add(new JObject { ["from"] = 900, ["to"] = stopGuid, ["when"] = 1, ["delay"] = 700 });
                releaseAt = .7f;
            }
            source["entrypoint"] = new JObject { ["start"] = 900 };
            source["entities"] = cues;
            source["connections"] = links;
            graph["assets"] = new JArray();
            var text = new TextAsset(graph.ToString()); assets.Add(text);
            var model = ReadModel("ras");
            Component Body() => Create(model.json, model.atlas, model.page);
            var caster = Body(); var ally = Body(); var opponent = Body();
            var recipients = Array.CreateInstance(ActorType, 1); recipients.SetValue(opponent, 0);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform, new[] { text }, Array.CreateInstance(assetType, 0), null, null, null);
            type.GetMethod("Begin").Invoke(playback, new object[] { (string)graph["id"], caster, opponent, recipients });
            void Tick(float seconds) => type.GetMethod("Tick").Invoke(playback, new object[] { seconds });
            Renderer Renderer(Component actor) => Player(actor).Root.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            float Alpha(Component actor)
            {
                Renderer(actor).GetPropertyBlock(block);
                return block.HasFloat("_SkillBodyOpacity") ? block.GetFloat("_SkillBodyOpacity") : 1f;
            }
            Tick(fadeIn / 2f);
            Assert.That(Alpha(ally), Is.EqualTo(.5f).Within(.002f));
            Assert.That(Alpha(opponent), Is.EqualTo(.5f).Within(.002f));
            Assert.That(Alpha(caster), Is.EqualTo(1f));
            Tick(fadeIn / 2f + .001f);
            Assert.That(Renderer(ally).forceRenderingOff && Renderer(opponent).forceRenderingOff, Is.True);
            Assert.That(Renderer(caster).forceRenderingOff, Is.False);
            Tick(releaseAt - fadeIn - .001f + fadeOut / 2f + .00001f);
            Assert.That(Alpha(ally), Is.EqualTo(fadeOut == 0f ? 1f : .5f).Within(.002f));
            Tick(fadeOut / 2f + .01f);
            Assert.That(Alpha(opponent), Is.EqualTo(1f));
            Assert.That(Renderer(ally).forceRenderingOff, Is.False);
        }

        [TestCase("c1070", "krau", 34, 3.5f, .2f, 0f)]
        [TestCase("c1073", "kaweric", 26, 2.166f, .16f, .16f)]
        public void TimedAnonymousTargetHideFadesOnlyRecipientsThenReleasesBeforeTheActionEnds(
            string hero, string modelName, int guid, float duration, float fadeIn, float fadeOut)
        {
            var graph = JObject.Parse(File.ReadAllText($"Assets/Game/Modules/CharacterPresentation/Res/Characters/{hero}/Configs/stagept/c_{modelName}_skill_03.graph.json"));
            var source = graph["source"];
            var hide = source["entities"].Single(node => (int)node["guid"] == guid);
            Assert.That((int)hide["blend_target"], Is.EqualTo(66));
            source["entrypoint"] = new JObject { ["start"] = 900 };
            source["entities"] = new JArray(hide, new JObject { ["guid"] = 900, ["etty"] = "ANI", ["ani"] = "skill1" },
                new JObject { ["guid"] = 901, ["etty"] = "IDLE" });
            source["connections"] = new JArray(new JObject { ["from"] = 900, ["to"] = guid, ["when"] = 1 },
                new JObject { ["from"] = 900, ["to"] = 901, ["when"] = 1, ["delay"] = 6000 });
            graph["assets"] = new JArray();
            var text = new TextAsset(graph.ToString()); assets.Add(text);
            var model = ReadModel("ras");
            var caster = Create(model.json, model.atlas, model.page);
            var target = Create(model.json, model.atlas, model.page);
            var recipients = Array.CreateInstance(ActorType, 1); recipients.SetValue(target, 0);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform, new[] { text },
                Array.CreateInstance(assetType, 0), null, null, null);
            type.GetMethod("Begin").Invoke(playback, new object[] { (string)graph["id"], caster, target, recipients });
            void Tick(float delta) => type.GetMethod("Tick").Invoke(playback, new object[] { delta });
            var body = Player(target).Root.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            float Opacity() { body.GetPropertyBlock(block); return block.GetFloat("_SkillBodyOpacity"); }
            Tick(fadeIn / 2f);
            Assert.That(Opacity(), Is.EqualTo(.5f).Within(.001f));
            Tick(duration - fadeIn / 2f - .01f);
            Assert.That(body.forceRenderingOff, Is.True);
            Assert.That(Player(caster).Root.GetComponent<Renderer>().forceRenderingOff, Is.False);
            Tick(.01f + fadeOut / 2f + .00001f);
            Assert.That(body.forceRenderingOff, Is.False);
            Assert.That(Opacity(), Is.EqualTo(fadeOut == 0f ? 1f : .5f).Within(.002f));
            Tick(fadeOut / 2f + .01f);
            Assert.That(Opacity(), Is.EqualTo(1f).Within(.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MuiSourceFadesOnlyAcceptedTargetBodiesAndRestoresOtherOpacity(bool interrupt)
        {
            const string file = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1044/Configs/stagept/c_mui_skill_03.graph.json";
            var graph = JObject.Parse(File.ReadAllText(file));
            var source = graph["source"];
            var nodes = source["entities"].ToDictionary(node => (int)node["guid"]);
            Assert.That((int)nodes[7]["fadeIn"], Is.EqualTo(200));
            source["entities"] = new JArray(nodes[1], nodes[2], nodes[7], nodes[21], nodes[28]);
            source["connections"] = new JArray(((JArray)source["connections"]).Single(edge => (int)edge["from"] == 1 && (int)edge["to"] == 7),
                new JObject { ["from"] = 1, ["to"] = 2 },
                new JObject { ["from"] = 1, ["to"] = 21, ["when"] = 1, ["delay"] = 500 },
                new JObject { ["from"] = 1, ["to"] = 28, ["when"] = 1, ["delay"] = 800 });
            graph["assets"] = new JArray();
            var text = new TextAsset(graph.ToString()); assets.Add(text);
            var model = ReadModel("mui");
            Component Unit() => Create(model.json, model.atlas, model.page);
            var caster = Unit(); var target = Unit();
            var recipients = Array.CreateInstance(ActorType, 1); recipients.SetValue(target, 0);
            var body = Player(target).Root.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            block.SetFloat("_PresentationOpacity", .4f);
            body.SetPropertyBlock(block);
            var type = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentation", true);
            var assetType = ActorType.Assembly.GetType("AstralOath.Client.BattleScene.BattleSkillPresentationAsset", true);
            using var playback = (IDisposable)Activator.CreateInstance(type, root.transform, new[] { text },
                Array.CreateInstance(assetType, 0), null, null, null);
            type.GetMethod("Begin").Invoke(playback, new object[] { (string)graph["id"], caster, target, recipients });
            void Tick(float delta) => type.GetMethod("Tick").Invoke(playback, new object[] { delta });
            Tick(.1f);
            body.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_SkillBodyOpacity"), Is.EqualTo(.5f).Within(.001f), "Halfway through the source fade.");
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(.4f));
            Assert.That(body.forceRenderingOff, Is.False);
            Tick(.11f);
            Assert.That(body.forceRenderingOff, Is.True);
            Assert.That(Player(caster).Root.GetComponent<Renderer>().forceRenderingOff, Is.False);
            if (interrupt) playback.Dispose(); else Tick(.3f);
            body.GetPropertyBlock(block);
            Assert.That(body.forceRenderingOff, Is.False);
            Assert.That(block.GetFloat("_SkillBodyOpacity"), Is.EqualTo(1f));
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(.4f));
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
        private static void EnterBattleIdle(Component actor, ISkeletonPlayer player)
        {
            Invoke(actor, "SetTurnReady", true);
            player.Tick(player.Duration);
            Invoke(actor, "Update");
            Assert.That(player.CurrentAnimation, Is.EqualTo("b_idle"));
            // Start the recovery fixture from the steady battle loop, independent of entry blending.
            player.Play("b_idle", true);
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
