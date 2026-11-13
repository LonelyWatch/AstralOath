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
                Assert.That(Width(frame, camera) / width, Is.EqualTo(1.5f).Within(.03f));
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
            yield return new WaitForSecondsRealtime(.15f);
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
            var trail = marker.Find("Damage Trail").GetComponent<Image>();
            void Call(string method, params object[] args) => view.GetType().GetMethod(method).Invoke(view, args);
            Call("SetHealth", 1000L, 1000); Call("SetHealth", 350L, 1000);
            Call("TickVisual", .1f);
            Assert.That(hp.fillAmount, Is.InRange(.35f, 1f));
            Assert.That(trail.fillAmount, Is.GreaterThan(hp.fillAmount));
            Capture("damage-010");
            Call("TickVisual", .35f);
            Assert.That(hp.fillAmount, Is.EqualTo(.35f).Within(.001f));
            Assert.That(trail.fillAmount, Is.EqualTo(.35f).Within(.001f));
            Capture("damage-settled");
            Call("BeginDeath"); Call("TickVisual", .1f); Capture("death-010");
            Call("TickVisual", .2f); Capture("death-030");
            Call("TickVisual", .25f); Capture("death-055");
            Call("TickVisual", 10f);
            Assert.That(actor.gameObject.activeInHierarchy, Is.False, "The physical slot is removed after the visual tail.");
            Call("Show", true);
            Assert.That(actor.gameObject.activeInHierarchy, Is.True);
            // Revival in the same frame must bind only the new skeleton, never deferred-destroy objects.
            Call("BeginDeath"); Call("TickVisual", .3f);
            var current = (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Private).GetValue(actor);
            var block = new MaterialPropertyBlock(); current.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_PresentationOpacity"), Is.EqualTo(.75f).Within(.01f));
            host.RestartBattle(); yield return null;
            Assert.That(actor.gameObject.activeInHierarchy, Is.True);
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
            Vector3 expectedAura = shadow.position + body.transform.TransformVector(Vector3.up * .2f);
            Assert.That(Vector2.Distance(aura.position, expectedAura), Is.LessThan(.001f));
            Capture("status-barrier-full");

            var first = (RectTransform)icons.GetChild(0);
            float originalWidth = Width(first, camera);
            Vector2 originalPosition = first.position;
            Vector3 auraLocal = aura.localPosition;
            actor.transform.position += new Vector3(.7f, 1.25f, 0f);
            actor.transform.localScale *= 1.25f;
            view.GetType().GetMethod("LateTick").Invoke(view, new object[] { camera });
            Assert.That((Vector2)first.position, Is.Not.EqualTo(originalPosition));
            Assert.That(Width(first, camera) / originalWidth, Is.EqualTo(1.25f).Within(.03f));
            Assert.That(Vector2.Distance(aura.position, body.transform.TransformPoint(auraLocal)), Is.LessThan(.001f),
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

            Present(BattleFlowEventKind.StatusApplied, 90, BattleStatusKind.Barrier, 2);
            Call("ShowDueFeedback");
            simulation.GetType().GetField("hitTimes", Private).SetValue(simulation, new[] { 1f, 2f, 3f });
            Present(BattleFlowEventKind.BarrierAbsorbed, 90);
            Present(BattleFlowEventKind.StatusRemoved, statusKind: BattleStatusKind.Barrier);
            Present(BattleFlowEventKind.Damage, 60);
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

            ShowAt(1f);
            Assert.That(aura.gameObject.activeSelf, Is.True, "The first hit only spends part of the shield.");
            Assert.That(healthValue.GetValue(view), Is.EqualTo(previousHealth));
            Assert.That(friendlyIcons.childCount, Is.Zero);
            CollectionAssert.Contains(VisibleIcons(), "stic_att_up");
            ShowAt(2f);
            Assert.That(aura.gameObject.activeSelf, Is.False);
            Assert.That(healthValue.GetValue(view), Is.EqualTo(previousHealth - 10));
            Assert.That(friendlyIcons.childCount, Is.Zero);
            CollectionAssert.Contains(VisibleIcons(), "stic_att_up");
            ShowAt(3f);
            Assert.That(healthValue.GetValue(view), Is.EqualTo(previousHealth - 60));
            Assert.That(friendlyIcons.GetChild(0).GetComponent<Image>().sprite.name, Is.EqualTo("stic_def_up"));
            CollectionAssert.DoesNotContain(VisibleIcons(), "stic_att_up");
            yield return null;
        }

        void Capture(string name)
        {
            string output = Environment.GetEnvironmentVariable("ASTRAL_UNIT_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var camera = map.GetComponentInParent<Canvas>().worldCamera;
            var target = RenderTexture.GetTemporary(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previous = camera.targetTexture; var previousActive = RenderTexture.active;
            var rect = camera.rect; var aspect = camera.aspect;
            try
            {
                camera.targetTexture = target; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = 1920f/1080f;
                Canvas.ForceUpdateCanvases();
                var simulation = typeof(BattleHudPreview).GetField("simulation", Private).GetValue(host);
                simulation.GetType().GetMethod("Tick").Invoke(simulation, new object[] { 0f });
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply(false,false);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; camera.rect = rect; camera.aspect = aspect;
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
