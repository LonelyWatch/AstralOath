using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralOath.Tests
{
    public sealed class BattleStageContactTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private sealed class Shot : IDisposable
        {
            private readonly Type type = Type.GetType(
                "AstralOath.Client.BattleScene.Editor.SkillTimelinePreviewSession, AstralOath.Client.BattleScene.Editor", true);
            private readonly IDisposable session;
            internal readonly Camera Camera;
            internal readonly Transform World;
            internal readonly Component[] Targets;
            internal readonly Component Caster;

            internal Shot(string id, string graph, bool right, int width, bool all = false)
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("镜头验收需要图形设备。");
                session = (IDisposable)Activator.CreateInstance(type, nonPublic: true);
                type.GetMethod("Load", Members).Invoke(session, new object[] { id, graph, null, "c1001", right, all, false });
                Camera = (Camera)type.GetField("camera", Members).GetValue(session);
                World = (Transform)type.GetField("world", Members).GetValue(session);
                Targets = (Component[])type.GetField("recipients", Members).GetValue(session);
                Caster = (Component)type.GetField("caster", Members).GetValue(session);
                Camera.aspect = width / 540f;
            }
            internal void Seek(float t) => type.GetMethod("Seek", Members).Invoke(session, new object[] { t });
            internal Vector3 View(Vector3 p) => Camera.WorldToViewportPoint(p);
            internal MeshRenderer Effect(string name) => World.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == name);
            internal Vector3[] AtlasCorners(Transform model, Vector2 atlas, Rect region)
            {
                var mesh = model.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices;
                var uv = mesh.uv;
                return Enumerable.Range(0, uv.Length).Where(i =>
                {
                    float x = uv[i].x * atlas.x, y = (1f - uv[i].y) * atlas.y;
                    return x >= region.xMin - .01f && x <= region.xMax + .01f &&
                           y >= region.yMin - .01f && y <= region.yMax + .01f;
                }).Select(i => View(model.TransformPoint(vertices[i]))).ToArray();
            }
            public void Dispose() => session?.Dispose();
        }

        [TestCase(false, 720), TestCase(true, 720), TestCase(false, 960), TestCase(true, 960)]
        [TestCase(false, 1260), TestCase(true, 1260)]
        public void KayronOpeningKeepsBothRockPlatesBeyondTheViewport(bool right, int width)
        {
            using var shot = new Shot("c1023", "c_kylon_skill_03", right, width);
            foreach (float time in new[] { .65f, 1.25f, 1.60f })
            {
                shot.Seek(time);
                var plate = shot.Effect("kylon_sk3_1eff_b").transform;
                // The two rotated regions occupy x 2..180 and 182..334 of the 1282-wide source atlas.
                foreach (var rectangle in new[] { new Rect(2, 2, 178, 665), new Rect(182, 2, 152, 444) })
                {
                    var corners = shot.AtlasCorners(plate, new Vector2(1282, 1005), rectangle);
                    Assert.That(corners.Length, Is.EqualTo(4));
                    Assert.That(corners.Min(p => p.x), Is.LessThanOrEqualTo(.001f));
                    Assert.That(corners.Max(p => p.x), Is.GreaterThanOrEqualTo(.999f));
                }
            }
        }

        [TestCase(false, 720), TestCase(true, 720), TestCase(false, 960), TestCase(true, 960)]
        [TestCase(false, 1260), TestCase(true, 1260)]
        public void KayronGraspStaysOnTheVictimDuringTheLift(bool right, int width)
        {
            using var shot = new Shot("c1023", "c_kylon_skill_03", right, width);
            foreach (float time in new[] { 1.737f, 1.82f, 2.4f, 3.1f })
            {
                shot.Seek(time);
                var flare = shot.View(shot.Effect("kylon_sk3_2eff_target").transform.position);
                var actor = shot.Targets.Single();
                var arguments = new object[] { "target", Vector3.zero };
                Assert.That(actor.GetType().GetMethod("TryGetPresentationAnchor", Members).Invoke(actor, arguments), Is.True);
                var torso = shot.View((Vector3)arguments[1]);
                Assert.That(Vector2.Distance(flare, torso), Is.LessThan(.012f), $"Grasp and target at {time} s");
                Assert.That((torso.x - .5f) * (right ? -1f : 1f), Is.GreaterThan(0f), "The held target stays across from Kayron.");
            }
        }

        [TestCase(false, 720), TestCase(true, 720), TestCase(false, 960), TestCase(true, 960)]
        [TestCase(false, 1260), TestCase(true, 1260)]
        public void TenebriaMoonPairMeetsAtTheTargetFormation(bool right, int width)
        {
            using var shot = new Shot("c1050", "c_tenebria_skill_03", right, width, true);
            foreach (float time in new[] { 4.0f, 5.3f })
            {
                shot.Seek(time);
                var line = shot.View(shot.Effect("tenebria_skill_03_hori_light").transform.position);
                Assert.That(line.y, Is.EqualTo(.5f).Within(.035f), "The two mirrored moons meet at screen center.");
                Assert.That(shot.Targets.Length, Is.GreaterThanOrEqualTo(3));
                var casterMesh = (Renderer)shot.Caster.GetType().GetProperty("PresentationRenderer", Members).GetValue(shot.Caster);
                Assert.That(casterMesh.enabled && !casterMesh.forceRenderingOff, Is.False, "Only recipients appear in the moon shot.");
                if (time == 4f)
                {
                    // Blue discs are centered at y=.18 and y=.82 in the 26:29.50 reference frame.
                    var moons = shot.World.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "tenebria_skill_03_up_moon").ToArray();
                    Assert.That(moons.Length, Is.EqualTo(2));
                    foreach (var moon in moons)
                    {
                        var corners = shot.AtlasCorners(moon.transform, new Vector2(698, 969), new Rect(2, 699, 210, 210));
                        Assert.That(corners.Length, Is.EqualTo(4));
                        Assert.That(Mathf.Abs(corners.Average(p => p.y) - line.y), Is.EqualTo(.32f).Within(.04f));
                        Assert.That(corners.All(p => p.y > .01f && p.y < .99f), Is.True);
                    }
                }
                foreach (var actor in shot.Targets)
                    Assert.That(shot.View(((Bounds)actor.GetType().GetProperty("Bounds").GetValue(actor)).center).y, Is.EqualTo(line.y).Within(.06f),
                        $"Target {actor.name} left the horizon at {time} s");
            }
        }
    }
}
