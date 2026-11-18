using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        public Task<SummonReceipt> LatestAsync(CancellationToken token) =>
            Send<SummonReceipt>(HttpMethod.Get, "/draw/latest", null, token, missingAllowed: true);

        public Task<SummonState> GetAsync(CancellationToken token) => Send<SummonState>(HttpMethod.Get, "", null, token);
        public Task<SummonReceipt> DrawAsync(SummonRequest request, CancellationToken token) =>
            Send<SummonReceipt>(HttpMethod.Post, "/draw", request, token);
        public Task<SummonReceipt> ReceiptAsync(string id, CancellationToken token) =>
            Send<SummonReceipt>(HttpMethod.Get, "/draw/" + id, null, token, missingAllowed: true);
        public Task<SummonState> SaveWishesAsync(string[] heroes, string[] artifacts, CancellationToken token) =>
            Send<SummonState>(HttpMethod.Put, "/wishlist", new WishList { heroes = heroes, artifacts = artifacts }, token);
        public Task<SummonStorage> StorageAsync(CancellationToken token) =>
            Send<SummonStorage>(HttpMethod.Get, "/storage", null, token);
        public Task<SummonStorage> ClaimAsync(string[] ids, CancellationToken token) =>
            Send<SummonStorage>(HttpMethod.Post, "/storage/claim", new Claims { instanceIds = ids }, token);

        private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken cancellation,
            bool missingAllowed = false) where T : class
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(16));
            try
            {
                string session = await bearer(deadline.Token);
                using var request = new HttpRequestMessage(method, service.AbsoluteUri + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
                if (body != null) request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                if (missingAllowed && response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode)
                    throw new SummonRequestException((int)response.StatusCode);
                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                return JsonUtility.FromJson<T>(json) ?? throw new IOException("Missing summon response.");
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested && !cancellation.IsCancellationRequested)
            {
                throw new IOException("Summon request timed out.");
            }
        }

        public void Dispose() => http.Dispose();
    }

    public sealed class SummonRequestException : IOException
    {
        public SummonRequestException(int status) : base("Summon service status " + status.ToString(CultureInfo.InvariantCulture)) => Status = status;
        public int Status { get; }
    }
}
