#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace AstralOath.Client.Tests
{
    public sealed class BattleUnitPresentationIntegrationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        Scene scene; BattleHudPreview host; BindingMap map;
        [UnitySetUp] public IEnumerator Load()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null; yield return null;
            host = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BattleHudPreview>()).Single();
            map = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BindingMap>()).Single();
        }
        [UnityTearDown] public IEnumerator Unload() { if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene); }
        [UnityTest] public IEnumerator RealAnimatedTopDrivesMarkerPositionAndCameraScale()
        {
            var actor = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BattlePreviewActor>()).Single(x => x.name == "Ras [c1001]");
            var marker = map.transform.Find("Unit Status/Allies/01 Front");
            var frame = (RectTransform)marker.Find("Frame");
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            float width = Width(frame, camera);
            Capture("idle");
            actor.transform.position += Vector3.right * 1.5f;
            actor.transform.localScale *= 1.2f; camera.orthographicSize *= .8f;
            // NODE_ANI scales the renderer child rather than its parent actor.
            actor.GetComponentInChildren<MeshRenderer>().transform.localScale *= 1.1f;
            actor.PlayBattleAnimation("attacked");
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
                simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { 0f });
                Canvas.ForceUpdateCanvases();
                object[] args = { "top", Vector3.zero };
                Assert.That((bool)typeof(BattlePreviewActor).GetMethod("TryGetPresentationAnchor", Private).Invoke(actor, args), Is.True);
                var target = (Vector2)camera.WorldToScreenPoint((Vector3)args[1]);
                var actual = RectTransformUtility.WorldToScreenPoint(camera, frame.TransformPoint(frame.rect.center));
                Assert.That(Vector2.Distance(actual, target), Is.LessThan(2f), "Status must follow the animated top bone, not the original canvas slot.");
                Assert.That(Width(frame, camera) / width, Is.EqualTo(1.65f).Within(.03f));
                if (i == 11) Capture("moved-scaled-attacked");
            }
        }
        [UnityTest] public IEnumerator DefeatKeepsTheActorForBlackeningAndPauseFreezesTheFade()
        {
            var actor = scene.GetRootGameObjects().Single(x => x.name == "Units").transform
                .Find("Enemies").GetChild(0).GetComponentInChildren<BattlePreviewActor>();
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var defeat = BattleDualAttackTimingTests.Event(BattleFlowEventKind.UnitDefeated, "ally-1", "c1001.s1", null, BattleActionOrigin.Primary);
            simulation.GetType().GetMethod("Present", Private).Invoke(simulation, new object[] { defeat });
            simulation.GetType().GetMethod("ShowDueFeedback", Private).Invoke(simulation, null);
            Assert.That(actor.gameObject.activeInHierarchy, Is.True, "Accepted defeat starts a visual lifecycle; it cannot immediately deactivate the actor.");
            yield return new WaitForSecondsRealtime(.12f);
            var renderer = actor.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            float shade = block.GetFloat("_PresentationBrightness");
            Assert.That(shade, Is.InRange(.001f, .999f));
            host.PauseBattle(); yield return new WaitForSecondsRealtime(.15f);
            renderer.GetPropertyBlock(block); Assert.That(block.GetFloat("_PresentationBrightness"), Is.EqualTo(shade).Within(.01f));
            host.ResumeBattle();
            yield return new WaitForSecondsRealtime(.25f);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.LessThan(1f), "The body fades after blackening.");
            host.RestartBattle(); yield return null;
            Assert.That(actor.gameObject.activeInHierarchy, Is.True);
            renderer = actor.GetComponentInChildren<MeshRenderer>(); renderer.GetPropertyBlock(block);
            Assert.That(!block.HasFloat(Shader.PropertyToID("_PresentationOpacity")) || block.GetFloat("_PresentationOpacity") == 1f, Is.True);
        }
        [UnityTest] public IEnumerator RealSceneDamageTailDeathRemovalAndSameFrameRevival()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            var views = (IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation);
            var view = views["enemy-1"];
            var actor = (BattlePreviewActor)view.GetType().GetProperty("Actor").GetValue(view);
            var marker = map.transform.Find("Unit Status/Enemies/01 Front");
            var hp = marker.Find("HP").GetComponent<Image>();
            var trail = hp.transform.Find("Damage Trail").GetComponent<Image>();
            void Call(string method, params object[] args) => view.GetType().GetMethod(method).Invoke(view, args);
            Call("SetHealth", 1000L, 1000); Call("SetHealth", 350L, 1000);
            Call("TickVisual", .1f);
            Assert.That(hp.fillAmount, Is.InRange(.35f, 1f));
            Assert.That(trail.rectTransform.rect.width, Is.GreaterThan(0f));
            Capture("damage-010");
            Call("TickVisual", .6f);
            Assert.That(hp.fillAmount, Is.EqualTo(.35f).Within(.001f));
            Assert.That(trail.enabled, Is.False);
            Capture("damage-settled");
            Call("ApplyStatus", Activator.CreateInstance(typeof(BattleStatusSnapshot),
                Private, null, new object[] { BattleStatusKind.DefenseDown, 2, 0 }, null));
            var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                .GetField("player", Private).GetValue(actor);
            Assert.That(actor.PlayBattleAnimation("attacked"), Is.GreaterThan(.2f));
            var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            var deathBlock = new MaterialPropertyBlock();
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            Call("SetHealth", 0L, 1000);
            Call("BeginDeath"); Call("Show", false); Call("TickVisual", .1f); Call("LateTick", camera);
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"));
            Assert.That(player.Time, Is.EqualTo(.1f).Within(.001f), "The final source reaction continues into blackening.");
            Assert.That(marker.gameObject.activeInHierarchy, Is.True);
            Capture("death-010");
            typeof(BattlePreviewActor).GetMethod("PlayBattleIdle", Private).Invoke(actor, null);
            Call("TickVisual", .1f); Call("LateTick", camera);
            body.GetPropertyBlock(deathBlock);
            Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"), "Action cleanup must not replace the defeated pose.");
            Assert.That(deathBlock.GetFloat("_PresentationBrightness"), Is.Zero);
            Assert.That(deathBlock.GetFloat("_PresentationOpacity"), Is.EqualTo(1f));
            Assert.That(marker.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(hp.fillAmount, Is.Zero.Within(.001f));
            Assert.That(trail.enabled, Is.True);
            Assert.That(marker.Find("Frame/Status Icons").GetChild(0).gameObject.activeInHierarchy, Is.True);
            var particles = actor.transform.Find("Battle death particles");
            Assert.That(particles.GetComponentsInChildren<MeshRenderer>(), Is.Empty,
                "The particle burst must leave the opaque black silhouette unobscured.");
            Capture("death-020");
            Call("TickVisual", .08f); Call("LateTick", camera);
            body.GetPropertyBlock(deathBlock);
            Assert.That(deathBlock.GetFloat("_PresentationOpacity"), Is.EqualTo(1f));
            Assert.That(marker.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            Assert.That(particles.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
            Capture("death-028");
            Call("TickVisual", .11f); Call("LateTick", camera);
            Assert.That(marker.GetComponent<CanvasGroup>().alpha, Is.EqualTo(.75f).Within(.01f));
            Assert.That(particles.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty);
            Capture("death-039");
            Call("TickVisual", .16f); Capture("death-055");
            Call("TickVisual", .1f); Call("LateTick", camera);
            Assert.That(marker.gameObject.activeSelf, Is.False, "Only the particle tail remains after the body fades.");
            Call("TickVisual", 10f);
            Assert.That(actor.gameObject.activeInHierarchy, Is.False, "The physical slot is removed after the visual tail.");
            Call("Show", true);
            Assert.That(actor.gameObject.activeInHierarchy, Is.True);
            // Revival in the same frame must bind only the new skeleton, never deferred-destroy objects.
            Call("BeginDeath"); Call("TickVisual", .39f);
            var current = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            var block = new MaterialPropertyBlock(); current.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(.75f).Within(.01f));
            host.RestartBattle(); yield return null;
            Assert.That(actor.gameObject.activeInHierarchy, Is.True);
        }
        [UnityTest] public IEnumerator KiseBarrierTracksAnimatedShadowBoneOnBothFacings()
        {
            typeof(BattleHudPreview).GetField("allyHeroIds", Private)
                .SetValue(host, new[] { "c1001", "c1062", "c1005", "c1006" });
            typeof(BattleHudPreview).GetField("enemyHeroIds", Private)
                .SetValue(host, new[] { "c1006", "c1008", "c1008", "c1008" });
            host.RestartBattle();
            yield return null;
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var views = (IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation);
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            var barrier = (BattleStatusSnapshot)Activator.CreateInstance(typeof(BattleStatusSnapshot),
                Private, null, new object[] { BattleStatusKind.Barrier, 3, 2000 }, null);

            foreach (string id in new[] { "ally-4", "enemy-1" })
            {
                var view = views[id];
                var actor = (BattlePreviewActor)view.GetType().GetProperty("Actor").GetValue(view);
                var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
                var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                    .GetField("player", Private).GetValue(actor);
                var shadow = (Transform)typeof(BattlePreviewActor).GetProperty("PresentationShadow", Private).GetValue(actor);
                Vector3 groundPosition = shadow.position;
                void Sample(string clip, float time)
                {
                    player.Play(clip, false);
                    player.Seek(time);
                    view.GetType().GetMethod("LateTick").Invoke(view, new object[] { camera });
                }

                Sample("b_idle", 0f);
                view.GetType().GetMethod("ApplyStatus").Invoke(view, new object[] { barrier });
                var aura = body.transform.Find("Battle shield aura");
                Assert.That(aura, Is.Not.Null, id);
                AssertAnchor(.7405f);
                if (id == "ally-4") Capture("kise-barrier-waiting");

                Sample("b_idle_ready", 0f);
                AssertAnchor(-.368f);
                Sample("b_idle_ready", 1f);
                AssertAnchor(.7405f);

                body.transform.localPosition += new Vector3(.45f, .7f, 0f);
                body.transform.localScale *= 1.18f;
                camera.orthographicSize *= .9f;
                Sample("b_idle", 0f);
                AssertAnchor(.7405f);
                Assert.That(shadow.position, Is.EqualTo(groundPosition), "The separate ground shadow stays in place.");
                if (id == "enemy-1") Capture("kise-barrier-both-facings");

                void AssertAnchor(float authoredX)
                {
                    Assert.That(player.TryGetBoneSkeletonPosition("shadow", out var foot), Is.True, id);
                    Assert.That(foot.x, Is.EqualTo(authoredX).Within(.001f), id);
                    var expected = body.transform.TransformPoint((Vector3)foot + Vector3.up * .2f);
                    Assert.That(Vector3.Distance(aura.position, expected), Is.LessThan(.001f), id);
                }
            }
        }

        [UnityTest] public IEnumerator StatusIconsFollowTheBarAndBarrierUsesAcceptedHitEvents()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var marker = map.transform.Find("Unit Status/Enemies/01 Front");
            var view = ((IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation))["enemy-1"];
            var actor = (BattlePreviewActor)view.GetType().GetProperty("Actor").GetValue(view);
            var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            var frame = (RectTransform)marker.Find("Frame");
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            float health = marker.Find("HP").GetComponent<Image>().fillAmount;
            void Call(string method, params object[] args) => simulation.GetType().GetMethod(method, Private).Invoke(simulation, args);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            void Present(BattleFlowEventKind kind, int amount = 0, BattleStatusKind? statusKind = null, int turns = 0,
                string source = "ally-1", string target = "enemy-1")
            {
                var status = turns == 0 ? null : (BattleStatusSnapshot)Activator.CreateInstance(typeof(BattleStatusSnapshot),
                    Private, null, new object[] { statusKind.Value, turns, amount }, null);
                var ctor = typeof(BattleFlowEvent).GetConstructors(Private).Single();
                Call("Present", ctor.Invoke(new object[] { 1UL, 1, kind, source, target, amount, null, null,
                    BattleActionOrigin.Primary, statusKind, status, null, null, BattleHitKind.Normal, null, false, 2000 }));
            }

            Present(BattleFlowEventKind.StatusApplied, 5000, BattleStatusKind.AttackUp, 1);
            Present(BattleFlowEventKind.StatusApplied, 2000, BattleStatusKind.Barrier, 3);
            Present(BattleFlowEventKind.StatusApplied, 5000, BattleStatusKind.DefenseDown, 3);
            Call("ShowDueFeedback");
            var icons = frame.Find("Status Icons");
            Assert.That(icons, Is.Not.Null, "The scene needs status icon Images under the projected marker.");
            string[] VisibleIcons() => icons.Cast<Transform>().Where(x => x.gameObject.activeSelf)
                .Select(x => x.GetComponent<Image>().sprite.name).ToArray();
            CollectionAssert.AreEqual(new[] { "stic_protect", "stic_att_up", "stic_def_dn" }, VisibleIcons());
            var digit = icons.GetChild(0).Find("Remaining Turns").GetComponent<Image>();
            Assert.That(digit.sprite.name, Is.EqualTo("itxt_num3"));
            Present(BattleFlowEventKind.StatusApplied, 5000, BattleStatusKind.AttackUp, 4);
            Call("ShowDueFeedback");
            CollectionAssert.AreEqual(new[] { "stic_protect", "stic_att_up", "stic_def_dn" }, VisibleIcons());
            var shield = marker.Find("HP/Shield Fill").GetComponent<Image>();
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .25f });
            Assert.That(shield.gameObject.activeSelf, Is.True);
            var aura = body.transform.Find("Battle shield aura");
            Assert.That(aura, Is.Not.Null);
            Assert.That(aura.gameObject.activeInHierarchy, Is.True);
            var shadow = (Transform)typeof(BattlePreviewActor).GetProperty("PresentationShadow", Private).GetValue(actor);
            var skeletonPlayer = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                .GetField("player", Private).GetValue(actor);
            Assert.That(skeletonPlayer.TryGetBoneSkeletonPosition("shadow", out var foot), Is.True);
            Vector3 expectedAura = body.transform.TransformPoint((Vector3)foot + Vector3.up * .2f);
            Assert.That(Vector2.Distance(aura.position, expectedAura), Is.LessThan(.001f));
            Capture("status-barrier-full");

            var first = (RectTransform)icons.GetChild(0);
            float originalWidth = Width(first, camera);
            Vector2 originalPosition = first.position;
            actor.transform.position += new Vector3(.7f, 1.25f, 0f);
            actor.transform.localScale *= 1.25f;
            view.GetType().GetMethod("LateTick").Invoke(view, new object[] { camera });
            Assert.That((Vector2)first.position, Is.Not.EqualTo(originalPosition));
            Assert.That(Width(first, camera) / originalWidth, Is.EqualTo(1.25f).Within(.03f));
            Assert.That(skeletonPlayer.TryGetBoneSkeletonPosition("shadow", out foot), Is.True);
            Assert.That(Vector2.Distance(aura.position, body.transform.TransformPoint((Vector3)foot + Vector3.up * .2f)), Is.LessThan(.001f),
                "A jump moves the shield with the body even while its ground shadow stays behind.");
            Assert.That(Vector2.Distance(aura.position, shadow.position), Is.GreaterThan(.7f));
            Assert.That(first.IsChildOf(frame.parent), Is.True);
            body.enabled = false;
            view.GetType().GetMethod("LateTick").Invoke(view, new object[] { camera });
            Assert.That(icons.gameObject.activeInHierarchy, Is.False);
            Assert.That(aura.gameObject.activeInHierarchy, Is.False);
            body.enabled = true;
            view.GetType().GetMethod("LateTick").Invoke(view, new object[] { camera });
            Assert.That(icons.gameObject.activeInHierarchy, Is.True);
            Assert.That(aura.gameObject.activeInHierarchy, Is.True);

            Present(BattleFlowEventKind.BarrierAbsorbed, 2000);
            Present(BattleFlowEventKind.StatusRemoved, statusKind: BattleStatusKind.Barrier);
            Present(BattleFlowEventKind.Damage);
            Call("ShowDueFeedback");
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .5f });
            Assert.That(shield.gameObject.activeSelf, Is.False);
            Assert.That(aura.gameObject.activeInHierarchy, Is.False);
            Assert.That(marker.Find("HP").GetComponent<Image>().fillAmount, Is.EqualTo(health).Within(.001f));
            CollectionAssert.AreEqual(new[] { "stic_att_up", "stic_def_dn" }, VisibleIcons());
            Capture("status-barrier-consumed");

            Present(BattleFlowEventKind.StatusApplied, 1100, BattleStatusKind.Barrier, 2);
            Call("ShowDueFeedback");
            var healthImage = marker.Find("HP").GetComponent<Image>();
            var red = marker.GetComponentsInChildren<Image>(true).Single(image => image.name == "Damage Trail");
            float RedWidth() => red.enabled ? red.rectTransform.rect.width : 0f;
            simulation.GetType().GetField("hitTimes", Private).SetValue(simulation, new[] { 1f, 2f, 3f });
            Present(BattleFlowEventKind.BarrierAbsorbed, 1100);
            Present(BattleFlowEventKind.StatusRemoved, statusKind: BattleStatusKind.Barrier);
            Present(BattleFlowEventKind.Damage, 1800);
            Present(BattleFlowEventKind.StatusApplied, 5000, BattleStatusKind.DefenseUp, 2, target: "ally-1");
            Present(BattleFlowEventKind.StatusRemoved, statusKind: BattleStatusKind.AttackUp, source: "enemy-1");
            var friendlyIcons = map.transform.Find("Unit Status/Allies/01 Front/Frame/Status Icons");
            var healthValue = view.GetType().GetField("presentedHealth", Private);
            long previousHealth = (long)healthValue.GetValue(view);

            void ShowAt(float at)
            {
                simulation.GetType().GetField("actionElapsed", Private).SetValue(simulation, at);
                Call("ShowDueFeedback");
            }

            var player = (AstralOath.Client.Skeletons.ISkeletonPlayer)typeof(BattlePreviewActor)
                .GetField("player", Private).GetValue(actor);
            void AssertReactionRestart(string hit)
            {
                Assert.That(player.CurrentAnimation, Is.EqualTo("attacked"), hit);
                Assert.That(player.Time, Is.Zero, hit);
                player.Tick(player.Duration * .25f);
                Assert.That(player.Time, Is.GreaterThan(0f), "Advance the real reaction clock before the next impact.");
            }

            ShowAt(1f);
            AssertReactionRestart("A shield-only hit still plays the reaction.");
            Assert.That(aura.gameObject.activeSelf, Is.True, "The first hit only spends part of the shield.");
            Assert.That(RedWidth(), Is.Zero, "A shield-only hit cannot start the HP damage trail.");
            Assert.That(healthValue.GetValue(view), Is.EqualTo(previousHealth));
            Assert.That(friendlyIcons.childCount, Is.Zero);
            CollectionAssert.Contains(VisibleIcons(), "stic_att_up");
            ShowAt(2f);
            AssertReactionRestart("The second hit restarts the reaction even while the first animation is playing.");
            Assert.That(aura.gameObject.activeSelf, Is.False);
            float redAtFirstHpHit = RedWidth();
            Assert.That(redAtFirstHpHit, Is.GreaterThan(0f), "The first hit that removes HP must immediately show red.");
            Assert.That(healthImage.fillAmount, Is.LessThan(health));
            Assert.That(shield.gameObject.activeSelf, Is.False, "The depleted shield cannot obscure the red segment.");
            Capture("damage-trail-first-hit", new Vector2Int(1920, 1080));
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .5f });
            Assert.That(RedWidth(), Is.EqualTo(redAtFirstHpHit).Within(.001f), "The red range holds until the last hit.");

            Assert.That(friendlyIcons.childCount, Is.Zero);
            CollectionAssert.Contains(VisibleIcons(), "stic_att_up");
            ShowAt(3f);
            AssertReactionRestart("The third hit restarts the reaction at its own cue.");
            float redAtLastHpHit = RedWidth();
            Assert.That(redAtLastHpHit, Is.GreaterThan(redAtFirstHpHit), "The last hit extends the red range.");
            Capture("damage-trail-last-hit", new Vector2Int(1920, 1080));
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .3f });
            Assert.That(RedWidth(), Is.InRange(.001f, redAtLastHpHit - .001f), "Red retracts after the last hit.");
            Capture("damage-trail-retracting", new Vector2Int(1920, 1080));
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .6f });
Assert.That(RedWidth(), Is.Zero, "The red segment is cleared after the transition.");
            Assert.That(healthValue.GetValue(view), Is.EqualTo(previousHealth - 1800));
            Assert.That(friendlyIcons.GetChild(0).GetComponent<Image>().sprite.name, Is.EqualTo("stic_def_up"));
            CollectionAssert.DoesNotContain(VisibleIcons(), "stic_att_up");
            yield return null;
        }

        [UnityTest]
        public IEnumerator StealthOpacityRestoresOnRemovalAndBeforeDeath()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            var view = ((IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation))["enemy-1"];
            var actor = (BattlePreviewActor)view.GetType().GetProperty("Actor").GetValue(view);
            var body = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            var block = new MaterialPropertyBlock();
            var opacity = Shader.PropertyToID("_PresentationOpacity");
            var constructor = typeof(BattleStatusSnapshot).GetConstructor(Private, null,
                new[] { typeof(BattleStatusKind), typeof(int), typeof(int) }, null);
            var stealth = constructor.Invoke(new object[] { BattleStatusKind.Stealth, 2, 0 });

            float ReadOpacity()
            {
                body.GetPropertyBlock(block);
                return block.HasFloat(opacity) ? block.GetFloat(opacity) : 1f;
            }

            view.GetType().GetMethod("ApplyStatus").Invoke(view, new[] { stealth });
            Assert.That(ReadOpacity(), Is.InRange(.01f, .99f));
            Assert.That(body.enabled, Is.True);
            view.GetType().GetMethod("RemoveStatus").Invoke(view, new object[] { BattleStatusKind.Stealth });
            Assert.That(ReadOpacity(), Is.EqualTo(1f));
            view.GetType().GetMethod("ApplyStatus").Invoke(view, new[] { stealth });
            view.GetType().GetMethod("BeginDeath").Invoke(view, null);
            Assert.That(ReadOpacity(), Is.EqualTo(1f), "Death owns its own opacity once it starts.");
            view.GetType().GetMethod("TickVisual").Invoke(view, new object[] { .45f });
            Assert.That(ReadOpacity(), Is.LessThan(1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProvokedPlayerAutomaticallyAttacksTheSourceWithoutExposingControls()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var match = (BattleMatch)simulation.GetType().GetField("match", Private).GetValue(simulation);
            var actor = match.Snapshot.GetUnit(match.Snapshot.CurrentActorId);
            Assert.That(actor.Slot.Side, Is.EqualTo(BattleSide.Ally));
            var mutableActor = match.GetType().GetField("actor", Private).GetValue(match);
            var statuses = (IList)mutableActor.GetType().GetProperty("Statuses").GetValue(mutableActor);
            var makeStatus = typeof(BattleStatusSnapshot).GetConstructor(Private, null,
                new[] { typeof(BattleStatusKind), typeof(int), typeof(int), typeof(string) }, null);
            const string provokerId = "enemy-2";
            statuses.Add(makeStatus.Invoke(new object[] { BattleStatusKind.Provoke, 1, 0, provokerId }));
            var snapshot = match.GetType().GetMethod("Capture", Private).Invoke(match, null);
            match.GetType().GetProperty("Snapshot").SetValue(match, snapshot);

            simulation.GetType().GetMethod("Refresh", Private).Invoke(simulation, null);
            Assert.That(map.transform.Find("Skills").gameObject.activeSelf, Is.False);
            Assert.That(host.TrySelectBattleTarget("enemy-1"), Is.False);
            Assert.That(match.Snapshot.LastCommandSequence, Is.Zero);
            simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { 0f });
            Assert.That(match.Snapshot.LastCommandSequence, Is.EqualTo(1UL));
            var events = (System.Collections.Generic.Queue<BattleFlowEvent>)simulation.GetType().GetField("playback", Private)
                .GetValue(simulation);
            var action = events.First(item => item.Kind == BattleFlowEventKind.Action);
            Assert.That(action.TargetId, Is.EqualTo(provokerId));
            Assert.That(action.SkillId, Is.EqualTo(actor.Form.SkillIds[0]));
            Assert.That(action.UseSoulBurn, Is.False);
            simulation.GetType().GetMethod("PresentReadyEvents", Private).Invoke(simulation, null);
            Assert.That(map.transform.Find("Skills").gameObject.activeSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProvokeUsesNativeIconAndDurationChangesFollowTheirPlaybackCue()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var present = simulation.GetType().GetMethod("Present", Private);
            var showDue = simulation.GetType().GetMethod("ShowDueFeedback", Private);
            var elapsed = simulation.GetType().GetField("actionElapsed", Private);
            simulation.GetType().GetMethod("SetPaused").Invoke(simulation, new object[] { true });
            var icons = map.transform.Find("Unit Status/Enemies/01 Front/Frame/Status Icons");
            var kind = (BattleStatusKind)Enum.Parse(typeof(BattleStatusKind), "Provoke");
            var durationChanged = (BattleFlowEventKind)Enum.Parse(typeof(BattleFlowEventKind), "StatusDurationChanged");
            var makeStatus = typeof(BattleStatusSnapshot).GetConstructor(Private, null,
                new[] { typeof(BattleStatusKind), typeof(int), typeof(int) }, null);
            var makeEvent = typeof(BattleFlowEvent).GetConstructors(Private).Single();

            void Deliver(BattleFlowEventKind eventKind, int turns)
            {
                var status = makeStatus.Invoke(new object[] { kind, turns, 0 });
                present.Invoke(simulation, new[] { makeEvent.Invoke(new object[] { 1UL, 1, eventKind, "ally-1",
                    "enemy-1", 0, null, null, BattleActionOrigin.Primary, kind, status, null, null,
                    BattleHitKind.None, null, false, null }) });
            }

            Deliver(BattleFlowEventKind.StatusApplied, 2);
            showDue.Invoke(simulation, null);
            var icon = icons.GetChild(0).GetComponent<Image>();
            var digit = icon.transform.Find("Remaining Turns").GetComponent<Image>();
            Assert.That(icon.sprite.name, Is.EqualTo("stic_provoke"));
            Assert.That(digit.sprite.name, Is.EqualTo("itxt_num2"));
            simulation.GetType().GetField("hitTimes", Private).SetValue(simulation, new[] { 0.5f, 1f });
            Deliver(durationChanged, 1);
            elapsed.SetValue(simulation, 0.75f);
            showDue.Invoke(simulation, null);
            Assert.That(digit.sprite.name, Is.EqualTo("itxt_num2"));
            elapsed.SetValue(simulation, 1f);
            showDue.Invoke(simulation, null);
            Assert.That(icons.GetChild(0).GetComponent<Image>(), Is.SameAs(icon));
            Assert.That(digit.sprite.name, Is.EqualTo("itxt_num1"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FiveOverheadStatusIconsWrapAfterThreeAndCompactWhenRemoved()
        {
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var view = ((IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation))["enemy-1"];
            var icons = map.transform.Find("Unit Status/Enemies/01 Front/Frame/Status Icons");
            var apply = view.GetType().GetMethod("ApplyStatus");
            foreach (var item in new[]
            {
                (BattleStatusKind.SpeedUp, 2), (BattleStatusKind.Barrier, 2), (BattleStatusKind.Immunity, 1),
                (BattleStatusKind.AttackUp, 1), (BattleStatusKind.DefenseUp, 1)
            })
            {
                var status = Activator.CreateInstance(typeof(BattleStatusSnapshot), Private, null,
                    new object[] { item.Item1, item.Item2, 1000 }, null);
                apply.Invoke(view, new[] { status });
            }

            AssertLayout(new[] { "stic_speed_up", "stic_protect", "stic_debuf_impossible", "stic_att_up", "stic_def_up" });
            Capture("status-wrap-three");
            view.GetType().GetMethod("RemoveStatus").Invoke(view, new object[] { BattleStatusKind.Immunity });
            AssertLayout(new[] { "stic_speed_up", "stic_protect", "stic_att_up", "stic_def_up" });
            yield return null;

            void AssertLayout(string[] expected)
            {
                var visible = icons.Cast<RectTransform>().Where(item => item.gameObject.activeSelf)
                    .OrderBy(item => item.anchoredPosition.y).ThenBy(item => item.anchoredPosition.x).ToArray();
                CollectionAssert.AreEqual(expected, visible.Select(item => item.GetComponent<Image>().sprite.name));
                Assert.That(visible[3].GetSiblingIndex(), Is.LessThan(visible[0].GetSiblingIndex()),
                    "Upper row tiles must render behind the overlapping turn digits of the lower row.");
                float step = visible[0].rect.width;
                for (int index = 0; index < visible.Length; index++)
                {
                    Vector2 position = visible[index].anchoredPosition;
                    Assert.That(position.x, Is.EqualTo(index % 3 * step).Within(.01f), $"Column for {expected[index]}");
                    Assert.That(position.y, Is.EqualTo(index / 3 * step).Within(.01f), $"Row for {expected[index]}");
                }
            }
        }

        [UnityTest]
        public IEnumerator SkillAndOverheadStatusHoldShowDetailsWithoutCasting()
        {
            var eventSystem = scene.GetRootGameObjects().Select(root => root.GetComponent<UnityEngine.EventSystems.EventSystem>())
                .Single(item => item);
            var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
            var ras = host.SimulationSnapshot.GetUnit("ally-1");
            simulation.GetType().GetField("displayedActor", Private).SetValue(simulation, ras);
            var s2 = map.Require<GameButton>("skill2");
            var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem)
            {
                pointerId = 4,
                button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
            };
            var skillHold = s2.GetComponent<GameLongPress>();
            Assert.That(skillHold, Is.Not.Null);
            skillHold.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(.55f);
            var inspection = map.transform.Find("Battle Inspection");
            Assert.That(inspection.gameObject.activeSelf, Is.True);
            var labels = inspection.GetComponentsInChildren<GameText>();
            Assert.That(labels.Any(label => label.text.Contains("获得灵魂") && label.font.faceInfo.familyName == "EpicSeven"), Is.True);
            Assert.That(labels.Any(label => label.text.Contains("冷却时间3回合")), Is.True);
            var skillCard = inspection.GetComponentsInChildren<BindingMap>().Single(item => item.Require<GameText>("title").text == "指挥一击");
            Assert.That(skillCard.Require<Image>("background").sprite.name, Is.EqualTo("_box_equip_0"));
            Assert.That(skillCard.Require<Image>("burnDivider").sprite.name, Is.EqualTo("_bar_2"));
            var primary = (RectTransform)skillCard.transform;
            Assert.That(inspection.GetComponent<Canvas>().overrideSorting, Is.True);
            var secondary = (RectTransform)inspection.GetComponentsInChildren<BindingMap>()
                .Single(item => item.Require<GameText>("title").text == "夹攻").transform;
            Assert.That(primary.anchoredPosition.x, Is.GreaterThan(secondary.anchoredPosition.x));
            var primaryBounds = BoundsIn(primary, (RectTransform)inspection);
            var relatedBounds = BoundsIn(secondary, (RectTransform)inspection);
            Assert.That(primaryBounds.yMax - relatedBounds.yMax, Is.InRange(26f, 32f),
                "The associated card starts at the main card's top edge.");
            var viewport = skillCard.Require<ScrollRect>("scroll").viewport;
            var contentBounds = BoundsIn(viewport, primary);
            Assert.That(contentBounds.xMin - primary.rect.xMin, Is.GreaterThanOrEqualTo(56f));
            Assert.That(primary.rect.xMax - contentBounds.xMax, Is.GreaterThanOrEqualTo(56f));
            Assert.That(contentBounds.yMin - primary.rect.yMin, Is.GreaterThanOrEqualTo(42f));
            Assert.That(skillCard.Require<Image>("background").material.name, Is.EqualTo("InfoPanelSkill"),
                "Skill panel must use reference alpha transfer with soft edges.");
            var heldSkill = (RectTransform)s2.transform;
            float heldSkillCenter = ((RectTransform)inspection).InverseTransformPoint(heldSkill.position).x;
            Assert.That(primary.anchoredPosition.x + primary.rect.width, Is.InRange(heldSkillCenter - 135f, heldSkillCenter),
                "Keep the skill card to the left of the selected skill.");
            Assert.That(labels.Any(label => label.text.Contains("灵魂燃烧效果")), Is.True);
            Assert.That(labels.Any(label => label.text == "夹攻"), Is.True);
            Assert.That(labels.Any(label => label.text.Contains("强化技能")), Is.True);
            skillHold.OnPointerUp(pointer);
            s2.OnPointerClick(pointer);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);
            Capture("long-press-skill", new Vector2Int(2608, 1200));
            inspection.Find("Dismiss Inspection").GetComponent<GameButton>().onClick.Invoke();
            Assert.That(inspection.gameObject.activeSelf, Is.False);

            var view = ((IDictionary)simulation.GetType().GetField("views", Private).GetValue(simulation))["ally-1"];
            var status = (BattleStatusSnapshot)Activator.CreateInstance(typeof(BattleStatusSnapshot),
                Private, null, new object[] { BattleStatusKind.DefenseUp, 2, 6000 }, null);
            view.GetType().GetMethod("ApplyStatus").Invoke(view, new object[] { status });
            var icon = map.transform.Find("Unit Status/Allies/01 Front/Frame/Status Icons").GetChild(0);
            var buffHold = icon.GetComponent<GameLongPress>();
            Assert.That(buffHold, Is.Not.Null);
            buffHold.OnPointerDown(pointer);
            yield return new WaitForSecondsRealtime(.55f);
            Assert.That(inspection.gameObject.activeSelf, Is.True);
            labels = inspection.GetComponentsInChildren<GameText>();
            Assert.That(labels.Any(label => label.text == "防御力提升"), Is.True);
            Assert.That(labels.Any(label => label.text.Contains("60%") && label.font.faceInfo.familyName == "EpicSeven"), Is.True);
            Assert.That(labels.Any(label => label.text.Contains("剩余")), Is.False);
            var effectCard = inspection.GetComponentsInChildren<BindingMap>().Single();
            var effectBounds = BoundsIn((RectTransform)effectCard.transform, (RectTransform)inspection);
            var iconBounds = BoundsIn((RectTransform)icon, (RectTransform)inspection);
            Assert.That(effectBounds.xMin - iconBounds.xMax, Is.InRange(60f, 85f));
            Assert.That(effectBounds.yMin - iconBounds.center.y, Is.InRange(0f, 34f));
            var binder = (InfoPanelBinder)simulation.GetType().GetField("inspection", Private).GetValue(simulation);
            var iconRect = (RectTransform)icon;
            Vector2 originalIconPosition = iconRect.anchoredPosition;
            iconRect.anchoredPosition += new Vector2(25f, 14f);
            binder.Tick();
            var movedEffect = BoundsIn((RectTransform)effectCard.transform, (RectTransform)inspection);
            var movedIcon = BoundsIn(iconRect, (RectTransform)inspection);
            Assert.That(movedEffect.xMin - effectBounds.xMin, Is.EqualTo(movedIcon.xMin - iconBounds.xMin).Within(.1f));
            Assert.That(movedEffect.yMin - effectBounds.yMin, Is.EqualTo(movedIcon.yMin - iconBounds.yMin).Within(.1f));
            iconRect.anchoredPosition = originalIconPosition;
            binder.Tick();
            buffHold.OnPointerUp(pointer);
            Capture("long-press-status", new Vector2Int(2608, 1200));
            host.PauseBattle();
            Assert.That(inspection.gameObject.activeSelf, Is.False);
        }

        private static Rect BoundsIn(RectTransform rect, RectTransform space)
        {
            var min = space.InverseTransformPoint(rect.TransformPoint(rect.rect.min));
            var max = space.InverseTransformPoint(rect.TransformPoint(rect.rect.max));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        void Capture(string name, Vector2Int? requestedSize = null)
        {
            string output = Environment.GetEnvironmentVariable("ASTRAL_UNIT_CAPTURE_DIR") ??
                Environment.GetEnvironmentVariable("ASTRAL_BATTLE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            var size = requestedSize ?? new Vector2Int(camera.pixelWidth, camera.pixelHeight);
            int width = size.x, height = size.y;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = camera.targetTexture; var previousActive = RenderTexture.active;
            var rect = camera.rect; var aspect = camera.aspect;
            var viewport = camera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", Private);
            try
            {
                camera.targetTexture = target;
                if (requestedSize.HasValue) apply.Invoke(viewport, new object[] { size, new Rect(Vector2.zero, size) });
                camera.Render();
                if (requestedSize.HasValue) apply.Invoke(viewport, new object[] { size, new Rect(Vector2.zero, size) });
                Canvas.ForceUpdateCanvases();
                var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
                simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { 0f });
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height),0,0); image.Apply(false,false);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                if (requestedSize.HasValue)
                    apply.Invoke(viewport, new object[] { new Vector2Int(Screen.width, Screen.height), Screen.safeArea });
                camera.rect = rect; camera.aspect = aspect;
                RenderTexture.active = previousActive; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(image);
            }
        }
        static float Width(RectTransform rect, Camera camera)
        {
            var a = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(rect.rect.xMin, 0, 0)));
            var b = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector3(rect.rect.xMax, 0, 0)));
            return Vector2.Distance(a,b);
        }
    }
}
#endif
