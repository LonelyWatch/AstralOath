#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaBattlePlaybackTests
    {
        private static readonly BindingFlags Members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        private const string CatalogPath = "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json";

        [TestCase(true)]
        [TestCase(false)]
        public void ReceivesFullServerBattleAndPreservesAuthorityForBothSeats(bool host)
        {
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath).text);
            var match = BattlePreviewFactory.Create("draft-match", 7723, catalog,
                new[] { "c1001", "c1002", "c1008", "c1005" },
                new[] { "c1003", "c1004", "c1062", "c1007" }, duel: true);
            var before = match.Snapshot;
            var actor = before.GetUnit(before.CurrentActorId);
            var skill = actor.Form.SkillIds[0];
            var action = new BattleAiPolicy(catalog).ChooseTarget(before, skill);
            var result = match.Submit(new BattleSkillCommand(before.BattleId, before.RulesVersion, 1, "turn-1",
                before.CurrentActorId, action.SkillId, action.TargetId));
            Assert.That(result.Status, Is.EqualTo(BattleCommandStatus.Accepted));

            var sessionType = typeof(BattleOnlineLaunch).Assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineSession", true);
            using var session = (IDisposable)Activator.CreateInstance(sessionType, Members, null,
                new object[] { new Uri("http://localhost"), "room", "draft-match", "test-hash", "token", host }, null);
            sessionType.GetField("heroes", Members).SetValue(session, catalog.heroes.ToDictionary(row => row.id, StringComparer.Ordinal));
            var wire = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(
                new { ContentHash = "test-hash", Snapshot = result.Snapshot, SnapshotReset = false, Events = result.Events },
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
            var update = sessionType.GetMethod("Read", Members).Invoke(session, new object[] { wire, true, before.LastEventSequence });
            Assert.That(update.GetType().GetField("Reset", Members).GetValue(update), Is.False);
            var snapshot = (BattleSnapshot)update.GetType().GetField("Snapshot", Members).GetValue(update);
            var events = (BattleFlowEvent[])update.GetType().GetField("Events", Members).GetValue(update);
            var mine = snapshot.Units.Single(unit => unit.Slot.Side == BattleSide.Ally && unit.Slot.Position == 1);
            Assert.That(mine.DefinitionId, Is.EqualTo(host ? "c1001" : "c1003"));
            Assert.That(mine.InstanceId, Is.EqualTo(host ? "ally-1" : "enemy-1"), "The server's IDs still determine legal commands.");
            Assert.That(snapshot.AllySoul, Is.EqualTo(host ? result.Snapshot.AllySoul : result.Snapshot.EnemySoul));
            Assert.That(snapshot.LastEventSequence, Is.EqualTo(result.Snapshot.LastEventSequence));
            Assert.That(events.Select(item => item.Sequence), Is.EqualTo(result.Events.Select(item => item.Sequence)));

            var gap = sessionType.GetMethod("Read", Members).Invoke(session, new object[] { wire, true, 0UL });
            Assert.That(gap.GetType().GetField("Reset", Members).GetValue(gap), Is.True);
        }

        [UnityTest]
        public IEnumerator NativeGateAndIgnitionPlayAndReleaseTheOverlay()
        {
            var assembly = typeof(BattleOnlineLaunch).Assembly;
            var resource = Resources.Load("AstralOathOnlineTransition");
            Assert.That(resource, Is.Not.Null);
            var assetType = resource.GetType();
            var close = (TextAsset)assetType.GetField("closeCfx", Members).GetValue(resource);
            var open = (TextAsset)assetType.GetField("openCfx", Members).GetValue(resource);
            Assert.That(close.name, Is.EqualTo("treff_pd_pvp_battle_close.cfx"));
            Assert.That(open.name, Is.EqualTo("treff_pd_pvp_battle_open.cfx"));
            var closeAudio = (AudioClip)assetType.GetField("doorClose", Members).GetValue(resource);
            var openAudio = (AudioClip)assetType.GetField("doorOpen", Members).GetValue(resource);
            Assert.That(closeAudio && closeAudio.name == "DoorClose" && closeAudio.length > 1.5f, Is.True);
            Assert.That(openAudio && openAudio.name == "DoorOpen" && openAudio.length > 1.5f, Is.True);
            var backdrop = (Sprite)assetType.GetField("arenaBackdrop", Members).GetValue(resource);
            Assert.That(backdrop && backdrop.name == "rta_021", Is.True);
            var labels = assetType.GetNestedType("Copy", BindingFlags.NonPublic);
            var copy = labels.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new object[] { assetType.GetField("zhHans", Members).GetValue(resource) });
            Assert.That(labels.GetField("arenaTitle").GetValue(copy), Is.EqualTo("决斗场"));
            var type = assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineGate", true);
            GameObject listener = UnityEngine.Object.FindObjectOfType<AudioListener>() ?
                null : new GameObject("Test Audio Listener", typeof(AudioListener));
            try
            {
                using var gate = (IDisposable)Activator.CreateInstance(type, Members, null, new[] { resource }, null);
                var stage = (GameObject)type.GetField("stage", Members).GetValue(gate);
                var players = (System.Collections.Generic.Dictionary<string, AstralOath.Client.Skeletons.ISkeletonPlayer>)
                    type.GetField("players", Members).GetValue(gate);
                foreach (var shot in new[]
                {
                    (method: "CloseAsync", layer: "treff_pd_pvp_battle_close_f", time: .60f, file: "pvp-gate-closing"),
                    (method: "OpenAsync", layer: "treff_pd_pvp_battle_open_f", time: .35f, file: "pvp-gate-opening"),
                    (method: "IgniteAsync", layer: "pvp_ready_font_fast", time: .35f, file: "pvp-ignition")
                })
                {
                    var task = (Task)type.GetMethod(shot.method, Members).Invoke(gate, new object[] { CancellationToken.None });
                    float deadline = Time.realtimeSinceStartup + 15f;
                    bool captured = false;
                    while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                    {
                        if (!captured && players[shot.layer].Time >= shot.time)
                        {
                            // Wait for the transition camera to render the sampled pose.
                            yield return null;
                            CaptureTransition(gate, type, shot.file, false);
                            captured = true;
                        }
                        yield return null;
                    }
                    Assert.That(captured, Is.True, "The source animation must reach the captured frame.");
                    Assert.That(task.IsCompleted, Is.True, shot.method);
                    task.GetAwaiter().GetResult();
                    if (shot.method == "CloseAsync")
                    {
                        yield return null;
                        var active = stage.GetComponentsInChildren<MeshRenderer>().Select(renderer => renderer.name);
                        Assert.That(active, Is.EquivalentTo(new[]
                        {
                            "treff_pd_pvp_battle_close_b", "treff_pd_pvp_battle_close_f", "treff_pd_pvp_battle_close_m"
                        }), "The three native loop layers must cover both scenes until reveal.");
                        CaptureTransition(gate, type, "pvp-gate-closed", true);
                    }
                }
            }
            finally
            {
                if (listener) UnityEngine.Object.Destroy(listener);
            }
            yield return null;
            Assert.That(GameObject.Find("Online Battle Gate"), Is.Null);
        }

        [UnityTest]
        public IEnumerator IgnitionCoversTheCanvasAtWideAndTabletAspectRatios()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required to check the transition camera.");

            var assembly = typeof(BattleOnlineLaunch).Assembly;
            var type = assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineGate", true);
            var resource = Resources.Load("AstralOathOnlineTransition");
            using var cancellation = new CancellationTokenSource();
            using var gate = (IDisposable)Activator.CreateInstance(type, Members, null, new[] { resource }, null);
            var overlay = (GameObject)type.GetField("overlay", Members).GetValue(gate);
            var effectCamera = (Camera)type.GetField("camera", Members).GetValue(gate);
            var canvas = overlay.GetComponent<Canvas>();
            var picture = overlay.GetComponentInChildren<UnityEngine.UI.RawImage>();
            var viewport = new GameObject("Test viewport", typeof(Camera));
            var outputCamera = viewport.GetComponent<Camera>();
            outputCamera.enabled = false;
            var task = (Task)type.GetMethod("IgniteAsync", Members).Invoke(gate, new object[] { cancellation.Token });
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = outputCamera;
                foreach (var size in new[] { new Vector2Int(1920, 900), new Vector2Int(2240, 960), new Vector2Int(1440, 1080) })
                {
                    var output = RenderTexture.GetTemporary(size.x, size.y, 0);
                    outputCamera.targetTexture = output;
                    try
                    {
                        Canvas.ForceUpdateCanvases();
                        yield return null;
                        yield return null;
                        var target = effectCamera.targetTexture;
                        float outputAspect = size.x / (float)size.y;
                        Assert.That(target.width / (float)target.height, Is.EqualTo(outputAspect).Within(.003f),
                            "A fixed-size intermediate surface cuts off the native translucent effects inside the visible frame.");
                        Assert.That(effectCamera.aspect, Is.EqualTo(outputAspect).Within(.003f));
                        Assert.That(picture.texture, Is.SameAs(target));
                        var bounds = picture.rectTransform.rect.size;
                        var rootBounds = ((RectTransform)canvas.transform).rect.size;
                        Assert.That(bounds.x, Is.EqualTo(rootBounds.x).Within(1f));
                        Assert.That(bounds.y, Is.EqualTo(rootBounds.y).Within(1f));
                        yield return null;
                        Assert.That(effectCamera.targetTexture, Is.SameAs(target), "Avoid reallocating if the output size is stable.");
                    }
                    finally
                    {
                        outputCamera.targetTexture = null;
                        RenderTexture.ReleaseTemporary(output);
                    }
                }
            }
            finally
            {
                cancellation.Cancel();
                UnityEngine.Object.Destroy(viewport);
            }
        }

        private static void CaptureTransition(IDisposable gate, Type gateType, string filename, bool requireOpaqueCenter)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            string folder = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder) && !requireOpaqueCenter) return;
            var target = (RenderTexture)gateType.GetField("texture", Members).GetValue(gate);
            var previous = RenderTexture.active;
            bool full = !string.IsNullOrEmpty(folder);
            int width = full ? target.width : 4;
            int height = full ? target.height : 4;
            var sample = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                sample.ReadPixels(new Rect((target.width - width) / 2, (target.height - height) / 2, width, height), 0, 0);
                if (requireOpaqueCenter)
                {
                    var center = sample.GetPixel(width / 2, height / 2);
                    Assert.That(center.a, Is.GreaterThan(.6f),
                        "The native closed gate must render opaque pixels over the previous screen.");
                    Assert.That(center.b, Is.GreaterThan(center.r + .08f), "The closed gate must show the authored blue diamond.");
                }
                if (full)
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, filename + ".png"), sample.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(sample);
            }
        }

        [UnityTest]
        public IEnumerator BothTeamsLandWithTheGroundShadowHeldAndReleaseAllEffects()
        {
            var scene = EditorSceneManager.LoadSceneInPlayMode(BattleOnlineLaunch.ScenePath,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
            try
            {
                yield return null;
                var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
                var actors = units.GetComponentsInChildren<BattlePreviewActor>(true);
                var originalPositions = actors.Select(actor => actor.transform.position).ToArray();
                var originalShadows = actors.Select(actor => actor.transform.Find("Shadow").position).ToArray();
                var type = typeof(BattleHudPreview).Assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineArrival", true);
                var dust = typeof(BattleHudPreview).GetField("readyDust", Members).GetValue(host);
                using (var animation = (IDisposable)Activator.CreateInstance(type, Members, null,
                           new object[] { units, actors, dust }, null))
                {
                    type.GetMethod("HoldAboveStage", Members).Invoke(animation, null);
                    for (int i = 0; i < actors.Length; i++)
                    {
                        Assert.That(actors[i].transform.position.y, Is.GreaterThan(originalPositions[i].y + 3f));
                        Assert.That(actors[i].transform.Find("Shadow").position.y, Is.EqualTo(originalShadows[i].y).Within(.0001f));
                    }
                    type.GetMethod("Begin", Members).Invoke(animation, null);
                    for (int step = 0; step < 20; step++)
                        type.GetMethod("Tick", Members).Invoke(animation, new object[] { .04f });
                    Assert.That(type.GetProperty("Complete", Members).GetValue(animation), Is.True);
                    Assert.That(actors.Select(actor => actor.transform.position), Is.EqualTo(originalPositions));
                }
                yield return null;
                Assert.That(units.GetComponentsInChildren<Transform>(true).Any(tr => tr.name == "Battle Arrival Dust"), Is.False);
            }
            finally { UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
#endif
