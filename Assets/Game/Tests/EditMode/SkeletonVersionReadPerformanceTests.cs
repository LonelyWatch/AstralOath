using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonVersionReadPerformanceTests
    {
        private static readonly Func<SkeletonPlayerSource, string> ReadVersion =
            (Func<SkeletonPlayerSource, string>)Delegate.CreateDelegate(typeof(Func<SkeletonPlayerSource, string>),
                typeof(SkeletonPlayerFactory).GetMethod("ReadVersion", BindingFlags.NonPublic | BindingFlags.Static));

        [Test]
        public void VersionProbeDoesNotAllocateUnrelatedMeshDocument()
        {
            // Real imported Spine documents put their skeleton header before large mesh timelines.
            var json = new StringBuilder("{\"skeleton\":{\"spine\":\"2.1.27\"},\"ignored\":[");
            for (int i = 0; i < 20000; i++)
            {
                if (i != 0) json.Append(',');
                json.Append("{\"vertices\":[0,1,2,3,4,5,6,7],\"spine\":\"4.2\"}");
            }
            json.Append("]}");
            var source = Source(json.ToString());
            ReadVersion(Source("{\"skeleton\":{\"spine\":\"3.8.99\"}}"));

            // Unity's Mono may return zero for GC.GetAllocatedBytesForCurrentThread.
            // Use the same current-thread GC.Alloc recorder as Unity Test Framework instead.
            Action positiveControl = () => GC.KeepAlive(new byte[1024 * 1024]);
            CountAllocations(positiveControl); // Warm the recorder wrapper and delegate paths.
            int control = CountAllocations(positiveControl);
            if (control == 0)
                Assert.Inconclusive("GC.Alloc did not observe the 1 MiB positive control; allocation results are unavailable.");
            string version = null;
            Action read = () => version = ReadVersion(source);
            int allocations = CountAllocations(read);
            Assert.That(version, Is.EqualTo("2.1.27"));
            Assert.That(allocations, Is.LessThan(128),
                "Version dispatch must not materialize unrelated mesh arrays. GC allocation count=" + allocations);
        }

        [TestCase("{\"skeleton\":{\"spine\":\"2.1.27\"}}", "2.1.27")]
        [TestCase("{\"ignored\":[{\"spine\":\"4.2\"}],\"skeleton\":{\"spine\":\"3.8.99\"}}", "3.8.99")]
        [TestCase("{\"spine\":\"4.2\",\"skeleton\":{\"nested\":{\"spine\":\"4.2\"},\"spine\":\"2.1.27\"}}", "2.1.27")]
        [TestCase("{\"skel\\u0065ton\":{\"sp\\u0069ne\":\"3.8.99\"}}", "3.8.99")]
        public void ReadsActualHeaderRegardlessOfUnrelatedFieldsOrEscapedKeys(string json, string expected)
        {
            var source = Source(json);
            Assert.That(ReadVersion(source), Is.EqualTo(expected));
            Assert.That(ReadVersion(source), Is.EqualTo(expected), "The cached value must match the validated token.");
        }

        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("true")]
        [TestCase("27")]
        [TestCase("\"2.1.27\"")]
        [TestCase("{}")]
        [TestCase("{\"skeleton\":null}")]
        [TestCase("{\"skeleton\":[]}")]
        [TestCase("{\"skeleton\":\"2.1.27\"}")]
        [TestCase("{\"skeleton\":false}")]
        [TestCase("{\"skeleton\":{}}")]
        [TestCase("{\"skeleton\":{\"spine\":null}}")]
        [TestCase("{\"skeleton\":{\"spine\":2.1}}")]
        [TestCase("{\"skeleton\":{\"spine\":true}}")]
        [TestCase("{\"skeleton\":{\"spine\":[]}}")]
        [TestCase("{\"skeleton\":{\"spine\":{}}}")]
        [TestCase("{\"skeleton\":{\"nested\":{\"spine\":\"2.1.27\"}}}")]
        [TestCase("{\"other\":{\"skeleton\":{\"spine\":\"2.1.27\"}}}")]
        public void RejectsMissingOrIncorrectlyTypedVersionHeaders(string json)
        {
            Assert.Throws<ArgumentException>(() => ReadVersion(Source(json)));
        }

        private static SkeletonPlayerSource Source(string json) =>
            new SkeletonPlayerSource(json, SkeletonPlayerTests.Atlas, new Dictionary<string, Texture2D>());

        private static int CountAllocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            if (!recorder.isValid) Assert.Inconclusive("GC.Alloc recorder is unavailable.");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { action(); }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }
    }
}