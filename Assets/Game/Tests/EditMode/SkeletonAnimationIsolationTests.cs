using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonAnimationIsolationTests
    {
        private GameObject parent;
        private Texture2D page;
        private readonly List<ISkeletonPlayer> players = new List<ISkeletonPlayer>();
        private Dictionary<string, JObject> resources;

        [SetUp] public void SetUp()
        {
            parent = new GameObject("animation-isolation");
            page = new Texture2D(2, 2);
            var manifest = JObject.Parse(File.ReadAllText("Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json"));
            resources = ((JArray)manifest["entries"]).Cast<JObject>().ToDictionary(row => (string)row["address"], StringComparer.Ordinal);
        }

        [TearDown] public void TearDown()
        {
            foreach (var player in players) player.Dispose();
            players.Clear();
            UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(page);
        }

        private string Read(string address) => File.ReadAllText((string)resources[address]["path"]);
        private ISkeletonPlayer Create(string source)
        {
            var descriptor = JObject.Parse(Read("characterlab/source/" + source + ".scsp"));
            var textures = ((JArray)descriptor["pages"]).ToDictionary(item => (string)item["name"], _ => page, StringComparer.Ordinal);
            var player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(Read((string)descriptor["jsonAddress"]),
                Read((string)descriptor["atlasAddress"]), textures, (bool)descriptor["premultipliedAlpha"], (float)descriptor["scale"]), parent.transform);
            players.Add(player);
            return player;
        }

        [TestCase("ras", "idle", 0.516646f)]
        [TestCase("mercedes", "skill3", 1.98f)]
        public void RealModelSeekMatchesForwardPlaybackAttachmentsAndDeformation(string model, string animation, float time)
        {
            var player = Create("model/" + model);
            player.SetSkin("default");
            player.Play(animation, false);
            player.Tick(time);
            var expected = new Geometry(player);
            player.Seek(time);
            expected.AssertMatches(player, model + " " + animation + " Seek(" + time + ")");
        }

        [TestCase("ras")][TestCase("mercedes")][TestCase("angelica")]
        public void EveryRealModelZeroMixSwitchMatchesAnIndependentStart(string model)
        {
            var player = Create("model/" + model);
            var independent = Create("model/" + model);
            player.SetSkin("default"); independent.SetSkin("default");
            foreach (var next in player.Animations)
            {
                float sample = next.Duration * .31f;
                independent.Stop(); independent.Play(next.Name, false); independent.Tick(sample);
                var expected = new Geometry(independent);
                foreach (var previous in player.Animations)
                {
                    player.Stop(); player.Play(previous.Name, false); player.Tick(previous.Duration * .63f);
                    player.Play(next.Name, false, 0); player.Tick(sample);
                    expected.AssertMatches(player, model + " " + previous.Name + " -> " + next.Name);
                }
            }
        }

        [TestCase("c1005")][TestCase("c1001")]
        public void SeekAndZeroMixRestartKeepTheSelectedSkinAndFlip(string portrait)
        {
            var player = Create("portrait/" + portrait);
            player.SetSkin("normal"); player.SetFlip(true, false);
            string animation = player.Animations[0].Name;
            float time = player.Animations[0].Duration * .31f;
            player.Play(animation, false); player.Tick(time);
            var expected = new Geometry(player);
            player.Seek(time);
            expected.AssertMatches(player, portrait + " seek must preserve normal skin and flip");
            player.Play(animation, false, 0); player.Tick(time);
            expected.AssertMatches(player, portrait + " restart must preserve normal skin and flip");
        }

        [Test]
        public void RealRasSeekDoesNotReplayPresentationEventsOnTheNextTick()
        {
            var player = Create("model/ras");
            player.Play("skill2", false);
            int events = 0;
            player.Event += _ => events++;
            player.Seek(1.5f);
            Assert.That(events, Is.Zero);
            player.Tick(.001f);
            Assert.That(events, Is.Zero, "Seek must suppress skipped events and advance the event cursor.");
        }

        private ISkeletonPlayer CreateSynthetic(string version, bool hold = false, bool duplicateEvents = false)
        {
            string json = SkeletonPlayerTests.Json(version);
            if (hold) json = json.Replace("\"animations\":{", "\"animations\":{\"hold\":{\"bones\":{\"root\":{\"translate\":[{\"time\":0,\"x\":20},{\"time\":1,\"x\":20}]}}},");
            if (duplicateEvents) json = json.Replace("\"time\":0.5,\"name\":\"hit\"", "\"time\":0.5,\"name\":\"hit\"},{\"time\":0.5,\"name\":\"hit\"");
            var player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(json, SkeletonPlayerTests.Atlas,
                new Dictionary<string, Texture2D> { ["page.png"] = page }, scale: 1), parent.transform);
            players.Add(player);
            return player;
        }

        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void LoopSeekSuppressesPastEventsButKeepsFutureEventsAfterWrap(string version)
        {
            var player = CreateSynthetic(version);
            player.Play("move", true);
            int events = 0;
            player.Event += value => { if (value.Kind == SkeletonPlaybackEventKind.Event) events++; };
            player.Seek(1.75f);
            Assert.That(player.Time, Is.EqualTo(.75f).Within(.0001));
            Assert.That(events, Is.Zero);
            player.Tick(.1f); player.Tick(.4f);
            Assert.That(events, Is.Zero, "The skipped event must not replay on wrapping.");
            player.Tick(.3f);
            Assert.That(events, Is.EqualTo(1), "The next cycle's future event must still fire.");
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(5.5f).Within(.001));
        }

        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void SeekCancelsAnExplicitMixWithoutLosingTheCurrentClip(string version)
        {
            var player = CreateSynthetic(version, hold: true);
            player.Play("move", false); player.Tick(.25f);
            player.Play("hold", false, 1); player.Tick(.25f);
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(8.75f).Within(.001));
            player.Seek(.25f);
            Assert.That(player.CurrentAnimation, Is.EqualTo("hold"));
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(20).Within(.001));
        }

        [TestCase("2.1.25")][TestCase("3.8.99")]
        public void EventCallbackCanReplaceTheClipWithoutDispatchingOldPendingEvents(string version)
        {
            var player = CreateSynthetic(version, hold: true, duplicateEvents: true);
            int events = 0;
            player.Event += value =>
            {
                if (value.Kind != SkeletonPlaybackEventKind.Event) return;
                events++; player.Play("hold", false, 0);
            };
            player.Play("move", false); player.Tick(.6f);
            Assert.That(events, Is.EqualTo(1));
            Assert.That(player.CurrentAnimation, Is.EqualTo("hold"));
            Assert.That(player.Root.GetComponent<MeshFilter>().sharedMesh.bounds.center.x, Is.EqualTo(20).Within(.001));
        }

        [Test]
        public void ReapplyingRasSkinMidAnimationKeepsItsActiveAttachmentsAndDeformation()
        {
            var player = Create("model/ras");
            player.SetSkin("default"); player.Play("idle", false); player.Tick(.516646f);
            var expected = new Geometry(player);
            player.SetSkin("default");
            expected.AssertMatches(player, "same-skin selection must not restore setup attachments");
        }

        [TestCase("normal", "angry")][TestCase("angry", "normal")]
        public void ChangingPortraitSkinUsesOnlyTheNewExpressionAtTheCurrentPose(string from, string to)
        {
            var player = Create("portrait/c1005"); var independent = Create("portrait/c1005");
            string animation = player.Animations[0].Name;
            float time = player.Animations[0].Duration * .31f;
            player.SetSkin(from); player.Play(animation, false); player.Tick(time);
            independent.SetSkin(to); independent.Play(animation, false); independent.Tick(time);
            player.SetSkin(to);
            new Geometry(independent).AssertMatches(player, from + " -> " + to + " must remove stale expression parts and restore new ones");
            Assert.That(player.Time, Is.EqualTo(time).Within(.0001));
        }

        [Test]
        public void ReapplyingSkinDuringAPositiveMixKeepsItsWeightAndFutureProgress()
        {
            var player = Create("model/mercedes"); var independent = Create("model/mercedes");
            foreach (var item in new[] { player, independent })
            { item.SetSkin("default"); item.Play("idle", false); item.Tick(.6f); item.Play("skill3", false, .4f); item.Tick(.1f); }
            player.SetSkin("default");
            new Geometry(independent).AssertMatches(player, "same skin must preserve active mix");
            player.Tick(.1f); independent.Tick(.1f);
            new Geometry(independent).AssertMatches(player, "skin selection must not cancel future mix progress");
        }

        [TestCase("normal", "angry")][TestCase("angry", "normal")]
        public void ChangingExpressionDuringAPositiveMixPreservesItsProgress(string from, string to)
        {
            var player = Create("portrait/c1005"); var independent = Create("portrait/c1005");
            player.SetSkin(from); independent.SetSkin(to);
            foreach (var item in new[] { player, independent })
            { item.Play("idle", false); item.Tick(.6f); item.Play("idle2", false, .4f); item.Tick(.1f); }
            player.SetSkin(to);
            new Geometry(independent).AssertMatches(player, from + " -> " + to + " during mix");
            player.Tick(.1f); independent.Tick(.1f);
            new Geometry(independent).AssertMatches(player, "new expression must retain future mix progress");
        }

        private sealed class Geometry
        {
            private readonly Vector3[] vertices;
            private readonly Vector2[] uv;
            private readonly int[][] triangles;
            internal Geometry(ISkeletonPlayer player)
            {
                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                vertices = mesh.vertices; uv = mesh.uv;
                triangles = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            }
            internal void AssertMatches(ISkeletonPlayer player, string context)
            {
                var mesh = player.Root.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(vertices.Length), context + " attachment topology");
                Assert.That(mesh.subMeshCount, Is.EqualTo(triangles.Length), context + " draw order/material segments");
                var actual = mesh.vertices; var actualUv = mesh.uv;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Assert.That(Vector3.Distance(actual[i], vertices[i]), Is.LessThan(0.0001f), context + " vertex " + i);
                    Assert.That(Vector2.Distance(actualUv[i], uv[i]), Is.LessThan(0.00001f), context + " attachment UV " + i);
                }
                for (int i = 0; i < triangles.Length; i++) CollectionAssert.AreEqual(triangles[i], mesh.GetTriangles(i), context + " triangles " + i);
            }
        }
    }
}
