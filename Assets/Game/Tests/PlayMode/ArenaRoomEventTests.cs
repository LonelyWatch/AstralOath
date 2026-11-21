#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
    public sealed class ArenaRoomEventTests
    {
        private sealed class PendingSocket : MemoryStream
        {
            internal readonly TaskCompletionSource<int> PendingRead = new TaskCompletionSource<int>();
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => PendingRead.Task;
        }

        private sealed class SocketTransport : HttpMessageHandler
        {
            internal readonly PendingSocket Stream = new PendingSocket();
            internal readonly TaskCompletionSource<HttpResponseMessage> Response = new TaskCompletionSource<HttpResponseMessage>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
                request.RequestUri.AbsolutePath.EndsWith("/events", StringComparison.Ordinal)
                    ? Task.FromResult(new HttpResponseMessage { Content = new StreamContent(Stream) })
                    : Response.Task;
        }

        public enum SocketScenario { ClosingRoom, LiveRoom, ClosingDraft, LiveDraft }
        private static readonly SocketScenario[] SocketScenarios = (SocketScenario[])Enum.GetValues(typeof(SocketScenario));

        [UnityTest, Timeout(5000)]
        public IEnumerator WindowsSocketAbortRespectsRoomAndDraftLifetimes([ValueSource(nameof(SocketScenarios))] SocketScenario scenario)
        {
            bool feed = scenario == SocketScenario.ClosingRoom || scenario == SocketScenario.LiveRoom;
            bool leaving = scenario == SocketScenario.ClosingRoom || scenario == SocketScenario.ClosingDraft;
            using var scope = new CancellationTokenSource();
            var transport = new SocketTransport();
            using var api = new ArenaApi(new Uri("http://localhost/"), default,
                new GameAccountSession("room-test", "local", "Local", "c1001"), transport);
            var subscribe = typeof(ArenaApi).GetMethod("WatchRoomEventsAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Func<string, Task> receive = _ => Task.CompletedTask;
            Task request = feed ? (Task)subscribe.Invoke(api, new object[] { "r", receive, scope.Token }) :
                api.DraftAsync("r", scope.Token);
            Assert.That(request.IsCompleted, Is.False);
            if (leaving) scope.Cancel();
            var socket = new SocketException((int)(leaving ? SocketError.OperationAborted : SocketError.ConnectionReset));
            if (feed) transport.Stream.PendingRead.SetException(socket);
            else transport.Response.SetException(socket);
            yield return new WaitUntil(() => request.IsCompleted);
            if (leaving)
                Assert.That(() => request.GetAwaiter().GetResult(), Throws.InstanceOf<OperationCanceledException>());
            else
            {
                var error = Assert.Throws<ArenaRequestException>(() => request.GetAwaiter().GetResult());
                Assert.That(error.Message, Is.EqualTo("unavailable"));
                Assert.That(error.InnerException, Is.SameAs(socket));
            }
        }

        private sealed class Assets : IAssetService
        {
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : Object =>
                Task.FromResult(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Modules/Arena/" +
                    (address == ArenaRoom.Address ? "Res/Prefab/ArenaRoom.prefab" : "Configs/arena-zh-hans.json")));
        }

        private sealed class RoomTransport : HttpMessageHandler
        {
            public const string State = "{\"room\":{\"id\":\"r\",\"name\":\"Local\",\"rule\":\"basic\",\"memberCount\":1,\"capacity\":8,\"allowChat\":true},\"members\":[]}";
            public readonly List<string> Cursors = new List<string>();
            public int Connections, RoomReads, DraftReads, LastMessage;
            private TcpClient sender;

            public void Push(string data)
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                sender.GetStream().Write(bytes, 0, bytes.Length);
            }

            public void Disconnect() => sender?.Close();
            protected override void Dispose(bool disposing) { Disconnect(); base.Dispose(disposing); }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo("room-test"));
                string path = request.RequestUri.AbsolutePath;
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                if (path.EndsWith("/events", StringComparison.Ordinal))
                {
                    Assert.That(request.Headers.GetValues("Accept"), Is.EqualTo(new[] { "application/x-ndjson" }));
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    try
                    {
                        sender = new TcpClient();
                        sender.Connect((IPEndPoint)listener.LocalEndpoint);
                        response.Content = new StreamContent(listener.AcceptTcpClient().GetStream());
                    }
                    finally { listener.Stop(); }
                    Connections++;
                    Push("{\"kind\":\"sync\"}\n");
                }
                else if (path.EndsWith("/messages", StringComparison.Ordinal))
                {
                    var query = request.RequestUri.Query;
                    Cursors.Add(query);
                    string items = query == "?after=" + LastMessage || LastMessage == 0 ? "" :
                        "{\"id\":" + LastMessage + ",\"kind\":\"system\",\"text\":\"message" + LastMessage + "\"}";
                    response.Content = new StringContent("{\"messages\":[" + items + "]}");
                }
                else if (path.EndsWith("/draft", StringComparison.Ordinal))
                {
                    DraftReads++;
                    response.StatusCode = HttpStatusCode.NoContent;
                }
                else { RoomReads++; response.Content = new StringContent(State); }
                return Task.FromResult(response);
            }
        }

        [UnityTest, Timeout(25000)]
        public IEnumerator RoomConsumesOneFeedAndCatchesUpFromItsCursorOnReconnect()
        {
            using var scope = new BindingScope(default);
            using var windows = new WindowCoordinator(scope.Token);
            var transport = new RoomTransport();
            using var api = new ArenaApi(new Uri("http://localhost/"), scope.Token,
                new GameAccountSession("room-test", "local", "Local", "c1001"), transport);
            var canvas = new GameObject("Room test", typeof(Canvas));
            try
            {
                var assets = new Assets();
                var registry = new UIViewRegistry();
                registry.Register<ArenaRoom>();
                var room = registry.CreateAsync<ArenaRoom>(assets, canvas.transform, scope).GetAwaiter().GetResult();
                var labels = ArenaLabels.LoadAsync(assets, scope, "zh-Hans", "ja").GetAwaiter().GetResult();
                var binder = new ArenaRoomBinder(room, labels, api, windows, () => Task.CompletedTask, _ => Task.CompletedTask);
                binder.OpenAsync(JsonUtility.FromJson<ArenaRoomState>(RoomTransport.State), scope).GetAwaiter().GetResult();
                Assert.That(transport.Connections, Is.EqualTo(1), "The room must subscribe on open.");
                yield return new WaitUntil(() => transport.Cursors.Count == 1 && transport.DraftReads == 1);
                yield return new WaitForSecondsRealtime(2.25f);
                Assert.That(transport.RoomReads, Is.EqualTo(1));
                Assert.That(transport.DraftReads, Is.EqualTo(1));
                Assert.That(transport.Cursors, Is.EqualTo(new[] { "?after=0" }));
                transport.LastMessage = 11;
                transport.Push("{\"kind\":\"ch");
                yield return null;
                transport.Push("at\"}\n{\"kind\":\"room\"}\n");
                bool Visible(string value) => room.Messages.content.GetComponentsInChildren<GameText>()
                    .Any(label => label.text == value);
                yield return new WaitUntil(() => Visible("message11") && transport.DraftReads == 2);
                Assert.That(transport.RoomReads, Is.EqualTo(2));
                transport.LastMessage = 12;
                transport.Disconnect();
                yield return new WaitUntil(() => transport.Connections == 2 && Visible("message12"));
                Assert.That(transport.Cursors.Last(), Is.EqualTo("?after=11"));
                scope.Dispose();
                yield return new WaitForSecondsRealtime(1.5f);
                Assert.That(transport.Connections, Is.EqualTo(2), "A closed room subscribed again.");
            }
            finally { Object.Destroy(canvas); }
        }
    }
}
#endif
