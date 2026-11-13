using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonHeaderConsistencyTests
    {
        private Texture2D texture;
        private GameObject root;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(16, 16);
            root = new GameObject("Skeleton Header Contract");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [TestCase("2.1.25", "3.8.99", false)]
        [TestCase("2.1.25", "3.8.99", true)]
        [TestCase("2.1.25", "4.2.0", false)]
        [TestCase("2.1.25", "4.2.0", true)]
        [TestCase("3.8.99", "2.1.27", false)]
        [TestCase("3.8.99", "2.1.27", true)]
        [TestCase("3.8.99", "4.2.0", false)]
        [TestCase("3.8.99", "4.2.0", true)]
        public void ARepeatedRootHeaderCannotOverrideTheSelectedRuntimeOrPoisonItsCache(
            string firstVersion, string finalVersion, bool create)
        {
            var source = Source(WithFinalHeader(firstVersion, finalVersion));
            var factory = new SkeletonPlayerFactory();
            ISkeletonPlayer player = null;
            void Execute()
            {
                if (create) player = factory.Create(source, root.transform);
                else factory.Prepare(source);
            }
            try
            {
                for (int retry = 0; retry < 2; retry++)
                {
                    Assert.Throws<InvalidDataException>(Execute,
                        "The full parser uses the last root header; it must agree with the probed dispatch version.");
                    Assert.That(ParsedData(source), Is.Null, "A failed parse must never publish immutable cache data.");
                    Assert.That(root.transform.childCount, Is.Zero, "Rejection must not leave a player or renderer.");
                    Assert.That(texture != null, Is.True, "Rejected data must not destroy borrowed atlas textures.");
                }
            }
            finally { player?.Dispose(); }
        }

        [TestCase("2.1.25", false)]
        [TestCase("2.1.25", true)]
        [TestCase("3.8.99", false)]
        [TestCase("3.8.99", true)]
        public void MatchingCompleteAndProbedVersionsStillCreateReusableData(string version, bool create)
        {
            var source = Source(WithFinalHeader(version, version));
            var factory = new SkeletonPlayerFactory();
            ISkeletonPlayer player = null;
            try
            {
                if (create) player = factory.Create(source, root.transform);
                else factory.Prepare(source);
                var parsed = ParsedData(source);
                Assert.That(parsed, Is.Not.Null);
                factory.Prepare(source);
                Assert.That(ParsedData(source), Is.SameAs(parsed));
            }
            finally { player?.Dispose(); }
        }

        [TestCase("[]", false)]
        [TestCase("[]", true)]
        [TestCase("{\"skeleton\":[]}", false)]
        [TestCase("{\"skeleton\":[]}", true)]
        [TestCase("{\"skeleton\":{\"spine\":2.1}}", false)]
        [TestCase("{\"skeleton\":{\"spine\":2.1}}", true)]
        [TestCase("{\"other\":{\"skeleton\":{\"spine\":\"2.1.27\"}}}", false)]
        [TestCase("{\"other\":{\"skeleton\":{\"spine\":\"2.1.27\"}}}", true)]
        public void CompleteFactoryEntrypointsRejectInvalidRootOrHeaderTypes(string json, bool create)
        {
            var source = Source(json);
            var factory = new SkeletonPlayerFactory();
            ISkeletonPlayer player = null;
            try
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    if (create) player = factory.Create(source, root.transform);
                    else factory.Prepare(source);
                });
                Assert.That(ParsedData(source), Is.Null);
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { player?.Dispose(); }
        }

        private SkeletonPlayerSource Source(string json) => new SkeletonPlayerSource(json, SkeletonPlayerTests.Atlas,
            new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: 1);

        private static string WithFinalHeader(string firstVersion, string finalVersion)
        {
            string json = SkeletonPlayerTests.Json(firstVersion);
            return json.Substring(0, json.Length - 1) + ",\"skeleton\":{\"hash\":\"duplicate-header-test\",\"spine\":\"" + finalVersion + "\"}}";
        }

        private static object ParsedData(SkeletonPlayerSource source) => typeof(SkeletonPlayerSource)
            .GetField("ParsedData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
    }
}