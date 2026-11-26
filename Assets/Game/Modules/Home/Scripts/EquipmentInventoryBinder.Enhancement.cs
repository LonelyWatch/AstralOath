using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private WindowCoordinator enhancementWindows;
        private CancellationToken enhancementLifetime;
        private EquipmentEnhancementBinder activeEnhancement;
        private Task enhancementLoading = Task.CompletedTask;

        private void BindEquipmentEnhancement(BindingScope scope)
        {
            enhancementLifetime = scope.Token;
            enhancementWindows = new WindowCoordinator(scope.Token);
            // This owner represents the already bound equipment page. Each opening has its own child scope.
            _ = enhancementWindows.ShowPageAsync(_ => Task.CompletedTask);
            scope.Add(enhancementWindows.Dispose);
            void Hook(GameButton button, UnityAction handler)
            {
                button.onClick.AddListener(handler);
                scope.Add(() => { if (button) button.onClick.RemoveListener(handler); });
            }
            Hook(bagPane.Require<GameButton>("enhance"), () => OpenEquipmentEnhancement(selected));
            Hook(singleView.Map.Require<GameButton>("popupEnhance"), () => OpenEquipmentEnhancement(WornSlotItem()));
            Hook(singleView.Map.Require<GameButton>("currentEnhance"), () => OpenEquipmentEnhancement(WornSlotItem()));
            Hook(singleView.Map.Require<GameButton>("candidateEnhance"), () => OpenEquipmentEnhancement(CandidateSlotItem()));
        }

        private void OpenEquipmentEnhancement(EquipmentInventoryItem item)
        {
            if (busy || item == null || snapshot == null || enhancementLifetime.IsCancellationRequested) return;
            enhancementWindows.ShowOverlay("equipment-enhancement", scope =>
            {
                var view = new EquipmentEnhancement(page.Require<BindingMap>("gearEnhancement"));
                view.Root.transform.SetAsLastSibling();
                view.Show();
                var binder = new EquipmentEnhancementBinder(view, scope, locale, api,
                    () => { CloseEquipmentEnhancement(); }, OnEquipmentEnhanced, BindSlotArt, ItemName,
                    StatArt, StatValue, row => L(row.grade) + L(row.slot), row => SetProgress(row));
                activeEnhancement = binder;
                scope.Add(() => { if (activeEnhancement == binder) activeEnhancement = null; });
                scope.Add(view.Hide);
                enhancementLoading = LoadEnhancementAsync(binder, item, scope.Token);
            });
        }

        private async Task LoadEnhancementAsync(EquipmentEnhancementBinder binder, EquipmentInventoryItem item,
            CancellationToken token)
        {
            try { await binder.ShowAsync(snapshot, item, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                CloseEquipmentEnhancement();
                SetEquipmentStatus(L("loadFailed"));
                Debug.LogWarning("Unable to open equipment enhancement: " + error.GetType().Name);
            }
        }

        private void OnEquipmentEnhanced(EquipmentInventorySnapshot incoming)
        {
            string chosen = selected?.instanceId;
            AcceptSnapshot(incoming);
            selected = Items.FirstOrDefault(item => item.instanceId == chosen);
            // Updating the selected item's own level keeps the single-slot selection valid.
            slotVersion = incoming.version;
            changed();
            Refresh();
        }

        private bool CloseEquipmentEnhancement()
        {
            if (enhancementWindows == null || !enhancementWindows.IsOverlayOpen("equipment-enhancement")) return false;
            enhancementWindows.CloseOverlay("equipment-enhancement");
            return true;
        }
    }
}
