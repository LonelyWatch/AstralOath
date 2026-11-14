#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class GameHomeArenaTests
    {
        private const string ScenePath = "Assets/Game/Modules/Home/Scenes/GameHome.unity";
        private Scene scene;
        private readonly List<Peer> peers = new List<Peer>();

        [UnityTest, Timeout(120000)]
        public IEnumerator StartupCreatesRoomAndSecondSessionJoinsAndChats()
        {
            string endpoint = Environment.GetEnvironmentVariable("ASTRAL_ARENA_TEST_URL");
            if (string.IsNullOrWhiteSpace(endpoint)) Assert.Ignore("Set ASTRAL_ARENA_TEST_URL to a running test API.");
            string original = Environment.GetEnvironmentVariable("ASTRAL_FOUNDATION_URL");
            Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", endpoint);
            try
            {
                scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                yield return Wait(() => ScreenMap("HomeWindow"), "HomeWindow did not boot.");
                var home = ScreenMap("HomeWindow");
                Assert.That(home.Require<GameButton>("arena").isActiveAndEnabled, Is.True);
                yield return Capture("home");
                yield return Capture("home-16x9", 1600, 900);
                yield return Capture("home-4x3", 1200, 900);
                home.Require<GameButton>("arena").onClick.Invoke();
                yield return Wait(() => ScreenMap("ArenaDirectory"), "Directory did not open.");
                var directory = ScreenMap("ArenaDirectory");
                yield return Wait(() => directory.Require<GameButton>("create").interactable, "Directory did not finish loading.");
                directory.Require<GameButton>("filter").onClick.Invoke();
                Assert.That(directory.Require<GameObject>("choice").activeInHierarchy, Is.True);
                var filterBody = directory.Require<GameObject>("choice").GetComponent<BindingMap>()
                    .Require<RectTransform>("body").GetComponent<BindingMap>();
                Assert.That(filterBody.Require<Image>("players_down").enabled, Is.True);
                filterBody.Require<GameButton>("players").onClick.Invoke();
                Assert.That(filterBody.Require<Image>("players_up").enabled, Is.True);
                yield return Wait(() => directory.Require<GameButton>("create").interactable, "Sorting did not finish.");
                Assert.That(directory.Require<GameObject>("choice").activeInHierarchy, Is.True);
                yield return Capture("arena-filter");
                var feedback = directory.GetComponentInParent<Canvas>().rootCanvas.GetComponent<GameUiFeedback>();
                Assert.That(feedback, Is.Not.Null);
                var track = typeof(GameUiFeedback).GetMethod("Track", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var clickPoint = new Vector2(Screen.width * .37f, Screen.height * .61f);
                track.Invoke(feedback, new object[] { 15, clickPoint, true, false, false });
                track.Invoke(feedback, new object[] { 15, clickPoint, false, true, false });
                yield return new WaitForSecondsRealtime(.12f);
                yield return Capture("arena-click-glow");
                directory.Require<GameButton>("filter").onClick.Invoke();
                var choice = directory.Require<GameObject>("choice");
                if (choice.activeInHierarchy) choice.GetComponent<BindingMap>().Require<GameButton>("dismiss").onClick.Invoke();

                directory.Require<GameButton>("create").onClick.Invoke();
                yield return Wait(() => ScreenMap("ArenaCreateRoom"), "Create modal did not open.");
                var create = ScreenMap("ArenaCreateRoom");
                Assert.That(create.Require<Image>("selection_wild").enabled, Is.True);
                Assert.That(create.Require<ScrollRect>("rules").horizontalNormalizedPosition, Is.EqualTo(0f).Within(.01f));
                var allow = create.Require<GameButton>("allow");
                var deny = create.Require<GameButton>("deny");
                deny.onClick.Invoke();
                var allowSelection = create.Require<Image>("allowSelection");
                var denySelection = create.Require<Image>("denySelection");
                Assert.That(denySelection.enabled && !allowSelection.enabled, Is.True);
                Assert.That((Color32)denySelection.color, Is.EqualTo(new Color32(162, 27, 27, 255)));
                var locked = create.Require<Toggle>("locked");
                var password = create.Require<TMP_InputField>("password");
                var passwordState = create.Require<CanvasGroup>("passwordState");
                Assert.That(passwordState.alpha, Is.LessThan(.6f));
                Assert.That(password.targetGraphic.color.a, Is.LessThan(.4f));
                Assert.That(password.interactable || passwordState.blocksRaycasts, Is.False);
                Assert.That(create.Require<GameButton>("dismiss").FeedbackSound, Is.EqualTo(GameUiFeedback.Sound.Normal));
                yield return new WaitForSecondsRealtime(.5f);
                yield return Capture("arena-create");
                yield return Capture("arena-create-16x9", 1600, 900);
                locked.isOn = true;
                Assert.That(passwordState.alpha, Is.EqualTo(1f));
                Assert.That(password.targetGraphic.color.a, Is.EqualTo(1f));
                Assert.That(password.interactable && passwordState.blocksRaycasts, Is.True);
                password.text = "1234";
                locked.isOn = false;
                Assert.That(password.text, Is.Empty);
                Assert.That(password.interactable || passwordState.blocksRaycasts, Is.False);
                allow.onClick.Invoke();
                Assert.That(allowSelection.enabled && !denySelection.enabled, Is.True);
                Assert.That((Color32)allowSelection.color, Is.EqualTo(new Color32(61, 119, 8, 255)));
                create.Require<GameButton>("dismiss").onClick.Invoke();
                yield return Wait(() => ScreenMap("ArenaCreateRoom") == null, "Outside click did not close the dialog.");
                directory.Require<GameButton>("create").onClick.Invoke();
                yield return Wait(() => ScreenMap("ArenaCreateRoom"), "Create modal did not reopen.");
                create = ScreenMap("ArenaCreateRoom");
                Assert.That(create.Require<Toggle>("locked").isOn, Is.False);
                Assert.That(create.Require<Image>("allowSelection").enabled, Is.True);
                create.Require<GameButton>("confirm").onClick.Invoke();
                Assert.That(create.Require<GameText>("status").text, Does.Contain("28"));
                string title = "双端验证" + Guid.NewGuid().ToString("N").Substring(0, 8);
                create.Require<TMP_InputField>("name").text = title;
                create.Require<Toggle>("locked").isOn = true;
                create.Require<TMP_InputField>("password").text = "1234";
                yield return Capture("arena-create-filled");
                create.Require<GameButton>("confirm").onClick.Invoke();
                Assert.That(create.Require<GameButton>("cancel").interactable, Is.False);
                Assert.That(create.Require<GameButton>("dismiss").interactable, Is.False);
                yield return Wait(() => ScreenMap("ArenaRoom"), "First client did not enter its room.");
                var room = ScreenMap("ArenaRoom");
                Assert.That(room.Require<GameText>("name").text, Is.EqualTo(title));
                Assert.That(room.Require<GameText>("count").text, Is.EqualTo("1/50"));
                yield return Capture("arena-room");
                var peer = NewPeer(endpoint);
                yield return Complete(peer.Open());
                yield return Complete(peer.Find(title));
                Assert.That(peer.RoomId, Is.Not.Null);
                yield return Complete(peer.Join());
                yield return Wait(() => room.Require<GameText>("count").text == "2/50", "Peer did not appear in room.");
                room.Require<TMP_InputField>("message").text = "来自 Unity";
                float sendStarted = Time.realtimeSinceStartup;
                room.Require<GameButton>("send").onClick.Invoke();
                yield return Wait(() => ShowsChat(room, "来自 Unity"), "Unity did not display its confirmed message.", 1.25f);
                Debug.Log($"ARENA_CHAT_SENDER_MS {(Time.realtimeSinceStartup - sendStarted) * 1000:F0}");
                Assert.That(room.Require<TMP_InputField>("message").text, Is.Empty);
                yield return Complete(peer.Read());
                Assert.That(peer.Chat, Does.Contain("来自 Unity"));
                float peerStarted = Time.realtimeSinceStartup;
                yield return Complete(peer.Send());
                yield return Wait(() => ShowsChat(room, "来自第二会话"), "Unity did not receive peer chat.", 1.25f);
                Debug.Log($"ARENA_CHAT_RECEIVER_MS {(Time.realtimeSinceStartup - peerStarted) * 1000:F0}");
                Assert.That(ChatCount(room, "来自 Unity"), Is.EqualTo(1), "Sender message appeared twice.");
                yield return Capture("arena-room-two");
                room.Require<GameButton>("emoji").onClick.Invoke();
                var picker = room.Require<GameObject>("emojiPicker");
                Assert.That(picker.activeInHierarchy, Is.True);
                yield return Capture("arena-emoji-picker");
                var scroll = picker.GetComponent<BindingMap>().Require<ScrollRect>("choices");
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(picker.GetComponentsInChildren<Image>().Any(image => image.sprite &&
                    image.sprite.name == "emoji_picker_frame" && image.type == Image.Type.Sliced), Is.True);
                var portrait = room.Require<BindingMap>("host").Require<Image>("portrait").sprite;
                Assert.That(portrait.name, Is.EqualTo("c1001_avatar_room"));
                Assert.That(portrait.rect.width, Is.EqualTo(140f));
                var emojiRows = picker.GetComponentsInChildren<GameButton>();
                Assert.That(emojiRows.Length, Is.GreaterThan(60));
                // This exercises a successful send after the server's 500 ms cadence guard.
                yield return new WaitForSecondsRealtime(.55f);
                emojiRows.Single(button => button.name == "c1001_smile_001").onClick.Invoke();
                yield return Wait(() => !picker.activeInHierarchy, "Sticker picker did not close.");
                room.Require<GameButton>("emoji").onClick.Invoke();
                emojiRows.Single(button => button.name == "c1001_smile_001").onClick.Invoke();
                yield return Wait(() => !string.IsNullOrEmpty(room.Require<GameText>("status").text),
                    "The cadence guard did not report the rapid second send.");
                Assert.That(picker.activeInHierarchy, Is.True, "A rejected sticker send closed the picker.");
                room.Require<GameButton>("emoji").onClick.Invoke();
                yield return Complete(peer.Read());
                Assert.That(peer.Chat, Does.Contain("c1001_smile_001"));
                Assert.That(room.Require<ScrollRect>("messages").content.GetComponentsInChildren<Image>()
                    .Count(image => image.sprite && image.sprite.name == "c1001_smile_001"), Is.EqualTo(1));
                yield return Complete(peer.SendSticker("c1005_cry_001"));
                yield return Wait(() => room.Require<ScrollRect>("messages").content.GetComponentsInChildren<Image>()
                    .Any(image => image.sprite && image.sprite.name == "c1005_cry_001"), "Unity did not render a peer sticker.");
                yield return Capture("arena-emoji-chat");
                yield return Complete(peer.Leave());
                room.Require<GameButton>("leave").onClick.Invoke();
                yield return Wait(() => ScreenMap("ArenaDirectory"), "Leaving did not return to the directory.");
                yield return ExerciseDirectory(endpoint);

            }
            finally { Environment.SetEnvironmentVariable("ASTRAL_FOUNDATION_URL", original); }
        }

        private IEnumerator ExerciseDirectory(string endpoint)
        {
            string group = "目录" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string privateTitle = group + "锁定";
            var host = NewPeer(endpoint);
            yield return Complete(host.Open());
            yield return Complete(host.Create(privateTitle, "4321"));

            for (int i = 0; i < 8; i++)
            {
                var filler = NewPeer(endpoint);
                yield return Complete(filler.Open());
                yield return Complete(filler.Create(group + i, ""));
            }

            var directory = ScreenMap("ArenaDirectory");
            var list = directory.Require<ScrollRect>("list");
            yield return Wait(() => directory.Require<GameButton>("create").interactable, "Directory did not finish loading.");
            directory.Require<GameButton>("refresh").onClick.Invoke();
            yield return Wait(() => FindRows(list, group).Length == 9, "Refreshed directory did not include peer rooms.");
            Canvas.ForceUpdateCanvases();
            Assert.That(list.content.rect.height, Is.GreaterThan(list.viewport.rect.height), "Nine rows must overflow the viewport.");
            Assert.That(list.verticalNormalizedPosition, Is.EqualTo(1f).Within(.01f));
            var scroll = new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 12f };
            ExecuteEvents.Execute<IScrollHandler>(list.gameObject, scroll, ExecuteEvents.scrollHandler);
            yield return Wait(() => list.verticalNormalizedPosition < .99f, "Directory did not scroll in response to wheel input.");
            yield return Capture("arena-directory-scrolled");

            directory.Require<GameButton>("search").onClick.Invoke();
            var search = directory.Require<GameObject>("searchPanel");
            Assert.That(search.activeInHierarchy, Is.True);
            var searchButton = directory.Require<GameButton>("search");
            Assert.That(((Image)searchButton.targetGraphic).sprite.name, Is.EqualTo("_btn_white_flat"));
            Assert.That(searchButton.spriteState.pressedSprite.name, Is.EqualTo("_btn_white_flat_p"));
            Assert.That(directory.Require<TMP_InputField>("query").isFocused, Is.False);
            directory.Require<TMP_InputField>("query").text = privateTitle;
            yield return Capture("arena-search");
            directory.Require<GameButton>("submitSearch").onClick.Invoke();
            yield return Wait(() => FindRows(list, group).Length == 1, "Search did not narrow the visible list.");
            Assert.That(search.activeInHierarchy, Is.True);
            var row = FindRows(list, group).Single();
            Assert.That(row.Require<GameText>("name").text, Is.EqualTo(privateTitle));
            Assert.That(row.Require<Image>("lock").enabled, Is.True);

            row.Require<GameButton>("join").onClick.Invoke();
            var password = directory.Require<GameObject>("password");
            yield return Wait(() => password.activeInHierarchy, "Locked room did not open the password dialog.");
            var map = password.GetComponent<BindingMap>();
            var input = map.Require<TMP_InputField>("input");
            var confirm = map.Require<GameButton>("confirm");
            input.text = "0000";
            confirm.onClick.Invoke();
            Assert.That(map.Require<GameButton>("cancel").interactable, Is.False);
            yield return Wait(() => map.Require<GameText>("status").text == "密码错误。", "Wrong PIN was not rejected by the server.");
            Assert.That(ScreenMap("ArenaRoom"), Is.Null);
            Assert.That(password.activeInHierarchy, Is.True);
            yield return Wait(() => confirm.interactable, "Password action did not become available after rejection.");
            input.text = "4321";
            confirm.onClick.Invoke();
            yield return Wait(() => ScreenMap("ArenaRoom"), "Unity did not join the peer's room.");
            var room = ScreenMap("ArenaRoom");
            Assert.That(room.Require<GameText>("name").text, Is.EqualTo(privateTitle));
            Assert.That(room.Require<GameText>("count").text, Is.EqualTo("2/50"));
            room.Require<GameButton>("leave").onClick.Invoke();
            yield return Wait(() => ScreenMap("ArenaDirectory"), "Leaving the peer's room did not return to the directory.");
            yield return Complete(host.ReadRoom());
            Assert.That(host.MemberCount, Is.EqualTo(1));
        }

        private static BindingMap[] FindRows(ScrollRect list, string prefix) =>
            list.content.GetComponentsInChildren<BindingMap>()
                .Where(map => map.transform.parent == list.content &&
                    map.Require<GameText>("name").text.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        private Peer NewPeer(string endpoint)
        {
            var peer = new Peer(endpoint);
            peers.Add(peer);
            return peer;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                var room = ScreenMap("ArenaRoom");
                if (room)
                {
                    room.Require<GameButton>("leave").onClick.Invoke();
                    float deadline = Time.realtimeSinceStartup + 6f;
                    while (ScreenMap("ArenaRoom") && Time.realtimeSinceStartup < deadline) yield return null;
                }
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            foreach (var peer in peers.Where(item => item.RoomId != null))
                yield return Complete(peer.Leave());
            peers.Clear();
        }

        private BindingMap ScreenMap(string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BindingMap>())
            .FirstOrDefault(item => item.gameObject.name == name || item.gameObject.name == name + "(Clone)");

        private static int ChatCount(BindingMap room, string message) =>
            room.Require<ScrollRect>("messages").content.GetComponentsInChildren<GameText>()
                .Count(label => label.text.Contains(message));

        private static bool ShowsChat(BindingMap room, string message) => ChatCount(room, message) != 0;

        private static IEnumerator Wait(Func<bool> predicate, string reason, float timeout = 18f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, reason);
        }

        private static IEnumerator Complete(Task task)
        {
            yield return Wait(() => task.IsCompleted, "Peer request timed out.");
            task.GetAwaiter().GetResult();
        }

        private IEnumerator Capture(string name, int width = 1920, int height = 900)
        {
            string directory = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var canvas = roots.Select(item => item.GetComponent<Canvas>()).First(item => item);
            var camera = roots.Select(item => item.GetComponent<Camera>()).First(item => item);
            var stageCamera = GameObject.Find("UI portrait camera")?.GetComponent<Camera>();
            if (stageCamera) stageCamera.Render();
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var oldMode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            var oldDistance = canvas.planeDistance;
            var oldTarget = camera.targetTexture;
            var oldMask = camera.cullingMask;
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
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                camera.targetTexture = oldTarget;
                camera.cullingMask = oldMask;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }

        [Serializable] private sealed class Session { public string token; }
        [Serializable] private sealed class Rooms { public Room[] rooms; }
        [Serializable] private sealed class Room { public string id, name; public int memberCount; }
        [Serializable] private sealed class RoomState { public Room room; }
        [Serializable] private sealed class CreateRoom { public string name, password; public string rule = "basic"; public bool allowChat = true; }

        private sealed class Peer
        {
            private readonly string url;
            private string token;
            public string RoomId { get; private set; }
            public string Chat { get; private set; }
            public int MemberCount { get; private set; }
            public Peer(string endpoint) => url = endpoint.TrimEnd('/') + "/api/arena/";
            public async Task Open() => token = JsonUtility.FromJson<Session>(await Call("sessions",
                "{\"displayName\":\"第二会话\",\"avatarId\":\"c1001\"}")).token;
            public async Task Find(string title) => RoomId = JsonUtility.FromJson<Rooms>(await Call("rooms?visibility=private&sort=recent&query="
                + Uri.EscapeDataString(title))).rooms.Single(room => room.name == title).id;
            public Task Join() => Call("rooms/" + RoomId + "/join", "{\"password\":\"1234\"}");
            public async Task Create(string title, string password)
            {
                var response = await Call("rooms", JsonUtility.ToJson(new CreateRoom { name = title, password = password }));
                RoomId = JsonUtility.FromJson<RoomState>(response).room.id;
            }

            public async Task Leave()
            {
                await Call("rooms/" + RoomId + "/leave", "{}");
                RoomId = null;
            }

            public async Task ReadRoom() =>
                MemberCount = JsonUtility.FromJson<RoomState>(await Call("rooms/" + RoomId)).room.memberCount;
            public async Task Read() => Chat = await Call("rooms/" + RoomId + "/messages?after=0");
            public Task Send() => Call("rooms/" + RoomId + "/messages", "{\"text\":\"来自第二会话\"}");
            public Task SendSticker(string id) => Call("rooms/" + RoomId + "/messages",
                "{\"kind\":\"sticker\",\"stickerId\":\"" + id + "\"}");

            private async Task<string> Call(string path, string payload = null)
            {
                using var request = new UnityWebRequest(url + path, payload == null ? "GET" : "POST")
                    { downloadHandler = new DownloadHandlerBuffer(), timeout = 10 };
                if (payload != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                if (token != null) request.SetRequestHeader("Authorization", "Bearer " + token);
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                Assert.That(request.result, Is.EqualTo(UnityWebRequest.Result.Success), path + ": " + request.error);
                return request.downloadHandler.text;
            }
        }
    }
}
#endif
