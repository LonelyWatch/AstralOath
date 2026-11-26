using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        internal async Task RefreshExternalAsync(CancellationToken token)
        {
            while (busy) await Task.Delay(50, token);
            var fresh = await fetch(token);
            token.ThrowIfCancellationRequested();
            if (snapshot != null && fresh.version < snapshot.version) return;
            var scrolls = ui.GetComponentsInChildren<ScrollRect>(true);
            var positions = scrolls.Select(scroll => scroll.content.anchoredPosition).ToArray();
            bool staleQuote = snapshot != null && fresh.version != snapshot.version && Get<UnityEngine.GameObject>("dialog").activeSelf;
            AcceptSnapshot(fresh);
            loadFailed = false;
            if (staleQuote) CloseDialog();
            // A new mailbox reward changes the inventory version, not the user's candidate selection.
            // Submission creates a new quote against this newly read version.
            slotVersion = snapshot.version;
            changed();
            if (ui.gameObject.activeInHierarchy) Refresh();
            for (int i = 0; i < scrolls.Length; ++i)
                if (scrolls[i] && scrolls[i].content) scrolls[i].content.anchoredPosition = positions[i];
            if (staleQuote) SetEquipmentStatus(L("conflict"));
            var enhancement = activeEnhancement;
            if (enhancement != null)
            {
                var opening = enhancementLoading;
                while (!opening.IsCompleted) await Task.Delay(50, token);
                await opening;
                token.ThrowIfCancellationRequested();
                if (activeEnhancement == enhancement)
                    await enhancement.RefreshExternalAsync(snapshot, token);
            }
        }
    }
}
