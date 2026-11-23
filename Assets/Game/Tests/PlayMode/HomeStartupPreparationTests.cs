#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Arena;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public class HomeStartupPreparationTests
    {
        private sealed class Art : IAssetService, ISceneAssetService
        {
            internal readonly List<string> Loaded = new List<string>();
            internal readonly TaskCompletionSource<bool> Gate = new TaskCompletionSource<bool>();
            private static readonly Dictionary<string, string> Files = new Dictionary<string, string>
            {
                ["arena/zh-hans"]="Arena/Configs/arena-zh-hans.json", ["home/main"]="Home/Res/Prefab/HomeWindow.prefab",
                ["development/gm-window"]="FoundationDemo/Res/Prefab/DevelopmentGmWindow.prefab",
                ["arena/directory"]="Arena/Res/Prefab/ArenaDirectory.prefab", ["arena/create-room"]="Arena/Res/Prefab/ArenaCreateRoom.prefab",
                ["arena/room"]="Arena/Res/Prefab/ArenaRoom.prefab", ["home/more-menu"]="Home/Res/Prefab/HomeMoreMenu.prefab",
                ["arena/draft"]="Arena/Res/Prefab/ArenaDraft.prefab", ["arena/draft-art"]="Arena/Configs/ArenaDraftArt.asset",
                ["home/hero-inventory"]="Home/Res/Prefab/HeroInventory.prefab", ["summon/window"]="Summon/Res/Prefab/SummonWindow.prefab",
                ["home/hero-art"]="Home/Configs/HomeHeroArt.asset", ["home/artifact-art"]="Home/Configs/ArtifactArtCatalog.asset",
                ["summon/presentation"]="Summon/Res/Presentation/SummonPresentation.asset", ["summon/iseria-skill"]="Summon/Res/Presentation/IseriaSkillLoop/IseriaSkillLoop.asset"
            };
            public async Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                Loaded.Add(address);
                using var cancel = scope.Token.Register(() => Gate.TrySetCanceled());
                await Gate.Task;
                scope.Token.ThrowIfCancellationRequested();
                return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/" + Files[address]);
            }
            public Task<ILoadedScene> LoadAdditiveAsync(string address, CancellationToken token) => throw new NotSupportedException();
        }

        private sealed class ReadServer : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Task worker;
            internal readonly ConcurrentQueue<string> Calls = new ConcurrentQueue<string>();
            internal readonly TaskCompletionSource<bool> AllowResponse = new TaskCompletionSource<bool>();
            internal readonly TaskCompletionSource<bool> SignalRoomChange = new TaskCompletionSource<bool>();
            internal readonly TaskCompletionSource<bool> AllowDirectoryList = new TaskCompletionSource<bool>();
            internal bool CrossFreeReset, FailFirstArtifact, KeepArenaRoom, SlowDirectory;
            internal int InventoryVersion;
            internal int EquipmentVersion = 1;
            internal bool RejectEquipment;
            internal string LastEquipmentValidator;
            internal string DrawReceipt;
            internal bool RejectDraw;
            internal string StoredRewards = "{\"items\":[]}";
            internal readonly ConcurrentQueue<string> GmRequests = new ConcurrentQueue<string>();
            internal int HoldGmRequest;
            internal readonly TaskCompletionSource<bool> ReleaseGmFailure =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal string GmAuthorization;
            internal volatile string DraftId = "settled-draft";
            internal volatile bool BattleEnded;
            private volatile bool stopping;
            internal Uri Address => new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
            internal ReadServer() { listener.Start(); worker = Task.Run(Run); }
            private async Task Run()
            {
                var connections = new List<Task>();
                try
                {
                    while (!stopping)
                    {
                        var client = await listener.AcceptTcpClientAsync();
                        connections.Add(Respond(client));
                    }
                }
                catch (ObjectDisposedException) when (stopping) { }
                catch (SocketException) when (stopping) { }
                finally { await Task.WhenAll(connections); }
            }

            private async Task Respond(TcpClient client)
            {
                using var connection = client;
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var line = (await reader.ReadLineAsync()).Split(' ');
                    Calls.Enqueue(line[0] + " " + line[1]);
                    string validator = null, field;
                    int contentLength = 0;
                    bool sendContinue = false;
                    while (!string.IsNullOrEmpty(field = await reader.ReadLineAsync()))
                    {
                        if (line[1].StartsWith("/api/development/gm") && field.StartsWith("Authorization: "))
                            GmAuthorization = field.Substring("Authorization: ".Length);
                        if (field.StartsWith("If-None-Match:", StringComparison.OrdinalIgnoreCase))
                            validator = field.Substring("If-None-Match:".Length).Trim();
                        else if (field.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(field.Substring("Content-Length:".Length).Trim());
                        else if (field.Equals("Expect: 100-continue", StringComparison.OrdinalIgnoreCase))
                            sendContinue = true;
                    }
                    if (sendContinue)
                    {
                        var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                        await stream.WriteAsync(interim, 0, interim.Length);
                    }
                    if (contentLength > 0)
                    {
                        // Drain the request before closing. An unread POST body can reset this test socket on Windows.
                        var payload = new char[Math.Min(contentLength, 1024)];
                        var gmPayload = line[1] == "/api/development/gm/actions" ? new StringBuilder() : null;
                        while (contentLength > 0)
                        {
                            int consumed = await reader.ReadAsync(payload, 0, Math.Min(contentLength, payload.Length));
                            if (consumed == 0) return;
                            gmPayload?.Append(payload, 0, consumed);
                            contentLength -= consumed;
                        }
                        if (gmPayload != null) GmRequests.Enqueue(gmPayload.ToString());
                    }
                    await AllowResponse.Task.ConfigureAwait(false);
                    if (line[0] == "HEAD" && line[1] == "/api/inventory/equipment")
                    {
                        LastEquipmentValidator = validator;
                        string tag = "\"" + EquipmentVersion + "\"";
                        string statusLine = tag == validator ? "304 Not Modified" : "200 OK";
                        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 " + statusLine + "\r\nETag: " + tag + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(bytes, 0, bytes.Length);
                        return;
                    }
                    if (line[1].EndsWith("/events", StringComparison.Ordinal))
                    {
                        var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson\r\nConnection: close\r\n\r\n{\"kind\":\"sync\"}\n");
                        await stream.WriteAsync(header, 0, header.Length);
                        await SignalRoomChange.Task;
                        var change = Encoding.ASCII.GetBytes("{\"kind\":\"room\"}\n");
                        await stream.WriteAsync(change, 0, change.Length);
                        return;
                    }
                    string schedule = CrossFreeReset ? "\"serverTimeUtc\":\"2030-01-01T01:00:00Z\",\"freeResetUtc\":\"2030-01-01T01:00:00.001Z\"," : "";
                    string body = line[1].Contains("summon") ? "{" + schedule + "\"banners\":[{\"id\":\"covenant\",\"entries\":[]}],\"wishHeroes\":[],\"wishArtifacts\":[]}" :
                        "{\"capacity\":" + (1000 + InventoryVersion) + ",\"used\":0,\"version\":1,\"items\":[],\"heroes\":[],\"artifacts\":[],\"materials\":{}}";
                    bool fail = FailFirstArtifact && line[1].EndsWith("/artifacts") && Calls.Count(x => x.EndsWith("/artifacts")) == 1;
                    string status = fail ? "503 Unavailable" : "200 OK";
                    if (line[1].StartsWith("/api/development/gm"))
                    {
                        var otherBalances = new[] { "epicBookmarks", "goldTransmitStones", "galaxyBookmarks", "mysticMedals",
                            "lesserCharm", "greaterCharm", "epicCharm", "bottle", "powder" };
                        var resources = "{\"key\":\"gold\",\"value\":30},{\"key\":\"bookmarks\",\"value\":50},{\"key\":\"skystones\",\"value\":250}," +
                            string.Join(",", otherBalances.Select(key => "{\"key\":\"" + key + "\",\"value\":0}"));
                        body = "{\"playerId\":\"test\",\"realmId\":\"test-realm\",\"resources\":[" + resources + "]," +
                            "\"heroes\":[\"c1001\",\"c1002\",\"c1003\",\"c1004\",\"c1005\",\"c1006\",\"c1007\",\"c1008\",\"c1009\",\"c1010\"],\"artifacts\":[],\"equipment\":[],\"forgeRules\":" +
                            DevelopmentGmForgeChecks.Rules + "}";
                        if (line[0] == "POST" && GmRequests.Count == 1) status = "503 Unavailable";
                        if (line[0] == "POST" && GmRequests.Count == HoldGmRequest)
                        {
                            await ReleaseGmFailure.Task.ConfigureAwait(false);
                            status = "503 Unavailable";
                        }
                    }
                    if (line[1].StartsWith("/api/inventory/equipment", StringComparison.Ordinal))
                    {
                        body = "{\"capacity\":100,\"used\":0,\"version\":" + EquipmentVersion + ",\"items\":[]}";
                        if (line[0] == "POST" && RejectEquipment)
                        {
                            status = "409 Conflict";
                            body = "{\"code\":\"equipment_version_conflict\"}";
                        }
                    }
                    if (line[1] == "/api/summon/draw" && DrawReceipt != null) body = DrawReceipt;
                    if (line[1] == "/api/summon/draw" && RejectDraw)
                    {
                        status = "409 Conflict";
                        body = "{\"code\":\"insufficient_bookmarks\"}";
                    }
                    if (line[1] == "/api/summon/storage") body = StoredRewards;
                    if (line[1] == "/api/summon/storage/claim") body = "{\"items\":[]}";
                    if (line[1] == "/api/arena/rooms/current") { status = "204 No Content"; body = ""; }
                    if (line[1].StartsWith("/api/arena/rooms?", StringComparison.Ordinal)) body = "{\"rooms\":[]}";
                    if ((KeepArenaRoom || SlowDirectory) && line[1].StartsWith("/api/arena/rooms", StringComparison.Ordinal))
                    {
                        status = "200 OK";
                        const string host = "{\"id\":\"host\",\"displayName\":\"Host\",\"avatarId\":\"c1001\"}";
                        const string guest = "{\"id\":\"test\",\"displayName\":\"Guest\",\"avatarId\":\"c1001\"}";
                        if (line[1].EndsWith("/leave"))
                        {
                            status = KeepArenaRoom ? "503 Unavailable" : "204 No Content";
                            body = KeepArenaRoom ? "{\"code\":\"unavailable\"}" : "";
                        }
                        else if (SlowDirectory && BattleEnded && line[1].EndsWith("/current")) { status = "204 No Content"; body = ""; }
                        else if (line[1].Contains("/rooms?"))
                        {
                            await AllowDirectoryList.Task.ConfigureAwait(false);
                            body = "{\"rooms\":[]}";
                        }
                        else if (line[1].Contains("/messages?")) body = "{\"messages\":[]}";
                        else if (line[1].EndsWith("/draft"))
                        {
                            string phase = BattleEnded && DraftId == "settled-draft" ? "battle" : "preban";
                            body = "{\"id\":\"" + DraftId + "\",\"roomId\":\"surviving-room\",\"phase\":\"" + phase +
                                "\",\"battleId\":\"finished-battle\",\"firstPick\":\"host\",\"mySide\":\"guest\",\"eligibleHeroIds\":[],\"host\":{\"player\":" +
                                host + ",\"prebans\":[],\"picks\":[],\"formation\":[]},\"guest\":{\"player\":" + guest + ",\"prebans\":[],\"picks\":[],\"formation\":[]}}";
                        }
                        else body = "{\"room\":{\"id\":\"surviving-room\",\"name\":\"Recovered room\",\"rule\":\"basic\",\"firstPick\":\"host\",\"capacity\":2,\"memberCount\":2,\"host\":" + host + "},\"members\":[" + host + "," + guest + "]}";
                    }
                    byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
                    await stream.WriteAsync(data, 0, data.Length);
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) when (stopping) { }
                catch (SocketException) { }
                catch (IOException) { }
            }
            public void Dispose()
            {
                stopping = true;
                listener.Stop();
                SignalRoomChange.TrySetResult(true);
                AllowResponse.TrySetResult(true);
                ReleaseGmFailure.TrySetResult(true);
                AllowDirectoryList.TrySetResult(true);
                worker.GetAwaiter().GetResult();
            }
        }

        [UnityTest]
        public IEnumerator LobbyPortraitMatches1080pAnd4kWithoutChangingFraming()
        {
            using var owner = new BindingScope(default);
            var assets = new Art();
            assets.Gate.SetResult(true);
            var camera = new GameObject("Lobby resolution test", typeof(Camera)).GetComponent<Camera>();
            var canvas = new GameObject("Lobby resolution canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            RenderTexture screen = null;
            try
            {
                var views = new UIViewRegistry();
                views.Register<HomeWindow>();
                var view = views.CreateAsync<HomeWindow>(assets, canvas.transform, owner).GetAwaiter().GetResult();
                var locale = ArenaLabels.LoadAsync(assets, owner, "zh-Hans", "ja").GetAwaiter().GetResult();
                var binder = new HomeWindowBinder(view, locale, null, null, null, null);
                using var page = new BindingScope(owner.Token);
                binder.OpenAsync(page).GetAwaiter().GetResult();
                var actor = view.Root.GetComponentsInChildren<RawImage>().Single(image => image.name == "Charlotte portrait");
                var target = (RenderTexture)actor.texture;
                var portraitCamera = Resources.FindObjectsOfTypeAll<Camera>().Single(c => c.targetTexture == target);
                foreach (var height in new[] {1080, 2160, 1080})
                {
                    camera.targetTexture = null;
                    if (screen) RenderTexture.ReleaseTemporary(screen);
                    screen = RenderTexture.GetTemporary(height * 16 / 9, height, 16);
                    camera.targetTexture = screen;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    portraitCamera.Render();
                    Assert.That(portraitCamera.orthographicSize, Is.EqualTo(3.10f).Within(.001f));
                    Assert.That(portraitCamera.transform.localPosition, Is.EqualTo(new Vector3(0, 4.85f, -10)));
                    var expected = Mathf.RoundToInt(height * 1.10f);
                    var folder = Environment.GetEnvironmentVariable("ASTRAL_LOBBY_CAPTURE");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        camera.Render();
                        var active = RenderTexture.active;
                        var shot = new Texture2D(screen.width, screen.height, TextureFormat.RGB24, false);
                        try
                        {
                            RenderTexture.active = screen;
                            shot.ReadPixels(new Rect(0, 0, screen.width, screen.height), 0, 0);
                            shot.Apply();
                            Directory.CreateDirectory(folder);
                            File.WriteAllBytes(Path.Combine(folder, $"lobby-{height}.png"), shot.EncodeToPNG());
                        }
                        finally { RenderTexture.active = active; Object.Destroy(shot); }
                    }
                    Assert.That(actor.texture, Is.SameAs(target), "The source actor must retain ownership of its target.");
                    Assert.That(target.width, Is.EqualTo(expected).Within(1), $"Lobby portrait at {height}p");
                    Assert.That(target.height, Is.EqualTo(target.width));
                }
                page.Dispose();
                Assert.That(actor.texture, Is.Null);
            }
            finally
            {
                camera.targetTexture = null;
                if (screen) RenderTexture.ReleaseTemporary(screen);
                Object.Destroy(canvas.gameObject);
                Object.Destroy(camera.gameObject);
            }
        }

        [UnityTest] public IEnumerator HomeReusesLoginResourcesForBagHeroesAndSummon() => Check(false, false);
        [UnityTest, Timeout(120000)] public IEnumerator GmActionsRetryAndRefreshTheAccount() => Check(false, false, gm: true);
        [UnityTest] public IEnumerator CancellingLoginRemovesTheBlackScreen() => Check(true, false);
        [UnityTest] public IEnumerator FailedHomeLoadRemovesTheBlackScreen() => Check(false, true);
        [UnityTest] public IEnumerator FullPageFadeSamplesBothRampsWhileGameTimeIsPaused()
        {
            float previous = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                yield return Check(false, false, sampleFade: true);
            }
            finally { Time.timeScale = previous; }
        }
        [UnityTest] public IEnumerator SummonRereadsStateAfterTheFreeReset() => Check(false, false, crossFreeReset: true);
        [UnityTest] public IEnumerator BagRetriesOnlyTheUnavailableInitialRead() => Check(false, false, failFirstArtifact: true);
        [UnityTest, Timeout(65000)]
        public IEnumerator BagAndHeroSnapshotsStayResidentWhileWaitingInTheArena() => Check(false, false, waitInArena: true);
        [UnityTest] public IEnumerator BattleReturnReopensTheOriginalRoomWithoutLeavingOrReplayingDraft() => Check(false, false, recoverRoom: true);
        [UnityTest] public IEnumerator ReturnedDirectoryCanCloseWhileTheRoomListIsStillLoading() => Check(false, false, slowDirectory: true);

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator SettledDrawsUpdateResidentInventoriesBeforeTheHeroPageOpens()
        {
            using var server = new ReadServer();
            server.AllowResponse.SetResult(true);
            var navigationType = typeof(FoundationEntry).Assembly.GetType("AstralOath.Client.FoundationDemo.StartupNavigation", true);
            var start = navigationType.GetMethod("StartAsync", BindingFlags.NonPublic | BindingFlags.Static);
            for (int login = 1; login <= 2; ++login)
            {
                using var owner = new BindingScope(default);
                using var services = new ServiceRegistry(owner.Token);
                var canvas = new GameObject("Account pages", typeof(Canvas));
                try
                {
                    var assets = new Art();
                    assets.Gate.SetResult(true);
                    services.Add<IAssetService>(assets);
                    services.Add<ISceneAssetService>(assets);
                    services.Add(new GameServerEndpoint(server.Address.AbsoluteUri));
                    services.Add(new GameAccountSession("test-token-" + login, "p" + login, "Player", "c1001"));
                    server.InventoryVersion = login * 10;
                    var open = new GameObject("Open", typeof(RectTransform), typeof(GameButton)).GetComponent<GameButton>();
                    var status = new GameObject("Status", typeof(RectTransform), typeof(GameText)).GetComponent<GameText>();
                    open.transform.SetParent(canvas.transform, false);
                    status.transform.SetParent(canvas.transform, false);
                    var context = new GameLaunchContext(services, owner, canvas.transform, open, status, status, "zh-Hans", "ja", false, true);
                    var opening = (Task)start.Invoke(null, new object[] { context, owner });
                    yield return new WaitUntil(() => opening.IsCompleted);
                    opening.GetAwaiter().GetResult();
                    var nav = opening.GetType().GetProperty("Result").GetValue(opening);
                    var cache = navigationType.GetField("firstReads", PrivateInstance).GetValue(nav);
                    object Value(string name) => cache.GetType().GetProperty(name, PrivateInstance).GetValue(cache);
                    Task Fetch(string name) => (Task)cache.GetType().GetMethod(name, PrivateInstance)
                        .Invoke(cache, new object[] { owner.Token });
                    var heroes = (HeroInventorySnapshot)Value("Heroes");
                    Assert.That(heroes.capacity, Is.EqualTo(1000 + login * 10));
                    Assert.That(((ArtifactInventorySnapshot)Value("Artifacts")).capacity, Is.EqualTo(1000 + login * 10));
                    Assert.That(((EquipmentInventorySnapshot)Value("Equipment")).version, Is.EqualTo(server.EquipmentVersion));
                    if (login == 2) continue;

                    var equipment = (EquipmentInventoryApi)navigationType.GetField("equipment", PrivateInstance).GetValue(nav);
                    string item = Guid.NewGuid().ToString("N"), ownerId = Guid.NewGuid().ToString("N");
                    Task<EquipmentInventorySnapshot> Change() => equipment.ChangeAsync("equip", Guid.NewGuid().ToString("N"),
                        ownerId, item, "weapon", 1, owner.Token);
                    server.EquipmentVersion = 2;
                    var equip = Change();
                    yield return new WaitUntil(() => equip.IsCompleted);
                    Assert.That(Value("Equipment"), Is.SameAs(equip.GetAwaiter().GetResult()));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(1));
                    server.EquipmentVersion = 3;
                    server.RejectEquipment = true;
                    var conflict = Change();
                    yield return new WaitUntil(() => conflict.IsCompleted);
                    Assert.That((conflict.Exception?.InnerException as EquipmentRequestException)?.Code, Is.EqualTo("equipment_version_conflict"));
                    var reload = Fetch("ReadEquipmentAsync");
                    yield return new WaitUntil(() => reload.IsCompleted);
                    reload.GetAwaiter().GetResult();
                    Assert.That(((EquipmentInventorySnapshot)Value("Equipment")).version, Is.EqualTo(3));
                    cache.GetType().GetMethod("ApplyEquipment", PrivateInstance).Invoke(cache, new object[] { equip.Result });
                    Assert.That(((EquipmentInventorySnapshot)Value("Equipment")).version, Is.EqualTo(3),
                        "Replaying an older equipment receipt must preserve the reconciled inventory.");
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(2));
                    Assert.That(Value("Heroes"), Is.SameAs(heroes));
                    var focus = navigationType.GetMethod("RefreshEquipmentOnReturnAsync", PrivateInstance);
                    Assert.That(focus, Is.Not.Null, "Switching back to this client must validate the known equipment version.");
                    Task Refocus() => (Task)focus.Invoke(nav, null);
                    var unchanged = Refocus();
                    yield return new WaitUntil(() => unchanged.IsCompleted);
                    unchanged.GetAwaiter().GetResult();
                    Assert.That(server.LastEquipmentValidator, Is.EqualTo("\"3\""));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(2));
                    server.EquipmentVersion = 4;
                    var remoteChange = Refocus();
                    yield return new WaitUntil(() => remoteChange.IsCompleted);
                    remoteChange.GetAwaiter().GetResult();
                    Assert.That(((EquipmentInventorySnapshot)Value("Equipment")).version, Is.EqualTo(4));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(3));

                    ++server.InventoryVersion;
                    var artifacts = (ArtifactInventoryApi)navigationType.GetField("artifacts", PrivateInstance).GetValue(nav);
                    var locking = artifacts.LockAsync("a1", Guid.NewGuid().ToString("N"), true, owner.Token);
                    yield return new WaitUntil(() => locking.IsCompleted);
                    locking.GetAwaiter().GetResult();
                    Assert.That(Value("Artifacts"), Is.Null);
                    Assert.That(Value("Heroes"), Is.SameAs(heroes));
                    var refresh = Fetch("ReadArtifactsAsync");
                    yield return new WaitUntil(() => refresh.IsCompleted);
                    refresh.GetAwaiter().GetResult();
                    Assert.That(((ArtifactInventorySnapshot)Value("Artifacts")).capacity, Is.EqualTo(1011));

                    ++server.InventoryVersion;
                    var summon = navigationType.GetField("summon", PrivateInstance).GetValue(nav);
                    var draw = summon.GetType().GetMethod("DrawAsync");
                    var requestType = draw.GetParameters()[0].ParameterType;
                    var request = Activator.CreateInstance(requestType);
                    requestType.GetField("requestId").SetValue(request, Guid.NewGuid().ToString("N"));
                    requestType.GetField("bannerId").SetValue(request, "covenant");
                    requestType.GetField("count").SetValue(request, 1);
                    var newHero = Guid.NewGuid().ToString("N");
                    var newArtifact = Guid.NewGuid().ToString("N");
                    foreach (var reward in new[] { ("hero", newHero, false), ("hero", newHero, false),
                        ("hero", Guid.NewGuid().ToString("N"), true), ("artifact", newArtifact, false) })
                    {
                        string requestId = Guid.NewGuid().ToString("N");
                        requestType.GetField("requestId").SetValue(request, requestId);
                        string template = reward.Item1 == "hero" ? "c1001" : "ef0001";
                        server.DrawReceipt = "{\"requestId\":\"" + requestId + "\",\"count\":1,\"results\":[{\"kind\":\"" +
                            reward.Item1 + "\",\"templateId\":\"" + template + "\",\"instanceId\":\"" + reward.Item2 +
                            "\",\"stars\":3,\"inStorage\":" + (reward.Item3 ? "true" : "false") + "}]}";
                        var drawing = (Task)draw.Invoke(summon, new object[] { request, owner.Token });
                        yield return new WaitUntil(() => drawing.IsCompleted);
                        drawing.GetAwaiter().GetResult();
                        Assert.That(Value("Heroes"), Is.Not.Null, "A confirmed draw evicted the login roster.");
                        Assert.That(Value("Artifacts"), Is.Not.Null);
                    }
                    var granted = (HeroInventorySnapshot)Value("Heroes");
                    Assert.That(granted.used, Is.EqualTo(1));
                    Assert.That(granted.heroes.Single().instanceId, Is.EqualTo(newHero));
                    Assert.That(granted.heroes.Single().level, Is.EqualTo(1));
                    Assert.That(((ArtifactInventorySnapshot)Value("Artifacts")).artifacts.Single().instanceId, Is.EqualTo(newArtifact));
                    server.RejectDraw = true;
                    var rejected = (Task)draw.Invoke(summon, new object[] { request, owner.Token });
                    yield return new WaitUntil(() => rejected.IsCompleted);
                    Assert.That(rejected.Exception?.InnerException?.GetType().Name, Is.EqualTo("SummonRequestException"));
                    Assert.That(Value("Heroes"), Is.SameAs(granted));
                    string storedId = Guid.NewGuid().ToString("N");
                    server.StoredRewards = "{\"items\":[{\"kind\":\"hero\",\"templateId\":\"c1001\",\"instanceId\":\"" +
                        storedId + "\",\"stars\":3}]}";
                    var storage = (Task)summon.GetType().GetMethod("StorageAsync").Invoke(summon, new object[] { owner.Token });
                    yield return new WaitUntil(() => storage.IsCompleted);
                    storage.GetAwaiter().GetResult();
                    var claim = (Task)summon.GetType().GetMethod("ClaimAsync").Invoke(summon, new object[] { new[] { storedId }, owner.Token });
                    yield return new WaitUntil(() => claim.IsCompleted);
                    claim.GetAwaiter().GetResult();
                    Assert.That(((HeroInventorySnapshot)Value("Heroes")).heroes.Select(hero => hero.instanceId),
                        Is.EqualTo(new[] { newHero, storedId }));
                    var inventory = (Task)navigationType.GetMethod("OpenInventory", PrivateInstance)
                        .Invoke(nav, new object[] { true, owner.Token });
                    yield return new WaitUntil(() => inventory.IsCompleted);
                    inventory.GetAwaiter().GetResult();
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/heroes"), Is.EqualTo(1));
                }
                finally { owner.Dispose(); Object.Destroy(canvas); }
            }
            Assert.That(server.Calls.Count(x => x == "GET /api/inventory/heroes"), Is.EqualTo(2));
            Assert.That(server.Calls.Count(x => x == "GET /api/inventory/artifacts"), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PendingReadCannotRestoreTheSnapshotInvalidatedByAWrite()
        {
            var type = typeof(FoundationEntry).Assembly.GetType("AstralOath.Client.FoundationDemo.HomeFirstReads", true)
                .GetNestedType("Snapshot`1", BindingFlags.NonPublic).MakeGenericType(typeof(HeroInventorySnapshot));
            var before = new TaskCompletionSource<HeroInventorySnapshot>();
            var after = new TaskCompletionSource<HeroInventorySnapshot>();
            int fetches = 0;
            Func<CancellationToken, Task<HeroInventorySnapshot>> fetch = _ => ++fetches == 1 ? before.Task : after.Task;
            var cache = Activator.CreateInstance(type, PrivateInstance, null, new object[] { fetch, CancellationToken.None }, null);
            Task<HeroInventorySnapshot> Read(CancellationToken ct) =>
                (Task<HeroInventorySnapshot>)type.GetMethod("GetAsync", PrivateInstance).Invoke(cache, new object[] { ct });
            var first = Read(default);
            using var closing = new CancellationTokenSource();
            var closedPage = Read(closing.Token);
            closing.Cancel();
            yield return new WaitUntil(() => closedPage.IsCompleted);
            Assert.That(closedPage.IsCanceled, Is.True);
            Assert.That(fetches, Is.EqualTo(1), "Closing one page cannot cancel another page's account read.");
            type.GetMethod("Invalidate", PrivateInstance).Invoke(cache, null);
            var second = Read(default);
            var authoritative = new HeroInventorySnapshot { used = 2 };
            after.SetResult(authoritative);
            yield return new WaitUntil(() => second.IsCompleted);
            before.SetResult(new HeroInventorySnapshot { used = 1 });
            yield return new WaitUntil(() => first.IsCompleted);
            Assert.That(first.GetAwaiter().GetResult(), Is.SameAs(authoritative));
            Assert.That(second.GetAwaiter().GetResult(), Is.SameAs(authoritative));
            Assert.That(Read(default).Result, Is.SameAs(authoritative));
            Assert.That(fetches, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ClosingTheReturnedDirectoryWhileItsRoomReadIsPendingIsANormalExit()
        {
            using var server = new ReadServer();
            using var lifetime = new BindingScope(default);
            using var page = new BindingScope(lifetime.Token);
            using var windows = new WindowCoordinator(lifetime.Token);
            using var api = new ArenaApi(server.Address, lifetime.Token, new GameAccountSession("sample-test-session", "test", "test", "c1001"));
            var canvas = new GameObject("Directory cancellation", typeof(Canvas));
            try
            {
                var assets = new Art();
                assets.Gate.SetResult(true);
                var views = new UIViewRegistry();
                views.Register<ArenaDirectory>();
                views.Register<ArenaCreateRoom>();
                var directory = views.CreateAsync<ArenaDirectory>(assets, canvas.transform, lifetime);
                var create = views.CreateAsync<ArenaCreateRoom>(assets, canvas.transform, lifetime);
                var labels = ArenaLabels.LoadAsync(assets, lifetime, "zh-Hans", "ja");
                yield return new WaitUntil(() => directory.IsCompleted && create.IsCompleted && labels.IsCompleted);
                var binder = new ArenaDirectoryBinder(directory.Result, create.Result, labels.Result, api, windows,
                    (_, __) => Task.CompletedTask, () => Task.CompletedTask, "test");
                var opening = binder.OpenAsync(page);
                yield return new WaitUntil(() => !server.Calls.IsEmpty);
                page.Dispose();
                yield return new WaitUntil(() => opening.IsCompleted);
                Assert.That(opening.Status, Is.EqualTo(TaskStatus.RanToCompletion),
                    "Closing the page while its room request is pending must not report a task cancellation to the player.");
            }
            finally { Object.Destroy(canvas); }
        }

        private static IEnumerator Check(bool cancel, bool fail, bool crossFreeReset = false, bool failFirstArtifact = false, bool recoverRoom = false, bool slowDirectory = false, bool waitInArena = false, bool sampleFade = false, bool gm = false)
        {
            using var server = new ReadServer { CrossFreeReset = crossFreeReset, FailFirstArtifact = failFirstArtifact, KeepArenaRoom = recoverRoom, SlowDirectory = slowDirectory };
            using var owner = new BindingScope(default);
            using var services = new ServiceRegistry(owner.Token);
            using var entry = new FoundationEntry();
            var canvas = new GameObject("Prepared home test", typeof(Canvas));
            if (gm)
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1;
            }
            var assets = new Art();
            services.Add<IAssetService>(assets);
            services.Add<ISceneAssetService>(assets);
            services.Add(new GameServerEndpoint(server.Address.AbsoluteUri));
            services.Add(new GameAccountSession("sample-test-session", "test", "test", "c1001"));
            var status = new GameObject("Status", typeof(RectTransform), typeof(GameText)).GetComponent<GameText>();
            var open = new GameObject("Open", typeof(RectTransform), typeof(GameButton)).GetComponent<GameButton>();
            open.transform.SetParent(canvas.transform, false);
            status.transform.SetParent(canvas.transform, false);
            BindingMap Page(string name) => canvas.GetComponentsInChildren<BindingMap>(true)
                .SingleOrDefault(v => v.name == name || v.name == name + "(Clone)");
            try
            {
                var start = entry.StartAsync(new GameLaunchContext(services, owner, canvas.transform, open, status, status, "zh-Hans", "ja", false, true));
                yield return null;
                var cover = GameObject.Find("Home loading curtain");
                Assert.That(cover, Is.Not.Null, "The admitted player must see black while the first load is pending.");
                Assert.That(cover.GetComponent<Image>().color, Is.EqualTo(Color.black));
                Assert.That(cover.GetComponent<Image>().raycastTarget, Is.True);
                var fade = cover.GetComponent<CanvasGroup>();
                Assert.That(fade, Is.Not.Null, "The cover must fade between the two pages.");
                if (!cancel)
                {
                    if (sampleFade)
                    {
                        yield return new WaitUntil(() => fade.alpha > 0f);
                        Assert.That(fade.alpha, Is.LessThan(1f), "Fade to black must draw intermediate frames.");
                    }
                    yield return new WaitUntil(() => fade.alpha >= 1f);
                    Assert.That(cover.activeInHierarchy, Is.True);
                }
                Assert.That(Page("HomeWindow"), Is.Null);
                if (cancel) owner.Dispose();
                else if (fail) assets.Gate.SetException(new IOException("Test unavailable asset"));
                else
                {
                    assets.Gate.SetResult(true);
                    yield return new WaitUntil(() => !server.Calls.IsEmpty || start.IsCompleted);
                    if (start.IsFaulted || start.IsCanceled) start.GetAwaiter().GetResult();
                    Assert.That(cover.activeInHierarchy, Is.True, "The account read is still pending.");
                    Assert.That(Page("HomeWindow").gameObject.activeInHierarchy, Is.False);
                    server.AllowResponse.SetResult(true);
                    if (sampleFade)
                    {
                        yield return new WaitUntil(() => fade.alpha < 1f || start.IsCompleted);
                        Assert.That(fade.alpha, Is.GreaterThan(0f), "Reveal the new page over multiple frames.");
                        Assert.That(Page("HomeWindow").gameObject.activeInHierarchy, Is.True);
                    }
                }
                yield return new WaitUntil(() => start.IsCompleted);
                Assert.That(GameObject.Find("Home loading curtain"), Is.Null);
                if (cancel || fail) { Assert.That(start.IsCanceled || start.IsFaulted, Is.True); yield break; }
                start.GetAwaiter().GetResult();
                Assert.That(Page("HomeWindow").gameObject.activeInHierarchy, Is.True);
                Assert.That(Page("DevelopmentGmWindow"), Is.Not.Null, "A signed-in Editor account must have a floating GM entry.");
                Assert.That(Page("DevelopmentGmWindow").Require<GameObject>("panel").activeSelf, Is.False);
                Assert.That(assets.Loaded.Distinct().Count(), Is.EqualTo(13), "The developer entry and seven first-open dependencies should be resident.");
                Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(1),
                    "Equipment must load during the login curtain, before the first page opens.");
                Assert.That(server.Calls.Count, Is.EqualTo(4), string.Join(", ", server.Calls));
                Assert.That(server.Calls.All(x => x.StartsWith("GET ")), Is.True);
                int beforeClicks = assets.Loaded.Count;
                var home = Page("HomeWindow");
                if (gm)
                {
                    var panel = Page("DevelopmentGmWindow");
                    void Click(string key) => panel.Require<GameButton>(key).onClick.Invoke();
                    GameButton Row() => panel.Require<ScrollRect>("items").content.GetComponentsInChildren<GameButton>()
                        .FirstOrDefault(button => button.gameObject.activeInHierarchy);
                    yield return DevelopmentGmCapture.Frame(canvas, "entry");
                    DevelopmentGmCapture.DragAndTapEntry(panel);
                    yield return new WaitUntil(() => Row() != null);
                    Assert.That(server.GmAuthorization, Is.EqualTo("Bearer sample-test-session"));
                    yield return DevelopmentGmCapture.Frame(canvas, "resources");
                    Click("diagnostics");
                    Assert.That(panel.Require<GameText>("report").text, Does.Contain("test-realm").And.Not.Contain("sample-test-session"));
                    yield return new WaitUntil(() => !panel.Require<GameText>("report").text.Contains("— FPS"));
                    yield return DevelopmentGmCapture.Frame(canvas, "diagnostics");
                    Click("inventory");
                    Row().onClick.Invoke();
                    yield return DevelopmentGmCapture.Frame(canvas, "inventory");
                    panel.Require<TMPro.TMP_InputField>("amount").text = "11";
                    Click("submit");
                    Assert.That(server.GmRequests, Is.Empty);
                    panel.Require<TMPro.TMP_InputField>("amount").text = "2";
                    Click("submit");
                    yield return new WaitUntil(() => panel.Require<GameText>("submitLabel").text == "重试原请求");
                    Click("close");
                    Assert.That(panel.Require<GameObject>("panel").activeSelf, Is.False);
                    Click("entry");
                    yield return new WaitUntil(() => panel.Require<GameText>("status").text.Contains("尚未确认"));
                    Click("submit");
                    yield return new WaitUntil(() => panel.Require<GameText>("status").text.Contains("已生效"));
                    var payloads = server.GmRequests.ToArray();
                    Assert.That(payloads.Length, Is.EqualTo(2));
                    Assert.That(payloads[1], Is.EqualTo(payloads[0]));
                    Assert.That(payloads[0], Does.Contain("\"action\":\"hero\"").And.Contain("\"key\":\"c1001\"").And.Contain("\"value\":2"));
                    yield return new WaitUntil(() => server.Calls.Count(call => call == "GET /api/inventory/heroes") >= 3);
                    Click("resources");
                    panel.Require<TMPro.TMP_InputField>("amount").text = "1000000001";
                    Click("submit");
                    Assert.That(panel.Require<GameText>("status").text, Does.Contain("1,000,000,000"));
                    Assert.That(server.GmRequests.Count, Is.EqualTo(2));
                    panel.Require<TMPro.TMP_InputField>("amount").text = "123";
                    Click("submit");
                    yield return new WaitUntil(() => panel.Require<GameText>("status").text.Contains("已生效"));
                    Assert.That(server.GmRequests.Last(), Does.Contain("\"action\":\"set\"").And.Contain("\"key\":\"gold\"").And.Contain("\"value\":123"));
                    server.HoldGmRequest = server.GmRequests.Count + 1;
                    yield return DevelopmentGmForgeChecks.Check(panel, server.GmRequests, canvas,
                        () => server.ReleaseGmFailure.TrySetResult(true));
                    Click("close");
                    yield return DevelopmentGmCapture.Frame(canvas, "entry-dragged");
                    yield return DevelopmentGmCapture.BattleRoundTrip(canvas, panel);
                    owner.Dispose();
                    yield return null;
                    Assert.That(Page("DevelopmentGmWindow"), Is.Null);
                    yield break;
                }
                if (waitInArena)
                {
                    home.Require<GameButton>("arena").onClick.Invoke();
                    yield return new WaitUntil(() => Page("ArenaDirectory")?.Require<GameButton>("create").interactable == true &&
                        server.Calls.Any(call => call.StartsWith("GET /api/arena/rooms?")));
                    yield return new WaitForSecondsRealtime(30.25f);
                    Page("ArenaDirectory").Require<GameButton>("back").onClick.Invoke();
                    yield return new WaitUntil(() => home.gameObject.activeInHierarchy);
                }
                if (recoverRoom || slowDirectory)
                {
                    home.Require<GameButton>("arena").onClick.Invoke();
                    yield return new WaitUntil(() => Page("ArenaDraft") && Page("ArenaDraft").gameObject.activeInHierarchy);
                    server.BattleEnded = true;
                    var returning = BattleOnlineLaunch.ExitAsync();
                    if (slowDirectory)
                    {
                        yield return new WaitUntil(() => server.Calls.Any(x => x.StartsWith("GET /api/arena/rooms?")));
                        yield return null;
                        Assert.That(returning.IsCompleted, Is.True, "Returning must finish once the service confirms no current room.");
                        returning.GetAwaiter().GetResult();
                        Assert.That(Page("ArenaDirectory").gameObject.activeInHierarchy, Is.True);
                        Page("ArenaDirectory").Require<GameButton>("back").onClick.Invoke();
                        Assert.That(home.gameObject.activeInHierarchy, Is.True);
                        server.AllowDirectoryList.SetResult(true);
                        yield return null;
                        yield break;
                    }
                    yield return new WaitUntil(() => returning.IsCompleted);
                    returning.GetAwaiter().GetResult();
                    Assert.That(server.Calls.Count(x => x == "POST /api/arena/rooms/surviving-room/leave"), Is.Zero);
                    Assert.That(server.Calls.Count(x => x == "GET /api/arena/rooms/current"), Is.EqualTo(2));
                    Assert.That(Page("ArenaRoom").gameObject.activeInHierarchy, Is.True);
                    Assert.That(Page("ArenaDirectory").gameObject.activeInHierarchy, Is.False);
                    yield return new WaitUntil(() => server.Calls.Count(x => x.EndsWith("/events")) >= 2);
                    yield return new WaitUntil(() => server.Calls.Count(x => x.EndsWith("/draft")) >= 2);
                    yield return new WaitForSecondsRealtime(.1f);
                    Assert.That(assets.Loaded.Count(x => x == "arena/draft"), Is.EqualTo(1), "The resumed room reopened the settled draft.");
                    server.DraftId = "new-draft";
                    server.SignalRoomChange.SetResult(true);
                    yield return new WaitUntil(() => Page("ArenaDraft") && Page("ArenaDraft").gameObject.activeInHierarchy);
                    Assert.That(assets.Loaded.Count(x => x == "arena/draft"), Is.EqualTo(2));
                    yield break;
                }
                home.Require<GameButton>("bag").onClick.Invoke();
                yield return new WaitUntil(() => Page("HeroInventory") && Page("HeroInventory").gameObject.activeInHierarchy);
                var inventory = Page("HeroInventory");
                if (failFirstArtifact)
                {
                    yield return new WaitUntil(() => server.Calls.Count == 5);
                    Assert.That(server.Calls.Count(x => x.EndsWith("/artifacts")), Is.EqualTo(2));
                    inventory.Require<GameButton>("back").onClick.Invoke();
                    home.Require<GameButton>("more").onClick.Invoke();
                    Page("HomeMoreMenu").Require<GameButton>("heroes").onClick.Invoke();
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(server.Calls.Count(x => x.EndsWith("/artifacts")), Is.EqualTo(2),
                        "A successful retry must be retained for the next page in this login.");
                    yield break;
                }
                inventory.Require<GameButton>("back").onClick.Invoke();
                home.Require<GameButton>("more").onClick.Invoke();
                Page("HomeMoreMenu").Require<GameButton>("heroes").onClick.Invoke();
                Assert.That(cover.activeInHierarchy, Is.True, "The hero route must reuse the login curtain.");
                Assert.That(Page("HomeMoreMenu").gameObject.activeInHierarchy, Is.False, "Dismiss the quick menu before the lobby fades.");
                yield return new WaitUntil(() => !cover.activeInHierarchy);
                Assert.That(inventory.gameObject.activeInHierarchy, Is.True);
                Assert.That(Page("HeroInventory"), Is.SameAs(inventory));
                if (waitInArena)
                {
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(assets.Loaded.Count, Is.EqualTo(beforeClicks));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/heroes"), Is.EqualTo(1));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/artifacts"), Is.EqualTo(1));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/equipment"), Is.EqualTo(1));
                    yield break;
                }
                inventory.Require<GameButton>("back").onClick.Invoke();
                home.Require<GameButton>("more").onClick.Invoke();
                Page("HomeMoreMenu").Require<GameButton>("summon").onClick.Invoke();
                yield return new WaitUntil(() => Page("SummonWindow") && Page("SummonWindow").gameObject.activeInHierarchy);
                if (crossFreeReset) yield return new WaitUntil(() => server.Calls.Count == 5);
                Assert.That(Page("SummonWindow").Require<GameButton>("covenant").interactable, Is.True);
                Assert.That(server.Calls.Count, Is.EqualTo(crossFreeReset ? 5 : 4), string.Join(", ", server.Calls));
                Assert.That(assets.Loaded.Count, Is.EqualTo(beforeClicks));
                Debug.Log("Home warm login: " + beforeClicks + " asset loads, " + string.Join(", ", server.Calls) + "; bag, heroes and summon reused all reads.");
            }
            finally { owner.Dispose(); Object.Destroy(canvas); }
        }
    }
}
#endif
