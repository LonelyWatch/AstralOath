using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace AstralOath.Client.Summon
{
    [Serializable, Preserve]
    public sealed class SummonState
    {
        public string[] ownedWishIds;
        public string serverTimeUtc, freeResetUtc;
        public bool freeAvailable;
        public int bookmarks, epicBookmarks;
        public int? goldTransmitStones, galaxyBookmarks, mysticMedals, skystones;
        public string[] wishHeroes, wishArtifacts, wishHeroCandidates, wishArtifactCandidates;
        public SummonBanner[] banners;
    }

    [Serializable, Preserve]
    public sealed class SummonBanner
    {
        public string id, revision, title, featuredHero, featuredArtifact;
        public int misses, guaranteeAfter, bookmarksPerDraw;
        public bool dailyFree, allowsEpicBookmarks, usesWishlist;
        public string startUtc, endUtc;
        public SummonEntry[] entries;
    }

    [Serializable, Preserve]
    public sealed class SummonEntry
    {
        public string kind, templateId;
        public int stars, weight;
        public float probability;
        public bool featured;
    }

    [Serializable, Preserve]
    public sealed class SummonReceipt
    {
        public string requestId, bannerId, revision, payment;
        public int count, cost, bookmarks, epicBookmarks, misses;
        public bool freeAvailable;
        public SummonReward[] results;
    }

    [Serializable, Preserve]
    public sealed class SummonReward
    {
        public int position, stars;
        public string kind, templateId, instanceId;
        public bool isNew, inStorage;
    }

    [Serializable, Preserve]
    public sealed class SummonStorage
    {
        public int capacity;
        public SummonReward[] items;
    }

    [Serializable, Preserve]
    public sealed class SummonRequest
    {
        public string requestId, bannerId, revision, payment;
        public int count;
        public int channel = 1;
    }

    public sealed class SummonApi : IDisposable
    {
        [Serializable, Preserve] private sealed class WishList { public string[] heroes, artifacts; }
        [Serializable, Preserve] private sealed class Claims { public string[] instanceIds; }

        private readonly Uri service;
        private readonly Func<CancellationToken, Task<string>> bearer;
        private readonly CancellationToken lifetime;
        private readonly HttpClient http;

        public SummonApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer, CancellationToken lifetime)
        {
            service = new Uri(endpoint, "/api/summon");
            this.bearer = bearer;
            this.lifetime = lifetime;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 512 * 1024
            };
        }

        public SummonRequest Pending { get; set; }
        public event Action<SummonReward[]> InventoryGranted;
        public event Action InventoryUncertain;
        private readonly HashSet<string> appliedDraws = new HashSet<string>(StringComparer.Ordinal);
        private SummonStorage lastStorage;
        public Task<SummonReceipt> LatestAsync(CancellationToken token) =>
            Send<SummonReceipt>(HttpMethod.Get, "/draw/latest", null, token, missingAllowed: true);

        public Task<SummonState> GetAsync(CancellationToken token) => Send<SummonState>(HttpMethod.Get, "", null, token);
        public async Task<SummonReceipt> DrawAsync(SummonRequest request, CancellationToken token)
        {
            var receipt = await Send<SummonReceipt>(HttpMethod.Post, "/draw", request, token);
            ApplyReceipt(request.requestId, receipt);
            return receipt;
        }
        public async Task<SummonReceipt> ReceiptAsync(string id, CancellationToken token)
        {
            var receipt = await Send<SummonReceipt>(HttpMethod.Get, "/draw/" + id, null, token, missingAllowed: true);
            if (receipt != null && Pending?.requestId == id) ApplyReceipt(id, receipt);
            return receipt;
        }
        private void ApplyReceipt(string id, SummonReceipt receipt)
        {
            if (receipt?.requestId != id || receipt?.results == null || receipt.results.Length != receipt.count ||
                receipt.count is not (1 or 10) || receipt.results.Any(item => !ValidGrant(item)))
            {
                InventoryUncertain?.Invoke();
                throw new IOException("Invalid summon grant receipt.");
            }
            if (appliedDraws.Add(id))
                InventoryGranted?.Invoke(receipt.results.Where(item => !item.inStorage).ToArray());
        }
        private static bool ValidGrant(SummonReward item) =>
            item != null && item.kind is ("hero" or "artifact") && !string.IsNullOrEmpty(item.instanceId) &&
            !string.IsNullOrEmpty(item.templateId) && item.stars >= 1 && item.stars <= 6;
        public Task<SummonState> SaveWishesAsync(string[] heroes, string[] artifacts, CancellationToken token) =>
            Send<SummonState>(HttpMethod.Put, "/wishlist", new WishList { heroes = heroes, artifacts = artifacts }, token);
        public async Task<SummonStorage> StorageAsync(CancellationToken token) =>
            lastStorage = await Send<SummonStorage>(HttpMethod.Get, "/storage", null, token);
        public async Task<SummonStorage> ClaimAsync(string[] ids, CancellationToken token)
        {
            var claimed = lastStorage?.items?.Where(item => ValidGrant(item) && Array.IndexOf(ids, item.instanceId) >= 0).ToArray();
            lastStorage = await Send<SummonStorage>(HttpMethod.Post, "/storage/claim", new Claims { instanceIds = ids }, token);
            if (claimed?.Length == ids.Length) InventoryGranted?.Invoke(claimed);
            else InventoryUncertain?.Invoke();
            return lastStorage;
        }

        private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellation,
            bool missingAllowed = false) where T : class
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellation);
            bool submitted = false;
            deadline.CancelAfter(TimeSpan.FromSeconds(16));
            try
            {
                string session = await bearer(deadline.Token);
                using var request = new HttpRequestMessage(method, service.AbsoluteUri + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
                if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                submitted = method == HttpMethod.Post;
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                if (missingAllowed && response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode)
                {
                    string code = null;
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        string problem = await response.Content.ReadAsStringAsync();
                        try { code = Newtonsoft.Json.Linq.JObject.Parse(problem).Value<string>("code"); }
                        catch (JsonException) { /* An empty or invalid error body still represents a rejection. */ }
                    }
                    throw new SummonRequestException((int)response.StatusCode, code);
                }
                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    return JsonConvert.DeserializeObject<T>(json) ?? throw new IOException("Missing summon response.");
                }
                catch (JsonException error)
                {
                    throw new IOException("Invalid summon response.", error);
                }
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                if (submitted) InventoryUncertain?.Invoke();
                throw new IOException("Summon request timed out.");
            }
            catch (Exception error) when (submitted && !lifetime.IsCancellationRequested &&
                !(error is SummonRequestException rejection && rejection.Status >= 400 && rejection.Status < 500))
            {
                InventoryUncertain?.Invoke();
                throw;
            }
        }

        public void Dispose() => http.Dispose();
    }

    public sealed class SummonRequestException : IOException
    {
        public SummonRequestException(int status, string code = null) : base("Summon service status " + status.ToString(CultureInfo.InvariantCulture))
        {
            Status = status;
            Code = code;
        }
        public int Status { get; }
        public string Code { get; }
    }
}
