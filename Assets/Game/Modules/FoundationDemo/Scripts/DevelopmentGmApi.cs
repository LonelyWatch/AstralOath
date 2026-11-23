using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    [Serializable, Preserve]
    internal sealed class GmSnapshot
    {
        public string playerId, realmId;
        public GmBalance[] resources;
        public string[] heroes, artifacts, equipment;
        public GmForgeRules forgeRules;

        [Serializable, Preserve]
        internal sealed class GmBalance
        {
            public string key;
            public int value;
        }
    }

    [Serializable, Preserve]
    internal class GmAction
    {
        public string requestId, action, key;
        public int value;
    }

    [Serializable, Preserve]
    internal sealed class GmForgeAction : GmAction
    {
        public GmForgeRecipe forge;
    }

    [Serializable, Preserve]
    internal sealed class GmForgeRecipe
    {
        public int itemLevel, enhanceLevel;
        public string grade, setId, mainKind;
        public GmForgeSubstat[] substats;
    }

    [Serializable, Preserve]
    internal sealed class GmForgeSubstat
    {
        public string kind;
        public int rolls, value;
    }

    [Serializable, Preserve]
    internal sealed class GmForgeRules
    {
        public Tier[] tiers;
        public Slot[] slots;
        public Grade[] grades;
        public string[] sets;
        [Serializable, Preserve] internal sealed class Tier { public int itemLevel, minEnhance, maxEnhance; public Stat[] stats; }
        [Serializable, Preserve] internal sealed class Slot { public string slot; public string[] mainKinds, subKinds; }
        [Serializable, Preserve] internal sealed class Grade
        {
            public string grade;
            public int initialSubstats;
            public int[] unlockEnhancements;
        }
        [Serializable, Preserve] internal sealed class Stat { public string kind; public int mainBase, mainStep, rollMin, rollMax, step; }
    }

    internal sealed class GmRejectedException : IOException
    {
        internal readonly int Status;
        internal readonly string Code;
        internal GmRejectedException(int status, string code) : base("GM request rejected.") { Status = status; Code = code; }
    }

    internal sealed class DevelopmentGmApi : IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private readonly Func<CancellationToken, Task<string>> bearer;
        [Serializable, Preserve] private sealed class Problem { public string code; }

        internal DevelopmentGmApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer)
        {
            this.endpoint = endpoint;
            this.bearer = bearer;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback })
            {
                MaxResponseContentBufferSize = 256 * 1024
            };
        }

        internal async Task<GmSnapshot> SendAsync(GmAction action, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var address = new Uri(endpoint, action == null ? "/api/development/gm" : "/api/development/gm/actions");
                using var request = new HttpRequestMessage(action == null ? HttpMethod.Get : HttpMethod.Post, address);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await bearer(deadline.Token));
                if (action != null)
                    request.Content = new StringContent(JsonUtility.ToJson(action), Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, deadline.Token);
                int status = (int)response.StatusCode;
                if (status >= 400 && status < 500 && status != 408)
                {
                    string code = null;
                    try { code = JsonUtility.FromJson<Problem>(await response.Content.ReadAsStringAsync())?.code; }
                    catch (ArgumentException) { }
                    throw new GmRejectedException(status, code);
                }
                response.EnsureSuccessStatusCode();
                var data = JsonUtility.FromJson<GmSnapshot>(await response.Content.ReadAsStringAsync());
                deadline.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(data?.playerId) || string.IsNullOrEmpty(data.realmId) ||
                    data.resources == null || data.heroes == null || data.artifacts == null || data.equipment == null)
                    throw new InvalidDataException("Incomplete GM response.");
                return data;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException("GM request timed out.");
            }
        }

        public void Dispose() => http.Dispose();
    }
}
