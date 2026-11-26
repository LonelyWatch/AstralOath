using System;
using System.Threading;
using System.Threading.Tasks;

namespace AstralOath.Client.Home
{
    public sealed partial class HeroInventoryBinder
    {
        // Receiving mail updates authority-owned data without reopening the page or resetting its tools.
        public async Task RefreshExternalAsync(CancellationToken token)
        {
            var opening = screen;
            if (opening == null || opening.Token.IsCancellationRequested) return;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, opening.Token);
            linked.CancelAfter(System.TimeSpan.FromSeconds(15));
            var current = linked.Token;
            var fresh = await fetch(current);
            current.ThrowIfCancellationRequested();
            if (screen != opening) return;
            snapshot = fresh;
            failed = fresh == null;
            await Task.WhenAll(artifacts?.RefreshExternalAsync(current) ?? Task.CompletedTask,
                equipment?.RefreshExternalAsync(current) ?? Task.CompletedTask);
            current.ThrowIfCancellationRequested();
            if (screen == opening) Refresh(resetScroll: false);
        }
    }
}
