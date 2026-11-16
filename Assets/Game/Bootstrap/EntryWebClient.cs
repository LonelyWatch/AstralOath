using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace AstralOath.Client.Bootstrap
{
    internal sealed class EntryWebClient
    {
        internal const string RealmId = "undying-memory";
        private readonly Uri endpoint;

        internal EntryWebClient(Uri endpoint) => this.endpoint = endpoint;

        internal Task<Policy> ReadPolicyAsync(CancellationToken token, bool allowUnavailable = false) =>
            SendAsync<Policy>("version", null, null, token, allowUnavailable);

        internal Task RequestCodeAsync(string email, CancellationToken token) =>
            SendAsync<Reply>("email-code", new Credentials { email = email }, null, token);

        internal async Task<string> AuthenticateAsync(EntryAuthAction action, bool register, CancellationToken token)
        {
            var request = new Credentials { password = action.Password };
            if (register)
            {
                request.username = action.Account?.Trim();
                request.email = action.Email?.Trim();
                request.code = action.Code?.Trim();
            }
            else request.account = action.Account?.Trim();

            var reply = await SendAsync<Reply>(register ? "register" : "login", request, null, token);
            if (string.IsNullOrWhiteSpace(reply?.token)) throw new EntryRequestException("invalid_response");
            return reply.token;
        }

        internal async Task<GameAccountSession> SelectRealmAsync(string admissionToken, string appVersion, CancellationToken token)
        {
            var reply = await SendAsync<Reply>("select-realm", new Realm { realmId = RealmId, appVersion = appVersion }, admissionToken, token);
            if (string.IsNullOrWhiteSpace(reply?.token) || string.IsNullOrWhiteSpace(reply.player?.id) ||
                string.IsNullOrWhiteSpace(reply.player.displayName))
                throw new EntryRequestException("invalid_response");
            return new GameAccountSession(reply.token, reply.player.id, reply.player.displayName, reply.player.avatarId);
        }

        internal static bool RequiresNewPlayer(string current, string minimum)
        {
            if (string.IsNullOrWhiteSpace(minimum)) return false;
            return ReadVersion(current).CompareTo(ReadVersion(minimum)) < 0;
        }

        private static Version ReadVersion(string value)
        {
            if (!Version.TryParse(value, out var version))
                throw new EntryRequestException("invalid_version");
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private async Task<T> SendAsync<T>(string path, object data, string bearer, CancellationToken token,
            bool allowUnavailable = false) where T : class
        {
            token.ThrowIfCancellationRequested();
            var uri = new Uri(endpoint, "/api/entry/" + path);
            using var request = new UnityWebRequest(uri, data == null ? "GET" : "POST")
            {
                timeout = allowUnavailable ? 3 : 20,
                redirectLimit = 0,
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (data != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);

            var operation = request.SendWebRequest();
            if (!operation.isDone)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Complete(AsyncOperation _) => completion.TrySetResult(true);
                operation.completed += Complete;
                try
                {
                    using var stop = token.Register(() => completion.TrySetCanceled(token));
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
            if (request.result != UnityWebRequest.Result.Success)
            {
                // The test entry can use signed content with an older server or no game server.
                if (allowUnavailable && (request.responseCode == 404 || request.result == UnityWebRequest.Result.ConnectionError))
                    return null;

                // Display only our local, approved copy. Never display or log a raw authentication response.
                if (request.responseCode >= 400)
                {
                    var reply = ReadJson<Reply>(request.downloadHandler.text);
                    throw new EntryRequestException(reply?.code ?? (request.responseCode == 429 ? "rate_limited" : "network"));
                }
                throw new EntryRequestException("network");
            }
            if (request.responseCode == 204) return null;
            return ReadJson<T>(request.downloadHandler.text) ?? throw new EntryRequestException("invalid_response");
        }

        private static T ReadJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 8192) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        [Serializable, Preserve] internal sealed class Policy { public string minimumAppVersion, realmId; }
        [Serializable, Preserve] private sealed class Credentials { public string account, username, email, password, code; }
        [Serializable, Preserve] private sealed class Realm { public string realmId, appVersion; }
        [Serializable, Preserve] private sealed class Reply { public string token, code; public Player player; }
        [Serializable, Preserve] private sealed class Player { public string id, displayName, avatarId; }
    }

    internal sealed class EntryRequestException : IOException
    {
        internal string Code { get; }
        internal EntryRequestException(string code) : base("Entry request failed.") => Code = code;
    }
}
