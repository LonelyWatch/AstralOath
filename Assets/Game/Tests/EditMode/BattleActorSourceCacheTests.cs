using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class BattleActorSourceCacheTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type ActorType = Type.GetType(
            "AstralOath.Client.BattleScene.BattlePreviewActor, AstralOath.Client.BattleScene", true);
        private readonly List<Component> actors = new List<Component>();
        private GameObject root;
        private TextAsset ras, slime, atlas;
        private Texture2D page;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Actor source cache regression");
            root.SetActive(false);
            string json = SkeletonPlayerTests.Json("2.1.25").Replace("\"move\"", "\"idle\"");
            ras = new TextAsset(json) { name = "Ras source" };
            slime = new TextAsset(json) { name = "Slime source" };
            atlas = new TextAsset(SkeletonPlayerTests.Atlas);
            page = new Texture2D(16, 16);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var actor in actors) Disable(actor);
            actors.Clear();
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(ras);
            UnityEngine.Object.DestroyImmediate(slime);
            UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(page);
        }

        [Test]
        public void TwoRasAndFourSlimesShareTwoParsedSourcesButKeepIndependentPoseAndMaterials()
        {
            var roster = new[] { Create(ras), Create(ras), Create(slime), Create(slime), Create(slime), Create(slime) };
            foreach (var actor in roster) Enable(actor);
            Assert.That(Data(Player(roster[1])), Is.SameAs(Data(Player(roster[0]))),
                "Repeated heroes must not parse their complete bone and animation data again.");
            for (int i = 3; i < roster.Length; i++)
                Assert.That(Data(Player(roster[i])), Is.SameAs(Data(Player(roster[2]))),
                    "Four slime skins share one immutable skeleton source, not four parses.");
            Assert.That(Data(Player(roster[2])), Is.Not.SameAs(Data(Player(roster[0]))));
            Assert.That(SourceCount(), Is.EqualTo(2));

            var first = Player(roster[0]);
            var second = Player(roster[1]);
            first.Tick(.75f); second.Tick(.25f);
            Assert.That(first.Time, Is.EqualTo(.75f).Within(.001f));
            Assert.That(second.Time, Is.EqualTo(.25f).Within(.001f));
            var firstMesh = first.Root.GetComponent<MeshFilter>().sharedMesh;
            var secondMesh = second.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(firstMesh, Is.Not.SameAs(secondMesh));
            Assert.That(firstMesh.bounds.center.x, Is.EqualTo(.075f).Within(.001f));
            Assert.That(secondMesh.bounds.center.x, Is.EqualTo(.025f).Within(.001f));
            Assert.That(first.Root.GetComponent<MeshRenderer>().sharedMaterial,
                Is.Not.SameAs(second.Root.GetComponent<MeshRenderer>().sharedMaterial));
        }

        [Test]
        public void LastOwnerReleaseEvictsSourceAndReactivationParsesFreshData()
        {
            var first = Create(ras); var second = Create(ras);
            Enable(first); Enable(second);
            object previous = Data(Player(first));
            Disable(first);
            Assert.That(SourceCount(), Is.EqualTo(1));
            Player(second).Tick(.25f);
            Assert.That(Player(second).Time, Is.EqualTo(.25f).Within(.001f));
            Disable(second);
            Assert.That(SourceCount(), Is.Zero, "Disabled actors must not retain a static asset cache.");
            Assert.That(page != null, Is.True, "The cache borrows imported textures.");
            Enable(first);
            Assert.That(Data(Player(first)), Is.Not.SameAs(previous));
        }

        [Test]
        public void AtlasAndPageIdentityParticipateInTheSourceKey()
        {
            var anotherAtlas = new TextAsset(SkeletonPlayerTests.Atlas);
            var anotherPage = new Texture2D(16, 16);
            try
            {
                var first = Create(ras); var changedAtlas = Create(ras); var changedPage = Create(ras);
                ActorType.GetField("atlas", Private).SetValue(changedAtlas, anotherAtlas);
                ActorType.GetField("page", Private).SetValue(changedPage, anotherPage);
                Enable(first); Enable(changedAtlas); Enable(changedPage);
                Assert.That(Data(Player(first)), Is.Not.SameAs(Data(Player(changedAtlas))));
                Assert.That(Data(Player(first)), Is.Not.SameAs(Data(Player(changedPage))));
                Assert.That(SourceCount(), Is.EqualTo(3));
                Disable(changedAtlas); Disable(changedPage);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anotherAtlas);
                UnityEngine.Object.DestroyImmediate(anotherPage);
            }
        }

        [Test]
        public void SeparateScenesKeepIndependentSourceLifetimes()
        {
            var foreignScene = EditorSceneManager.NewPreviewScene();
            var foreignRoot = new GameObject("Independent battle owner");
            foreignRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(foreignRoot, foreignScene);
            Component foreign = null;
            try
            {
                var first = Create(ras);
                foreign = Create(ras);
                foreign.gameObject.SetActive(false);
                foreign.transform.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(foreign.gameObject, foreignScene);
                foreign.transform.SetParent(foreignRoot.transform, false);
                Enable(first); Enable(foreign);
                Assert.That(Data(Player(first)), Is.Not.SameAs(Data(Player(foreign))));
                Assert.That(SourceCount(), Is.EqualTo(2));
                Disable(foreign);
                Assert.That(SourceCount(), Is.EqualTo(1));
            }
            finally
            {
                if (foreign) { Disable(foreign); actors.Remove(foreign); }
                UnityEngine.Object.DestroyImmediate(foreignRoot);
                EditorSceneManager.ClosePreviewScene(foreignScene);
            }
        }

        [Test]
        public void InvalidSkeletonDoesNotRetainAnEntryOrPreventAValidRetry()
        {
            var invalid = new TextAsset("{\"skeleton\":{\"spine\":\"unsupported\"}}");
            try
            {
                var actor = Create(invalid);
                Assert.Throws<TargetInvocationException>(() => Enable(actor));
                Assert.That(Player(actor), Is.Null);
                Assert.That(SourceCount(), Is.Zero);
                ActorType.GetField("skeleton", Private).SetValue(actor, ras);
                Enable(actor);
                Assert.That(Player(actor), Is.Not.Null);
                Assert.That(SourceCount(), Is.EqualTo(1));
                Disable(actor);
            }
            finally { UnityEngine.Object.DestroyImmediate(invalid); }
        }

        [Test]
        public void RejectedSkinReleasesThePreparedSourceAndThePartialPlayer()
        {
            var actor = Create(ras);
            ActorType.GetField("skin", Private).SetValue(actor, "missing-skin");
            Assert.Throws<TargetInvocationException>(() => Enable(actor));
            Assert.That(Player(actor), Is.Null);
            Assert.That(SourceCount(), Is.Zero);
            Assert.That(actor.transform.childCount, Is.Zero);
            ActorType.GetField("skin", Private).SetValue(actor, "default");
            Enable(actor);
            Assert.That(SourceCount(), Is.EqualTo(1));
        }

        [Test]
        public void SubsystemResetKeepsLiveOldPlayersValidAndOldReleaseCannotEvictNewEntry()
        {
            var oldActor = Create(ras); Enable(oldActor);
            var oldPlayer = Player(oldActor);
            object oldData = Data(oldPlayer);
            var reset = ActorType.GetMethod("ResetSharedSources", Static);
            Assert.That(reset, Is.Not.Null, "Disabled domain reload needs a SubsystemRegistration reset path.");
            reset.Invoke(null, null);
            Assert.That(SourceCount(), Is.Zero);
            oldPlayer.Tick(.25f);
            Assert.That(oldPlayer.Time, Is.EqualTo(.25f).Within(.001f));
            var currentActor = Create(ras); Enable(currentActor);
            Assert.That(Data(Player(currentActor)), Is.Not.SameAs(oldData));
            Disable(oldActor);
            Assert.That(SourceCount(), Is.EqualTo(1), "Releasing an old lease must not remove the new epoch entry.");
            var follower = Create(ras); Enable(follower);
            Assert.That(Data(Player(follower)), Is.SameAs(Data(Player(currentActor))));
        }

        private Component Create(TextAsset source)
        {
            var gameObject = new GameObject(source.name);
            gameObject.transform.SetParent(root.transform, false);
            var actor = gameObject.AddComponent(ActorType);
            ActorType.GetField("skeleton", Private).SetValue(actor, source);
            ActorType.GetField("atlas", Private).SetValue(actor, atlas);
            ActorType.GetField("page", Private).SetValue(actor, page);
            ActorType.GetField("pageName", Private).SetValue(actor, "page.png");
            actors.Add(actor);
            return actor;
        }

        private static void Enable(Component actor) => ActorType.GetMethod("OnEnable", Private).Invoke(actor, null);
        private static void Disable(Component actor) => ActorType.GetMethod("OnDisable", Private).Invoke(actor, null);
        private static ISkeletonPlayer Player(Component actor) => (ISkeletonPlayer)ActorType.GetField("player", Private).GetValue(actor);
        private static int SourceCount()
        {
            var field = ActorType.GetField("SharedSources", Static);
            Assert.That(field, Is.Not.Null, "Source entries must be owned by active actors and removed when the last one releases.");
            return ((IDictionary)field.GetValue(null)).Count;
        }
        private static object Data(ISkeletonPlayer player)
        {
            var runtime = player.GetType().GetField("runtime", Private).GetValue(player);
            var skeleton = runtime.GetType().GetField("skeleton", Private).GetValue(runtime);
            return skeleton.GetType().GetProperty("Data").GetValue(skeleton);
        }
    }
}