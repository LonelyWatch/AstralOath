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
        public int memberCount, capacity;
    }
    [Serializable, Preserve] public sealed class ArenaRoomState { public ArenaRoomInfo room; public ArenaPlayer[] members; }
    [Serializable, Preserve] public sealed class ArenaMessage { public long id; public ArenaPlayer player; public string text; }
    [Serializable, Preserve] public sealed class ArenaCreateRequest { public string name, password, rule; public bool allowChat; }

    internal sealed class ArenaRequestException : IOException
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
        [Serializable, Preserve] private sealed class Error { public string code; }

        private readonly Uri endpoint;
        private Session session;
        public ArenaApi(Uri endpoint) => this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        public ArenaPlayer Player => session?.player;

        public async Task ConnectAsync(string displayName, string avatarId, CancellationToken token)
        {
            if (session != null) return;
            session = await Send<Session>("sessions", new Identity { displayName = displayName, avatarId = avatarId }, token);
            if (session.player == null || string.IsNullOrWhiteSpace(session.token))
                throw new ArenaRequestException("invalid_response");
        }

        public async Task<ArenaRoomInfo[]> ListAsync(string visibility, string sort, string query, CancellationToken token)
        {
            string search = Uri.EscapeDataString(query ?? "");
            var result = await Send<Directory>($"rooms?visibility={visibility}&sort={sort}&query={search}", null, token);
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
        public async Task<ArenaMessage[]> ReadMessagesAsync(string id, long after, CancellationToken token)
        {
            var result = await Send<History>(Room(id) + "/messages?after=" + after.ToString(CultureInfo.InvariantCulture), null, token);
            return result.messages ?? Array.Empty<ArenaMessage>();
        }
        public Task<ArenaMessage> SendMessageAsync(string id, string text, CancellationToken token) =>
            Send<ArenaMessage>(Room(id) + "/messages", new Chat { text = text }, token);

        private static string Room(string id) => "rooms/" + Uri.EscapeDataString(id);

        private async Task<T> Send<T>(string path, object data, CancellationToken token) where T : class
        {
            token.ThrowIfCancellationRequested();
            var url = new Uri(endpoint, "/api/arena/" + path);
            using var request = new UnityWebRequest(url, data == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
                redirectLimit = 0
            };
            if (data != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            if (session != null) request.SetRequestHeader("Authorization", "Bearer " + session.token);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            string json = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string code = null;
                try { if (!string.IsNullOrEmpty(json)) code = JsonUtility.FromJson<Error>(json)?.code; }
                catch (ArgumentException) { }
                if (request.responseCode == 401) session = null;
                throw new ArenaRequestException(code ?? (request.responseCode == 0 ? "unavailable" : "request_failed"));
            }
            if (request.responseCode == 204) return null;
            try { return JsonUtility.FromJson<T>(json) ?? throw new ArenaRequestException("invalid_response"); }
            catch (ArgumentException) { throw new ArenaRequestException("invalid_response"); }
        }
    }
}
