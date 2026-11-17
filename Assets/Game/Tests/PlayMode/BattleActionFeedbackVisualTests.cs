#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    // Event-fixture visual evidence, not a claim of a live-server or original-video match.
    public sealed class BattleActionFeedbackVisualTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private Scene scene;
        private object feedback, onlineHud;

        [UnityTest]
        public IEnumerator NativeActionFlashesAndBothSoulBurnAvatarsRenderAndClear()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null; yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            var simulation = Field(host, "simulation");
            Call(simulation, "SetPaused", true);
            var assets = (BattleSkillPresentationAsset[])Field(host, "skillAssets");
            Assert.That(assets.Any(a => a.source == "effect/stse_chain.cfx"), Is.True, "Regenerate BattleScene feedback asset references first.");
            feedback = Create("BattleActionFeedback", (object)assets);
            var map = roots.SelectMany(x => x.GetComponentsInChildren<BindingMap>()).Single();
            var onlineAssets = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>(
                "Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
            var copyType = typeof(BattleOnlineTransitionAssets).GetNestedType("Copy", BindingFlags.NonPublic);
            var copy = copyType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { Field(onlineAssets, "zhHans") });
            var releaseClip = (AudioClip)Field(onlineAssets, "soulBurnRelease");
            Assert.That(releaseClip, Is.Not.Null, "Online Battle must reference the original release clip.");
            Assert.That(releaseClip.name, Is.EqualTo("SoulBurnRelease"));
            Assert.That(releaseClip.length, Is.EqualTo(1.6f).Within(.01f));
            var sprites = ((Sprite[])Field(host, "battleSprites")).GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
            onlineHud = Create("BattleOnlineHud", map, new BattleOnlinePlayers("本方测试", "对方测试", "c1001", "c1005"), copy, sprites, onlineAssets, assets);
            var actor = roots.SelectMany(x => x.GetComponentsInChildren<BattlePreviewActor>()).First(x => x.name.StartsWith("Ras ", StringComparison.Ordinal));
            ulong sequence = 1;
            foreach (BattleSide side in new[] { BattleSide.Ally, BattleSide.Enemy })
            {
                var item = Event(sequence++, BattleFlowEventKind.Action, BattleActionOrigin.Primary, true);
                Assert.That(Call(feedback, "PresentAction", item, actor), Is.True);
                Call(onlineHud, "PlaySoulBurn", side);
                Call(onlineHud, "Tick", .25f);
                string sideName = side == BattleSide.Ally ? "Local" : "Opponent";
                var anchor = map.transform.parent.Find("Online PvP HUD/" + sideName + " Soul Burn Anchor");
                Assert.That(anchor.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled && r.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0), Is.True);
                yield return Capture("fixture-soul-burn-" + sideName.ToLowerInvariant());
                Call(onlineHud, "Tick", 2f);
                Assert.That(anchor.localScale.x, Is.GreaterThan(1.3f),
                    "The sustained fire is wider than the original-sized intro rays at their common origin.");
                Assert.That(anchor.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty,
                    "The authored avatar loop lasts until the action chain settles.");
                yield return Capture("fixture-soul-burn-" + sideName.ToLowerInvariant() + "-loop");
                Call(onlineHud, "FinishSoulBurn"); Call(onlineHud, "Tick", .2f);
                Assert.That(anchor.GetComponentsInChildren<MeshFilter>().Any(m =>
                    m.gameObject.name.StartsWith("HUD particle ", StringComparison.Ordinal) && m.sharedMesh.vertexCount > 0), Is.True,
                    "Native particles remain visible after the short Spine fade.");
                yield return Capture("fixture-soul-burn-" + sideName.ToLowerInvariant() + "-drain");
                Call(onlineHud, "Tick", 2f);
                Assert.That(anchor.GetComponentsInChildren<MeshRenderer>(), Is.Empty);
            }
            foreach (var kind in new[] { BattleActionOrigin.Counter, BattleActionOrigin.DualAttack, BattleActionOrigin.ExtraAttack })
            {
                var item = Event(sequence++, BattleFlowEventKind.Action, kind, false);
                Assert.That(Call(feedback, "PresentAction", item, actor), Is.False);
                if (kind == BattleActionOrigin.Counter)
                {
                    Call(feedback, "Tick", .10f);
                    yield return Capture("fixture-action-counter-onset");
                    Call(feedback, "Tick", .08f);
                }
                else Call(feedback, "Tick", .18f);
                var actionRenderers = actor.GetComponentsInChildren<MeshRenderer>();
                Assert.That(actionRenderers.Any(r => r.transform.parent.name == "stse_chain"), Is.True);
                Assert.That(actionRenderers.Any(r => r.transform.parent.name == "stse_counter"), Is.False,
                    "Counter action onset uses lightning; the blue fists belong to status application.");
                yield return Capture("fixture-action-" + kind.ToString().ToLowerInvariant());
                Call(feedback, "Clear", 0UL);
            }
            Call(feedback, "PresentExtraTurn", Event(sequence, BattleFlowEventKind.ExtraTurnGranted, BattleActionOrigin.Primary, false), actor);
            Call(feedback, "Tick", .22f);
            yield return Capture("fixture-extra-turn");
            Call(feedback, "Clear", sequence);
            Assert.That(actor.GetComponentsInChildren<MeshRenderer>().Any(r => r.transform.parent.name.StartsWith("stse_", StringComparison.Ordinal)), Is.False);
        }

        private IEnumerator Capture(string name)
        {
            string directory = Environment.GetEnvironmentVariable("ASTRAL_ACTION_FEEDBACK_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                "Native feedback screenshots require a graphics device, not an interactive Game view.");
            Directory.CreateDirectory(directory);
            const int width = 1920, height = 1080;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            var canvas = host.GetComponent<Canvas>();
            var scaler = host.GetComponent<CanvasScaler>();
            var camera = canvas.worldCamera;
            Assert.That(camera, Is.Not.Null);
            var cameras = roots.SelectMany(x => x.GetComponentsInChildren<Camera>())
                .Where(x => x.enabled && !x.targetTexture).OrderBy(x => x.depth).ToArray();
            Assert.That(cameras, Does.Contain(camera));
            var original = cameras.Select(x => (x.targetTexture, x.rect, x.aspect)).ToArray();
            float originalScale = canvas.scaleFactor;
            bool originalScalerEnabled = scaler.enabled;
            var viewport = camera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", Instance);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var active = RenderTexture.active;
            Texture2D image = null;
            try
            {
                foreach (var item in cameras)
                {
                    item.targetTexture = target;
                    item.rect = new Rect(0, 0, 1, 1);
                    item.aspect = width / (float)height;
                }
                // A batch Editor does not pump WaitForEndOfFrame. Drive the exact
                // capture projection and render explicitly without a visible Game view.
                scaler.enabled = false;
                canvas.scaleFactor = 1f;
                apply.Invoke(viewport, new object[] { new Vector2Int(width, height), new Rect(0, 0, width, height) });
                Canvas.ForceUpdateCanvases();
                // One ordinary player-loop tick updates projected status anchors after
                // the viewport change; unlike WaitForEndOfFrame this runs in batchmode.
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var item in cameras) item.Render();
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                Debug.Log("ASTRAL_ACTION_FEEDBACK_CAPTURE " + name);
            }
            finally
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].targetTexture = original[i].targetTexture;
                    cameras[i].rect = original[i].rect;
                    cameras[i].aspect = original[i].aspect;
                }
                canvas.scaleFactor = originalScale;
                scaler.enabled = originalScalerEnabled;
                apply.Invoke(viewport, new object[] { new Vector2Int(Screen.width, Screen.height), Screen.safeArea });
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                if (image) Object.Destroy(image);
            }
        }
        private static BattleFlowEvent Event(ulong sequence, BattleFlowEventKind kind, BattleActionOrigin origin, bool burn)
        {
            var ctor = typeof(BattleFlowEvent).GetConstructors(Instance).Single();
            var values = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(ulong) ? (object)sequence : p.ParameterType == typeof(int) ? 1 : (object)kind).ToArray();
            var named = new Dictionary<string, object> { ["sequence"] = sequence, ["kind"] = kind, ["sourceId"] = "ally-1", ["targetId"] = "ally-1", ["origin"] = origin, ["useSoulBurn"] = burn };
            foreach (var p in ctor.GetParameters()) if (named.TryGetValue(p.Name, out var value)) values[p.Position] = value;
            return (BattleFlowEvent)ctor.Invoke(values);
        }
        private static object Create(string type, params object[] args) => Activator.CreateInstance(Type.GetType("AstralOath.Client.BattleScene." + type + ", AstralOath.Client.BattleScene", true), Instance, null, args, null);
        private static object Field(object value, string name) => value.GetType().GetField(name, Instance).GetValue(value);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Instance).Invoke(value, args);
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            (onlineHud as IDisposable)?.Dispose(); (feedback as IDisposable)?.Dispose();
            onlineHud = feedback = null;
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
