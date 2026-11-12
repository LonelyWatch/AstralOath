using System;
using System.Collections.Generic;
using System.Reflection;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonSourceReuseTests
    {
        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void InstancesOfOneSourceShareParsedDataButKeepIndependentPlaybackAndDisposal(string version)
        {
            var texture = new Texture2D(16, 16);
            var parent = new GameObject("shared-source");
            ISkeletonPlayer first = null, second = null;
            try
            {
                var source = new SkeletonPlayerSource(SkeletonPlayerTests.Json(version), SkeletonPlayerTests.Atlas,
                    new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: 1);
                var factory = new SkeletonPlayerFactory();
                first = factory.Create(source, parent.transform);
                second = factory.Create(source, parent.transform);
                Assert.That(Data(second), Is.SameAs(Data(first)), "One immutable source must not parse all animation and mesh keys twice.");
                first.Play("move", false);
                first.Tick(.75f);
                second.Play("move", false);
                second.Tick(.25f);
                first.Dispose();
                second.Tick(.25f);
                Assert.That(second.Time, Is.EqualTo(.5f).Within(.001f));
                Assert.That(second.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(5f).Within(.001f));
                Assert.That(texture, Is.Not.Null);
            }
            finally
            {
                first?.Dispose(); second?.Dispose();
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void SharedSourceKeepsSkinFlipDeformationMixAndEventsLocal(string version)
        {
            var texture = new Texture2D(16, 16);
            var parent = new GameObject("independent-shared-state");
            ISkeletonPlayer first = null, second = null;
            try
            {
                var mesh = JObject.Parse(@"{'type':'mesh','path':'quad','vertices':[-1,-1,-1,1,1,1,1,-1],
                    'uvs':[0,0,0,1,1,1,1,0],'triangles':[0,1,2,2,3,0],'width':2,'height':2}");
                var otherMesh = (JObject)mesh.DeepClone();
                otherMesh["vertices"] = new JArray(19,-1,19,1,21,1,21,-1);
                var attachments = new JObject { ["body"] = new JObject { ["quad"] = mesh } };
                var other = new JObject { ["body"] = new JObject { ["quad"] = otherMesh } };
                var json = new JObject
                {
                    ["skeleton"] = new JObject { ["hash"] = "shared-state-test", ["spine"] = version },
                    ["bones"] = JArray.Parse("[{ 'name':'root' }]"),
                    ["slots"] = JArray.Parse("[{ 'name':'body', 'bone':'root', 'attachment':'quad' }]"),
                    ["events"] = JObject.Parse("{ 'hit': {} }"),
                    ["animations"] = JObject.Parse(@"{'move':{'events':[{'name':'hit','time':0.4}],
                        'bones':{'root':{'translate':[{'time':0},{'time':1,'x':10}]}}},
                        'hold':{'bones':{'root':{'translate':[{'time':0,'x':20},{'time':1,'x':20}]}}}}")
                };
                json["skins"] = version.StartsWith("2", StringComparison.Ordinal)
                    ? (JToken)new JObject { ["default"] = attachments, ["other"] = other }
                    : new JArray(new JObject { ["name"] = "default", ["attachments"] = attachments },
                        new JObject { ["name"] = "other", ["attachments"] = other });
                json["animations"]["move"][version.StartsWith("2", StringComparison.Ordinal) ? "ffd" : "deform"] =
                    JObject.Parse(@"{'default':{'body':{'quad':[{'time':0},{'time':1,'vertices':[4,0,4,0,4,0,4,0]}]}}}");
                var source = new SkeletonPlayerSource(json.ToString(), SkeletonPlayerTests.Atlas,
                    new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: 1);
                var factory = new SkeletonPlayerFactory();
                factory.Prepare(source);
                first = factory.Create(source, parent.transform);
                second = factory.Create(source, parent.transform);
                Assert.That(Data(second), Is.SameAs(Data(first)));
                int firstEvents = 0, secondEvents = 0;
                first.Event += item => { if (item.Name == "hit") firstEvents++; };
                second.Event += item => { if (item.Name == "hit") secondEvents++; };
                second.Play("move", false); second.Tick(.25f);
                var before = second.Root.GetComponent<MeshFilter>().sharedMesh.vertices;
                first.Play("move", false); first.Tick(.75f); first.SetSkin("other");
                first.SetFlip(true, true); first.Play("hold", false, .5f); first.Tick(.1f);
                Assert.That(firstEvents, Is.EqualTo(1));
                Assert.That(secondEvents, Is.Zero);
                Assert.That(second.Root.GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(before));
                first.Dispose(); first = null;
                second.Tick(.25f);
                Assert.That(secondEvents, Is.EqualTo(1));
                Assert.That(second.Time, Is.EqualTo(.5f).Within(.001f));
                Assert.That(second.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(7f).Within(.001f));
            }
            finally
            {
                first?.Dispose(); second?.Dispose();
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static object Data(ISkeletonPlayer player)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var runtime = player.GetType().GetField("runtime", flags).GetValue(player);
            var skeleton = runtime.GetType().GetField("skeleton", flags).GetValue(runtime);
            return skeleton.GetType().GetProperty("Data").GetValue(skeleton);
        }
    }
}
