using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.Shop
{
    // One instance belongs to a signed-in player's navigation session, across page openings.
    public sealed class ShopExchangeRequests
    {
        readonly Dictionary<string,string> pending = new Dictionary<string,string>(StringComparer.Ordinal);

        public async Task<ShopPageDto> SubmitAsync(string offerId,
            Func<string,string,CancellationToken,Task<ShopPageDto>> send, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!pending.TryGetValue(offerId,out var id)) pending[offerId] = id = Guid.NewGuid().ToString("N");
            try
            {
                var receipt = await send(offerId,id,token);
                if (receipt?.offers == null) throw new InvalidDataException("invalid_shop_receipt");
                Forget(offerId,id);
                return receipt;
            }
            catch (IOException rejection) when (rejection.Message is "invalid_shop_offer" or "shop_limit" or
                "insufficient_conquest" or "resource_overflow" or "request_conflict")
            {
                Forget(offerId,id);
                throw;
            }
        }

        void Forget(string offerId,string id)
        {
            if (pending.TryGetValue(offerId,out var current) && current == id) pending.Remove(offerId);
        }
    }
}
