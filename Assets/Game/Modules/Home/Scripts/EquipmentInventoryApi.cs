using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Home
{
    [Serializable, Preserve]
    public sealed class EquipmentInventorySnapshot
    {
        public int capacity, used;
        public long version;
        public EquipmentInventoryItem[] items;
    }

    [Serializable, Preserve]
    public sealed class EquipmentStatValue
    {
        public string kind;
        public int value;
        internal EquipmentStat ToStat() => new EquipmentStat(kind, value);
    }

    [Serializable, Preserve]
    public sealed class EquipmentInventoryItem
    {
        public string instanceId, templateId, slot, setId, grade, equippedHeroId;
        public int itemLevel, enhanceLevel;
        public EquipmentStatValue mainStat;
        public EquipmentStatValue[] substats;
        [NonSerialized] private EquipmentPiece piece;

        public EquipmentPiece ToPiece()
        {
            if (piece != null) return piece;
            var values = new EquipmentStat[substats.Length];
            for (int i = 0; i < substats.Length; ++i) values[i] = substats[i].ToStat();
            return piece = new EquipmentPiece(setId, mainStat.ToStat(), values);
        }
    }

    public sealed class EquipmentRequestException : IOException
    {
        public HttpStatusCode Status { get; }
        public string Code { get; }
        internal EquipmentRequestException(HttpStatusCode status, string code)
            : base("Equipment request rejected: " + (int)status)
        {
            Status = status;
            Code = code;
        }
    }

    public sealed class EquipmentInventoryApi : IDisposable
    {
        private readonly Uri inventory, actions;
        private readonly Func<CancellationToken, Task<string>> getBearer;
        private readonly CancellationToken lifetime;
        private readonly HttpClient http;
        public event Action<EquipmentInventorySnapshot> InventoryChanged;
        public event Action<EquipmentInventorySnapshot> RemoteInventoryChanged;
        public event Action InventoryUncertain;

        public EquipmentInventoryApi(Uri endpoint, Func<CancellationToken, Task<string>> getBearer, CancellationToken lifetime)
        {
            inventory = new Uri(endpoint, "/api/inventory/equipment");
            actions = new Uri(endpoint, "/api/inventory/equipment/actions");
            this.getBearer = getBearer;
            this.lifetime = lifetime;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 2 * 1024 * 1024
            };
        }

        public Task<EquipmentInventorySnapshot> FetchAsync(CancellationToken token) => SendAsync(null, token);

        public async Task RefreshIfChangedAsync(long knownVersion, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            string bearer = await getBearer(deadline.Token);
            using var request = new HttpRequestMessage(HttpMethod.Head, inventory);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Headers.TryAddWithoutValidation("If-None-Match", "\"" + knownVersion.ToString(CultureInfo.InvariantCulture) + "\"");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotModified) return;
            if (!response.IsSuccessStatusCode) throw new EquipmentRequestException(response.StatusCode, null);
            var fresh = await FetchAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (fresh.version <= knownVersion) return;
            InventoryChanged?.Invoke(fresh);
            RemoteInventoryChanged?.Invoke(fresh);
        }

        public Task<EquipmentInventorySnapshot> ChangeAsync(string action, string requestId, string heroInstanceId,
            string instanceId, string slot, long expectedVersion, CancellationToken token) =>
            SendAsync(new Mutation { action = action, requestId = requestId, heroInstanceId = heroInstanceId,
                instanceId = instanceId, slot = slot, expectedVersion = expectedVersion }, token);

        private async Task<EquipmentInventorySnapshot> SendAsync(Mutation mutation, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            bool dispatched = false;
            try
            {
                var bearer = await getBearer(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(mutation == null ? HttpMethod.Get : HttpMethod.Post,
                    mutation == null ? inventory : actions);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (mutation != null)
                    request.Content = new StringContent(JsonUtility.ToJson(mutation), Encoding.UTF8, "application/json");
                dispatched = mutation != null;
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                {
                    string code = null;
                    try { code = JsonUtility.FromJson<Problem>(json)?.code; }
                    catch (ArgumentException) { }
                    // Conflicts and server failures need a fresh snapshot. Other business rejections made no change.
                    dispatched = dispatched && (response.StatusCode == HttpStatusCode.Conflict || (int)response.StatusCode >= 500);
                    throw new EquipmentRequestException(response.StatusCode, code);
                }
                var result = Validate(JsonUtility.FromJson<EquipmentInventorySnapshot>(json));
                dispatched = false;
                if (mutation != null) InventoryChanged?.Invoke(result);
                return result;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new IOException("Equipment inventory request timed out.");
            }
            finally
            {
                if (dispatched && !lifetime.IsCancellationRequested) InventoryUncertain?.Invoke();
            }
        }

        private static EquipmentInventorySnapshot Validate(EquipmentInventorySnapshot value)
        {
            if (value?.items == null || value.capacity < 1 || value.version < 0 ||
                value.used != value.items.Length || value.used > value.capacity)
                throw new InvalidDataException("Invalid equipment inventory summary.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in value.items)
            {
                if (item == null || !Guid.TryParseExact(item.instanceId, "N", out _) || !ids.Add(item.instanceId) ||
                    string.IsNullOrEmpty(item.templateId) || !IsSlot(item.slot) || string.IsNullOrEmpty(item.grade) ||
                    item.itemLevel < 1 || item.enhanceLevel < 0 || item.enhanceLevel > 15 || item.mainStat == null ||
                    item.substats == null || string.IsNullOrEmpty(item.mainStat.kind) ||
                    Array.Exists(item.substats, stat => stat == null || string.IsNullOrEmpty(stat.kind)) ||
                    (!string.IsNullOrEmpty(item.equippedHeroId) && (!Guid.TryParseExact(item.equippedHeroId, "N", out _) ||
                        !occupied.Add(item.equippedHeroId + "/" + item.slot))))
                    throw new InvalidDataException("Invalid equipment inventory item.");
                try { item.ToPiece(); }
                catch (ArgumentException error) { throw new InvalidDataException("Invalid equipment stats.", error); }
            }
            return value;
        }

        private static bool IsSlot(string value) => value == "weapon" || value == "helmet" || value == "armor" ||
            value == "necklace" || value == "ring" || value == "boots";

        public void Dispose() => http.Dispose();
        [Serializable, Preserve] private sealed class Problem { public string code; }
        [Serializable, Preserve] private sealed class Mutation
        {
            public string action, requestId, heroInstanceId, instanceId, slot;
            public long expectedVersion;
        }
    }
}
