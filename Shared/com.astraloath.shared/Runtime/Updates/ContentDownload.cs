using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Shared.Updates
{
    public sealed partial class ContentUpdater
    {
        private static void RequireHttpUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo) ||
                (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)))
                throw new ArgumentException("Content endpoints must use HTTPS or loopback HTTP.");
        }

        private async Task<byte[]> FetchAsync(Uri uri, int maximum, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > maximum)
                        throw new InvalidDataException("Content metadata exceeds its size limit.");
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + read > maximum)
                                throw new InvalidDataException("Content metadata exceeds its size limit.");
                            output.Write(buffer, 0, read);
                        }
                        return output.ToArray();
                    }
                }
            }
        }

        private async Task DownloadAsync(Uri uri, string local, ContentEntry entry, CancellationToken token)
        {
            long offset = File.Exists(local) ? new FileInfo(local).Length : 0;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                if (offset > 0)
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var range = response.Content.Headers.ContentRange;
                        if (offset == 0 || range == null || range.Unit != "bytes" ||
                            range.From != offset || range.To != entry.Size - 1 || range.Length != entry.Size)
                            throw new InvalidDataException("Invalid Content-Range: " + entry.Path);
                    }
                    else if (response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentRange == null)
                    {
                        offset = 0;
                    }
                    else
                    {
                        throw new InvalidDataException("Unexpected content HTTP status: " + response.StatusCode);
                    }
                    if (response.Content.Headers.ContentLength is long length && length != entry.Size - offset)
                        throw new InvalidDataException("Payload length differs from the signed size: " + entry.Path);

                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(local, offset == 0 ? FileMode.Create : FileMode.Append,
                        FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536];
                        long received = offset;
                        while (true)
                        {
                            int read;
                            try
                            {
                                read = await source.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false);
                            }
                            catch (IOException exception)
                            {
                                deadline.Token.ThrowIfCancellationRequested();
                                throw new InvalidDataException("Payload was interrupted: " + entry.Path, exception);
                            }
                            if (read == 0)
                                break;
                            if (received + read > entry.Size)
                                throw new InvalidDataException("Payload exceeded the signed size: " + entry.Path);
                            await output.WriteAsync(buffer, 0, read, deadline.Token).ConfigureAwait(false);
                            received += read;
                            deadline.CancelAfter(TimeSpan.FromSeconds(60));
                        }
                        output.Flush(true);
                        if (received != entry.Size)
                            throw new InvalidDataException("Payload was truncated: " + entry.Path);
                    }
                }
            }
        }
    }
}
