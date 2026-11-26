using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed class DevelopmentGmMailApi : IDevelopmentGmMailApi
    {
        [Serializable, Preserve] private sealed class Problem { public string error; }
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private readonly Func<CancellationToken, Task<string>> bearer;
        internal bool PreviewDiagnosticsEnabled => PreviewDiagnosticsAllowed(Debug.isDebugBuild, endpoint, Environment.GetCommandLineArgs());
        internal static bool PreviewDiagnosticsAllowed(bool development, Uri target, string[] arguments) => development &&
            target != null && target.IsAbsoluteUri && target.IsLoopback && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps) &&
            arguments != null && Array.IndexOf(arguments, "-astral-mail-smoke") >= 0;
        internal DevelopmentGmMailApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer)
            : this(endpoint, bearer, null) { }
        internal DevelopmentGmMailApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer, HttpMessageHandler transport)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
            http = new HttpClient(transport ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback });
            http.Timeout = Timeout.InfiniteTimeSpan;
        }
        public Task<GmMailCatalog> CatalogAsync(CancellationToken token) => Call<GmMailCatalog>("/api/mail/catalog", null, token);
        public async Task<GmMailPreview> PreviewAsync(GmMailDraft draft, CancellationToken token)
        {
            GmMailPreview preview = null;
            bool decoded = false;
            string phase = "request";
            try
            {
                preview = await Call<GmMailPreview>("/api/development/gm/mail/preview", draft, token, value => phase = value);
                decoded = true;
                phase = "validate";
                if (string.IsNullOrEmpty(preview.previewId) || string.IsNullOrEmpty(preview.realmId) || string.IsNullOrEmpty(preview.playerId) ||
                    preview.recipientCount != 1 || preview.mail == null || preview.totalRewards == null)
                    throw new IOException("Incomplete GM mail preview.");
                return preview;
            }
            catch (Exception error) when (!(error is OperationCanceledException && token.IsCancellationRequested))
            {
                // Native JsonUtility/HybridCLR bridge failures are otherwise reduced to a generic
                // UI message. Record only schema validity and call sites, never exception.Message,
                // response content, owner identifiers, a draft, or authentication material.
                if (PreviewDiagnosticsEnabled) Debug.LogWarning("GM_MAIL_PREVIEW_FAILURE phase=" + phase + ";decoded=" + decoded +
                    ";preview=" + !string.IsNullOrEmpty(preview?.previewId) +
                    ";realm=" + !string.IsNullOrEmpty(preview?.realmId) +
                    ";player=" + !string.IsNullOrEmpty(preview?.playerId) +
                    ";recipientCount=" + (preview?.recipientCount ?? -1) +
                    ";mail=" + (preview?.mail != null) +
                    ";rewards=" + (preview?.totalRewards != null) +
                    ";type=" + error.GetType().FullName + ";stack=" + error.StackTrace);
                throw;
            }
        }
        public async Task<GmMailReceipt> SendAsync(GmMailSendRequest request, CancellationToken token)
        {
            var receipt = await Call<GmMailReceipt>("/api/development/gm/mail/send", request, token);
            if (receipt.requestId != request.requestId || string.IsNullOrEmpty(receipt.deliveryId) || receipt.summary == null)
                throw new IOException("Incomplete GM mail receipt.");
            return receipt;
        }
        private async Task<T> Call<T>(string path, object body, CancellationToken token, Action<string> stage = null) where T : class
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(endpoint, path));
            stage?.Invoke("bearer");
            string credential = await bearer(deadline.Token);
            if (string.IsNullOrWhiteSpace(credential)) throw new GmMailRejectedException(401, "unauthorized");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            stage?.Invoke("serialize");
            if (body != null)
            {
                string json;
                try { json = JsonConvert.SerializeObject(body); }
                catch (JsonException) { throw new IOException("Invalid GM mail request."); }
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            stage?.Invoke("headers");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            stage?.Invoke("body_stream");
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = new StringBuilder(); var buffer = new char[4096];
            using (deadline.Token.Register(() => stream.Dispose()))
            {
                stage?.Invoke("body_read");
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (content.Length + count > 2 * 1024 * 1024) throw new IOException("GM mail response exceeds limit.");
                    content.Append(buffer, 0, count);
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            stage?.Invoke("status");
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                if (status < 500 && status != 408 && status != 429)
                {
                    string code = "mail_request_failed";
                    try { code = JsonConvert.DeserializeObject<Problem>(content.ToString())?.error ?? code; } catch (JsonException) { }
                    throw new GmMailRejectedException(status, code);
                }
                throw new IOException("GM mail service unavailable.");
            }
            stage?.Invoke("deserialize");
            try { return JsonConvert.DeserializeObject<T>(content.ToString()) ?? throw new IOException("Invalid GM mail response."); }
            catch (JsonException) { throw new IOException("Invalid GM mail response."); }
        }
        public void Dispose() => http.Dispose();
    }
}
