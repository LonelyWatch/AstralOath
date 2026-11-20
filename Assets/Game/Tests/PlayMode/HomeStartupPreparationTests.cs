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
            internal readonly TaskCompletionSource<bool> AllowDirectoryList = new TaskCompletionSource<bool>();
            internal bool CrossFreeReset, FailFirstArtifact, KeepArenaRoom, SlowDirectory;
            internal int InventoryVersion;
            internal volatile string DraftId = "settled-draft";
            private bool left;
            private volatile bool stopping;
            internal Uri Address => new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port);
            internal ReadServer() { listener.Start(); worker = Task.Run(Run); }
            private async Task Run()
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync();
                        using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                        var line = (await reader.ReadLineAsync()).Split(' ');
                        Calls.Enqueue(line[0] + " " + line[1]);
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        await AllowResponse.Task.ConfigureAwait(false);
                        string schedule = CrossFreeReset ? "\"serverTimeUtc\":\"2030-01-01T01:00:00Z\",\"freeResetUtc\":\"2030-01-01T01:00:00.001Z\"," : "";
                        string body = line[1].Contains("summon") ? "{" + schedule + "\"banners\":[{\"id\":\"covenant\",\"entries\":[]}],\"wishHeroes\":[],\"wishArtifacts\":[]}" :
                            "{\"capacity\":" + (1000 + InventoryVersion) + ",\"used\":0,\"heroes\":[],\"artifacts\":[],\"materials\":{}}";
                        bool fail = FailFirstArtifact && line[1].EndsWith("/artifacts") && Calls.Count(x => x.EndsWith("/artifacts")) == 1;
                        string status = fail ? "503 Unavailable" : "200 OK";
                        if (line[1] == "/api/arena/rooms/current") { status = "204 No Content"; body = ""; }
                        if (line[1].StartsWith("/api/arena/rooms?", StringComparison.Ordinal)) body = "{\"rooms\":[]}";
                        if ((KeepArenaRoom || SlowDirectory) && line[1].StartsWith("/api/arena/rooms", StringComparison.Ordinal))
                        {
                            status = "200 OK";
                            const string host = "{\"id\":\"host\",\"displayName\":\"Host\",\"avatarId\":\"c1001\"}";
                            const string guest = "{\"id\":\"test\",\"displayName\":\"Guest\",\"avatarId\":\"c1001\"}";
                            if (line[1].EndsWith("/leave"))
                            {
                                left = true;
                                status = KeepArenaRoom ? "503 Unavailable" : "204 No Content";
                                body = KeepArenaRoom ? "{\"code\":\"unavailable\"}" : "";
                            }
                            else if (SlowDirectory && left && line[1].EndsWith("/current")) { status = "204 No Content"; body = ""; }
                            else if (line[1].Contains("/rooms?"))
                            {
                                await AllowDirectoryList.Task.ConfigureAwait(false);
                                body = "{\"rooms\":[]}";
                            }
                            else if (line[1].Contains("/messages?")) body = "{\"messages\":[]}";
                            else if (line[1].EndsWith("/draft"))
                            {
                                string phase = left && DraftId == "settled-draft" ? "battle" : "preban";
                                body = "{\"id\":\"" + DraftId + "\",\"roomId\":\"surviving-room\",\"phase\":\"" + phase +
                                    "\",\"battleId\":\"finished-battle\",\"firstPick\":\"host\",\"mySide\":\"guest\",\"eligibleHeroIds\":[],\"host\":{\"player\":" +
                                    host + ",\"prebans\":[],\"picks\":[],\"formation\":[]},\"guest\":{\"player\":" + guest + ",\"prebans\":[],\"picks\":[],\"formation\":[]}}";
                            }
                            else body = "{\"room\":{\"id\":\"surviving-room\",\"name\":\"Recovered room\",\"rule\":\"basic\",\"firstPick\":\"host\",\"capacity\":2,\"memberCount\":2,\"host\":" + host + "},\"members\":[" + host + "," + guest + "]}";
                        }
                        byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
                        await stream.WriteAsync(data, 0, data.Length);
                    }
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
                AllowResponse.TrySetResult(true);
                AllowDirectoryList.TrySetResult(true);
                worker.GetAwaiter().GetResult();
            }
        }

        [UnityTest] public IEnumerator HomeReusesLoginResourcesForBagHeroesAndSummon() => Check(false, false);
        [UnityTest] public IEnumerator CancellingLoginRemovesTheBlackScreen() => Check(true, false);
        [UnityTest] public IEnumerator FailedHomeLoadRemovesTheBlackScreen() => Check(false, true);
        [UnityTest] public IEnumerator SummonRereadsStateAfterTheFreeReset() => Check(false, false, crossFreeReset: true);
        [UnityTest] public IEnumerator BagRetriesOnlyTheUnavailableInitialRead() => Check(false, false, failFirstArtifact: true);
        [UnityTest, Timeout(65000)]
        public IEnumerator BagAndHeroSnapshotsStayResidentWhileWaitingInTheArena() => Check(false, false, waitInArena: true);
        [UnityTest] public IEnumerator BattleReturnReopensTheRoomWhenLeaveFailsAndCurrentStillContainsTheGuest() => Check(false, false, recoverRoom: true);
        [UnityTest] public IEnumerator ReturnedDirectoryCanCloseWhileTheRoomListIsStillLoading() => Check(false, false, slowDirectory: true);

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator LocalWritesInvalidateOnlyAffectedLoginSnapshotsAndAccountsDoNotShareReads()
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
                    if (login == 2) continue;

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
                    var drawing = (Task)draw.Invoke(summon, new object[] { request, owner.Token });
                    yield return new WaitUntil(() => drawing.IsCompleted);
                    drawing.GetAwaiter().GetResult();
                    Assert.That(Value("Heroes"), Is.Null);
                    Assert.That(Value("Artifacts"), Is.Null);
                    var reload = Task.WhenAll(Fetch("ReadHeroesAsync"), Fetch("ReadArtifactsAsync"));
                    yield return new WaitUntil(() => reload.IsCompleted);
                    reload.GetAwaiter().GetResult();
                    Assert.That(((HeroInventorySnapshot)Value("Heroes")).capacity, Is.EqualTo(1012));
                    Assert.That(((ArtifactInventorySnapshot)Value("Artifacts")).capacity, Is.EqualTo(1012));
                }
                finally { owner.Dispose(); Object.Destroy(canvas); }
            }
            Assert.That(server.Calls.Count(x => x == "GET /api/inventory/heroes"), Is.EqualTo(3));
            Assert.That(server.Calls.Count(x => x == "GET /api/inventory/artifacts"), Is.EqualTo(4));
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

        private static IEnumerator Check(bool cancel, bool fail, bool crossFreeReset = false, bool failFirstArtifact = false, bool recoverRoom = false, bool slowDirectory = false, bool waitInArena = false)
        {
            using var server = new ReadServer { CrossFreeReset = crossFreeReset, FailFirstArtifact = failFirstArtifact, KeepArenaRoom = recoverRoom, SlowDirectory = slowDirectory };
            using var owner = new BindingScope(default);
            using var services = new ServiceRegistry(owner.Token);
            using var entry = new FoundationEntry();
            var canvas = new GameObject("Prepared home test", typeof(Canvas));
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
                Assert.That(Page("HomeWindow"), Is.Null);
                if (cancel) owner.Dispose();
                else if (fail) assets.Gate.SetException(new IOException("Test unavailable asset"));
                else
                {
                    assets.Gate.SetResult(true);
                    yield return new WaitUntil(() => !server.Calls.IsEmpty);
                    Assert.That(cover.activeInHierarchy, Is.True, "The account read is still pending.");
                    Assert.That(Page("HomeWindow").gameObject.activeInHierarchy, Is.False);
                    server.AllowResponse.SetResult(true);
                }
                yield return new WaitUntil(() => start.IsCompleted);
                Assert.That(GameObject.Find("Home loading curtain"), Is.Null);
                if (cancel || fail) { Assert.That(start.IsCanceled || start.IsFaulted, Is.True); yield break; }
                start.GetAwaiter().GetResult();
                Assert.That(Page("HomeWindow").gameObject.activeInHierarchy, Is.True);
                Assert.That(assets.Loaded.Distinct().Count(), Is.EqualTo(12), "All seven first-open dependencies should be resident.");
                Assert.That(server.Calls.Count, Is.EqualTo(3), string.Join(", ", server.Calls));
                Assert.That(server.Calls.All(x => x.StartsWith("GET ")), Is.True);
                int beforeClicks = assets.Loaded.Count;
                var home = Page("HomeWindow");
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
                    Assert.That(server.Calls.Count(x => x == "POST /api/arena/rooms/surviving-room/leave"), Is.EqualTo(1));
                    Assert.That(server.Calls.Count(x => x == "GET /api/arena/rooms/current"), Is.EqualTo(2));
                    Assert.That(Page("ArenaRoom").gameObject.activeInHierarchy, Is.True);
                    Assert.That(Page("ArenaDirectory").gameObject.activeInHierarchy, Is.False);
                    int polls = server.Calls.Count(x => x.EndsWith("/draft"));
                    yield return new WaitUntil(() => server.Calls.Count(x => x.EndsWith("/draft")) > polls);
                    yield return new WaitForSecondsRealtime(.1f);
                    Assert.That(assets.Loaded.Count(x => x == "arena/draft"), Is.EqualTo(1), "The resumed room reopened the settled draft.");
                    server.DraftId = "new-draft";
                    yield return new WaitUntil(() => Page("ArenaDraft") && Page("ArenaDraft").gameObject.activeInHierarchy);
                    Assert.That(assets.Loaded.Count(x => x == "arena/draft"), Is.EqualTo(2));
                    yield break;
                }
                home.Require<GameButton>("bag").onClick.Invoke();
                yield return new WaitUntil(() => Page("HeroInventory") && Page("HeroInventory").gameObject.activeInHierarchy);
                var inventory = Page("HeroInventory");
                if (failFirstArtifact)
                {
                    yield return new WaitUntil(() => server.Calls.Count == 4);
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
                Assert.That(Page("HeroInventory"), Is.SameAs(inventory));
                if (waitInArena)
                {
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(assets.Loaded.Count, Is.EqualTo(beforeClicks));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/heroes"), Is.EqualTo(1));
                    Assert.That(server.Calls.Count(x => x == "GET /api/inventory/artifacts"), Is.EqualTo(1));
                    yield break;
                }
                inventory.Require<GameButton>("back").onClick.Invoke();
                home.Require<GameButton>("more").onClick.Invoke();
                Page("HomeMoreMenu").Require<GameButton>("summon").onClick.Invoke();
                yield return new WaitUntil(() => Page("SummonWindow") && Page("SummonWindow").gameObject.activeInHierarchy);
                if (crossFreeReset) yield return new WaitUntil(() => server.Calls.Count == 4);
                Assert.That(Page("SummonWindow").Require<GameButton>("covenant").interactable, Is.True);
                Assert.That(server.Calls.Count, Is.EqualTo(crossFreeReset ? 4 : 3), string.Join(", ", server.Calls));
                Assert.That(assets.Loaded.Count, Is.EqualTo(beforeClicks));
                Debug.Log("Home warm login: " + beforeClicks + " asset loads, " + string.Join(", ", server.Calls) + "; bag, heroes and summon reused all reads.");
            }
            finally { owner.Dispose(); Object.Destroy(canvas); }
        }
    }
}
#endif
