using System.Net;
using System.Net.WebSockets;
using AstralOath.Protocol;
using AstralOath.Shared.Networking;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

public sealed class SocketClientTests
{
    [Fact]
    public async Task FragmentedEchoCanReconnectAfterRemoteDropAndExplicitClose()
    {
        var connected = 0;
        var closed = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.UseWebSockets();
        app.Map("/ws/foundation", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            Interlocked.Increment(ref connected);
            var buffer = new byte[8192];
            while (true)
            {
                var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    Interlocked.Increment(ref closed);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
                    return;
                }

                var request = EchoRequest.Parser.ParseFrom(buffer, 0, frame.Count);
                if (request.Message == "drop")
                {
                    socket.Abort();
                    return;
                }

                var response = new EchoResponse
                {
                    RequestId = request.RequestId, ProtocolVersion = 1, Message = request.Message
                }.ToByteArray();
                await socket.SendAsync(response.AsMemory(0, 1), WebSocketMessageType.Binary, false, context.RequestAborted);
                await socket.SendAsync(response.AsMemory(1), WebSocketMessageType.Binary, true, context.RequestAborted);
            }
        });

        await app.StartAsync(deadline.Token);
        var url = new UriBuilder(app.Urls.Single()) { Scheme = "ws", Path = "/ws/foundation" }.Uri;
        using var client = new ProtobufEchoSocket(url);
        var request = new EchoRequest { RequestId = "ws-client", ProtocolVersion = 1, Message = "你好" };
        var response = await client.EchoAsync(request, deadline.Token);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal("你好", response.Message);
        Assert.Equal(1, Volatile.Read(ref connected));

        request.Message = "drop";
        await Assert.ThrowsAsync<WebSocketException>(() => client.EchoAsync(request, deadline.Token));
        Assert.Equal(1, Volatile.Read(ref connected));

        request.Message = "again";
        Assert.Equal("again", (await client.EchoAsync(request, deadline.Token)).Message);
        Assert.Equal(2, Volatile.Read(ref connected));
        await client.CloseAsync(deadline.Token);
        Assert.Equal(1, Volatile.Read(ref closed));

        Assert.Equal("again", (await client.EchoAsync(request, deadline.Token)).Message);
        Assert.Equal(3, Volatile.Read(ref connected));
        await client.CloseAsync(deadline.Token);
    }
}
