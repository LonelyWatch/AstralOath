using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Protocol;
using Google.Protobuf;

namespace AstralOath.Shared.Networking
{
    /// <summary>A single-flight client for the development Echo endpoint.</summary>
    public sealed class ProtobufEchoSocket : IDisposable
    {
        private readonly object gate = new object();
        private readonly Uri endpoint;
        private readonly TimeSpan timeout;
        private ClientWebSocket socket;
        private bool active;
        private bool disposed;

        public ProtobufEchoSocket(Uri endpoint, TimeSpan? timeout = null)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));
            if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != "wss" && (endpoint.Scheme != "ws" || !endpoint.IsLoopback)))
                throw new ArgumentException("Use wss or a loopback ws endpoint.", nameof(endpoint));

            this.endpoint = endpoint;
            this.timeout = timeout ?? TimeSpan.FromSeconds(10);
            if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2))
                throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        public async Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.ProtocolVersion != FoundationProtocol.Version
                || request.CalculateSize() > FoundationProtocol.MaxPayloadBytes)
                throw new ArgumentException("Invalid echo version or payload size.", nameof(request));

            var payload = request.ToByteArray();
            var requestId = request.RequestId;
            var message = request.Message;
            using var deadline = CreateDeadline(cancellationToken);
            var current = Begin(create: true);
            var keepConnection = false;
            try
            {
                if (current.State == WebSocketState.None)
                    await current.ConnectAsync(endpoint, deadline.Token).ConfigureAwait(false);

                await current.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary,
                    endOfMessage: true, cancellationToken: deadline.Token).ConfigureAwait(false);

                var buffer = new byte[FoundationProtocol.MaxPayloadBytes + 1];
                var length = 0;
                WebSocketReceiveResult frame;
                do
                {
                    frame = await current.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length),
                        deadline.Token).ConfigureAwait(false);
                    if (frame.MessageType != WebSocketMessageType.Binary)
                        throw new IOException("Echo socket ended or returned a non-binary response.");
                    length += frame.Count;
                    if (length > FoundationProtocol.MaxPayloadBytes)
                        throw new IOException("Echo response exceeds the payload limit.");
                } while (!frame.EndOfMessage);

                var response = EchoResponse.Parser.ParseFrom(buffer, 0, length);
                if (response.ProtocolVersion != FoundationProtocol.Version || response.RequestId != requestId
                    || response.Message != message || response.Sequence != 0)
                    throw new IOException("Echo response does not match its request.");

                keepConnection = true;
                return response;
            }
            finally
            {
                End(current, keepConnection);
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            using var deadline = CreateDeadline(cancellationToken);
            var current = Begin(create: false);
            if (current == null)
                return;

            try
            {
                if (current.State == WebSocketState.Open || current.State == WebSocketState.CloseReceived)
                    await current.CloseAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                End(current, keepConnection: false);
            }
        }

        public void Dispose()
        {
            ClientWebSocket current;
            lock (gate)
            {
                disposed = true;
                current = socket;
                socket = null;
            }
            current?.Dispose();
        }

        private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
        {
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            return deadline;
        }

        private ClientWebSocket Begin(bool create)
        {
            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(ProtobufEchoSocket));
                if (active)
                    throw new InvalidOperationException("An echo operation is already active.");

                if (socket == null && create)
                    socket = new ClientWebSocket();
                active = socket != null;
                return socket;
            }
        }

        private void End(ClientWebSocket current, bool keepConnection)
        {
            lock (gate)
            {
                if (!keepConnection && ReferenceEquals(socket, current))
                {
                    socket.Dispose();
                    socket = null;
                }
                active = false;
            }
        }
    }
}
