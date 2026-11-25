using System;
using System.IO;
using System.Collections.Generic;
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
    [Serializable, Preserve] public sealed class EquipmentEnhancementBalances
    {
        public long gold;
        public EquipmentEnhancementBalance[] items;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementBalance
    {
        public string kind;
        public long quantity;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementMaterial
    {
        public string kind;
        public int quantity;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementRules
    {
        public string rulesVersion, economyScope;
        public EquipmentEnhancementMaterialRule[] materials;
        public EquipmentEnhancementProfile[] profiles;
        public EquipmentEnhancementSuccess[] successes;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementMaterialRule
    {
        public string id;
        public string[] slots;
        public int experience, goldPerUnit;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementProfile
    {
        public string id, grade, slot, mainKind, blockReason;
        public int itemLevel;
        public int[] mainValues;
        public long[] xpThresholds;
        public bool supported;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementSuccess
    {
        public string tier;
        public int probabilityBp, experienceMultiplierBp;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementReceipt
    {
        public EquipmentInventorySnapshot inventory;
        public EquipmentEnhancementResult result;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementResult
    {
        public string instanceId, successTier;
        public int beforeLevel, afterLevel;
        public long beforeXp, afterXp, grantedXp, consumedGold;
        public EquipmentEnhancementChange[] changes;
        public string returnedPowderKind;
        public int returnedPowderQuantity;
    }
    [Serializable, Preserve] public sealed class EquipmentEnhancementChange
    {
        public string kind;
        public int before, after;
        public bool added;
    }

    public sealed class EquipmentEnhancementPending
    {
        public string instanceId, requestId;
        public long expectedVersion;
        public EquipmentEnhancementMaterial[] materials;
    }

    public sealed partial class EquipmentInventoryApi
    {
        private readonly Dictionary<string, EquipmentEnhancementPending> pendingEnhancements =
            new Dictionary<string, EquipmentEnhancementPending>(StringComparer.Ordinal);

        public EquipmentEnhancementPending GetPendingEnhancement(string instanceId)
        {
            lock (pendingEnhancements)
                return instanceId != null && pendingEnhancements.TryGetValue(instanceId, out var pending) ? CopyPending(pending) : null;
        }

        private static EquipmentEnhancementPending CopyPending(EquipmentEnhancementPending pending)
        {
            var copy = new EquipmentEnhancementMaterial[pending.materials.Length];
            for (int i = 0; i < copy.Length; ++i)
                copy[i] = new EquipmentEnhancementMaterial { kind = pending.materials[i].kind, quantity = pending.materials[i].quantity };
            return new EquipmentEnhancementPending { instanceId = pending.instanceId, requestId = pending.requestId,
                expectedVersion = pending.expectedVersion, materials = copy };
        }

        private void ClearPendingEnhancement(string instanceId, string requestId)
        {
            lock (pendingEnhancements)
                if (pendingEnhancements.TryGetValue(instanceId, out var pending) && pending.requestId == requestId)
                    pendingEnhancements.Remove(instanceId);
        }

        public async Task<EquipmentEnhancementRules> FetchEnhancementRulesAsync(CancellationToken token)
        {
            string json = await SendEnhancementAsync(new Uri(inventory + "/enhancement-rules"), null, token);
            var rules = JsonUtility.FromJson<EquipmentEnhancementRules>(json);
            if (rules == null || string.IsNullOrEmpty(rules.rulesVersion) || rules.materials == null ||
                rules.profiles == null || rules.successes == null)
                throw new InvalidDataException("Invalid equipment enhancement rules.");
            foreach (var profile in rules.profiles)
            {
                if (!profile.supported) continue;
                if (profile.mainValues?.Length != 16 || profile.xpThresholds?.Length != 16 || profile.xpThresholds[0] != 0)
                    throw new InvalidDataException("Invalid equipment enhancement curve.");
                for (int level = 1; level < 16; ++level)
                    if (profile.mainValues[level] < profile.mainValues[level - 1] ||
                        profile.xpThresholds[level] <= profile.xpThresholds[level - 1])
                        throw new InvalidDataException("Invalid equipment enhancement curve order.");
            }
            return rules;
        }

        public async Task<EquipmentEnhancementReceipt> EnhanceAsync(string instanceId, string requestId,
            EquipmentEnhancementMaterial[] materials, long expectedVersion, CancellationToken token)
        {
            if (!Guid.TryParseExact(instanceId, "N", out _) || !Guid.TryParseExact(requestId, "N", out _) ||
                expectedVersion < 0 || materials == null || materials.Length == 0 || materials.Length > 32 ||
                Array.Exists(materials, material => material == null || string.IsNullOrEmpty(material.kind) || material.quantity < 1))
                throw new ArgumentException("Invalid equipment enhancement selection.");
            token.ThrowIfCancellationRequested();
            EquipmentEnhancementPending operation;
            lock (pendingEnhancements)
            {
                if (pendingEnhancements.TryGetValue(instanceId, out var pending))
                {
                    bool same = pending.expectedVersion == expectedVersion && pending.materials.Length == materials.Length;
                    for (int i = 0; same && i < materials.Length; ++i)
                        same = pending.materials[i].kind == materials[i].kind && pending.materials[i].quantity == materials[i].quantity;
                    if (pending.requestId != requestId || !same)
                        throw new InvalidOperationException("Resolve the pending equipment enhancement before creating another request.");
                    operation = CopyPending(pending);
                }
                else
                {
                    operation = CopyPending(new EquipmentEnhancementPending { instanceId = instanceId, requestId = requestId,
                        expectedVersion = expectedVersion, materials = materials });
                    pendingEnhancements.Add(instanceId, operation);
                }
            }
            string body = JsonUtility.ToJson(new EnhancementRequest { requestId = operation.requestId,
                expectedVersion = operation.expectedVersion, materials = operation.materials });
            string json;
            try { json = await SendEnhancementAsync(new Uri(inventory + "/" + instanceId + "/enhance"), body, token); }
            catch (EquipmentRequestException error) when ((int)error.Status >= 400 && (int)error.Status < 500 && error.Status != HttpStatusCode.RequestTimeout)
            {
                ClearPendingEnhancement(instanceId, requestId);
                throw;
            }
            EquipmentEnhancementReceipt receipt;
            try
            {
                receipt = JsonUtility.FromJson<EquipmentEnhancementReceipt>(json);
                if (receipt?.result == null || receipt.result.instanceId != instanceId ||
                    receipt.result.afterLevel < receipt.result.beforeLevel || receipt.result.afterLevel > 15)
                    throw new InvalidDataException("Invalid equipment enhancement receipt.");
                Validate(receipt.inventory);
            }
            catch { InventoryUncertain?.Invoke(); throw; }
            ClearPendingEnhancement(instanceId, requestId);
            InventoryChanged?.Invoke(receipt.inventory);
            return receipt;
        }

        private async Task<string> SendEnhancementAsync(Uri address, string body, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            bool dispatched = false;
            try
            {
                string bearer = await getBearer(deadline.Token);
                using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, address);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                deadline.Token.ThrowIfCancellationRequested();
                dispatched = body != null;
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if (!response.IsSuccessStatusCode)
                {
                    string code = null;
                    try { code = JsonUtility.FromJson<Problem>(json)?.code; }
                    catch (ArgumentException) { }
                    dispatched = dispatched && (response.StatusCode == HttpStatusCode.Conflict ||
                        response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500);
                    throw new EquipmentRequestException(response.StatusCode, code);
                }
                dispatched = false;
                return json;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new IOException("Equipment enhancement request timed out.");
            }
            finally
            {
                if (dispatched && !lifetime.IsCancellationRequested) InventoryUncertain?.Invoke();
            }
        }

        [Serializable, Preserve] private sealed class EnhancementRequest
        {
            public string requestId;
            public long expectedVersion;
            public EquipmentEnhancementMaterial[] materials;
        }
    }
}
