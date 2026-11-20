using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Skeletons;
using AstralOath.Client.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Tests
{
    public sealed class BattleCfxAnimationSelectionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Module = "AstralOath.Client.BattleScene";
        private GameObject root, group;
        private IDisposable playback;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private Type runtime;

        [TearDown]
        public void Cleanup()
        {
            // Runtime visuals normally retire in PlayMode. Remove this EditMode fixture's group first.
            if (group) UnityEngine.Object.DestroyImmediate(group);
            playback?.Dispose(); playback = null;
            if (root) UnityEngine.Object.DestroyImmediate(root);
            foreach (var item in owned) if (item) UnityEngine.Object.DestroyImmediate(item);
            owned.Clear();
        }

        [Test]
        public void RealAngelicaAlternatePrimitiveUsesItsOwnDuration()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1062/";
            var effect = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/angelica_skill_03_target.cfx.json").text);
            var part = (JObject)effect["primitive"][4];
            Assert.That((string)part["ani"], Is.EqualTo("animation2"));
            Create((string)part["source"], AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Animations/effect/angelica_sk3eff_target_b.spine.json"));
            Assert.That(Duration(part), Is.EqualTo(2.6333f).Within(.0002f),
                "The source fifth primitive selects animation2, not the shorter animation clip.");
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void ExplicitAnimationControlsPoseAndLifetime(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'animation2' }");
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .5f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(10f).Within(.001f), "ani must select the requested native runtime animation.");
            Assert.That(Duration(part), Is.EqualTo(2f).Within(.001f));
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void ExplicitZeroDurationPoseRendersForItsDeclaredLifetime(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'static','lifeTime':800 }");
            Assert.That(Duration(part), Is.EqualTo(.8f).Within(.001f),
                "A declared static clip exists even when its natural duration is zero.");
            Spawn(part);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f));
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .79f });
            var mesh = group.GetComponentInChildren<MeshFilter>();
            Assert.That(mesh, Is.Not.Null, "The explicit lifetime keeps the static visual alive.");
            Assert.That(mesh.sharedMesh.bounds.center.x, Is.EqualTo(25f).Within(.001f));
        }

        [TestCase("2.1.25")]
        [TestCase("3.8.99")]
        public void DelayedActionCanSwitchToAZeroDurationPose(string version)
        {
            CreateSynthetic(version);
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','lifeTime':800,'actions':[{'action':'static','delay':300}] }");
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .2f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(2f).Within(.001f), "The initial clip remains active before the declared delay.");
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .11f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f), "The action must select a real zero-duration pose at its delay.");
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .4f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(25f).Within(.001f));
            Assert.That(Duration(part), Is.EqualTo(.8f).Within(.001f));
        }

        [Test]
        public void ExplicitMissingAnimationIsDiagnosedAndNeverFallsBack()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','ani':'missing' }");
            LogAssert.Expect(LogType.Warning, "fixture_skill: Unknown CFX animation 'missing' in 'effect/fixture.scsp'.");
            Assert.That(Duration(part), Is.Zero);
            var failure = Assert.Throws<TargetInvocationException>(() => Spawn(part));
            Assert.That(failure.InnerException, Is.TypeOf<FormatException>());
            Assert.That(failure.InnerException.Message, Does.Contain("missing"));
            Assert.That(group.GetComponentInChildren<MeshFilter>(), Is.Null);
        }

        [Test]
        public void DelayedActionKeepsItsSeparateAnimationSwitchAndLifetimeOverride()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','actions':[{'action':'animation2','delay':300}] }");
            Assert.That(Duration(part), Is.EqualTo(2.3f).Within(.001f));
            Spawn(part);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .5f });
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.center.x,
                Is.EqualTo(4f).Within(.001f), "actions remain delayed transitions from the selected initial clip.");
            part["lifeTime"] = 420;
            Assert.That(Duration(part), Is.EqualTo(.42f).Within(.001f));
        }

        [Test]
        public void UnmappedActionDoesNotInventAnAnimationOrExtendDuration()
        {
            CreateSynthetic("2.1.25");
            var part = JObject.Parse("{ 'format':'spine','source':'fixture','actions':[{'action':'Fire','delay':10000}] }");
            LogAssert.Expect(LogType.Warning, "fixture_skill: Unmapped CFX action 'Fire' in 'effect/fixture.scsp'.");
            Assert.That(Duration(part), Is.EqualTo(1f).Within(.001f));
        }

        [TestCase(1.6f)]
        [TestCase(.8f)]
        [TestCase(.5f)]
        public void ClarissaS2RepeatedLayersSampleTheirAuthoredLocalFrame(float speed)
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1028/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix +
                "Effects/effect/clarisa_skill_02.cfx.json").text);
            var part = cfx["primitive"].OfType<JObject>().Single(row =>
                (string)row["source"] == "clarisa_sk2_eff_b" && (float?)row["timeScale"] == speed);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/clarisa_sk2_eff_b.spine.json");
            var atlas = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Atlas/effect/clarisa_sk2_eff_b.atlas.txt");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Textures/effect/clarisa_sk2eff.png");
            const string pageName = "clarisa_sk2eff.sct";
            Create((string)part["source"], json, atlas, texture, pageName);
            using var reference = new SkeletonPlayerFactory().Create(new SkeletonPlayerSource(json.text, atlas.text,
                new Dictionary<string, Texture2D> { [pageName] = texture }, true, 1f), root.transform);
            reference.Play("animation", false);
            Spawn(part);
            const float elapsed = .2f;
            reference.Tick(elapsed * speed);
            runtime.GetMethod("Tick").Invoke(playback, new object[] { elapsed });
            var expected = reference.Root.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.That(expected, Is.Not.Empty);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.vertices, Is.EqualTo(expected),
                "At one stage instant, the visible geometry must match the source rig at its local clip time.");
            Assert.That(Duration(part), Is.EqualTo(reference.Duration / speed).Within(.0001f));
        }

        private void CreateSynthetic(string version)
        {
            var json = JObject.Parse(SkeletonPlayerTests.Json(version));
            json["animations"] = JObject.Parse(@"{
                'animation':{'bones':{'root':{'translate':[{'time':0},{'time':1,'x':10}]}}},
                'animation2':{'bones':{'root':{'translate':[{'time':0},{'time':2,'x':40}]}}},
                'static':{'bones':{'root':{'translate':[{'time':0,'x':25}]}}}} ");
            var document = new TextAsset(json.ToString()); owned.Add(document);
            Create("fixture", document);
        }

        [Test]
        public void SezS3BurstAdvancesTenLocalFramesPerStageFrame()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1038/Effects/effect/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "sez_gaien_skill_03_target.cfx.json").text);
            var parts = cfx["primitive"].OfType<JObject>().Where(part => (string)part["format"] == "particle").ToArray();
            Assert.That(parts.Length, Is.EqualTo(5));
            Assert.That(parts.All(part => (float)part["timeScale"] == 10f), Is.True);
            string source = (string)parts[0]["source"];
            var definition = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + source + ".particle.json");
            Create(source, definition, particle: true);
            var texture = new Texture2D(2, 2); owned.Add(texture);
            var textures = (Dictionary<string, Texture2D>)runtime.GetProperty("textures", Private).GetValue(playback);
            textures.Add("particle/pati_01.sct", texture);
            var particleRuntime = runtime.Assembly.GetType(Module + ".BattleHudParticles", true);
            var referenceRoot = new GameObject("Source burst"); referenceRoot.transform.SetParent(root.transform);
            using var reference = (IDisposable)Activator.CreateInstance(particleRuntime, definition, textures,
                referenceRoot.transform, Vector2.zero, .01f, 55);
            particleRuntime.GetMethod("Play").Invoke(reference, null);
            runtime.GetMethod("SpawnParticles", Private).Invoke(playback,
                new object[] { source, parts[0], group.transform, 1f, 55, (Action)(() => { }), false });
            particleRuntime.GetMethod("Tick").Invoke(reference, new object[] { .01f * 10f });
            runtime.GetMethod("Tick").Invoke(playback, new object[] { .01f });
            var expected = referenceRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            Assert.That(expected, Is.Not.Empty);
            Assert.That(group.GetComponentInChildren<MeshFilter>().sharedMesh.vertices, Is.EqualTo(expected));
            Assert.That(Duration(parts[0]), Is.EqualTo(.04f).Within(.0001f),
                "The burst's 0.2 ± 0.2s local lifespan occupies at most 40ms of stage time.");
            parts[0]["lifeTime"] = 150;
            Assert.That(Duration(parts[0]), Is.EqualTo(.15f).Within(.0001f));
        }

        [Test]
        public void HasteHitUsesOneAndAHalfTimesLocalSpineTime()
        {
            const string prefix = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1039/";
            var cfx = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Effects/effect/haste_hit.cfx.json").text);
            var part = (JObject)cfx["primitive"][0];
            Assert.That((float)part["timeScale"], Is.EqualTo(1.5f));
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(prefix + "Animations/effect/hiteff_dark.spine.json");
            Create((string)part["source"], json);
            var unscaled = (JObject)part.DeepClone();
            unscaled.Remove("timeScale");
            Assert.That(Duration(unscaled), Is.GreaterThan(0f));
            Assert.That(Duration(part), Is.EqualTo(Duration(unscaled) / 1.5f).Within(.0001f));
        }

        private void Create(string source, TextAsset document, TextAsset atlas = null, Texture2D texture = null,
            string pageName = "page.png", bool particle = false)
        {
            runtime = Type.GetType(Module + ".BattleSkillPresentation, " + Module, true);
            var assetType = Type.GetType(Module + ".BattleSkillPresentationAsset, " + Module, true);
            root = new GameObject("CFX selection fixture");
            group = new GameObject("CFX group"); group.transform.SetParent(root.transform, false);
            if (!atlas) { atlas = new TextAsset(SkeletonPlayerTests.Atlas); owned.Add(atlas); }
            if (!texture) { texture = new Texture2D(16, 16); owned.Add(texture); }
            var asset = Activator.CreateInstance(assetType);
            assetType.GetField("source").SetValue(asset, "effect/" + source + (particle ? ".particle" : ".scsp"));
            assetType.GetField("document").SetValue(asset, document);
            assetType.GetField("atlas").SetValue(asset, atlas);
            assetType.GetField("pageNames").SetValue(asset, new[] { pageName });
            assetType.GetField("pages").SetValue(asset, new[] { texture });
            assetType.GetField("scale").SetValue(asset, 1f);
            var assets = Array.CreateInstance(assetType, 1); assets.SetValue(asset, 0);
            playback = (IDisposable)Activator.CreateInstance(runtime,
                new object[] { root.transform, Array.Empty<TextAsset>(), assets, null, null, null });
            runtime.GetField("currentPresentationId", Private).SetValue(playback, "fixture_skill");
        }

        private float Duration(JObject part) => (float)runtime.GetMethod("PrimitiveDuration", Private).Invoke(playback, new object[] { part });
        private void Spawn(JObject part) => runtime.GetMethod("SpawnSpine", Private).Invoke(playback,
            new object[] { (string)part["source"], part, group.transform, 1f, 55, 1f, (Action)(() => { }) });
    }
}
