#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDirectoryRefreshTests
    {
        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object
            {
                string file = address == ArenaDirectory.Address ? "Res/Prefab/ArenaDirectory.prefab"
                    : address == ArenaCreateRoom.Address ? "Res/Prefab/ArenaCreateRoom.prefab" : "Configs/arena-zh-hans.json";
                return Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Arena/" + file));
            }
        }

        private sealed class DirectoryTransport : HttpMessageHandler
        {
            public const string Room = "{\"id\":\"r1\",\"name\":\"Test\",\"rule\":\"basic\",\"capacity\":8,\"memberCount\":1}";
            public string Entries = Room;
            public bool InRoom, Overlap, HoldList;
            public int Current, Lists, Audits;
            public readonly TaskCompletionSource<bool> ReleaseAudit = new TaskCompletionSource<bool>();
            private int readingMembership;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.Headers.Authorization?.Parameter != "directory-test") throw new InvalidOperationException("Missing account");
                switch (request.RequestUri.AbsolutePath)
                {
                    case "/api/arena/rooms/current":
                        ++Current;
                        Interlocked.Increment(ref readingMembership);
                        try { await Task.Delay(120, token); }
                        finally { Interlocked.Decrement(ref readingMembership); }
                        return InRoom ? Json("{\"room\":" + Room + "}") : new HttpResponseMessage(HttpStatusCode.NoContent);
                    case "/api/arena/rooms":
                        ++Lists;
                        Overlap |= Volatile.Read(ref readingMembership) != 0;
                        await Task.Delay(HoldList ? Timeout.Infinite : 120, token);
                        return Json("{\"rooms\":[" + Entries + "]}");
                    case "/api/arena/telemetry":
                        ++Audits;
                        await ReleaseAudit.Task;
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    default: throw new InvalidOperationException(request.RequestUri.AbsolutePath);
                }
            }

            private static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json) };
        }

        [UnityTest]
        public IEnumerator ManualRefreshReusesRowsAndChecksMembershipWhileFetchingTheList()
        {
            var transport = new DirectoryTransport();
            using var lifetime = new BindingScope(default);
            using var windows = new WindowCoordinator(lifetime.Token);
            using var api = new ArenaApi(new Uri("http://localhost/"), lifetime.Token,
                new GameAccountSession("directory-test", "local", "Local", "c1001"), transport);
            var canvas = new GameObject("Directory refresh", typeof(Canvas));
            try
            {
                var assets = new Assets();
                var registry = new UIViewRegistry();
                registry.Register<ArenaDirectory>();
                registry.Register<ArenaCreateRoom>();
                var directory = registry.CreateAsync<ArenaDirectory>(assets, canvas.transform, lifetime);
                var create = registry.CreateAsync<ArenaCreateRoom>(assets, canvas.transform, lifetime);
                var labels = ArenaLabels.LoadAsync(assets, lifetime, "zh-Hans", "ja");
                yield return new WaitUntil(() => directory.IsCompleted && create.IsCompleted && labels.IsCompleted);
                var view = directory.Result;
                ArenaRoomState entered = null;
                var binder = new ArenaDirectoryBinder(view, create.Result, labels.Result, api, windows,
                    (state, _) => { entered = state; return Task.CompletedTask; }, () => Task.CompletedTask, "Local");
                var timer = Stopwatch.StartNew();
                var opening = binder.OpenAsync(lifetime);
                yield return new WaitUntil(() => opening.IsCompleted);
                opening.GetAwaiter().GetResult();
                long cold = timer.ElapsedMilliseconds;
                GameObject VisibleRow() => view.List.content.Cast<Transform>().Single(item => item.gameObject.activeSelf).gameObject;
                var initial = VisibleRow();
                transport.Overlap = false;
                timer.Restart();
                view.Refresh.onClick.Invoke();
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                UnityEngine.Debug.Log($"ARENA_DIRECTORY_TIMING coldMs={cold} warmMs={timer.ElapsedMilliseconds} latencyPerReadMs=120");
                Assert.That(transport.Current, Is.EqualTo(2));
                Assert.That(transport.Lists, Is.EqualTo(2));
                Assert.That(transport.Audits, Is.EqualTo(1));
                Assert.That(transport.ReleaseAudit.Task.IsCompleted, Is.False);
                Assert.That(transport.Overlap, Is.True, "The manual refresh waited an extra round trip for membership.");
                Assert.That(VisibleRow(), Is.SameAs(initial), "An unchanged snapshot must retain the visible row.");
                transport.Entries = DirectoryTransport.Room.Replace("\"memberCount\":1", "\"memberCount\":2");
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                Assert.That(VisibleRow().GetComponent<BindingMap>().Require<GameText>("count").text, Is.EqualTo("2/8"));
                transport.Entries = string.Join(",", Enumerable.Range(1, 15).Select(i =>
                    DirectoryTransport.Room.Replace("r1", "room-" + i)));
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                Canvas.ForceUpdateCanvases();
                view.List.verticalNormalizedPosition = .4f;
                var offset = view.List.content.anchoredPosition.y;
                Assert.That(offset, Is.GreaterThan(1f));
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                Assert.That(view.List.content.anchoredPosition.y, Is.EqualTo(offset).Within(1f));
                transport.Entries = "";
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                Assert.That(view.List.content.Cast<Transform>().Any(row => row.gameObject.activeSelf), Is.False);
                transport.InRoom = true;
                transport.HoldList = true;
                transport.Entries = DirectoryTransport.Room;
                view.Refresh.onClick.Invoke();
                yield return new WaitUntil(() => view.Create.interactable);
                Assert.That(entered?.room?.id, Is.EqualTo("r1"));
                Assert.That(view.List.content.Cast<Transform>().Any(row => row.gameObject.activeSelf), Is.False,
                    "Membership must take precedence over the directory result.");
            }
            finally { transport.ReleaseAudit.TrySetResult(true); Object.Destroy(canvas); }
        }
    }
}
#endif
