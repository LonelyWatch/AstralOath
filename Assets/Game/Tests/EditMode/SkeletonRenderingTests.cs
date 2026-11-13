using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
namespace AstralOath.Client.Tests
{
    public sealed class SkeletonRenderingTests
    {
        private GameObject parent;
        private Texture2D texture;
        private ISkeletonPlayer player;
        [SetUp] public void SetUp() { parent = new GameObject("render-test"); texture = new Texture2D(16,16); }
        [TearDown] public void TearDown()
        { player?.Dispose(); Object.DestroyImmediate(parent); Object.DestroyImmediate(texture); }
        private Mesh Create(string slots, string attachments, bool pma = false, string animations = null)
        {
            string json = "{\"skeleton\":{\"hash\":\"synthetic-test\",\"spine\":\"3.8.99\"},\"bones\":[{\"name\":\"root\"}],\"slots\":[" + slots +
                "],\"skins\":[{\"name\":\"default\",\"attachments\":{" + attachments + "}}]" +
                (animations == null ? "" : ",\"animations\":" + animations) + "}";
            player = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(json, SkeletonPlayerTests.Atlas,
                new Dictionary<string,Texture2D>{{"page.png",texture}}, pma, 1), parent.transform);
            return player.Root.GetComponent<MeshFilter>().sharedMesh;
        }
        [Test] public void ClippingCutsRealTrianglesAndEndsAtDeclaredSlot()
        {
            var mesh = Create(
                "{\"name\":\"clip\",\"bone\":\"root\",\"attachment\":\"mask\"},{\"name\":\"body\",\"bone\":\"root\",\"attachment\":\"quad\"}",
                "\"clip\":{\"mask\":{\"type\":\"clipping\",\"end\":\"body\",\"vertexCount\":4,\"vertices\":[0,-2,0,2,2,2,2,-2]}},\"body\":{\"quad\":{\"width\":2,\"height\":2}}");
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.bounds.min.x, Is.EqualTo(0).Within(0.001));
            Assert.That(mesh.bounds.max.x, Is.EqualTo(1).Within(0.001));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(2).Within(0.001));
        }
        [Test] public void RepeatedMaterialStaysInDrawOrderAndTintHasSecondColor()
        {
            var mesh = Create(
                "{\"name\":\"a\",\"bone\":\"root\",\"attachment\":\"quad\"}," +
                "{\"name\":\"b\",\"bone\":\"root\",\"attachment\":\"quad\",\"blend\":\"additive\",\"dark\":\"ff0000\"}," +
                "{\"name\":\"c\",\"bone\":\"root\",\"attachment\":\"quad\"}",
                "\"a\":{\"quad\":{\"width\":2,\"height\":2}},\"b\":{\"quad\":{\"width\":2,\"height\":2}},\"c\":{\"quad\":{\"width\":2,\"height\":2}}", true);
            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(mesh.GetTriangles(0)[0], Is.EqualTo(0));
            Assert.That(mesh.GetTriangles(1)[0], Is.EqualTo(4));
            Assert.That(mesh.GetTriangles(2)[0], Is.EqualTo(8));
            var materials = player.Root.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(materials[0], Is.SameAs(materials[2]));
            Assert.That(materials[0].GetInt("_DstBlend"), Is.EqualTo((int)BlendMode.OneMinusSrcAlpha));
            Assert.That(materials[1].GetInt("_DstBlend"), Is.EqualTo((int)BlendMode.One));
            Assert.That(materials[0].GetFloat("_PremultipliedAlpha"), Is.EqualTo(1));
            Assert.That(materials[0].GetFloat("_PmaSrgb"), Is.EqualTo(texture.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0));
            var dark = new List<Vector4>(); mesh.GetUVs(1, dark);
            Assert.That(dark[4].x, Is.EqualTo(1)); Assert.That(dark[4].y, Is.Zero);
        }
        [Test] public void ShrinkingDrawSegmentsClearsTrailingMaterialsAndCanRestoreSourceOrder()
        {
            var mesh = Create(
                "{\"name\":\"a\",\"bone\":\"root\",\"attachment\":\"quad\"}," +
                "{\"name\":\"b\",\"bone\":\"root\",\"attachment\":\"quad\",\"blend\":\"additive\"}," +
                "{\"name\":\"c\",\"bone\":\"root\",\"attachment\":\"quad\"}",
                "\"a\":{\"quad\":{\"width\":2,\"height\":2}},\"b\":{\"quad\":{\"width\":2,\"height\":2}},\"c\":{\"quad\":{\"width\":2,\"height\":2}}",
                animations: @"{""one"":{""slots"":{""b"":{""attachment"":[{""name"":null}]},""c"":{""attachment"":[{""name"":null}]}}},
                    ""empty"":{""slots"":{""a"":{""attachment"":[{""name"":null}]},""b"":{""attachment"":[{""name"":null}]},""c"":{""attachment"":[{""name"":null}]}}}}");
            var renderer = player.Root.GetComponent<MeshRenderer>();
            var original = renderer.sharedMaterials;
            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(original.Length, Is.EqualTo(3));
            Assert.That(original[0], Is.SameAs(original[2]));

            player.Play("one", false);
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { original[0] }));
            Assert.That(mesh.GetTriangles(0)[0], Is.Zero);

            player.Play("empty", false);
            Assert.That(mesh.subMeshCount, Is.Zero);
            Assert.That(mesh.vertexCount, Is.Zero);
            Assert.That(renderer.sharedMaterials, Is.Empty, "An empty frame must not retain the previous material slots.");

            player.Stop();
            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(original));
            Assert.That(mesh.GetTriangles(0)[0], Is.EqualTo(0));
            Assert.That(mesh.GetTriangles(1)[0], Is.EqualTo(4));
            Assert.That(mesh.GetTriangles(2)[0], Is.EqualTo(8));
        }

        [TestCase(180)][TestCase(270)]
        public void UnsupportedRegionRotationFailsExplicitly(int degrees)
        {
            Assert.Throws<System.NotSupportedException>(() => { using var unsupported = new SkeletonPlayerFactory().Create(
                new SkeletonPlayerSource(SkeletonPlayerTests.Json("3.8.99"),
                    SkeletonPlayerTests.Atlas.Replace("rotate: false", "rotate: " + degrees),
                    new Dictionary<string,Texture2D>{{"page.png",texture}}), parent.transform); });
            Assert.That(parent.transform.childCount, Is.Zero);
        }
        [TestCase("multiply", BlendMode.DstColor, BlendMode.OneMinusSrcAlpha)]
        [TestCase("screen", BlendMode.One, BlendMode.OneMinusSrcColor)]
        public void UsesRequestedSlotBlend(string blend, BlendMode source, BlendMode destination)
        {
            Create("{\"name\":\"body\",\"bone\":\"root\",\"attachment\":\"quad\",\"blend\":\"" + blend + "\"}",
                "\"body\":{\"quad\":{\"width\":2,\"height\":2}}");
            var material = player.Root.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.GetInt("_SrcBlend"), Is.EqualTo((int)source));
            Assert.That(material.GetInt("_DstBlend"), Is.EqualTo((int)destination));
        }
    }
}
