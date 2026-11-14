using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattleSkillTimelineDurationTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string PreviewTypeName =
            "AstralOath.Client.BattleScene.Editor.SkillTimelinePreviewSession, AstralOath.Client.BattleScene.Editor";

        [TestCase("c1017", "c_acha_skill_02", 3.733f)]
        [TestCase("c1008", "c_aming_skill_03", 6.0666f)]
        public void ActionBoundaryUsesSourceChoreographyInsteadOfWorstCaseAmbientParticleLifetime(
            string heroId, string graphId, float expectedActionEnd)
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var graph = AssetDatabase.LoadAssetAtPath<TextAsset>(
                $"Assets/Game/Modules/CharacterPresentation/Res/Characters/{heroId}/Configs/stagept/{graphId}.graph.json");
            Assert.That(graph, Is.Not.Null);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { heroId, graphId, graph.text, null, false, true, true });
            var cues = (IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)>)
                type.GetProperty("Cues", Instance).GetValue(session);
            float end = (float)type.GetProperty("MainStageDuration", Instance).GetValue(session);
            TestContext.Progress.WriteLine($"DURATION {graphId}: actual={end:F4}, expected={expectedActionEnd:F4}");
            foreach (var cue in cues.OrderByDescending(cue => cue.At + cue.Duration).Take(8))
                TestContext.Progress.WriteLine($"TAIL {cue.Node["etty"]} {cue.Node["source"]} #{cue.Node["guid"]}: at={cue.At:F4}, duration={cue.Duration:F4}");
            Assert.That(end, Is.EqualTo(expectedActionEnd).Within(.015f),
                "The editor main range must not wait for ambient particle maxima after the authored return choreography.");
        }

        [Test]
        public void AmbientTailRemainsAvailableAndRewindsWithoutTruncatingTheRuntime()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1008", "c_aming_skill_03", null, null, false, true, true });
            float main = (float)type.GetProperty("MainDuration", Instance).GetValue(session);
            float full = (float)type.GetProperty("Duration", Instance).GetValue(session);
            Assert.That(main, Is.EqualTo(6.0666f).Within(.015f));
            Assert.That(full, Is.EqualTo(11.552f).Within(.015f), "Preserve the original full CFX simulation lifetime.");
            var world = (Transform)type.GetField("world", Instance).GetValue(session);
            var seek = type.GetMethod("Seek", Instance);
            int TailVertices()
            {
                var meshes = world.GetComponentsInChildren<MeshFilter>(true)
                    .Where(mesh => mesh.gameObject.name == "HUD particle bubble" && mesh.gameObject.activeInHierarchy)
                    .ToArray();
                Assert.That(meshes, Has.Length.GreaterThan(0));
                return meshes.Sum(mesh => mesh.sharedMesh.vertexCount);
            }
            seek.Invoke(session, new object[] { 7f });
            int vertices = TailVertices();
            Assert.That(vertices, Is.GreaterThan(0), "The full range still renders real ambient particles after the main end.");
            seek.Invoke(session, new object[] { full });
            Assert.That(world.GetComponentsInChildren<MeshFilter>(true)
                .Any(mesh => mesh.gameObject.name == "HUD particle bubble"), Is.False, "Full playback releases the expired CFX.");
            seek.Invoke(session, new object[] { 7f });
            Assert.That(TailVertices(), Is.EqualTo(vertices), "Scrubbing back into the tail rebuilds the same particle state.");
        }

        [Test]
        public void ExplicitGraphEffectCompletionStillDelaysItsDownstreamHitAndMainRange()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1008/Configs/stagept/c_aming_skill_03.graph.json");
            var graph = JObject.Parse(source.text);
            var nodes = (JArray)graph["source"]["entities"];
            var edges = (JArray)graph["source"]["connections"];
            nodes.Add(new JObject { ["guid"] = 900, ["etty"] = "EFFECT", ["source"] = "aming_skill_03_front", ["location"] = 1 });
            nodes.Add(new JObject { ["guid"] = 901, ["etty"] = "HIT" });
            edges.Add(new JObject { ["from"] = (int)graph["source"]["entrypoint"]["start"], ["to"] = 900, ["when"] = 1 });
            edges.Add(new JObject { ["from"] = 900, ["to"] = 901 });
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1008", "c_aming_skill_03", graph.ToString(), null, false, true, true });
            var hits = (IReadOnlyList<float>)type.GetProperty("HitTimes", Instance).GetValue(session);
            Assert.That(hits.Last(), Is.EqualTo(10f).Within(.001f));
            Assert.That((float)type.GetProperty("MainStageDuration", Instance).GetValue(session),
                Is.EqualTo(10.15f).Within(.001f), "A graph completion edge is an authored wait, not an optional ambient tail.");
            Assert.That(JObject.Parse(source.text)["source"]["entities"].OfType<JObject>().Any(node => (int?)node["guid"] == 900), Is.False);
        }

        [Test]
        public void MainPreviewRangeIncludesOriginalCutinWallClockHolds()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1005", "c_mercedes_skill_03", null, null, false, true, false });
            float stage = (float)type.GetProperty("MainStageDuration", Instance).GetValue(session);
            float wall = (float)type.GetProperty("MainDuration", Instance).GetValue(session);
            var cutins = (IReadOnlyList<(float At, float Duration)>)type.GetProperty("Cutins", Instance).GetValue(session);
            Assert.That(cutins.Count, Is.GreaterThan(0));
            Assert.That(wall, Is.EqualTo(stage + cutins.Where(hold => hold.At <= stage + .00001f).Sum(hold => hold.Duration)).Within(.0001f));
            Assert.That((float)type.GetMethod("WallToStageTime", Instance).Invoke(session, new object[] { wall }),
                Is.EqualTo(stage).Within(.0001f));
        }
        [Test]
        public void MainEffectRangeKeepsSpineAnimationsEmissionWindowsAndExplicitLifetimes()
        {
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("Load", Instance).Invoke(session,
                new object[] { "c1008", "c_aming_skill_03", null, null, false, true, true });
            var playback = type.GetField("playback", Instance).GetValue(session);
            var runtime = playback.GetType();
            float Main(string key) => (float)runtime.GetMethod("EditorEffectDuration", Instance)
                .Invoke(playback, new object[] { key });
            Assert.That(Main("aming_skill_03_front"), Is.EqualTo(3.1f).Within(.001f),
                "Keep the original 3.1s Spine layer and 3s emission, excluding only the 7s particle drift.");
            Assert.That(Main("aming_skill_03_back"), Is.EqualTo(3.2f).Within(.001f),
                "Pure Spine model-timeline effects remain part of the main preview.");
            var declared = JObject.Parse("{'format':'particle','source':'aming_skill_03_patis','lifeTime':12000}");
            Assert.That((float)runtime.GetMethod("EditorPrimitiveDuration", Instance)
                .Invoke(playback, new object[] { declared }), Is.EqualTo(12f));
        }

        [Test]
        public void CorvusThirdSkillRendersAllOriginalParticleEmittersAfterTheCutinAndRewinds()
        {
            const string effectPath = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1012/Effects/effect/corvus_skill_03_pati1.particle.json";
            var definition = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(effectPath).text);
            int expectedEmitters = definition["emitters"].OfType<JObject>().Count(emitter => (bool?)emitter["disable"] != true);
            Assert.That(expectedEmitters, Is.EqualTo(6));
            Assert.That(definition["emitters"].Count(emitter => ((JArray)emitter["colorBlends"]).Count > 1), Is.EqualTo(5));
            var type = Type.GetType(PreviewTypeName, throwOnError: true);
            using var session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
            type.GetMethod("LoadScenario", Instance).Invoke(session,
                new object[] { "c1012", "c_corvus_skill_03", null, null, false, false, false, true, false });
            var seekStage = type.GetMethod("SeekStage", Instance);
            var world = (Transform)type.GetField("world", Instance).GetValue(session);
            MeshFilter[] SourceMeshes() => world.GetComponentsInChildren<MeshFilter>(true)
                .Where(mesh => mesh.gameObject.name == "HUD particle was_pati" && mesh.gameObject.activeInHierarchy)
                .ToArray();
            // Native CFX starts at stage 0; this primitive starts at 1.85s and its last emitter at +0.4s.
            // SeekStage includes the authored Cut-in hold rather than assuming wall time equals source time.
            seekStage.Invoke(session, new object[] { 2.4f });
            type.GetMethod("Render", Instance).Invoke(session, null);
            var meshes = SourceMeshes();
            Assert.That(meshes, Has.Length.EqualTo(expectedEmitters));
            foreach (var mesh in meshes)
            {
                Assert.That(mesh.sharedMesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.sharedMesh.colors.Any(color => color.a > .001f), Is.True,
                    "The source emitter must contain visible vertices, not just an empty placeholder mesh.");
            }
            var counts = meshes.Select(mesh => mesh.sharedMesh.vertexCount).ToArray();
            var colors = meshes.Select(mesh => mesh.sharedMesh.colors).ToArray();
            Assert.That(((IReadOnlyCollection<string>)type.GetProperty("Diagnostics", Instance).GetValue(session))
                .Any(message => message.Contains("Unordered HUD particle color key")), Is.False);
            float wall = (float)type.GetProperty("Time", Instance).GetValue(session);
            Assert.That(wall, Is.GreaterThan(2.4f), "The sample must include the actual Cut-in wall-clock suspension.");
            TestContext.Progress.WriteLine($"CORVUS_S3_PARTICLES stage=2.4s wall={wall:F4}s emitters={meshes.Length} vertices={counts.Sum()}");
            type.GetMethod("Seek", Instance).Invoke(session,
                new object[] { (float)type.GetProperty("Duration", Instance).GetValue(session) });
            seekStage.Invoke(session, new object[] { 2.4f });
            var replay = SourceMeshes();
            Assert.That(replay.Select(mesh => mesh.sharedMesh.vertexCount), Is.EqualTo(counts));
            for (int index = 0; index < replay.Length; index++)
                Assert.That(replay[index].sharedMesh.colors, Is.EqualTo(colors[index]));
        }
    }
}