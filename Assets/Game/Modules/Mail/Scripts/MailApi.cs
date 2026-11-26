using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Scripting;
namespace AstralOath.Client.Mail
{
    public sealed class MailRequestException : IOException
    {
        public string Code { get; }
        public HttpStatusCode Status { get; }
        public MailRequestException(HttpStatusCode status, string code) : base("Mail request rejected: " + (int)status) { Status = status; Code = code; }
    }
    public sealed class MailApi : IMailApi
    {
        // Lists contain up to 100 full 8000-character bodies; JSON escaping can expand each
        // UTF-16 code unit to six characters. Keep a bound that admits the current catalog.
        private const int MaximumResponseCharacters = 8 * 1024 * 1024;
        [Serializable, Preserve] private sealed class Error { public string error; }
        [Serializable, Preserve] private sealed class VersionEvent { public string kind; public long version; }
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private readonly Func<CancellationToken, Task<string>> bearer;
        public MailApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer) : this(endpoint, bearer, null) { }
        public MailApi(Uri endpoint, Func<CancellationToken, Task<string>> bearer, HttpMessageHandler transport)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.bearer = bearer ?? throw new ArgumentNullException(nameof(bearer));
            http = transport == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback }) : new HttpClient(transport);
            http.Timeout = Timeout.InfiniteTimeSpan;
        }
        public Task<MailSummary> SummaryAsync(CancellationToken token) => Send<MailSummary>("summary", null, token);
        public Task<MailPage> ListAsync(bool history, string cursor, CancellationToken token) => Send<MailPage>((history ? "history" : "inbox") + "?limit=100&cursor=" + Uri.EscapeDataString(cursor ?? ""), null, token);
        public Task<MailLetter> ReadAsync(string id, CancellationToken token) => Send<MailLetter>(Uri.EscapeDataString(id), null, token);
        public Task<MailClaimReceipt> ClaimAsync(MailClaimRequest request, CancellationToken token) => Send<MailClaimReceipt>("claim", request, token);
        public Task<MailClaimReceipt> ClaimAllAsync(MailClaimAllRequest request, CancellationToken token) => Send<MailClaimReceipt>("claim-all", request, token);
        private async Task<HttpRequestMessage> Request(string path, object body, CancellationToken token)
        {
            string value = await bearer(token);
            if (string.IsNullOrWhiteSpace(value)) throw new MailRequestException(HttpStatusCode.Unauthorized, "unauthorized");
            var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(endpoint, "/api/mail/" + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
            if (body != null)
            {
                string json;
                try { json = JsonConvert.SerializeObject(body); }
                catch (JsonException) { throw new IOException("Invalid mail request."); }
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return request;
        }
        private async Task<T> Send<T>(string path, object body, CancellationToken token) where T : class
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = await Request(path, body, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (body is MailClaimRequest claim) claim.dispatched = true;
            if (body is MailClaimAllRequest bulk) bulk.dispatched = true;
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = new StringBuilder(); var buffer = new char[4096];
            try
            {
            using (deadline.Token.Register(() => stream.Dispose()))
            {
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (content.Length + count > MaximumResponseCharacters) throw new IOException("Mail response exceeds limit.");
                    content.Append(buffer, 0, count);
                }
            }
            }
            catch (ObjectDisposedException) when (deadline.IsCancellationRequested) { throw new OperationCanceledException(deadline.Token); }
            deadline.Token.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
            {
                string code = "mail_request_failed";
                try { code = JsonConvert.DeserializeObject<Error>(content.ToString())?.error ?? code; } catch (JsonException) { }
                throw new MailRequestException(response.StatusCode, code);
            }
            try { return JsonConvert.DeserializeObject<T>(content.ToString()) ?? throw new IOException("Invalid mail response."); }
            catch (JsonException) { throw new IOException("Invalid mail response."); }
        }
        public async Task WatchAsync(Action<long> version, CancellationToken token)
        {
            using var request = await Request("events", null, token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(40));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new MailRequestException(response.StatusCode, response.StatusCode == HttpStatusCode.Unauthorized ? "unauthorized" : "mail_events_failed");
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            try
            {
            using (deadline.Token.Register(() => stream.Dispose()))
            {
                while (!deadline.IsCancellationRequested)
                {
                    string line = await ReadEventLineAsync(reader, deadline.Token);
                    if (line == null) return;
                    deadline.CancelAfter(TimeSpan.FromSeconds(40));
                    VersionEvent item;
                    try { item = JsonConvert.DeserializeObject<VersionEvent>(line); }
                    catch (JsonException) { throw new IOException("Invalid mail event."); }
                    if (item != null) version(item.version);
                }
            }
            }
            catch (ObjectDisposedException) when (deadline.IsCancellationRequested) { throw new OperationCanceledException(deadline.Token); }
        }
        private static async Task<string> ReadEventLineAsync(StreamReader reader, CancellationToken token)
        {
            var line = new StringBuilder(128); var character = new char[1];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await reader.ReadAsync(character, 0, 1) == 0) return line.Length == 0 ? null : line.ToString();
                if (character[0] == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length >= 4096) throw new IOException("Mail event exceeds limit.");
                line.Append(character[0]);
            }
        }
        public void Dispose() => http.Dispose();
    }
}
