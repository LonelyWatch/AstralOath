using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleShieldAuraTests
    {
        private const string AssetRoot = "Assets/Game/Modules/BattleScene/Res/Effects/Shield/stse_protect";
        private const string Assembly = ", AstralOath.Client.BattleScene";
        private const string Namespace = "AstralOath.Client.BattleScene.";

        [Test]
        public void BarrierRetainsNativeFillHighlightsAndAnimatedColors()
        {
            var document = JObject.Parse(Load<TextAsset>(".json").text);
            var slots = document["slots"].ToDictionary(slot => (string)slot["name"], StringComparer.Ordinal);
            CollectionAssert.AreEquivalent(new[] { "shield", "edge", "shield_f", "g1", "g2", "glow" }, slots.Keys);
            Assert.That((bool?)slots["shield"]["additive"] ?? false, Is.False);
            foreach (var name in new[] { "edge", "shield_f", "g1", "g2", "glow" })
                Assert.That((bool)slots[name]["additive"], Is.True, name);
            var animation = document["animations"]["animation"];
            Assert.That((string)animation["slots"]["edge"]["color"][0]["color"], Is.EqualTo("5EA4FFFF"));
            Assert.That(animation["ffd"]["default"]["glow"], Is.Not.Null);
        }

        [Test]
        public void ImportedAuraFollowsBodyAndStopsWithShieldOrVisibility()
        {
            var assetType = Type.GetType(Namespace + "BattleSkillPresentationAsset" + Assembly, true);
            var asset = Activator.CreateInstance(assetType);
            assetType.GetField("source").SetValue(asset, "effect/stse_protect.scsp");
            assetType.GetField("document").SetValue(asset, Load<TextAsset>(".json"));
            assetType.GetField("atlas").SetValue(asset, Load<TextAsset>(".atlas.txt"));
            assetType.GetField("pages").SetValue(asset, new[] { Load<Texture2D>(".png") });
            assetType.GetField("pageNames").SetValue(asset, new[] { "stse_protect.sct" });
            assetType.GetField("premultipliedAlpha").SetValue(asset, true);
            assetType.GetField("scale").SetValue(asset, .01f);

            var type = Type.GetType(Namespace + "BattleShieldAura" + Assembly, true);
            var definition = type.GetMethod("Prepare").Invoke(null, new[] { asset, Load<TextAsset>(".cfx.json") });
            var sync = type.GetMethod("Sync");
            var presentation = (IDisposable)Activator.CreateInstance(type, definition, null, null);
            var actor = new GameObject("Shielded test body");
            var replacement = new GameObject("Replacement test body");
            try
            {
                var body = actor.AddComponent<MeshRenderer>();
                body.sortingOrder = 52;
                sync.Invoke(presentation, new object[] { body, false, .1f });
                Assert.That(actor.transform.childCount, Is.Zero, "An unshielded actor should allocate no effect instance.");

                sync.Invoke(presentation, new object[] { body, true, 0f });
                var root = actor.transform.GetChild(0);
                var renderer = root.GetComponent<MeshRenderer>();
                Assert.That(root.gameObject.activeSelf, Is.True);
                var mesh = root.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.GreaterThan(20));
                Assert.That(mesh.subMeshCount, Is.EqualTo(2), "The ordinary shield fill and additive highlights need separate blending.");
                Assert.That(renderer.sortingOrder, Is.EqualTo(53));
                Assert.That(root.localScale, Is.EqualTo(Vector3.one * 1.5f));

                actor.transform.position = new Vector3(3f, 2f, 0f);
                actor.transform.localScale = new Vector3(-2f, 2f, 1f);
                Assert.That(Vector3.Distance(root.position, actor.transform.TransformPoint(0f, .2f, 0f)), Is.LessThan(.0001f));
                body.enabled = false;
                sync.Invoke(presentation, new object[] { body, true, .1f });
                Assert.That(root.gameObject.activeSelf, Is.False);
                body.enabled = true;
                sync.Invoke(presentation, new object[] { body, true, .1f });
                Assert.That(root.gameObject.activeSelf, Is.True);

                sync.Invoke(presentation, new object[] { body, false, .1f });
                Assert.That(root.gameObject.activeSelf, Is.False);
                sync.Invoke(presentation, new object[] { body, true, .1f });
                Assert.That(actor.transform.GetChild(0), Is.SameAs(root), "Shield reacquisition should reuse the player's mesh.");

                var newBody = replacement.AddComponent<MeshRenderer>();
                sync.Invoke(presentation, new object[] { newBody, true, .1f });
                Assert.That(actor.transform.childCount, Is.Zero);
                Assert.That(replacement.transform.childCount, Is.EqualTo(1));
                presentation.Dispose();
                Assert.That(replacement.transform.childCount, Is.Zero);
            }
            finally
            {
                presentation.Dispose();
                Object.DestroyImmediate(actor);
                Object.DestroyImmediate(replacement);
            }
        }

        private static T Load<T>(string suffix) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetRoot + suffix);
            Assert.That(asset, Is.Not.Null, AssetRoot + suffix);
            return asset;
        }
    }
}
