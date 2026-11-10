using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using AstralOath.Protocol;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class EchoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public EchoTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Theory]
    [InlineData("id", 2u, HttpStatusCode.BadRequest)]
    [InlineData("", 1u, HttpStatusCode.BadRequest)]
    [InlineData("contains space", 1u, HttpStatusCode.BadRequest)]
    public async Task InvalidRequestIsRejectedBeforeDatabaseAccess(string id, uint version, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var response = await PostAsync(client, new EchoRequest { RequestId = id, ProtocolVersion = version });
        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(1, "text/plain", HttpStatusCode.UnsupportedMediaType)]
    [InlineData(1, "application/x-protobuf", HttpStatusCode.BadRequest)]
    [InlineData(8193, "application/x-protobuf", HttpStatusCode.RequestEntityTooLarge)]
    public async Task RequestBodyBoundaryIsEnforced(int length, string mediaType, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        using var body = new ByteArrayContent(Enumerable.Repeat((byte)0xFF, length).ToArray());
        body.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var response = await client.PostAsync("/api/foundation/echo", body);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentSocketAssemblesFragmentsAndRejectsWrongVersion()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws/foundation"), deadline.Token);
        var request = new EchoRequest { RequestId = "ws1", ProtocolVersion = 1, Message = "中文" };
        var wire = request.ToByteArray();
        await socket.SendAsync(wire.AsMemory(0, 1), WebSocketMessageType.Binary, false, deadline.Token);
        await socket.SendAsync(wire.AsMemory(1), WebSocketMessageType.Binary, true, deadline.Token);
        var buffer = new byte[512];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
        Assert.True(received.EndOfMessage);
        Assert.Equal(WebSocketMessageType.Binary, received.MessageType);
        var echo = EchoResponse.Parser.ParseFrom(buffer, 0, received.Count);
        Assert.Equal(request.RequestId, echo.RequestId);
        Assert.Equal("中文", echo.Message);
        Assert.Equal(1u, echo.ProtocolVersion);
        Assert.Equal(0UL, echo.Sequence);
        request.ProtocolVersion = 2;
        await socket.SendAsync(request.ToByteArray().AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
        var close = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token);
    }

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, EchoRequest request)
    {
        using var body = new ByteArrayContent(request.ToByteArray());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return await client.PostAsync("/api/foundation/echo", body);
    }
}
