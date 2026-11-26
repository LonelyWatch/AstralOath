using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.Summon
{
    public sealed partial class SummonWindowBinder
    {
        public async Task RefreshExternalAsync(CancellationToken token)
        {
            var opening = screen;
            if (opening == null || opening.Token.IsCancellationRequested) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, opening.Token);
            linked.CancelAfter(System.TimeSpan.FromSeconds(15));
            while (busy) await Task.Delay(50, linked.Token);
            var fresh = await api.GetAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (screen != opening) return;
            state = fresh;
            if (Banner == null) bannerId = state.banners?.Length > 0 ? state.banners[0].id : Covenant;
            RenderMain();
        }
    }
}
