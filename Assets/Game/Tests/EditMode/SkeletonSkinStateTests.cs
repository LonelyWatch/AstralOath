using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonSkinStateTests
    {
        private const string Fixture = @"{
          'skeleton':{'hash':'skin-state-test','spine':'2.1.25'},'bones':[{'name':'root'}],
          'slots':[{'name':'body','bone':'root','attachment':'setup'}],
          'skins':{
            'default':{'body':{'setup':{'path':'quad','width':2,'height':2},'alternate':{'path':'quad','width':4,'height':4,'x':3}}},
            'other':{'body':{'setup':{'path':'quad','width':2,'height':2,'x':20},'alternate':{'path':'quad','width':4,'height':4,'x':30}}}},
          'animations':{
            'move':{'bones':{'root':{'translate':[{'time':0},{'time':1,'x':10}]}},'slots':{'body':{'attachment':[{'time':0.2,'name':'alternate'}]}}},
            'hold':{'bones':{'root':{'translate':[{'time':0,'x':20},{'time':1,'x':20}]}}},'empty':{},
            'hide':{'slots':{'body':{'attachment':[{'time':0,'name':null},{'time':1,'name':null}]}}}}}";
        private GameObject parent;
        private Texture2D texture;
        private readonly List<ISkeletonPlayer> players = new List<ISkeletonPlayer>();
        [SetUp] public void SetUp() { parent = new GameObject("skin-state"); texture = new Texture2D(16, 16); }
        [TearDown] public void TearDown()
        {
            foreach (var player in players) player.Dispose();
            players.Clear(); UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(texture);
        }
        private ISkeletonPlayer Create(JObject json = null)
        {
            var player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource((json ?? JObject.Parse(Fixture)).ToString(),
                SkeletonPlayerTests.Atlas, new Dictionary<string, Texture2D> { ["page.png"] = texture }, scale: 1), parent.transform);
            players.Add(player); return player;
        }
        private static Vector3[] Vertices(ISkeletonPlayer player) => player.Root.GetComponent<MeshFilter>().sharedMesh.vertices;
        private static void AssertGeometry(Vector3[] expected, ISkeletonPlayer actual)
        {
            var vertices = Vertices(actual);
            Assert.That(vertices.Length, Is.EqualTo(expected.Length), "Current attachment topology must survive skin selection.");
            for (int i = 0; i < vertices.Length; i++)
                Assert.That(Vector3.Distance(vertices[i], expected[i]), Is.LessThan(.0001f), "vertex " + i);
        }
        private static float RootX(ISkeletonPlayer player)
        {
            Assert.That(player.TryGetBonePose("root", out var pose), Is.True); return pose.LocalPosition.x;
        }
        private static object FirstSlot(ISkeletonPlayer player)
        {
            var runtime = player.GetType().GetField("runtime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            var skeleton = runtime.GetType().GetField("skeleton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            return ((IList)skeleton.GetType().GetProperty("Slots").GetValue(skeleton))[0];
        }
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);

        [TestCase(false)][TestCase(true)]
        public void SelectingTheEffectiveDefaultSkinKeepsAnimatedAttachments(bool explicitlySelected)
        {
            var player = Create(); if (explicitlySelected) player.SetSkin("default");
            player.Play("move", false); player.Tick(.5f); var expected = Vertices(player);
            player.SetSkin("default"); AssertGeometry(expected, player);
        }
        [TestCase(false)][TestCase(true)]
        public void DifferentSkinRetainsTheCurrentLogicalAttachmentDuringMix(bool mix)
        {
            var player = Create(); var reference = Create(); player.SetSkin("default"); reference.SetSkin("other");
            foreach (var item in new[] { player, reference })
            {
                item.Play("move", false); item.Tick(.5f);
                if (mix) { item.Play("hold", false, 1); item.Tick(.1f); }
            }
            player.SetSkin("other"); AssertGeometry(Vertices(reference), player);
            player.Tick(.1f); reference.Tick(.1f); AssertGeometry(Vertices(reference), player);
        }
        [Test]
        public void SkinSelectionDoesNotReblendAPropertyAbsentFromThePreviousClip()
        {
            var player = Create(); var reference = Create(); player.SetSkin("default"); reference.SetSkin("other");
            foreach (var item in new[] { player, reference })
            { item.Play("empty", false); item.Play("hold", false, 1); item.Tick(.25f); }
            float before = RootX(player); player.SetSkin("other");
            Assert.That(RootX(player), Is.EqualTo(before).Within(.0001));
            player.Tick(.1f); reference.Tick(.1f); AssertGeometry(Vertices(reference), player);
        }
        [TestCase("setup")][TestCase("alternate")][TestCase("hide")][TestCase("inherited-hide")]
        public void MissingSkinEntriesRetainTheirLogicalKeyAndExplicitHides(string mode)
        {
            var json = JObject.Parse(Fixture); json["skins"]["default"] = new JObject();
            var player = Create(json); var reference = Create(json); player.SetSkin("default"); reference.SetSkin("other");
            if (mode != "setup") foreach (var item in new[] { player, reference })
            {
                item.Play(mode == "alternate" ? "move" : "hide", false); item.Tick(.5f);
                if (mode == "inherited-hide") { item.Play("hold", false, 1); item.Tick(.1f); }
            }
            player.SetSkin("other"); AssertGeometry(Vertices(reference), player);
            player.SetSkin("default"); Assert.That(Vertices(player), Is.Empty, "Missing entries must remove old skin parts.");
        }
        [TestCase("move")][TestCase("hide")]
        public void ASourceWithoutDefaultSkinKeepsTheAnimatedKeyWhenSelectingItsFirstSkin(string animation)
        {
            var json = JObject.Parse(Fixture); ((JObject)json["skins"]).Remove("default");
            var player = Create(json); var reference = Create(json); reference.SetSkin("other");
            CollectionAssert.AreEqual(new[] { "other" }, player.Skins, "Internal lookup must not add a selectable skin.");
            foreach (var item in new[] { player, reference }) { item.Play(animation, false); item.Tick(.5f); }
            player.SetSkin("other"); AssertGeometry(Vertices(reference), player);
        }
        [Test]
        public void InternalFallbackSkinIsNotASelectablePublicSkin()
        {
            var json = JObject.Parse(Fixture); ((JObject)json["skins"]).Remove("default");
            var player = Create(json);
            Assert.Throws<ArgumentException>(() => player.SetSkin("<runtime-default>"));
            Assert.Throws<ArgumentException>(() => player.SetSkin("unknown"));
        }
        [TestCase(true, false, false)][TestCase(true, true, false)]
        [TestCase(false, false, false)][TestCase(false, true, false)]
        [TestCase(true, false, true)][TestCase(false, false, true)]
        public void SkinSelectionPreservesSharedDeformationAndEvaluatesNewMeshesWithoutChangingMix(bool shared, bool mix, bool completed)
        {
            var json = JObject.Parse(Fixture);
            const string mesh = @"{'path':'quad','type':'mesh','vertices':[-1,-1,-1,1,1,1,1,-1],
                'uvs':[0,0,0,1,1,1,1,0],'triangles':[0,1,2,2,3,0]}";
            json["skins"]["default"]["body"] = new JObject { ["setup"] = JObject.Parse(mesh) };
            json["skins"]["other"]["body"] = shared ? new JObject() : new JObject { ["setup"] = JObject.Parse(mesh) };
            json["animations"]["move"]["slots"] = new JObject();
            var ffd = JObject.Parse(@"{'default':{'body':{'setup':[{'time':0,'vertices':[2,0,2,0,2,0,2,0]},
                {'time':1,'vertices':[4,0,4,0,4,0,4,0]}]}}}");
            if (!shared) ffd["other"] = JObject.Parse(@"{'body':{'setup':[{'time':0,'vertices':[10,0,10,0,10,0,10,0]},
                {'time':1,'vertices':[12,0,12,0,12,0,12,0]}]}}");
            json["animations"]["move"]["ffd"] = ffd;
            json["events"] = JObject.Parse(@"{'hit':{}}");
            json["animations"]["move"]["events"] = JArray.Parse(@"[{'time':0.25,'name':'hit'},{'time':0.5,'name':'hit'}]");
            var player = Create(json); var reference = Create(json); player.SetSkin("default"); reference.SetSkin("other");
            int events = 0; player.Event += value => { if (value.Kind == SkeletonPlaybackEventKind.Event) events++; };
            foreach (var item in new[] { player, reference })
            {
                if (mix) item.Play("hold", false);
                item.Play("move", false, mix ? 1 : 0);
                item.Tick(completed ? 1 : .25f);
                if (completed) item.Tick(.1f); // Retires the non-looping native track while keeping the last displayed pose.
                item.Pause(true);
            }
            var slot = FirstSlot(player); object attachment = Property(slot, "Attachment");
            float attachmentTime = (float)Property(slot, "AttachmentTime"); float bone = RootX(player);
            player.SetSkin("other"); AssertGeometry(Vertices(reference), player);
            Assert.That(RootX(player), Is.EqualTo(bone));
            if (shared)
            {
                Assert.That(Property(slot, "Attachment"), Is.SameAs(attachment));
                Assert.That((float)Property(slot, "AttachmentTime"), Is.EqualTo(attachmentTime));
            }
            Assert.That(events, Is.EqualTo(completed ? 2 : 1), "Skin selection must not dispatch old events.");
            player.Pause(false); reference.Pause(false); player.Tick(.25f); reference.Tick(.25f);
            AssertGeometry(Vertices(reference), player); Assert.That(events, Is.EqualTo(2), "Future event must still fire once.");
        }
    }
}
