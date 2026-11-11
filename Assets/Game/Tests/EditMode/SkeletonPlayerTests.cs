using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEngine;
namespace AstralOath.Client.Tests
{
    public sealed class SkeletonPlayerTests
    {
        public const string Atlas = "page.png\nsize: 16,16\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\nquad\n  rotate: false\n  xy: 0,0\n  size: 16,16\n  orig: 16,16\n  offset: 0,0\n  index: -1\n";
        public static string Json(string version)
        {
            string attachments = "{\"body\":{\"quad\":{\"type\":\"region\",\"width\":2,\"height\":2}}}";
            string skins = version.StartsWith("2.") ? "{\"default\":" + attachments + "}" : "[{\"name\":\"default\",\"attachments\":" + attachments + "}]";
            return "{\"skeleton\":{\"hash\":\"synthetic-test\",\"spine\":\"" + version + "\"},\"bones\":[{\"name\":\"root\"}],\"slots\":[{\"name\":\"body\",\"bone\":\"root\",\"attachment\":\"quad\"}],\"skins\":" + skins + ",\"events\":{\"hit\":{}},\"animations\":{\"move\":{\"bones\":{\"root\":{\"translate\":[{\"time\":0},{\"time\":1,\"x\":10}]}},\"events\":[{\"time\":0.5,\"name\":\"hit\"}]}}}";
        }
        private Texture2D texture;
        private GameObject parent;
        private readonly List<ISkeletonPlayer> players = new List<ISkeletonPlayer>();
        [SetUp] public void SetUp() { texture = new Texture2D(16,16); parent = new GameObject("test-parent"); }
        [TearDown] public void TearDown()
        {
            foreach (var player in players) player.Dispose();
            players.Clear(); UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(texture);
        }
        private ISkeletonPlayer Create(string version, string json = null, string atlas = null)
        {
            var player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(json ?? Json(version), atlas ?? Atlas,
                new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: 1), parent.transform);
            players.Add(player); return player;
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void CreatesRealRegionAndAdvancesBoneGeometry(string version)
        {
            var player = Create(version);
            Assert.That(player.Version, Is.EqualTo(version));
            Assert.That(player.Animations.Single().Name, Is.EqualTo("move"));
            Assert.That(player.Skins, Does.Contain("default"));
            var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.bounds.center.x, Is.EqualTo(0).Within(0.001));
            player.Play("move", false); player.Tick(0.25f);
            Assert.That(mesh.bounds.center.x, Is.EqualTo(2.5f).Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void PauseSeekAndFlipPreserveIndependentInstances(string version)
        {
            var first = Create(version); var second = Create(version);
            first.Play("move", true); first.Pause(true); first.Tick(0.5f); Assert.That(first.Time, Is.Zero);
            int events = 0; first.Event += _ => events++;
            first.Seek(0.75f);
            Assert.That(first.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(7.5f).Within(0.001));
            Assert.That(events, Is.Zero, "Scrubbing must not replay presentation events");
            first.SetFlip(true, false);
            Assert.That(first.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(-7.5f).Within(0.001));
            Assert.That(second.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.Zero.Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void EmitsTimelineEventOnceAndDisposesOnlyOwnedObjects(string version)
        {
            var player = Create(version); int hits = 0;
            player.Event += e => { if (e.Kind == SkeletonPlaybackEventKind.Event && e.Name == "hit") hits++; };
            player.Play("move", false); player.Tick(0.6f); player.Tick(0.1f); Assert.That(hits, Is.EqualTo(1));
            var root = player.Root; var mesh = root.GetComponent<MeshFilter>().sharedMesh;
            var material = root.GetComponent<MeshRenderer>().sharedMaterial;
            player.Dispose(); player.Dispose();
            Assert.That(root == null && mesh == null && material == null, Is.True);
            Assert.That(texture != null && parent != null, Is.True);
            Assert.Throws<ObjectDisposedException>(() => player.Tick(1));
        }
        [TestCase("2.1.25", false)][TestCase("2.1.25", true)]
        [TestCase("3.8.99", false)][TestCase("3.8.99", true)]
        public void MeshAndWeightedMeshUseNativeBoneTransforms(string version, bool weighted)
        {
            string type = weighted && version.StartsWith("2") ? "skinnedmesh" : "mesh";
            string vertices = weighted ? "1,0,-1,-1,1,1,0,-1,1,1,1,0,1,1,1,1,0,1,-1,1" : "-1,-1,-1,1,1,1,1,-1";
            string attachment = "\"type\":\"" + type + "\",\"vertices\":[" + vertices +
                "],\"uvs\":[0,0,0,1,1,1,1,0],\"triangles\":[0,1,2,2,3,0],\"width\":2,\"height\":2";
            var player = Create(version, Json(version).Replace("\"type\":\"region\",\"width\":2,\"height\":2", attachment));
            player.Play("move", false); player.Tick(0.5f);
            var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.bounds.center.x, Is.EqualTo(5).Within(0.001));
            player.SetFlip(true, false);
            Assert.That(mesh.bounds.center.x, Is.EqualTo(-5).Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void RotatedTrimmedAtlasUsesCorrectGeometryAndUvExtent(string version)
        {
            var atlas = Atlas.Replace("size: 16,16", "size: 16,32").Replace("rotate: false", "rotate: true")
                .Replace("  size: 16,32", "  size: 2,6").Replace("orig: 16,16", "orig: 4,8").Replace("offset: 0,0", "offset: 1,1");
            var player = Create(version, Json(version).Replace("\"width\":2,\"height\":2", "\"width\":4,\"height\":8"), atlas);
            var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.bounds.size.x, Is.EqualTo(2).Within(0.001));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(6).Within(0.001));
            Assert.That(mesh.uv.Max(v=>v.x)-mesh.uv.Min(v=>v.x), Is.EqualTo(6f/16).Within(0.001));
            Assert.That(mesh.uv.Max(v=>v.y)-mesh.uv.Min(v=>v.y), Is.EqualTo(2f/32).Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void AnimationMixBlendsActualBonePositions(string version)
        {
            string json = Json(version).Replace("\"animations\":{", "\"animations\":{\"hold\":{\"bones\":{\"root\":{\"translate\":[{\"time\":0,\"x\":20},{\"time\":1,\"x\":20}]}}},");
            var player = Create(version, json);
            player.Play("move", false); player.Tick(0.5f); player.Play("hold", false, 1); player.Tick(0.5f);
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(15).Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void EventHandlerCanStopWithoutLeakingOldPendingEvents(string version)
        {
            string json = Json(version).Replace("\"time\":0.5,\"name\":\"hit\"", "\"time\":0.5,\"name\":\"hit\"},{\"time\":0.5,\"name\":\"hit\"");
            var player = Create(version, json); int events = 0;
            player.Event += _ => { events++; player.Stop(); };
            player.Play("move", false); player.Tick(0.6f);
            Assert.That(events, Is.EqualTo(1)); Assert.That(player.CurrentAnimation, Is.Null);
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.Zero.Within(0.001));
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void BonePoseReadsAnimatedLocalValuesWithoutApplyingGlobalFlip(string version)
        {
            string json = Json(version).Replace("\"name\":\"root\"}", "\"name\":\"root\",\"rotation\":30,\"scaleX\":2,\"scaleY\":3}");
            var player = Create(version, json); player.Play("move", false); player.Tick(0.25f); player.SetFlip(true, false);
            Assert.That(player.TryGetBonePose("root", out var pose), Is.True);
            Assert.That(pose.LocalPosition.x, Is.EqualTo(2.5f).Within(0.001));
            Assert.That(pose.LocalRotation, Is.EqualTo(30).Within(0.001));
            Assert.That(pose.LocalScale, Is.EqualTo(new Vector2(2,3)));
            Assert.That(player.TryGetBonePose("missing", out _), Is.False);
            player.Dispose();
            Assert.Throws<ObjectDisposedException>(() => player.TryGetBonePose("root", out _));
        }
        [Test] public void RejectsUnsupportedVersionAndRollsBackMissingPage()
        {
            Assert.Throws<NotSupportedException>(() => Create("4.2.0"));
            Assert.Throws<ArgumentException>(() => new SkeletonPlayerFactory().Create(
                new SkeletonPlayerSource(Json("3.8.99"), Atlas, new Dictionary<string,Texture2D>()), parent.transform));
            Assert.That(parent.transform.childCount, Is.Zero);
        }
    }
}
