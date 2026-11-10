using System.Net.WebSockets;
using AstralOath.Protocol;
using AstralOath.Shared;
using Google.Protobuf;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Net.Http.Headers;
using MySqlConnector;

namespace AstralOath.Server.Foundation;

internal static class EchoEndpoints
{
    public static void MapFoundationEcho(this WebApplication app, bool isDevelopment)
    {
        app.MapPost(FoundationProtocol.EchoPath, PostAsync).WithRequestTimeout(TimeSpan.FromSeconds(10));
        if (!isDevelopment)
            return;

        app.UseWebSockets();
        app.Map(FoundationProtocol.WebSocketPath, SocketAsync);
    }

    private static async Task<IResult> PostAsync(HttpContext context, EchoStore store, ILogger<EchoStore> log)
    {
        var request = context.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, FoundationProtocol.MediaType, StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        if (request.ContentLength > FoundationProtocol.MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var token = context.RequestAborted;
        var buffer = new byte[FoundationProtocol.MaxPayloadBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0)
                break;
            length += read;
        }
        if (length > FoundationProtocol.MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!TryParse(buffer, length, out var echo))
            return Results.BadRequest();
        if (!store.IsConfigured)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        ulong? sequence;
        try
        {
            sequence = await store.ResolveAsync(echo, token);
        }
        catch (MySqlException error)
        {
            log.LogWarning("Foundation MySQL operation failed with code {ErrorCode}, trace {TraceId}.",
                error.ErrorCode, context.TraceIdentifier);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return sequence is ulong value
            ? Results.Bytes(BuildResponse(echo, value), FoundationProtocol.MediaType)
            : Results.Conflict();
    }

    private static async Task SocketAsync(HttpContext context, IHostApplicationLifetime lifetime)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[FoundationProtocol.MaxPayloadBytes + 1];
        var length = 0;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var frame = await socket.ReceiveAsync(buffer.AsMemory(length), token);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
                    return;
                }
                if (frame.MessageType != WebSocketMessageType.Binary)
                {
                    await RejectSocketAsync(socket, WebSocketCloseStatus.InvalidMessageType, "binary_required", token);
                    return;
                }

                length += frame.Count;
                if (length > FoundationProtocol.MaxPayloadBytes)
                {
                    await RejectSocketAsync(socket, WebSocketCloseStatus.MessageTooBig, "payload_too_large", token);
                    return;
                }
                if (!frame.EndOfMessage)
                    continue;
                if (!TryParse(buffer, length, out var echo))
                {
                    await RejectSocketAsync(socket, WebSocketCloseStatus.PolicyViolation, "invalid_echo", token);
                    return;
                }

                await socket.SendAsync(BuildResponse(echo, 0).AsMemory(), WebSocketMessageType.Binary, true, token);
                length = 0;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            socket.Abort();
        }
        catch (WebSocketException)
        {
            socket.Abort();
        }
    }

    private static async Task RejectSocketAsync(
        WebSocket socket, WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await socket.CloseAsync(status, reason, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            socket.Abort();
        }
    }

    private static bool TryParse(byte[] buffer, int length, out EchoRequest request)
    {
        try
        {
            request = EchoRequest.Parser.ParseFrom(buffer, 0, length);
            return request.ProtocolVersion == FoundationProtocol.Version
                && request.RequestId.Length is > 0 and <= 64
                && request.RequestId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
        }
        catch (InvalidProtocolBufferException)
        {
            request = null!;
            return false;
        }
    }

    private static byte[] BuildResponse(EchoRequest request, ulong sequence) => new EchoResponse
    {
        RequestId = request.RequestId,
        ProtocolVersion = FoundationProtocol.Version,
        Message = request.Message,
        Sequence = sequence
    }.ToByteArray();
}
