using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class SkeletonCurveSemanticsTests
    {
        // Raw SCSP curve block 856524 is FE FF. This view change is a pose cut,
        // not a translation between the separate front/back character drawings.
        [TestCase(.7f)]
        [TestCase(1f)]
        [TestCase(1.081f)]
        [TestCase(1.36f)]
        public void MercedesSkill3HoldsTheViewUntilItsAuthoredPoseCut(float time)
        {
            WithMercedes(player =>
            {
                player.Play("skill3", false);
                player.Seek(time);
                Assert.That(player.TryGetBonePose("quarter_view", out var pose), Is.True);
                Assert.That(pose.LocalPosition.x, Is.EqualTo(.0026f).Within(.00001f),
                    "SCSP 0xFFFE must hold the preceding key, not move the body away from its book.");
            });
        }

        [Test] public void MercedesSkill3ChangesViewAtTheSourceKeyAndNotBefore()
        {
            WithMercedes(player =>
            {
                player.Play("skill3", false);
                player.Seek(1.3666000366210938f);
                Assert.That(player.TryGetBonePose("quarter_view", out var pose), Is.True);
                Assert.That(pose.LocalPosition.x, Is.EqualTo(5.3607f).Within(.00001f));
                player.Seek(1.466599941253662f);
                Assert.That(player.TryGetBonePose("quarter_view", out pose), Is.True);
                Assert.That(pose.LocalPosition.x, Is.EqualTo(.0026f).Within(.00001f));
            });
        }

        private static void WithMercedes(System.Action<ISkeletonPlayer> assertion)
        {
            var parent = new GameObject("SCSP curve semantics");
            var texture = new Texture2D(2, 2);
            try
            {
                var manifest = JObject.Parse(File.ReadAllText("Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json"));
                var paths = ((JArray)manifest["entries"]).ToDictionary(e => (string)e["address"], e => (string)e["path"]);
                var descriptor = JObject.Parse(File.ReadAllText(paths["characterlab/source/model/mercedes.scsp"]));
                var pages = ((JArray)descriptor["pages"]).ToDictionary(p => (string)p["name"], _ => texture);
                var source = new SkeletonPlayerSource(File.ReadAllText(paths[(string)descriptor["jsonAddress"]]),
                    File.ReadAllText(paths[(string)descriptor["atlasAddress"]]), pages,
                    (bool)descriptor["premultipliedAlpha"], (float)descriptor["scale"]);
                using (var player = new SkeletonPlayerFactory().Create(source, parent.transform)) assertion(player);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(parent);
            }
        }
    }
}
