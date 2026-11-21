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
                string sideName = side == BattleSide.Ally ? "Local" : "Opponent";
                var anchor = map.transform.parent.Find("Online PvP HUD/" + sideName + " Soul Burn Anchor");
                Call(onlineHud, "Tick", 1f / 30f);
                yield return Capture("fixture-soul-burn-" + sideName.ToLowerInvariant() + "-onset");
                Call(onlineHud, "Tick", .25f - 1f / 30f);
                Assert.That(anchor.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled && r.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0), Is.True);
                yield return Capture("fixture-soul-burn-" + sideName.ToLowerInvariant());
                // P2 13:41.2, 1920x1080. The white ring provides an independent scale
                // reference even when a beam endpoint already extends outside the camera.
                var ringRadius = SoulBurnAtlasRadius(anchor, new Rect(355, 2, 314, 314)) * .943f;
                Assert.That(ringRadius, Is.InRange(815f, 1100f), "Visible intro ring radius in 1080-high HUD units.");
                Call(onlineHud, "Tick", 2f);
                Assert.That(SoulBurnAtlasRadius(anchor, new Rect(842, 227, 100, 100)), Is.InRange(75f, 120f),
                    "The sustained avatar glow has its own scale after the opening flash.");
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
            foreach (var kind in new[] { BattleActionOrigin.Counter, BattleActionOrigin.DualAttack, BattleActionOrigin.ExtraAttack, BattleActionOrigin.Passive })
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

        [UnityTest]
        public IEnumerator ActualHealingAndAttackBuffRenderAtTheirOwnSourceBones()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            var assets = (BattleSkillPresentationAsset[])Field(host, "skillAssets");
            Assert.That(assets.Any(a => a.source == "effect/heal_hp_01.cfx"), Is.True);
            feedback = Create("BattleActionFeedback", (object)assets);
            var actors = roots.SelectMany(x => x.GetComponentsInChildren<BattlePreviewActor>()).ToArray();
            var actor = actors.First(x => x.name.StartsWith("Ras ", StringComparison.Ordinal));
            var other = actors.First(x => x != actor);
            BattleFlowEvent Heal(ulong sequence, int amount) =>
                Event(sequence, BattleFlowEventKind.Healing, BattleActionOrigin.Primary, false, amount);
            Call(feedback, "PresentHealing", Heal(1, 0), actor);
            Transform HealFx(BattlePreviewActor recipient) => recipient.transform.Find("Native Feedback heal_hp_01");
            Assert.That(HealFx(actor), Is.Null);
            Call(feedback, "PresentHealing", Heal(2, 123), actor);
            Call(feedback, "PresentHealing", Heal(3, 12), other);
            Call(feedback, "PresentStatusGain", Event(4, BattleFlowEventKind.StatusApplied,
                BattleActionOrigin.Primary, false, status: BattleStatusKind.AttackUp), actor);
            Call(feedback, "Tick", .18f);
            foreach (var recipient in new[] { actor, other })
            {
                var fx = HealFx(recipient);
                Assert.That(fx && fx.gameObject.activeInHierarchy, Is.True);
                Assert.That(fx.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.vertexCount), Is.GreaterThan(0));
                AssertAtBone(fx, recipient, "target");
            }
            var sword = actor.transform.Find("Native Feedback stse_attackpower_up");
            Assert.That(sword && sword.gameObject.activeInHierarchy, Is.True);
            AssertAtBone(sword, actor, "top");
            yield return Capture("healing-and-attack-up");
            Call(feedback, "Tick", 4f);
            Assert.That(HealFx(actor).gameObject.activeSelf || sword.gameObject.activeSelf, Is.False);
            Call(feedback, "PresentHealing", Heal(2, 123), actor);
            Assert.That(HealFx(actor).gameObject.activeSelf, Is.False, "Replaying the same event must not flash again.");

            var simulation = Field(host, "simulation");
            var unit = ((IDictionary)Field(simulation, "views"))["ally-1"];
            var health = (Image)unit.GetType().GetProperty("Health", Instance).GetValue(unit);
            Call(simulation, "ShowHealing", Heal(12, 3402));
            Call(Field(simulation, "actionFeedback"), "Tick", .18f);
            Call(Field(simulation, "turnHud"), "TickFeedback", .18f);
            var numbers = host.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Battle Feedback");
            var glyphs = numbers.Find("ally-1").GetComponentsInChildren<RawImage>();
            Assert.That(glyphs.Length, Is.EqualTo(5), "The ordinary healing event must show +3402.");
            Assert.That(glyphs.All(glyph => glyph.canvas.rootCanvas == health.canvas.rootCanvas), Is.True);
            var healedActor = (BattlePreviewActor)unit.GetType().GetProperty("Actor", Instance).GetValue(unit);
            var activeTreatment = healedActor.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "Native Feedback heal_hp_01");
            var treatment = activeTreatment.GetComponentsInChildren<MeshRenderer>();
            Assert.That(treatment.Length, Is.EqualTo(4));
            Assert.That(health.canvas.rootCanvas.sortingOrder, Is.GreaterThan(treatment.Max(renderer => renderer.sortingOrder)),
                "The health and feedback HUD must remain in front of the treatment.");
            yield return Capture("healing-with-hp-and-digits");

            void AssertAtBone(Transform effect, BattlePreviewActor recipient, string bone)
            {
                object[] args = { bone, Vector3.zero };
                Assert.That(Call(recipient, "TryGetPresentationAnchor", args), Is.True);
                Assert.That(Vector3.Distance(effect.position, (Vector3)args[1]), Is.LessThan(.001f));
            }
        }

        private static float SoulBurnAtlasRadius(Transform anchor, Rect region)
        {
            var mesh = anchor.GetComponentsInChildren<MeshFilter>().Single(filter =>
                filter.GetComponent<MeshRenderer>().sharedMaterials.Any(material => material.mainTexture &&
                    material.mainTexture.name == "ui_livepvp_soulburn_eff"));
            var points = mesh.sharedMesh.vertices;
            var uvs = mesh.sharedMesh.uv;
            float cornerDistance = 0;
            for (int i = 0; i < uvs.Length; i++)
            {
                var texel = new Vector2(uvs[i].x * 944, (1f - uvs[i].y) * 398);
                if (texel.x < region.xMin - .1f || texel.x > region.xMax + .1f ||
                    texel.y < region.yMin - .1f || texel.y > region.yMax + .1f) continue;
                var offset = anchor.parent.InverseTransformVector(mesh.transform.TransformPoint(points[i]) - anchor.position);
                cornerDistance = Mathf.Max(cornerDistance, ((Vector2)offset).magnitude);
            }
            return cornerDistance / Mathf.Sqrt(2);
        }

        private IEnumerator Capture(string name) => CaptureScene(scene, name, Environment.GetEnvironmentVariable("ASTRAL_ACTION_FEEDBACK_CAPTURE_DIR"));

        [UnityTest]
        public IEnumerator FrontHeroOccludesHealingFromARearHero()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            var assets = (BattleSkillPresentationAsset[])Field(host, "skillAssets");
            feedback = Create("BattleActionFeedback", (object)assets);
            var actors = roots.SelectMany(x => x.GetComponentsInChildren<BattlePreviewActor>()).ToArray();
            Renderer Body(BattlePreviewActor actor) => (Renderer)typeof(BattlePreviewActor).GetProperty("PresentationRenderer", Instance).GetValue(actor);
            var rear = actors.First(actor => Body(actor).sortingOrder == 54);
            var front = actors.First(actor => Body(actor).sortingOrder == 55);
            var rearBody = Body(rear);
            var frontBody = Body(front);
            var originalPosition = frontBody.transform.position;
            var sample = new GameObject("Healing overlap camera", typeof(Camera)).GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(320, 320, 24);
            var originalTarget = RenderTexture.active;
            var image = new Texture2D(320, 320, TextureFormat.RGBA32, false);
            try
            {
                // Two real adjacent formation depths, with the bodies overlapping as they do on return from an attack.
                frontBody.transform.position += rearBody.bounds.center - frontBody.bounds.center +
                    Vector3.right * rearBody.bounds.extents.x * .35f;
                Call(feedback, "PresentHealing", Event(9, BattleFlowEventKind.Healing, BattleActionOrigin.Primary, false, 3402), rear);
                Call(feedback, "Tick", .24f);
                var fx = rear.transform.Find("Native Feedback heal_hp_01");
                var emitters = fx.GetComponentsInChildren<MeshRenderer>();
                Assert.That(emitters.Length, Is.EqualTo(4));
                Assert.That(emitters.All(renderer => renderer.sortingLayerID == rearBody.sortingLayerID &&
                    renderer.sortingOrder == rearBody.sortingOrder), Is.True, "Each track belongs to its recipient's formation depth.");
                var solid = emitters.Single(renderer => renderer.sharedMaterial.mainTexture.name == "heal_cross_01");
                Assert.That(solid.sharedMaterial.GetInt("_DstBlend"), Is.EqualTo((int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                Assert.That(emitters.Where(renderer => renderer != solid).All(renderer =>
                    renderer.sharedMaterial.GetInt("_DstBlend") == (int)UnityEngine.Rendering.BlendMode.One &&
                    renderer.transform.position.z > solid.transform.position.z &&
                    renderer.transform.position.z < rearBody.bounds.center.z), Is.True, "Soft light must sit between the filled cross and the recipient.");
                const int layer = 28;
                rearBody.gameObject.layer = frontBody.gameObject.layer = layer;
                foreach (var emitter in emitters) emitter.gameObject.layer = layer;
                sample.enabled = false;
                sample.transform.position = rearBody.bounds.center + Vector3.back * 10;
                sample.orthographic = true;
                sample.orthographicSize = rearBody.bounds.extents.y * 1.22f;
                sample.cullingMask = 1 << layer;
                sample.clearFlags = CameraClearFlags.SolidColor;
                sample.backgroundColor = Color.clear;
                sample.targetTexture = target;
                Color32[] Frame(bool showRear, bool showFront, bool heal, string name = null)
                {
                    rearBody.enabled = showRear;
                    frontBody.enabled = showFront;
                    fx.gameObject.SetActive(heal);
                    sample.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 320, 320), 0, 0);
                    image.Apply();
                    var dir = Environment.GetEnvironmentVariable("ASTRAL_ACTION_FEEDBACK_CAPTURE_DIR");
                    if (name != null && !string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(Path.Combine(dir, name + ".png"), image.EncodeToPNG());
                    }
                    return image.GetPixels32();
                }
                var frontOnly = Frame(false, true, false);
                var rearOnly = Frame(true, false, false, "recipient");
                var healedRear = Frame(true, false, true, "healed-recipient");
                var both = Frame(true, true, false, "front-and-rear");
                var healedBoth = Frame(true, true, true, "front-and-healed-rear");
                int Difference(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
                var lit = Enumerable.Range(0, both.Length).Where(i => Difference(rearOnly[i], healedRear[i]) > 40).ToArray();
                Assert.That(lit.Count(i => rearOnly[i].a >= 250), Is.GreaterThan(200), "The treatment should visibly light the recipient.");
                var covered = lit.Where(i => frontOnly[i].a >= 254).ToArray();
                Assert.That(covered.Length, Is.GreaterThan(200), "The foreground silhouette must intersect the treatment.");
                int leaks = covered.Count(i => Difference(both[i], healedBoth[i]) > 5);
                TestContext.WriteLine($"Rear treatment changed {leaks} of {covered.Length} opaque foreground pixels.");
                Assert.That(leaks, Is.LessThanOrEqualTo(covered.Length / 100), "A rear actor's treatment is drawing onto an opaque nearer hero.");
                Assert.That(emitters.All(renderer => renderer.sortingOrder < host.GetComponent<Canvas>().sortingOrder), Is.True);
            }
            finally
            {
                frontBody.transform.position = originalPosition;
                rearBody.enabled = frontBody.enabled = true;
                RenderTexture.active = originalTarget;
                sample.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(sample.gameObject);
            }
        }

        internal static IEnumerator CaptureScene(Scene scene, string name, string directory)
        {
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
        private static BattleFlowEvent Event(ulong sequence, BattleFlowEventKind kind, BattleActionOrigin origin, bool burn,
            int amount = 0, BattleStatusKind? status = null)
        {
            var ctor = typeof(BattleFlowEvent).GetConstructors(Instance).Single();
            var values = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(ulong) ? (object)sequence : p.ParameterType == typeof(int) ? 1 : (object)kind).ToArray();
            var named = new Dictionary<string, object> { ["sequence"] = sequence, ["kind"] = kind, ["sourceId"] = "ally-1", ["targetId"] = "ally-1", ["origin"] = origin, ["useSoulBurn"] = burn };
            named["amount"] = amount;
            if (status.HasValue)
                named["status"] = Activator.CreateInstance(typeof(BattleStatusSnapshot), Instance, null, new object[] { status.Value, 2, 10 }, null);
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
