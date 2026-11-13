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
using UnityEditor.SceneManagement;
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
            scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
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
            var actor = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BattlePreviewActor>()).Single(x => x.name == "Ice Slime");
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
