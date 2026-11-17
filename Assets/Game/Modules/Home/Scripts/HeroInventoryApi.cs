using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.Home
{
    [Serializable, Preserve]
    public sealed class HeroInventorySnapshot
    {
        public int capacity;
        public int used;
        public HeroInventoryItem[] heroes;
    }

    [Serializable, Preserve]
    public sealed class HeroInventoryItem
    {
        public string instanceId;
        public string templateId;
        public int level;
        public int stars;
    }

    public sealed class HeroInventoryApi : IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private readonly Func<CancellationToken, Task<string>> getBearer;
        private readonly CancellationToken lifetime;

        public HeroInventoryApi(Uri endpoint, Func<CancellationToken, Task<string>> getBearer, CancellationToken lifetime)
        {
            this.endpoint = new Uri(endpoint, "/api/inventory/heroes");
            this.getBearer = getBearer;
            this.lifetime = lifetime;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 256 * 1024
            };
        }

        public async Task<HeroInventorySnapshot> FetchAsync(CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var bearer = await getBearer(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                    throw new IOException("Hero inventory request was rejected.");
                var json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                return Validate(JsonUtility.FromJson<HeroInventorySnapshot>(json));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new IOException("Hero inventory request timed out.");
            }
        }

        private static HeroInventorySnapshot Validate(HeroInventorySnapshot snapshot)
        {
            if (snapshot?.heroes == null || snapshot.capacity < 1 || snapshot.used < 0 ||
                snapshot.used > snapshot.capacity || snapshot.used != snapshot.heroes.Length)
                throw new InvalidDataException("Invalid hero inventory summary.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hero in snapshot.heroes)
                if (hero == null || string.IsNullOrWhiteSpace(hero.instanceId) || !identities.Add(hero.instanceId) ||
                    string.IsNullOrWhiteSpace(hero.templateId) || hero.level < 1 || hero.level > 60 ||
                    hero.stars < 1 || hero.stars > 6)
                    throw new InvalidDataException("Invalid hero inventory instance.");
            return snapshot;
        }

        public void Dispose() => http.Dispose();
    }
}
