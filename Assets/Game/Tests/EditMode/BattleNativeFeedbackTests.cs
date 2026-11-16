using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleNativeFeedbackTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private GameObject root;
        private object library;
        private readonly System.Collections.Generic.List<IDisposable> effects = new System.Collections.Generic.List<IDisposable>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Feedback test");
            var loader = Type.GetType("AstralOath.Client.BattleScene.Editor.BattleActionFeedbackAssets, AstralOath.Client.BattleScene.Editor", true);
            var assets = loader.GetMethod("Load", Static).Invoke(null, null);
            var type = NativeType().GetNestedType("Library", BindingFlags.NonPublic);
            library = Activator.CreateInstance(type, Instance, null, new[] { assets }, null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in effects) item.Dispose();
            effects.Clear();
            if (root) Object.DestroyImmediate(root);
        }

        [Test]
        public void SoulBurnUsesNativeIntroThenLoopAndExpiresWithoutMutatingMaterials()
        {
            var effect = New("ui_livepvp_soulburn_eff");
            Call(effect, "Play");
            var player = Player(effect);
            Assert.That(Property(player, "CurrentAnimation"), Is.EqualTo("intro"));
            // Spine opacity belongs in its renderer property block. Particle emitters
            // own their materials and intentionally update those private instances.
            var renderers = ((GameObject)Property(player, "Root")).GetComponentsInChildren<MeshRenderer>(true);
            var materials = renderers.SelectMany(x => x.sharedMaterials).Distinct().ToArray();
            var opacity = materials.Select(x => x.GetFloat("_PresentationOpacity")).ToArray();
            var other = New("ui_livepvp_soulburn_eff");
            Call(other, "Play");
            var otherMaterials = EffectRoot(other).GetComponentsInChildren<MeshRenderer>(true)
                .SelectMany(x => x.sharedMaterials).Distinct().ToArray();
            var otherOpacity = otherMaterials.Select(x => x.GetFloat("_PresentationOpacity")).ToArray();
            Call(effect, "Tick", 1.05f);
            Assert.That(Property(player, "CurrentAnimation"), Is.EqualTo("loop"));
            Call(effect, "Tick", .34f);
            Assert.That(Property(effect, "Active"), Is.True);
            var block = new MaterialPropertyBlock();
            renderers[0].GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.InRange(0f, 1f));
            Assert.That(materials.Select(x => x.GetFloat("_PresentationOpacity")), Is.EqualTo(opacity));
            Assert.That(otherMaterials.Select(x => x.GetFloat("_PresentationOpacity")), Is.EqualTo(otherOpacity),
                "Fading one effect must not change another effect's Spine or private particle materials.");
            Call(effect, "Tick", .1f);
            Assert.That(Property(effect, "Active"), Is.False);
            Assert.That(EffectRoot(effect).activeSelf, Is.False);
        }

        [Test]
        public void AvatarLoopSurvivesLongPresentationThenOwnerFinishIsIdempotent()
        {
            var held = New("ui_livepvp_soulburn_eff");
            var bounded = New("ui_livepvp_soulburn_eff");
            Call(held, "PlayUntilFinished"); Call(bounded, "Play");
            Call(held, "Tick", 8f); Call(bounded, "Tick", 8f);
            Assert.That(Property(held, "Active"), Is.True);
            Assert.That(Property(Player(held), "CurrentAnimation"), Is.EqualTo("loop"));
            Assert.That(Property(bounded, "Active"), Is.False);
            Call(held, "Finish"); Call(held, "Tick", .06f); Call(held, "Finish");
            Assert.That(Property(held, "Active"), Is.True);
            Call(held, "Tick", .07f);
            Assert.That(Property(held, "Active"), Is.True, "Source particles drain after the short Spine fade.");
            var block = new MaterialPropertyBlock();
            ((GameObject)Property(Player(held), "Root")).GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.Zero, "Repeated settle checks must not restart the fade.");
            Call(held, "Tick", 2f);
            Assert.That(Property(held, "Active"), Is.False);
            Call(held, "PlayUntilFinished");
            Assert.That(Property(Player(held), "CurrentAnimation"), Is.EqualTo("intro"));
            Assert.That(Property(Player(held), "Time"), Is.EqualTo(0f));
            Call(held, "Stop");
            Assert.That(EffectRoot(held).activeSelf, Is.False);
        }

        [Test]
        public void EarlyOwnerFinishCompletesAuthoredIntroBeforeFading()
        {
            var effect = New("ui_livepvp_soulburn_eff");
            Call(effect, "PlayUntilFinished"); Call(effect, "Tick", .1f); Call(effect, "Finish");
            Call(effect, "Tick", .8f);
            Assert.That(Property(effect, "Active"), Is.True);
            Assert.That(Property(Player(effect), "CurrentAnimation"), Is.EqualTo("intro"));
            Call(effect, "Tick", .15f);
            Assert.That(Property(effect, "Active"), Is.True);
            Assert.That(Property(Player(effect), "CurrentAnimation"), Is.EqualTo("loop"));
            Call(effect, "Tick", .08f);
            Assert.That(Property(effect, "Active"), Is.True);
            Call(effect, "Tick", 2f);
            Assert.That(Property(effect, "Active"), Is.False);
        }

        [Test]
        public void ParticleEmissionStopDrainsLiveParticlesWhileStopAndReplayKeepTheirContracts()
        {
            var first = ParticleRunner(New("ui_livepvp_soulburn_eff"));
            var other = ParticleRunner(New("ui_livepvp_soulburn_eff"));
            Call(first, "Play"); Call(other, "Play");
            Call(first, "Tick", 3f); Call(other, "Tick", 3f);
            Assert.That(Property(first, "HasLiveParticles"), Is.True);
            Call(first, "StopEmission");
            Assert.That(Property(first, "HasLiveParticles"), Is.True, "Stopping emission must not clear existing particles.");
            Call(first, "Tick", .05f);
            Assert.That(Property(first, "HasLiveParticles"), Is.True);
            Call(first, "Tick", 2f);
            Assert.That(Property(first, "HasLiveParticles"), Is.False, "No new particles may spawn while draining.");
            Call(other, "Tick", 3f);
            Assert.That(Property(other, "HasLiveParticles"), Is.True, "The existing continuous runner remains unchanged.");
            Call(other, "Stop");
            Assert.That(Property(other, "HasLiveParticles"), Is.False, "Stop remains immediate for existing callers.");
            Call(first, "Play"); Call(first, "Tick", 3f);
            Assert.That(Property(first, "HasLiveParticles"), Is.True, "Replay restores emission.");
        }

        [Test]
        public void OneShotWithoutAuthoredLoopCannotBecomeUnbounded()
        {
            var effect = New("stse_chain");
            var failure = Assert.Throws<TargetInvocationException>(() => Call(effect, "PlayUntilFinished"));
            Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(Property(effect, "Active"), Is.False);
        }

        [Test]
        public void NativeExtraTurnRetainsCfxOffsetScaleAnd167MillisecondParticleDelay()
        {
            var effect = New("stse_addturn");
            Call(effect, "Play");
            var visual = EffectRoot(effect).transform.Find("stse_addturn_renew");
            var particle = EffectRoot(effect).transform.Find("stse_addturn_pati1");
            Assert.That(visual.localPosition.y, Is.EqualTo(-1f).Within(.0001f));
            Assert.That(visual.localScale.x, Is.EqualTo(1.2f).Within(.0001f));
            Call(effect, "Tick", .16f);
            Assert.That(particle.gameObject.activeSelf, Is.False);
            Call(effect, "Tick", .008f);
            Assert.That(particle.gameObject.activeSelf, Is.True);
            Call(effect, "Tick", .02f);
            Assert.That(particle.GetComponentsInChildren<MeshFilter>().Sum(x => x.sharedMesh.vertexCount), Is.GreaterThan(0));
        }

        [Test]
        public void NativeParticleMaterialsConsumeSourcePremultipliedAlphaOnce()
        {
            foreach (string source in new[] { "ui_livepvp_soulburn_eff", "stse_addturn" })
            {
                var effect = New(source);
                Call(effect, "Play"); Call(effect, "Tick", .2f);
                var renderers = EffectRoot(effect).GetComponentsInChildren<MeshRenderer>()
                    .Where(r => r.gameObject.name.StartsWith("HUD particle ", StringComparison.Ordinal)).ToArray();
                Assert.That(renderers, Is.Not.Empty);
                foreach (var renderer in renderers)
                    Assert.That(renderer.sharedMaterial.GetFloat("_PremultipliedAlpha"), Is.EqualTo(1f),
                        "Native white particle RGB already contains alpha; applying it twice erases the outer glow.");
            }
        }

        [Test]
        public void SharedSourceHasIndependentClocksPauseAndReplay()
        {
            var first = New("stse_chain");
            var second = New("stse_chain");
            Call(first, "Play"); Call(second, "Play");
            Call(first, "Tick", .2f);
            Assert.That(Property(Player(first), "Time"), Is.EqualTo(.2f).Within(.0001f));
            Assert.That(Property(Player(second), "Time"), Is.EqualTo(0f));
            Call(first, "Tick", 0f);
            Assert.That(Property(first, "Elapsed"), Is.EqualTo(.2f));
            Call(first, "Stop");
            Assert.That(Property(second, "Active"), Is.True);
            Call(first, "Play");
            Assert.That(Property(Player(first), "Time"), Is.EqualTo(0f));
        }

        [Test]
        public void DisposalDestroysOnlyOwnedEffectsAndIsIdempotent()
        {
            var first = New("stse_counter");
            var second = New("stse_chain");
            Call(first, "Play"); Call(second, "Play");
            ((IDisposable)first).Dispose(); ((IDisposable)first).Dispose();
            Assert.That(EffectRoot(first) == null, Is.True);
            Assert.That(EffectRoot(second), Is.Not.Null);
            Assert.That(Property(second, "Active"), Is.True);
            Assert.That(root.transform.childCount, Is.EqualTo(1));
        }

        private object New(string source)
        {
            var result = Activator.CreateInstance(NativeType(), Instance, null,
                new object[] { library, source, root.transform, .01f, 0, 100 }, null);
            effects.Add((IDisposable)result);
            return result;
        }
        private static Type NativeType() => Type.GetType("AstralOath.Client.BattleScene.BattleNativeFeedbackEffect, AstralOath.Client.BattleScene", true);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Instance).Invoke(value, args);
        private static object Property(object value, string name) => value.GetType().GetProperty(name, Instance).GetValue(value);
        private static GameObject EffectRoot(object value) => (GameObject)value.GetType().GetField("Root", Instance).GetValue(value);
        private static object ParticleRunner(object value)
        {
            var parts = (IEnumerable)value.GetType().GetField("parts", Instance).GetValue(value);
            return parts.Cast<object>().Select(p => p.GetType().GetField("Particles", Instance).GetValue(p)).First(p => p != null);
        }
        private static object Player(object value)
        {
            var parts = (IEnumerable)value.GetType().GetField("parts", Instance).GetValue(value);
            return parts.Cast<object>().Select(p => p.GetType().GetField("Spine", Instance).GetValue(p)).First(p => p != null);
        }
    }
}
