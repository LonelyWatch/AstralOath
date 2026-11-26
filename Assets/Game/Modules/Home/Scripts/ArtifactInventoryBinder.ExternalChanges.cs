using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.Home
{
    internal sealed partial class ArtifactInventoryBinder
    {
        internal async Task RefreshExternalAsync(CancellationToken token)
        {
            while (loading || busy) await Task.Delay(50, token);
            var fresh = await fetch(token);
            token.ThrowIfCancellationRequested();
            snapshot = fresh;
            unconfirmedProgressSnapshot = null;
            failed = false;
            failure = null;
            if (enhancementTarget != null)
                enhancementTarget = fresh?.artifacts?.FirstOrDefault(item => item.instanceId == enhancementTarget.instanceId);
            Refresh(reset: false);
            onChanged();
        }
    }
}
