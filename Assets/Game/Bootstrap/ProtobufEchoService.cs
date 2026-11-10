using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Protocol;
using AstralOath.Shared;
using Google.Protobuf;
using UnityEngine.Networking;

namespace AstralOath.Client.Bootstrap
{
    public sealed class ProtobufEchoService : IEchoService
    {
        private readonly Uri endpoint;

        public ProtobufEchoService(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
                throw new ArgumentException("Use HTTPS or an HTTP loopback address.", nameof(baseUrl));
            endpoint = new Uri(uri, FoundationProtocol.EchoPath);
        }

        public async Task<EchoResult> EchoAsync(string message, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var outgoing = ProtobufEchoRequests.Create(message);

            using var request = new UnityWebRequest(endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(outgoing.ToByteArray()),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
                redirectLimit = 0
            };
            request.SetRequestHeader("Content-Type", FoundationProtocol.MediaType);
            request.SetRequestHeader("Accept", FoundationProtocol.MediaType);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested)
                {
                    request.Abort();
                    token.ThrowIfCancellationRequested();
                }
                await Task.Yield();
            }

            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException($"HTTP {request.responseCode}: {request.error}");

            string mediaType = request.GetResponseHeader("Content-Type")?.Split(';')[0].Trim();
            if (!string.Equals(mediaType, FoundationProtocol.MediaType, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unexpected echo response Content-Type.");
            var result = EchoResponse.Parser.ParseFrom(request.downloadHandler.data);
            if (result.ProtocolVersion != FoundationProtocol.Version || result.RequestId != outgoing.RequestId ||
                result.Message != outgoing.Message || result.Sequence == 0)
                throw new IOException("Echo response does not match request.");

            return new EchoResult(result.Message, result.Sequence);
        }
    }
}