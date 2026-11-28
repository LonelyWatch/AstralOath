using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Arena
{
    [Serializable, Preserve] public sealed class ArenaPlayer { public string id, displayName, avatarId; }
    [Serializable, Preserve] public sealed class ArenaRoomInfo
    {
        public string id, name, rule;
        public ArenaPlayer host;
        public bool requiresPassword, allowChat;
        public int memberCount, capacity, prebanCount;
        public bool banProtection;
        public string firstPick;
    }
    [Serializable, Preserve] public sealed class ArenaRoomState
    {
        public ArenaRoomInfo room;
        public ArenaPlayer[] members;
        public bool supportsDraftReady;
        public string[] readyPlayerIds;
    }
    [Serializable, Preserve] public sealed class ArenaMessage { public long id; public ArenaPlayer player; public string text, createdAt, kind, stickerId, systemCode; }
    [Serializable, Preserve] public sealed class ArenaCreateRequest { public string name, password, rule; public bool allowChat; }

    public sealed class ArenaRequestException : IOException
    {
        internal string Code { get; }
        internal ArenaRequestException(string code, Exception cause = null) : base(code, cause) { Code = code; }
    }

    public sealed class ArenaApi : IDisposable
    {
        [Serializable, Preserve] private sealed class Session { public string token; public ArenaPlayer player; }
        [Serializable, Preserve] private sealed class Identity { public string displayName, avatarId; }
        [Serializable, Preserve] private sealed class Directory { public ArenaRoomInfo[] rooms; }
        [Serializable, Preserve] private sealed class History { public ArenaMessage[] messages; }
        [Serializable, Preserve] private sealed class RoomEvent { public string kind; }
        [Serializable, Preserve] private sealed class Password { public string password; }
        [Serializable, Preserve] private sealed class Chat { public string text; }
        [Serializable, Preserve] private sealed class Sticker { public string kind = "sticker"; public string stickerId; }
        [Serializable, Preserve] private sealed class Error { public string code; }
        [Serializable, Preserve] private sealed class Operation { public string eventId, name; }

        [Serializable, Preserve] private sealed class DraftStart { public string requestId; public bool requireReady; }
        [Serializable, Preserve] private sealed class DraftReady { public bool ready; }
        [Serializable, Preserve] private sealed class PrebanSettings { public int prebanCount; }
        [Serializable, Preserve] private sealed class FirstPickSettings { public string firstPick; }
        [Serializable, Preserve] private sealed class ProtectionSettings { public bool banProtection; }
        internal Uri Endpoint => endpoint;
        internal string Bearer => session?.token;
        private readonly Uri endpoint;
        private readonly HttpClient http;
        private readonly CancellationToken lifetime;
        private readonly SemaphoreSlim connectionGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private Session session;
        private int pendingOperations;
        private int disposed;
        public ArenaApi(Uri endpoint, CancellationToken lifetime, GameAccountSession account = null)
            : this(endpoint, lifetime, account, null) { }

        public ArenaApi(Uri endpoint, CancellationToken lifetime, GameAccountSession account, HttpMessageHandler transport)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.lifetime = lifetime;
            http = new HttpClient(transport ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 2 * 1024 * 1024
            };
            if (account != null)
            {
                session = new Session
                {
                    token = account.Token,
                    player = new ArenaPlayer { id = account.PlayerId, displayName = account.DisplayName, avatarId = account.AvatarId }
                };
            }
        }

        internal async Task RecordOperationAsync(string name)
        {
            if (session == null || lifetime.IsCancellationRequested || pendingOperations >= 4) return;
            ++pendingOperations;
            try
            {
                await Send<Error>("telemetry", new Operation { eventId = Guid.NewGuid().ToString("N"), name = name }, lifetime, 4);
            }
            catch (ArenaRequestException) { }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally { --pendingOperations; }
        }
        public ArenaPlayer Player => session?.player;

        public async Task<string> GetSessionTokenAsync(string displayName, CancellationToken token)
        {
            await ConnectAsync(displayName, "c1001", token);
            token.ThrowIfCancellationRequested();
            lifetime.ThrowIfCancellationRequested();
            closing.Token.ThrowIfCancellationRequested();
            return session?.token ?? throw new ArenaRequestException("unauthorized");
        }

        public async Task ConnectAsync(string displayName, string avatarId, CancellationToken token)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            using var closeRegistration = closing.Token.Register(cancellation.Cancel);
            var connectionToken = cancellation.Token;
            connectionToken.ThrowIfCancellationRequested();
            if (session != null) return;
            await connectionGate.WaitAsync(connectionToken);
            try
            {
                connectionToken.ThrowIfCancellationRequested();
                if (session != null) return;
                var connected = await Send<Session>("sessions", new Identity { displayName = displayName, avatarId = avatarId }, connectionToken);
                connectionToken.ThrowIfCancellationRequested();
                if (connected?.player == null || string.IsNullOrWhiteSpace(connected.token))
                    throw new ArenaRequestException("invalid_response");
                if (session == null) session = connected;
            }
            finally { connectionGate.Release(); }
        }

        public async Task<ArenaRoomInfo[]> ListAsync(string visibility, string sort, bool ascending, string query, CancellationToken token)
        {
            string search = Uri.EscapeDataString(query ?? "");
            string direction = ascending ? "asc" : "desc";
            var result = await Send<Directory>($"rooms?visibility={visibility}&sort={sort}&direction={direction}&query={search}", null, token);
            if (result == null) throw new ArenaRequestException("invalid_response");
            return result.rooms ?? Array.Empty<ArenaRoomInfo>();
        }

        public Task<ArenaRoomState> CreateAsync(ArenaCreateRequest data, CancellationToken token) =>
            Send<ArenaRoomState>("rooms", data, token);
        public Task<ArenaRoomState> JoinAsync(string id, string password, CancellationToken token) =>
            Send<ArenaRoomState>(Room(id) + "/join", new Password { password = password }, token);
        public Task<ArenaRoomState> CurrentAsync(CancellationToken token) =>
            Send<ArenaRoomState>("rooms/current", null, token);
        public Task<ArenaRoomState> GetAsync(string id, CancellationToken token) =>
            Send<ArenaRoomState>(Room(id), null, token);

        internal async Task WatchRoomEventsAsync(string id, Func<string, Task> receive, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "/api/arena/" + Room(id) + "/events"));
            request.Headers.TryAddWithoutValidation("Accept", "application/x-ndjson");
            var account = session ?? throw new ArenaRequestException("unauthorized");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.token);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized && session == account) session = null;
                    throw ResponseError(await response.Content.ReadAsStringAsync());
                }
                using var stream = await response.Content.ReadAsStreamAsync();
                using var onCancel = deadline.Token.Register(stream.Dispose);
                // One record can span reads. Bound the pending bytes even if a proxy returns an invalid body.
                var buffer = new byte[512];
                int pending = 0;
                while (true)
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(45));
                    int read = await stream.ReadAsync(buffer, pending, buffer.Length - pending, deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (read == 0) throw new ArenaRequestException("unavailable");
                    deadline.CancelAfter(Timeout.Infinite);
                    int used = pending + read, start = 0;
                    for (int i = pending; i < used; ++i)
                    {
                        if (buffer[i] != '\n') continue;
                        var json = Encoding.UTF8.GetString(buffer, start, i - start);
                        start = i + 1;
                        string kind;
                        try { kind = JsonUtility.FromJson<RoomEvent>(json)?.kind; }
                        catch (ArgumentException) { throw new ArenaRequestException("invalid_response"); }
                        if (kind is "sync" or "room" or "chat" or "heartbeat")
                        {
                            token.ThrowIfCancellationRequested();
                            lifetime.ThrowIfCancellationRequested();
                            await receive(kind);
                        }
                    }
                    pending = used - start;
                    if (pending == buffer.Length) throw new ArenaRequestException("invalid_response");
                    if (pending > 0) Buffer.BlockCopy(buffer, start, buffer, 0, pending);
                }
            }
            catch (ArenaRequestException) { throw; }
            catch (Exception error) when (error is OperationCanceledException or IOException or HttpRequestException
                or ObjectDisposedException or SocketException or WebException)
            {
                token.ThrowIfCancellationRequested();
                lifetime.ThrowIfCancellationRequested();
                throw new ArenaRequestException("unavailable", error);
            }
        }
        public Task LeaveAsync(string id, CancellationToken token) => Send<Error>(Room(id) + "/leave", new object(), token);
        public async Task<ArenaMessage[]> ReadMessagesAsync(string id, long after, CancellationToken token, bool waitForNewMessages = false)
        {
            string path = Room(id) + "/messages?after=" + after.ToString(CultureInfo.InvariantCulture);
            if (waitForNewMessages) path += "&waitMs=8000";
            var result = await Send<History>(path, null, token, waitForNewMessages ? 12 : 10);
            return result?.messages ?? Array.Empty<ArenaMessage>();
        }
        public Task<ArenaMessage> SendMessageAsync(string id, string text, CancellationToken token) =>
            Send<ArenaMessage>(Room(id) + "/messages", new Chat { text = text }, token);

        public Task<ArenaMessage> SendStickerAsync(string id, string stickerId, CancellationToken token) =>
            Send<ArenaMessage>(Room(id) + "/messages", new Sticker { stickerId = stickerId }, token);

        public Task<ArenaDraftState> DraftAsync(string id, CancellationToken token) =>
            Send<ArenaDraftState>(Room(id) + "/draft", null, token);
        public Task<ArenaDraftState> StartDraftAsync(string id, CancellationToken token, bool requireReady = false) =>
            Send<ArenaDraftState>(Room(id) + "/draft/start",
                new DraftStart { requestId = Guid.NewGuid().ToString("N"), requireReady = requireReady }, token);
        internal Task<ArenaRoomState> SetReadyAsync(string id, bool ready, CancellationToken token) =>
            Send<ArenaRoomState>(Room(id) + "/draft/ready", new DraftReady { ready = ready }, token);
        internal Task<ArenaRoomState> SetPrebanCountAsync(string id, int count, CancellationToken token) =>
            SaveDraftSettingAsync(id, new PrebanSettings { prebanCount = count }, token);
        internal Task<ArenaRoomState> SetFirstPickAsync(string id, string seat, CancellationToken token) =>
            SaveDraftSettingAsync(id, new FirstPickSettings { firstPick = seat }, token);
        internal Task<ArenaRoomState> SetBanProtectionAsync(string id, bool enabled, CancellationToken token) =>
            SaveDraftSettingAsync(id, new ProtectionSettings { banProtection = enabled }, token);
        private Task<ArenaRoomState> SaveDraftSettingAsync(string id, object setting, CancellationToken token) =>
            Send<ArenaRoomState>(Room(id) + "/draft/settings", setting, token);

        internal Task<ArenaDraftState> SubmitDraftAsync(string id, ArenaDraftAction action, CancellationToken token) =>
            Send<ArenaDraftState>(Room(id) + "/draft/actions", action, token);
        internal Task<ArenaDraftState> PreviewDraftAsync(string id, ArenaDraftPreview preview, CancellationToken token) =>
            Send<ArenaDraftState>(Room(id) + "/draft/preview", preview, token, 4);
        private static string Room(string id) => "rooms/" + Uri.EscapeDataString(id);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            closing.Cancel();
            http.Dispose();
            // Connect callers may still unwind/release or register cancellation.
            // Neither synchronizer uses WaitHandle; leave these managed objects to
            // the API lifetime's GC instead of disposing underneath those callers.
        }

        private async Task<T> Send<T>(string path, object data, CancellationToken token, int timeout = 10) where T : class
        {
            token.ThrowIfCancellationRequested();
            lifetime.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
            var url = new Uri(endpoint, "/api/arena/" + path);
            using var request = new HttpRequestMessage(data == null ? HttpMethod.Get : HttpMethod.Post, url);
            if (data != null)
                request.Content = new StringContent(JsonUtility.ToJson(data), Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            var requestSession = session;
            if (requestSession != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", requestSession.token);

            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (response.StatusCode == HttpStatusCode.Unauthorized && session == requestSession)
                    session = null;
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return null;

                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode) throw ResponseError(json);
                try { return JsonUtility.FromJson<T>(json) ?? throw new ArenaRequestException("invalid_response"); }
                catch (ArgumentException) { throw new ArenaRequestException("invalid_response"); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new ArenaRequestException("unavailable");
            }
            catch (Exception error) when (error is HttpRequestException or SocketException)
            {
                token.ThrowIfCancellationRequested();
                lifetime.ThrowIfCancellationRequested();
                throw new ArenaRequestException("unavailable", error);
            }
        }

        private static ArenaRequestException ResponseError(string json)
        {
            string code = null;
            try { if (!string.IsNullOrEmpty(json)) code = JsonUtility.FromJson<Error>(json)?.code; }
            catch (ArgumentException) { }
            return new ArenaRequestException(code ?? "request_failed");
        }
    }
}
