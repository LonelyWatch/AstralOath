using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Tests
{
    public sealed class BattlePresentationPreparationTests
    {
        [Test]
        public void FallbackPreparationBuildsDataWithoutPlayingAndSuccessorReusesIt()
        {
            const string module = "AstralOath.Client.BattleScene";
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = Type.GetType(module + ".BattleSkillPresentation, " + module, true);
            var assetType = Type.GetType(module + ".BattleSkillPresentationAsset, " + module, true);
            var actorType = Type.GetType(module + ".BattlePreviewActor, " + module, true);
            var root = new GameObject("Preparation only");
            root.SetActive(false);
            var actorObject = new GameObject("Unstarted actor");
            actorObject.transform.SetParent(root.transform, false);
            var actor = actorObject.AddComponent(actorType);
            var skeleton = new TextAsset(AstralOath.Client.Tests.SkeletonPlayerTests.Json("2.1.25"));
            var atlas = new TextAsset(AstralOath.Client.Tests.SkeletonPlayerTests.Atlas);
            var definition = new TextAsset("{\"primitive\":[{\"source\":\"hit\",\"format\":\"spine\"}]}");
            var texture = new Texture2D(16, 16);
            IDisposable playback = null, successor = null;
            try
            {
                actorType.GetField("skeleton", flags).SetValue(actor, skeleton);
                var hit = Activator.CreateInstance(assetType);
                assetType.GetField("source").SetValue(hit, "effect/hit_new_blunt_normal.cfx");
                assetType.GetField("document").SetValue(hit, definition);
                var spine = Activator.CreateInstance(assetType);
                assetType.GetField("source").SetValue(spine, "effect/hit.scsp");
                assetType.GetField("document").SetValue(spine, skeleton);
                assetType.GetField("atlas").SetValue(spine, atlas);
                assetType.GetField("pageNames").SetValue(spine, new[] { "page.png" });
                assetType.GetField("pages").SetValue(spine, new[] { texture });
                assetType.GetField("scale").SetValue(spine, 1f);
                var assets = Array.CreateInstance(assetType, 2);
                assets.SetValue(hit, 0); assets.SetValue(spine, 1);
                playback = (IDisposable)Activator.CreateInstance(type, new object[] { root.transform, Array.Empty<TextAsset>(), assets, null, null, null });
                type.GetMethod("Prepare").Invoke(playback, new object[] { actor, "no-source-graph" });
                var cache = type.GetField("cache", flags).GetValue(playback);
                var sources = (System.Collections.IDictionary)cache.GetType().GetField("Skeletons", flags).GetValue(cache);
                Assert.That(sources.Count, Is.EqualTo(1));
                var source = sources["effect/hit.scsp"];
                Assert.That(source.GetType().GetField("ParsedData", flags).GetValue(source), Is.Not.Null);
                Assert.That(actorType.GetField("player", flags).GetValue(actor), Is.Null);
                Assert.That(root.transform.Find("Battle Skill Effects").childCount, Is.Zero);
                successor = (IDisposable)type.GetMethod("CreateSuccessor").Invoke(playback, null);
                Assert.That(type.GetField("cache", flags).GetValue(successor), Is.SameAs(cache));
                playback.Dispose(); playback = null;
                type.GetMethod("Prepare").Invoke(successor, new object[] { actor, "no-source-graph" });
                Assert.That(sources["effect/hit.scsp"], Is.SameAs(source));
            }
            finally
            {
                playback?.Dispose(); successor?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(skeleton); UnityEngine.Object.DestroyImmediate(atlas);
                UnityEngine.Object.DestroyImmediate(definition); UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EventLookupDoesNotMaterializeUnrelatedSkeletonMeshKeys()
        {
            const string module = "AstralOath.Client.BattleScene";
            var type = Type.GetType(module + ".BattlePreviewActor, " + module, true);
            var root = new GameObject("Metadata-only actor");
            root.SetActive(false);
            var json = new StringBuilder("{\"skins\":[");
            for (int i = 0; i < 20000; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"vertices\":[0,1,2,3,4,5,6,7]}");
            }
            json.Append("],\"animations\":{\"skill1\":{\"events\":[{\"name\":\"hit\",\"time\":0.7},{\"name\":\"hit\",\"time\":0.8}]}}}");
            var skeleton = new TextAsset(json.ToString());
            try
            {
                var actor = root.AddComponent(type);
                using (var serialized = new SerializedObject(actor))
                {
                    serialized.FindProperty("skeleton").objectReferenceValue = skeleton;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var lookup = type.GetMethod("GetBattleAnimationEventTime", BindingFlags.Instance | BindingFlags.NonPublic);
                long before = GC.GetTotalMemory(false);
                float at = (float)lookup.Invoke(actor, new object[] { "skill1", "hit" });
                long allocated = GC.GetTotalMemory(false) - before;
                UnityEngine.Debug.Log($"Actor metadata bytes={allocated}");
                Assert.That(at, Is.EqualTo(.7f).Within(.0001f));
                Assert.That(allocated, Is.LessThan(8_000_000), "Event lookup must not allocate a DOM for unrelated skin meshes.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(skeleton);
            }
        }
    }
}
