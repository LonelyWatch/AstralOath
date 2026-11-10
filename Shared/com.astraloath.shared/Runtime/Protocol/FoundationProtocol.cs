namespace AstralOath.Shared
{
    public static class FoundationProtocol
    {
        public const uint Version = 1;
        public const string EchoPath = "/api/foundation/echo";
        public const string WebSocketPath = "/ws/foundation";
        public const string MediaType = "application/x-protobuf";
        public const int MaxPayloadBytes = 8192;
    }
}
