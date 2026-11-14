#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftOnlineTests
    {
        private const string HomePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private Scene home;
        private Scene previous;
        private string endpoint, priorEndpoint, peerToken, roomPath;

        [UnityTest, Timeout(140000)]
        public IEnumerator HomeAndAuthenticatedOpponentDraftAndLoadBattle()
        {
            endpoint = Environment.GetEnvironmentVariable("ASTRAL_ARENA_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint)) Assert.Ignore("Set ASTRAL_ARENA_TEST_URL to the local arena server.");
            priorEndpoint = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            previous = SceneManager.GetActiveScene();
            home = EditorSceneManager.LoadSceneInPlayMode(HomePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Wait(() => home.IsValid() && home.isLoaded, "Home scene did not finish loading.");
            Assert.That(SceneManager.SetActiveScene(home), Is.True);
            yield return Wait(() => Screen("HomeWindow"), "Home did not load.");
            Click(Screen("HomeWindow"), "arena");
            yield return Wait(() => Screen("ArenaDirectory") && Screen("ArenaDirectory").Require<GameButton>("create").interactable,
                "Arena directory did not open.");
            Click(Screen("ArenaDirectory"), "create");
            yield return Wait(() => Screen("ArenaCreateRoom"), "Create dialog did not open.");
            var create = Screen("ArenaCreateRoom");
            string name = "在线选禁" + RequestId()[..8];
            create.Require<TMP_InputField>("name").text = name;
            Click(create, "rule_basic");
            Click(create, "confirm");
            yield return Wait(() => Screen("ArenaRoom"), "Arena room did not open.");

            yield return Peer("sessions", new { displayName = "选禁对手" }, state => peerToken = (string)state["token"]);
            yield return Peer("rooms?query=" + Uri.EscapeDataString(name), null, response =>
            {
                var room = response["rooms"].Single();
                Assert.That((string)room["name"], Is.EqualTo(name));
                Assert.That((string)room["rule"], Is.EqualTo("basic"));
                Assert.That((int)room["prebanCount"], Is.EqualTo(2));
                Assert.That((bool)room["banProtection"], Is.True);
                roomPath = "rooms/" + (string)room["id"];
            });
            yield return Peer(roomPath + "/join", new { });
            yield return Wait(() => Screen("ArenaRoom").Require<GameText>("count").text == "2/50", "Second player did not appear.");
            yield return Capture("arena-draft-room-ready");
            Click(Screen("ArenaRoom"), "start");
            yield return Wait(() => Screen("ArenaDraft") && HeroCount(Screen("ArenaDraft"), "grid") >= 14, "Preban grid did not initialize.");
            var draft = Screen("ArenaDraft");
            JObject state = null;
            string route = roomPath + "/draft";
            yield return Peer(route, null, next => state = next);
            Assert.That((string)state["phase"], Is.EqualTo("preban"));
            var pool = state["eligibleHeroIds"].Values<string>().ToArray();
            ClickHero(draft, "grid", pool[0]);
            yield return Capture("arena-draft-preban");
            ClickHero(draft, "grid", pool[1]);
            Click(draft, "preAction");
            yield return ObserveHostSubmission(route, "preban", next => state = next);
            Assert.That(state["host"]["prebans"].Count(), Is.EqualTo(0));
            yield return PeerAction(route, state, "preban", pool.Skip(2).Take(2).ToArray(), next => state = next);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR")))
            {
                yield return Wait(() => draft.Require<GameObject>("reveal").activeInHierarchy, "Preban reveal did not show.");
                yield return Capture("arena-draft-preban-reveal");
            }

            bool[] hostPicks = { true, false, false, true, true, false, false, true, true, false };
            for (int i = 0; i < hostPicks.Length; i++)
            {
                string candidate = pool[4 + i];
                if (!hostPicks[i])
                {
                    yield return PeerAction(route, state, "pick", new[] { candidate }, next => state = next);
                    continue;
                }
                yield return Wait(() => Hero(draft, "roster", candidate)?.interactable == true, "Host hero did not become selectable.");
                ClickHero(draft, "roster", candidate);
                if (i == 3) yield return Capture("arena-draft-pick");
                int expected = state["host"]["picks"].Count() + 1;
                Click(draft, "action");
                yield return ReadUntil(route, next => next["host"]["picks"].Count() == expected, next => state = next);
            }
            Assert.That((string)state["phase"], Is.EqualTo("ban"));
            var target = draft.Require<BindingMap>("red0").Require<GameButton>("button");
            var protectedSlot = draft.Require<BindingMap>("red2");
            yield return Wait(() => target.interactable && !protectedSlot.Require<GameButton>("button").interactable,
                "Final ban did not enable ordinary targets and protect the third pick.");
            Assert.That(protectedSlot.Require<Image>("guard").enabled, Is.True);
            target.onClick.Invoke();
            yield return Capture("arena-draft-final-ban");
            Click(draft, "action");
            yield return ObserveHostSubmission(route, "ban", next => state = next);
            Assert.That(state["host"]["ban"].Type, Is.EqualTo(JTokenType.Null));
            string hostBanned = (string)state["host"]["picks"][0];
            yield return PeerAction(route, state, "ban", new[] { hostBanned }, next => state = next);

            yield return Wait(() => draft.Require<GameObject>("formation").activeInHierarchy, "Formation did not open.");
            var hostPicksSurviving = state["host"]["picks"].Values<string>().Where(id => id != hostBanned).ToArray();
            var guestPicksSurviving = state["guest"]["picks"].Values<string>().Where(id => id != (string)state["host"]["ban"]).ToArray();
            var first = draft.Require<BindingMap>("formation0").Require<GameButton>("button");
            var second = draft.Require<BindingMap>("formation1").Require<GameButton>("button");
            Assert.That(first.interactable && second.interactable, Is.True);
            first.onClick.Invoke();
            second.onClick.Invoke();
            (hostPicksSurviving[0], hostPicksSurviving[1]) = (hostPicksSurviving[1], hostPicksSurviving[0]);
            yield return Capture("arena-draft-formation");
            Click(draft, "action");
            yield return ObserveHostSubmission(route, "formation", next => state = next);
            Assert.That(state["host"]["formation"].Count(), Is.EqualTo(0));
            yield return PeerAction(route, state, "formation", guestPicksSurviving, next => state = next);

            Assert.That((string)state["phase"], Is.EqualTo("battle"));
            Assert.That(state["host"]["formation"].Values<string>(), Is.EqualTo(hostPicksSurviving));
            JObject battle = null;
            yield return Peer(roomPath + "/battle", null, next => battle = next);
            Assert.That((string)battle["snapshot"]["battleId"], Is.EqualTo((string)state["battleId"]));
            Assert.That(battle["snapshot"]["units"].Count(), Is.EqualTo(8));

            yield return Wait(() => SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath).isLoaded,
                "Draft did not transition to the online battle scene.", 30);
            var battleScene = SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath);
            var hostHud = battleScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>(true)).Single();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var arrival = typeof(BattleHudPreview).GetField("arrival", flags);
            var online = typeof(BattleHudPreview).GetField("online", flags);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR")))
            {
                var ready = typeof(BattleHudPreview).GetProperty("OnlineArrivalComplete", flags);
                yield return Wait(() => arrival.GetValue(hostHud) != null && (bool)ready.GetValue(hostHud),
                    "The online battle did not reach the landing frame.", 35);
                yield return CaptureBattle(battleScene, hostHud, "arena-draft-landing", 1920, 900, gateOverlay: true);
            }
            yield return Wait(() => online.GetValue(hostHud) != null && arrival.GetValue(hostHud) == null,
                "The online battle did not complete stage entry.", 40);
            // The second seat in this fixture is an HTTP peer, so explicitly acknowledge
            // its presentation window after the local scene has completed real entry.
            JObject timedBattle = null;
            yield return Peer(roomPath + "/battle", null, value => timedBattle = value);
            var timedSnapshot = timedBattle["snapshot"];
            yield return Peer(roomPath + "/battle/ready", new
            {
                battleId = (string)timedSnapshot["battleId"], commandSequence = (ulong)timedSnapshot["lastCommandSequence"],
                eventSequence = (ulong)timedSnapshot["lastEventSequence"], actorId = (string)timedSnapshot["currentActorId"]
            }, null);
            var clockNode = hostHud.transform.Find("Online PvP HUD/Authoritative Turn Clock");
            yield return Wait(() => clockNode && clockNode.gameObject.activeInHierarchy,
                "Both ready seats did not produce an authoritative visible countdown.");
            var heroId = typeof(BattlePreviewActor).GetProperty("HeroId", flags);
            var units = battleScene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            string[] HeroesAtPositions(string team) => units.Find(team).Cast<Transform>()
                .Select(slot => slot.GetComponentInChildren<BattlePreviewActor>(true))
                .Select(actor => (string)heroId.GetValue(actor)).ToArray();
            Assert.That(HeroesAtPositions("Allies"), Is.EqualTo(hostPicksSurviving));
            Assert.That(HeroesAtPositions("Enemies"), Is.EqualTo(guestPicksSurviving));
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle", 1920, 900);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle-16x9", 1920, 1080);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-battle-21x9", 2240, 960);
            var liveSession = online.GetValue(hostHud);
            BattleSnapshot LiveSnapshot() => (BattleSnapshot)liveSession.GetType().GetProperty("Snapshot", flags).GetValue(liveSession);
            var simulation = typeof(BattleHudPreview).GetField("simulation", flags).GetValue(hostHud);
            var busy = simulation.GetType().GetProperty("Busy", flags);
            var battleCatalog = JsonConvert.DeserializeObject<BattleCatalogData>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json").text);
            var policy = new BattleAiPolicy(battleCatalog);
            for (int step = 0; step < 8 && LiveSnapshot().GetUnit(LiveSnapshot().CurrentActorId).Slot.Side == BattleSide.Enemy; step++)
            {
                var current = LiveSnapshot();
                var active = current.GetUnit(current.CurrentActorId);
                var choice = policy.ChooseTarget(current, active.Form.SkillIds[0]) ?? policy.ChooseAction(current);
                Assert.That(choice, Is.Not.Null);
                JObject commandReply = null;
                yield return Peer(roomPath + "/battle/commands", new
                {
                    battleId = current.BattleId, rulesVersion = current.RulesVersion,
                    sequence = current.LastCommandSequence + 1, commandId = Guid.NewGuid().ToString("N"),
                    actorId = current.CurrentActorId, skillId = choice.SkillId, targetId = choice.TargetId,
                    useSoulBurn = choice.UseSoulBurn, contentHash = (string)timedBattle["contentHash"]
                }, value => commandReply = value);
                Assert.That((int)commandReply["status"], Is.EqualTo((int)BattleCommandStatus.Accepted));
                ulong acceptedSequence = (ulong)commandReply["snapshot"]["lastCommandSequence"];
                yield return Wait(() => LiveSnapshot().LastCommandSequence >= acceptedSequence && !(bool)busy.GetValue(simulation),
                    "The accepted opponent action did not finish its real presentation.", 30);
                current = LiveSnapshot();
                yield return Peer(roomPath + "/battle/ready", new
                {
                    battleId = current.BattleId, commandSequence = current.LastCommandSequence,
                    eventSequence = current.LastEventSequence, actorId = current.CurrentActorId
                }, null);
                yield return Wait(() => clockNode.gameObject.activeInHierarchy, "The next real turn did not open its clock.");
            }
            Assert.That(LiveSnapshot().GetUnit(LiveSnapshot().CurrentActorId).Slot.Side, Is.EqualTo(BattleSide.Ally));
            Assert.That((bool)simulation.GetType().GetProperty("IsTurnEntryPlaying", flags).GetValue(simulation), Is.False,
                "The authoritative clock must wait for the visible turn-entry animation.");
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-my-turn-16x9", 1920, 1080);
            yield return CaptureBattle(battleScene, hostHud, "arena-draft-my-turn-21x9", 2240, 960);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (home.IsValid() && !home.isLoaded)
            {
                float deadline = Time.realtimeSinceStartup + 8;
                while (home.IsValid() && !home.isLoaded && Time.realtimeSinceStartup < deadline) yield return null;
            }
            if (BattleOnlineLaunch.IsActive && SceneManager.GetSceneByPath(BattleOnlineLaunch.ScenePath).isLoaded)
            {
                var exit = BattleOnlineLaunch.ExitAsync();
                yield return Wait(() => exit.IsCompleted, "Online battle did not unload.");
                exit.GetAwaiter().GetResult();
            }
            var draft = Screen("ArenaDraft");
            if (draft && draft.Require<GameButton>("exit").interactable)
            {
                Click(draft, "exit");
                yield return Wait(() => !Screen("ArenaDraft"), "Arena draft did not close.");
            }
            var room = Screen("ArenaRoom");
            if (room)
            {
                Click(room, "leave");
                yield return Wait(() => !Screen("ArenaRoom"), "Arena room did not close.");
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (home.IsValid() && home.isLoaded) yield return SceneManager.UnloadSceneAsync(home);
            if (roomPath != null && peerToken != null) yield return Peer(roomPath + "/leave", new { });
            if (endpoint != null) Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", priorEndpoint);
        }

        private BindingMap Screen(string name) => !home.IsValid() || !home.isLoaded ? null : home.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BindingMap>()).FirstOrDefault(map =>
                map.gameObject.name == name || map.gameObject.name == name + "(Clone)");

        private static string RequestId() => Guid.NewGuid().ToString("N");
        private static void Click(BindingMap map, string name)
        {
            var button = map.Require<GameButton>(name);
            Assert.That(button.interactable, Is.True, name);
            button.onClick.Invoke();
        }

        private static Transform Heroes(BindingMap map, string list) => map.Require<ScrollRect>(list).content;
        private static int HeroCount(BindingMap map, string list) => Heroes(map, list).childCount;
        private static GameButton Hero(BindingMap map, string list, string heroId)
        {
            var row = Heroes(map, list).Cast<Transform>().FirstOrDefault(item => item && item.gameObject.activeInHierarchy &&
                string.Equals(item.gameObject.name, heroId, StringComparison.Ordinal));
            return row ? row.GetComponent<BindingMap>().Require<GameButton>("button") : null;
        }
        private static void ClickHero(BindingMap map, string list, string heroId)
        {
            var button = Hero(map, list, heroId);
            Assert.That(button, Is.Not.Null, heroId + " in " + list + ": " +
                string.Join(", ", Heroes(map, list).Cast<Transform>().Select(child => child.gameObject.name)));
            Assert.That(button.interactable, Is.True, heroId);
            button.onClick.Invoke();
        }

        private IEnumerator ObserveHostSubmission(string route, string phase, Action<JObject> observe) =>
            ReadUntil(route, next => (string)next["phase"] == phase && (bool)next["host"]["submitted"], observe);

        private IEnumerator ReadUntil(string route, Func<JObject, bool> condition, Action<JObject> observe)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < deadline)
            {
                JObject state = null;
                yield return Peer(route, null, next => state = next);
                if (condition(state))
                {
                    observe(state);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(.14f);
            }
            Assert.Fail("The server did not observe the pending host selection.");
        }

        private IEnumerator PeerAction(string route, JObject draft, string kind, string[] ids, Action<JObject> observe) =>
            Peer(route + "/actions", new
            {
                requestId = RequestId(), draftId = (string)draft["id"], version = (int)draft["version"], kind, heroIds = ids
            }, observe);

        private IEnumerator Peer(string route, object payload, Action<JObject> receive = null)
        {
            using var request = new UnityWebRequest(new Uri(new Uri(endpoint), "/api/arena/" + route),
                payload == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 8
            };
            if (payload != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (peerToken != null) request.SetRequestHeader("Authorization", "Bearer " + peerToken);
            yield return request.SendWebRequest();
            Assert.That(request.result, Is.EqualTo(UnityWebRequest.Result.Success), $"{route}: HTTP {request.responseCode}");
            if (receive != null) receive(JObject.Parse(request.downloadHandler.text));
        }

        private static IEnumerator Wait(Func<bool> condition, string message, float seconds = 15)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private static IEnumerator CaptureBattle(Scene scene, BattleHudPreview host, string label,
            int width, int height, bool gateOverlay = false)
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder);
            var canvas = host.GetComponent<Canvas>();
            var scaler = host.GetComponent<CanvasScaler>();
            var worldCamera = canvas.worldCamera;
            float originalScale = canvas.scaleFactor;
            bool originalScalerEnabled = scaler.enabled;
            Assert.That(worldCamera, Is.Not.Null, "Use the camera the battle HUD actually renders through.");
            var cameras = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>())
                .Where(camera => camera.enabled && !camera.targetTexture).OrderBy(camera => camera.depth).ToArray();
            Assert.That(cameras, Does.Contain(worldCamera));
            var original = cameras.Select(camera => (camera.targetTexture, camera.rect, camera.aspect)).ToArray();
            var viewport = worldCamera.GetComponent<BattlePreviewViewport>();
            var apply = typeof(BattlePreviewViewport).GetMethod("ApplyScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            var transitionCanvas = gateOverlay ? GameObject.Find("Online Battle Gate")?.GetComponent<Canvas>() : null;
            var transitionMode = transitionCanvas ? transitionCanvas.renderMode : default;
            var transitionCamera = transitionCanvas ? transitionCanvas.worldCamera : null;
            float transitionDistance = transitionCanvas ? transitionCanvas.planeDistance : 0f;
            try
            {
                foreach (var camera in cameras)
                {
                    camera.targetTexture = target;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = (float)width / height;
                }
                // CanvasScaler uses the Game view's physical size, which differs from this render target.
                // One canvas unit per capture pixel lets the real viewport apply its design scale once.
                scaler.enabled = false;
                canvas.scaleFactor = 1f;
                apply.Invoke(viewport, new object[] { new Vector2Int(width, height), new Rect(0, 0, width, height) });
                if (transitionCanvas)
                {
                    transitionCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    transitionCanvas.worldCamera = worldCamera;
                    transitionCanvas.planeDistance = 1f;
                }
                Canvas.ForceUpdateCanvases();
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var camera in cameras) camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);

                var samples = image.GetPixels32();
                int illuminatedBackdropSamples = 0;
                for (int y = height / 5; y < height * 4 / 5; y += 40)
                for (int x = width / 5; x < width * 4 / 5; x += 40)
                {
                    var sample = samples[y * width + x];
                    if (sample.r + sample.g + sample.b > 96) illuminatedBackdropSamples++;
                }
                Assert.That(illuminatedBackdropSamples, Is.GreaterThan(80),
                    "The battle capture must contain the rendered arena across the frame.");
                File.WriteAllBytes(Path.Combine(folder, label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                for (int i = 0; i < cameras.Length; i++)
                {
                    cameras[i].targetTexture = original[i].targetTexture;
                    cameras[i].rect = original[i].rect;
                    cameras[i].aspect = original[i].aspect;
                }
                if (transitionCanvas)
                {
                    transitionCanvas.renderMode = transitionMode;
                    transitionCanvas.worldCamera = transitionCamera;
                    transitionCanvas.planeDistance = transitionDistance;
                }
                canvas.scaleFactor = originalScale;
                scaler.enabled = originalScalerEnabled;
                apply.Invoke(viewport, new object[] { new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height), UnityEngine.Screen.safeArea });
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        private static IEnumerator Capture(string label)
        {
            string path = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrEmpty(path)) yield break;
            Directory.CreateDirectory(path);
            yield return null;
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>()).First(item => item && item.enabled);
            var canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>()).First(item => item && item.isRootCanvas);
            foreach (var portrait in UnityEngine.Object.FindObjectsOfType<Camera>().Where(item => item.targetTexture && item != camera))
                portrait.Render();
            const int width = 1920, height = 900;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            var originalTarget = camera.targetTexture;
            var originalMask = camera.cullingMask;
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            var originalDistance = canvas.planeDistance;
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = ~0;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                yield return null;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(path, label + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = originalTarget;
                camera.cullingMask = originalMask;
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                canvas.planeDistance = originalDistance;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }
    }
}
#endif
