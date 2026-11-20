using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Home
{
    [Serializable, Preserve]
    public sealed class ArtifactInventorySnapshot
    {
        public int capacity;
        public int used;
        public ArtifactInventoryItem[] artifacts;
        public ArtifactInventoryMaterials materials;
    }

    [Serializable, Preserve]
    public sealed class ArtifactInventoryItem
    {
        public string instanceId;
        public string templateId;
        public int level;
        public int experience;
        public int limitBreaks;
        public bool locked;
        public string heroInstanceId;
        public string source;
    }

    [Serializable, Preserve]
    public sealed class ArtifactInventoryMaterials
    {
        public int gold, lesserCharm, greaterCharm, epicCharm, bottle, powder;
    }

    [Serializable, Preserve]
    public sealed class ArtifactInventoryMaterial
    {
        public string kind;
        public string instanceId;
        public int quantity = 1;
    }

    [Serializable, Preserve]
    public sealed class ArtifactTrialInvitation
    {
        public string roomId, allyToken, contentHash, battleContentHash;
        public TrialOpening opening;

        [Serializable, Preserve]
        public sealed class TrialOpening { public TrialSnapshot snapshot; }

        [Serializable, Preserve]
        public sealed class TrialSnapshot { public string battleId; }

        internal void Validate()
        {
            if (!Guid.TryParseExact(roomId, "N", out _) || opening?.snapshot?.battleId != roomId ||
                !IsHash(allyToken) || !IsHash(contentHash) || !IsHash(battleContentHash))
                throw new InvalidDataException("Invalid artifact trial invitation.");
        }

        private static bool IsHash(string value)
        {
            if (value?.Length != 64) return false;
            foreach (char digit in value)
                if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }
    }

    public sealed class ArtifactInventoryApi : IDisposable
    {
        private const string Path = "/api/inventory/artifacts";
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private readonly Func<CancellationToken, Task<string>> getBearer;
        private readonly CancellationToken lifetime;
        public event Action InventoryMayChange;

        public ArtifactInventoryApi(Uri endpoint, Func<CancellationToken, Task<string>> getBearer, CancellationToken lifetime)
        {
            this.endpoint = endpoint;
            this.getBearer = getBearer;
            this.lifetime = lifetime;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 256 * 1024
            };
        }

        public Task<ArtifactInventorySnapshot> FetchAsync(CancellationToken token) => SendAsync(HttpMethod.Get, Path, null, token);

        public async Task<ArtifactTrialInvitation> StartTrialAsync(string heroInstanceId, CancellationToken token)
        {
            var payload = JsonUtility.ToJson(new TrialRequest { heroInstanceIds = new[] { heroInstanceId } });
            string json = await RequestAsync(HttpMethod.Post, "/api/battle-preview/owned-trial", payload, token);
            var invitation = JsonUtility.FromJson<ArtifactTrialInvitation>(json);
            if (invitation == null) throw new InvalidDataException("Missing artifact trial invitation.");
            invitation.Validate();
            return invitation;
        }

        public Task<ArtifactInventorySnapshot> EquipAsync(string item, string request, string hero, CancellationToken token) =>
            ChangeAsync(item, "equip", new EquipRequest { requestId = request, heroInstanceId = hero }, token);

        public Task<ArtifactInventorySnapshot> UnequipAsync(string item, string request, CancellationToken token) =>
            ChangeAsync(item, "unequip", new BasicRequest { requestId = request }, token);

        public Task<ArtifactInventorySnapshot> LockAsync(string item, string request, bool locked, CancellationToken token) =>
            ChangeAsync(item, "lock", new LockRequest { requestId = request, locked = locked }, token);

        public Task<ArtifactInventorySnapshot> SellAsync(string item, string request, CancellationToken token) =>
            ChangeAsync(item, "sell", new BasicRequest { requestId = request }, token);

        public Task<ArtifactInventorySnapshot> EnhanceAsync(string item, string request, ArtifactInventoryMaterial[] materials, CancellationToken token)
        {
            // JsonUtility writes null string fields as empty strings. A wallet resource carries no instance Id.
            string header = JsonUtility.ToJson(new BasicRequest { requestId = request });
            var payload = new StringBuilder(header, 0, header.Length - 1, header.Length + 80).Append(",\"materials\":[");
            foreach (var material in materials)
            {
                if (payload[payload.Length - 1] != '[') payload.Append(',');
                payload.Append(JsonUtility.ToJson(material.instanceId == null
                    ? (object)new CharmSelection { kind = material.kind, quantity = material.quantity } : material));
            }
            return SendAsync(HttpMethod.Post, Path + "/" + item + "/enhance", payload.Append("]}").ToString(), token);
        }

        private Task<ArtifactInventorySnapshot> ChangeAsync(string item, string action, object payload, CancellationToken token) =>
            SendAsync(HttpMethod.Post, Path + "/" + item + "/" + action, JsonUtility.ToJson(payload), token);

        private async Task<ArtifactInventorySnapshot> SendAsync(HttpMethod method, string path, string payload, CancellationToken token) =>
            Validate(JsonUtility.FromJson<ArtifactInventorySnapshot>(await RequestAsync(method, path, payload, token)));

        private async Task<string> RequestAsync(HttpMethod method, string path, string payload, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
            if (method != HttpMethod.Get && path.StartsWith(Path, StringComparison.Ordinal)) InventoryMayChange?.Invoke();
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                string bearer = await getBearer(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(method, new Uri(endpoint, path));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (payload != null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                    throw new ArtifactRequestException(response.StatusCode, ParseProblem(json));
                return json;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new IOException("Artifact inventory request timed out.");
            }
        }

        private static string ParseProblem(string json)
        {
            try { return JsonUtility.FromJson<Problem>(json)?.code; }
            catch (ArgumentException) { return null; }
        }

        private static ArtifactInventorySnapshot Validate(ArtifactInventorySnapshot snapshot)
        {
            if (snapshot?.artifacts == null || snapshot.materials == null || snapshot.capacity < 1 ||
                snapshot.used < 0 || snapshot.used > snapshot.capacity || snapshot.used != snapshot.artifacts.Length)
                throw new InvalidDataException("Invalid artifact inventory summary.");

            var items = new HashSet<string>(StringComparer.Ordinal);
            var heroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var artifact in snapshot.artifacts)
                if (artifact == null || !Guid.TryParseExact(artifact.instanceId, "N", out _) || !items.Add(artifact.instanceId) ||
                    string.IsNullOrEmpty(artifact.templateId) || artifact.experience < 0 || artifact.limitBreaks < 0 ||
                    artifact.limitBreaks > 5 || artifact.level < 0 || artifact.level > 15 + 3 * artifact.limitBreaks ||
                    !string.IsNullOrEmpty(artifact.heroInstanceId) && !heroes.Add(artifact.heroInstanceId))
                    throw new InvalidDataException("Invalid artifact inventory instance.");
            return snapshot;
        }

        public void Dispose() => http.Dispose();

        [Serializable, Preserve] private sealed class TrialRequest { public string[] heroInstanceIds; }
        [Serializable, Preserve] private sealed class Problem { public string code; }
        [Serializable, Preserve] private sealed class BasicRequest { public string requestId; }
        [Serializable, Preserve] private sealed class EquipRequest { public string requestId, heroInstanceId; }
        [Serializable, Preserve] private sealed class LockRequest { public string requestId; public bool locked; }
        [Serializable, Preserve] private sealed class CharmSelection { public string kind; public int quantity; }
    }

    public sealed class ArtifactRequestException : IOException
    {
        public HttpStatusCode Status { get; }
        public string Code { get; }
        internal ArtifactRequestException(HttpStatusCode status, string code) : base("Artifact inventory request was rejected.")
        {
            Status = status;
            Code = code;
        }
    }
}
