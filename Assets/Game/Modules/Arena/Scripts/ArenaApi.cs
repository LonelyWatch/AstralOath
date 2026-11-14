using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
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
    [Serializable, Preserve] public sealed class ArenaRoomState { public ArenaRoomInfo room; public ArenaPlayer[] members; }
    [Serializable, Preserve] public sealed class ArenaMessage { public long id; public ArenaPlayer player; public string text, createdAt, kind, stickerId; }
    [Serializable, Preserve] public sealed class ArenaCreateRequest { public string name, password, rule, firstPick; public bool allowChat, banProtection; public int prebanCount; }

    public sealed class ArenaRequestException : IOException
    {
        internal string Code { get; }
        internal ArenaRequestException(string code) : base(code) { Code = code; }
    }

    public sealed class ArenaApi
    {
        [Serializable, Preserve] private sealed class Session { public string token; public ArenaPlayer player; }
        [Serializable, Preserve] private sealed class Identity { public string displayName, avatarId; }
        [Serializable, Preserve] private sealed class Directory { public ArenaRoomInfo[] rooms; }
        [Serializable, Preserve] private sealed class History { public ArenaMessage[] messages; }
        [Serializable, Preserve] private sealed class Password { public string password; }
        [Serializable, Preserve] private sealed class Chat { public string text; }
        [Serializable, Preserve] private sealed class Sticker { public string kind = "sticker"; public string stickerId; }
        [Serializable, Preserve] private sealed class Error { public string code; }
        [Serializable, Preserve] private sealed class Operation { public string eventId, name; }

        [Serializable, Preserve] private sealed class DraftStart { public string requestId; }
        internal Uri Endpoint => endpoint;
        internal string Bearer => session?.token;
        private readonly Uri endpoint;
        private readonly CancellationToken lifetime;
        private Session session;
        private int pendingOperations;
        public ArenaApi(Uri endpoint, CancellationToken lifetime)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.lifetime = lifetime;
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

        public async Task ConnectAsync(string displayName, string avatarId, CancellationToken token)
        {
            if (session != null) return;
            session = await Send<Session>("sessions", new Identity { displayName = displayName, avatarId = avatarId }, token);
            if (session.player == null || string.IsNullOrWhiteSpace(session.token))
                throw new ArenaRequestException("invalid_response");
        }

        public async Task<ArenaRoomInfo[]> ListAsync(string visibility, string sort, bool ascending, string query, CancellationToken token)
        {
            string search = Uri.EscapeDataString(query ?? "");
            string direction = ascending ? "asc" : "desc";
            var result = await Send<Directory>($"rooms?visibility={visibility}&sort={sort}&direction={direction}&query={search}", null, token);
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
        public Task<ArenaDraftState> StartDraftAsync(string id, CancellationToken token) =>
            Send<ArenaDraftState>(Room(id) + "/draft/start", new DraftStart { requestId = Guid.NewGuid().ToString("N") }, token);
        internal Task<ArenaDraftState> SubmitDraftAsync(string id, ArenaDraftAction action, CancellationToken token) =>
            Send<ArenaDraftState>(Room(id) + "/draft/actions", action, token);
        private static string Room(string id) => "rooms/" + Uri.EscapeDataString(id);

        private async Task<T> Send<T>(string path, object data, CancellationToken token, int timeout = 10) where T : class
        {
            token.ThrowIfCancellationRequested();
            var url = new Uri(endpoint, "/api/arena/" + path);
            using var request = new UnityWebRequest(url, data == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeout,
                redirectLimit = 0
            };
            if (data != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            var requestSession = session;
            if (requestSession != null) request.SetRequestHeader("Authorization", "Bearer " + requestSession.token);
            var operation = request.SendWebRequest();
            if (!operation.isDone)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Complete(AsyncOperation _) => completion.TrySetResult(true);
                operation.completed += Complete;
                try
                {
                    using var cancellation = token.Register(() => completion.TrySetCanceled(token));
                    await completion.Task;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    request.Abort();
                    throw;
                }
                finally { operation.completed -= Complete; }
            }
            token.ThrowIfCancellationRequested();
            string json = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string code = null;
                try { if (!string.IsNullOrEmpty(json)) code = JsonUtility.FromJson<Error>(json)?.code; }
                catch (ArgumentException) { }
                if (request.responseCode == 401 && session == requestSession) session = null;
                throw new ArenaRequestException(code ?? (request.responseCode == 0 ? "unavailable" : "request_failed"));
            }
            if (request.responseCode == 204) return null;
            try { return JsonUtility.FromJson<T>(json) ?? throw new ArenaRequestException("invalid_response"); }
            catch (ArgumentException) { throw new ArenaRequestException("invalid_response"); }
        }
    }
}
