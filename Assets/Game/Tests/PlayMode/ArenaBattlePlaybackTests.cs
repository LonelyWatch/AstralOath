#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
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
            var resource = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>("Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
            Assert.That(resource, Is.Not.Null);
            var assetType = resource.GetType();
            var close = (TextAsset)assetType.GetField("closeCfx", Members).GetValue(resource);
            var open = (TextAsset)assetType.GetField("openCfx", Members).GetValue(resource);
            Assert.That(close.name, Is.EqualTo("treff_pd_pvp_battle_close.cfx"));
            Assert.That(open.name, Is.EqualTo("treff_pd_pvp_battle_open.cfx"));
            var ignition = (TextAsset)assetType.GetField("ignitionCfx", Members).GetValue(resource);
            Assert.That(ignition.name, Is.EqualTo("eff_pvp_intro.cfx"),
                "World Arena uses the white serif intro with red vertical light and translucent strip.");
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
                    (method: "IgniteAsync", layer: "eff_pvp_outro_draw", time: .66f, file: "pvp-ignition")
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
        public IEnumerator CountdownVisiblePixelsCenterOnTheNativeRing()
        {
            const string prefabPath = "Assets/Game/Modules/BattleScene/Res/Prefab/BattleHud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var canvas = new GameObject("Countdown test", typeof(RectTransform), typeof(Canvas));
            var bindings = UnityEngine.Object.Instantiate(prefab, canvas.transform, false).GetComponent<BindingMap>();
            var resource = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>("Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
            var assetType = resource.GetType();
            var copyType = assetType.GetNestedType("Copy", BindingFlags.NonPublic);
            var copy = copyType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                new[] { assetType.GetField("zhHans", Members).GetValue(resource) });
            var type = typeof(BattleOnlineLaunch).Assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineHud", true);
            try
            {
                using var view = (IDisposable)Activator.CreateInstance(type, Members, null,
                    new object[] { bindings, null, copy, null }, null);
                var draw = type.GetMethod("ShowTurn", Members);
                var clockRoot = (RectTransform)type.GetField("clockRoot", Members).GetValue(view);
                var allDigits = clockRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true)
                    .Where(item => item.name.StartsWith("Countdown ", StringComparison.Ordinal)).ToArray();
                foreach (int seconds in new[] { 0, 1, 9, 10, 30, 88, 100, 120, 9 })
                {
                    var clock = new BattleTurnClockView { DecisionSeconds = 120, ServerNowUnixMilliseconds = 1000,
                        DeadlineUnixMilliseconds = 1000 + seconds * 1000L };
                    draw.Invoke(view, new object[] { BattleSide.Ally, true, clock, 0d });
                    var visible = allDigits.Where(item => item.enabled).ToArray();
                    Assert.That(visible.Length, Is.EqualTo(seconds < 10 ? 1 : seconds < 100 ? 2 : 3));
                    Vector2 low = Vector2.one * float.PositiveInfinity;
                    Vector2 high = Vector2.one * float.NegativeInfinity;
                    foreach (var glyph in visible)
                    {
                        // Decode the actual Unity sprite input. Do not test the generated metrics against themselves.
                        var pixels = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        try
                        {
                            Assert.That(pixels.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(glyph.sprite))), Is.True);
                            var colors = pixels.GetPixels32();
                            var rect = glyph.rectTransform;
                            var size = rect.sizeDelta;
                            var origin = rect.anchoredPosition - size * .5f;
                            for (int y = 0; y < pixels.height; y++)
                            for (int x = 0; x < pixels.width; x++)
                            {
                                if (colors[y * pixels.width + x].a <= 15) continue;
                                low = Vector2.Min(low, origin + Vector2.Scale(size, new Vector2(x / (float)pixels.width, y / (float)pixels.height)));
                                high = Vector2.Max(high, origin + Vector2.Scale(size, new Vector2((x + 1f) / pixels.width, (y + 1f) / pixels.height)));
                            }
                        }
                        finally { UnityEngine.Object.Destroy(pixels); }
                    }
                    Assert.That(((low + high) * .5f).magnitude, Is.LessThan(.05f), $"{seconds} must center its visible pixels.");
                }
            }
            finally { UnityEngine.Object.Destroy(canvas); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator IgnitionCoversTheCanvasAtWideAndTabletAspectRatios()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("A graphics device is required to check the transition camera.");

            var assembly = typeof(BattleOnlineLaunch).Assembly;
            var type = assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineGate", true);
            var resource = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>("Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
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
        public IEnumerator ArenaEntryWarnsAlliesRunsEnemiesOnTheGroundAndThenZooms()
        {
            var scene = EditorSceneManager.LoadSceneInPlayMode(BattleOnlineLaunch.ScenePath,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
            try
            {
                yield return null;
                var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var camera = host.GetComponent<Canvas>().worldCamera;
                var viewport = camera.GetComponent<BattlePreviewViewport>();
                float baseline = camera.orthographicSize;
                var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
                var allies = units.Find("Allies").GetComponentsInChildren<BattlePreviewActor>(true);
                var enemies = units.Find("Enemies").GetComponentsInChildren<BattlePreviewActor>(true);
                var allyPositions = allies.Select(actor => actor.transform.position).ToArray();
                var enemyPositions = enemies.Select(actor => actor.transform.position).ToArray();
                var enemyShadows = enemies.Select(actor => actor.transform.Find("Shadow").position).ToArray();
                var type = typeof(BattleHudPreview).Assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineArrival", true);
                var dust = typeof(BattleHudPreview).GetField("readyDust", Members).GetValue(host);
                using (var animation = (IDisposable)Activator.CreateInstance(type, Members, null,
                           new object[] { units, viewport, allies, enemies, dust }, null))
                {
                    float FrontX(BattlePreviewActor actor) => camera.WorldToViewportPoint(actor.Bounds.min).x;
                    bool Flag(string name) => (bool)type.GetProperty(name, Members).GetValue(animation);
                    void Tick(float delta) => type.GetMethod("Tick", Members).Invoke(animation, new object[] { delta });

                    type.GetMethod("Prepare", Members).Invoke(animation, null);
                    float wide = camera.orthographicSize;
                    var warnings = units.GetComponentsInChildren<MeshFilter>(true)
                        .Where(mesh => mesh.name.StartsWith("PvP Arrival Warning ", StringComparison.Ordinal)).ToArray();
                    Assert.That(wide, Is.GreaterThan(baseline));
                    Assert.That(warnings, Has.Length.EqualTo(allies.Length));
                    Assert.That(warnings.All(warning => !warning.gameObject.activeSelf), Is.True);
                    Assert.That(allies.Select(actor => actor.transform.position), Is.EqualTo(allyPositions));
                    Assert.That(enemies.All(actor => FrontX(actor) > 1f), Is.True);
                    Assert.That(enemies.Select(actor => actor.transform.position.y), Is.EqualTo(enemyPositions.Select(pos => pos.y)));
                    Assert.That(enemies.Select(actor => actor.transform.Find("Shadow").position.y),
                        Is.EqualTo(enemyShadows.Select(pos => pos.y)));

                    float firstEnemyStartX = enemies[0].transform.position.x;
                    type.GetMethod("Begin", Members).Invoke(animation, null);
                    Tick(.25f);
                    Assert.That(warnings.All(warning => warning.gameObject.activeSelf && warning.sharedMesh.vertexCount > 0), Is.True);
                    Assert.That(enemies[0].transform.position.x, Is.LessThan(firstEnemyStartX));
                    Assert.That(FrontX(enemies[3]), Is.GreaterThan(1f));
                    Assert.That(enemies[0].transform.position.y, Is.EqualTo(enemyPositions[0].y));
                    Assert.That(camera.orthographicSize, Is.EqualTo(wide));

                    for (int step = 0; step < 30 && !Flag("ActorsReady"); step++)
                        Tick(.04f);
                    Assert.That(Flag("ActorsReady"), Is.True);
                    Assert.That(Flag("CameraBlending"), Is.True);
                    Assert.That(camera.orthographicSize, Is.EqualTo(wide));
                    Assert.That(allies.Select(actor => actor.transform.position), Is.EqualTo(allyPositions));
                    Assert.That(enemies.Select(actor => actor.transform.position), Is.EqualTo(enemyPositions));
                    Assert.That(enemies.Select(actor => actor.transform.Find("Shadow").position), Is.EqualTo(enemyShadows));
                    Tick(.17f);
                    Assert.That(camera.orthographicSize, Is.GreaterThan(baseline).And.LessThan(wide));
                    Tick(.3f);
                    Assert.That(Flag("Complete"), Is.True);
                    Assert.That(camera.orthographicSize, Is.EqualTo(baseline).Within(.0001f));
                }
                yield return null;
                Assert.That(units.GetComponentsInChildren<Transform>(true).Any(tr =>
                    tr.name == "Battle Arrival Dust" || tr.name.StartsWith("PvP Arrival Warning ", StringComparison.Ordinal)), Is.False);
            }
            finally { UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
#endif
