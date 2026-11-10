using System;
using System.IO;
using AstralOath.Protocol;
using AstralOath.Shared;

namespace AstralOath.Client.Core
{
    public static class ProtobufEchoRequests
    {
        public static void ValidateMessage(string message) => Create(message, Guid.Empty);

        public static EchoRequest Create(string message) => Create(message, Guid.NewGuid());

        private static EchoRequest Create(string message, Guid requestId)
        {
            var request = new EchoRequest
            {
                RequestId = requestId.ToString("D"),
                ProtocolVersion = FoundationProtocol.Version,
                Message = message
            };

            if (request.CalculateSize() > FoundationProtocol.MaxPayloadBytes)
                throw new InvalidDataException($"Echo request exceeds {FoundationProtocol.MaxPayloadBytes} Protobuf bytes.");
            return request;
        }
    }
}