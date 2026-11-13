using System;
using System.Collections.Generic;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace AstralOath.Client.Tests
{
    public sealed class SkeletonBoneSkeletonPositionTests
    {
        private GameObject owner;
        private Texture2D texture;
        private ISkeletonPlayer player;
        [SetUp] public void SetUp()
        {
            owner = new GameObject("Skeleton space test");
            owner.transform.SetPositionAndRotation(new Vector3(40, -20, 0), Quaternion.Euler(0, 0, 25));
            owner.transform.localScale = new Vector3(2, 3, 1);
            texture = new Texture2D(16, 16);
        }
        [TearDown] public void TearDown()
        {
            player?.Dispose(); player = null;
            UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(texture);
        }
        private void Create(string version, float scale)
        {
            var data = JObject.Parse(SkeletonPlayerTests.Json(version));
            data["bones"] = JArray.Parse("[{\"name\":\"root\",\"x\":3,\"y\":5,\"rotation\":90,\"scaleX\":2,\"scaleY\":3},{\"name\":\"top\",\"parent\":\"root\",\"x\":4,\"y\":7}]");
            data["slots"][0]["bone"] = "top";
            player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(data.ToString(), SkeletonPlayerTests.Atlas,
                new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: scale), owner.transform);
        }
        private bool Read(string name, out Vector2 position)
        {
            var method = typeof(ISkeletonPlayer).GetMethod("TryGetBoneSkeletonPosition");
            Assert.That(method, Is.Not.Null, "A parent-relative bone pose cannot drive a rendered attachment anchor.");
            object[] args = { name, Vector2.zero };
            bool found = (bool)method.Invoke(player, args);
            position = (Vector2)args[1]; return found;
        }
        private void AssertTop(Vector2 expected)
        {
            Assert.That(Read("top", out var position), Is.True);
            Assert.That(Vector2.Distance(position, expected), Is.LessThan(.0001f));
            // The attachment origin and the sampled anchor must be in the same mesh coordinate space.
            var center = (Vector2)player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            Assert.That(Vector2.Distance(position, center), Is.LessThan(.0001f));
        }
        [TestCase("2.1.25", 1f)][TestCase("3.8.99", 1f)]
        [TestCase("2.1.25", .01f)][TestCase("3.8.99", .01f)]
        public void ParentTranslationRotationScaleAndSourceUnitsMatchTheRenderedAttachment(string version, float scale)
        {
            Create(version, scale);
            AssertTop(new Vector2(-18, 13) * scale);
            Assert.That(player.TryGetBonePose("top", out var local), Is.True);
            Assert.That(local.LocalPosition, Is.EqualTo(new Vector2(4, 7) * scale), "Camera local-pose contract must remain unchanged.");
            player.Play("move", false); player.Tick(.25f);
            AssertTop(new Vector2(-15.5f, 13) * scale);
            player.Seek(.75f); AssertTop(new Vector2(-10.5f, 13) * scale);
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void RuntimeFlipIsIncludedButUnityRootTransformIsNot(string version)
        {
            Create(version, .1f);
            player.SetFlip(true, true);
            AssertTop(new Vector2(18, -13) * .1f);
            player.Root.transform.localScale = new Vector3(-4, 5, 1);
            player.Root.transform.localPosition = new Vector3(20, 10, 0);
            AssertTop(new Vector2(18, -13) * .1f);
        }
        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void MissingBonesReturnFalseAndDisposedPlayersRejectReads(string version)
        {
            Create(version, 1f);
            Assert.That(Read("missing", out var missing), Is.False); Assert.That(missing, Is.EqualTo(Vector2.zero));
            Assert.That(Read(null, out _), Is.False);
            player.Dispose();
            var exception = Assert.Throws<TargetInvocationException>(() => Read("root", out _));
            Assert.That(exception.InnerException, Is.TypeOf<ObjectDisposedException>());
        }
    }
}
