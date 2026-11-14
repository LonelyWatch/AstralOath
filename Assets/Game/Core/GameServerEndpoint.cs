using System;

namespace AstralOath.Client.Core
{
    public sealed class GameServerEndpoint
    {
        public GameServerEndpoint(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
                throw new ArgumentException("Use HTTPS or an HTTP loopback address.", nameof(address));
            Address = uri;
        }

        public Uri Address { get; }
    }
}
