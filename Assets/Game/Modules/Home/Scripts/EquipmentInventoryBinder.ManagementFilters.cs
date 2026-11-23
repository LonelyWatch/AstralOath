using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private static readonly string[] ManagementStatKinds =
        {
            "all", "attack", "defense", "health", "attackPct", "defensePct", "healthPct",
            "speed", "critChanceBp", "critDamageBp", "effectivenessBp", "resistanceBp"
        };

        // Index zero is the primary stat. The four remaining filters match substats in any order.
        private readonly string[] managementStatFilters = new string[5];
        private int openStatFilter = -1;

        private void BindManagementFilters(BindingScope scope)
        {
            void Listen(string key, UnityAction callback)
            {
                var button = Get<GameButton>(key);
                button.onClick.AddListener(callback);
                scope.Add(() => { if (button) button.onClick.RemoveListener(callback); });
            }
            for (int i = 0; i < managementStatFilters.Length; i++)
            {
                int index = i;
                Listen("manageStatFilter" + index, () =>
                {
                    if (busy) return;
                    if (openStatFilter == index) { CloseManagementFilter(); return; }
                    openStatFilter = index;
                    Get<GameObject>("manageStatMenu").SetActive(true);
                    PaintManagementFilters();
                });
            }
            Listen("manageStatBackdrop", () => CloseManagementFilter());
            foreach (string option in ManagementStatKinds)
            {
                string value = option == "all" ? null : option;
                Listen("manageStatOption_" + option, () =>
                {
                    if (busy || openStatFilter < 0 || UsedByOtherSubfilter(value)) return;
                    managementStatFilters[openStatFilter] = value;
                    CloseManagementFilter();
                    Refresh();
                });
            }
        }

        private bool UsedByOtherSubfilter(string kind) =>
            openStatFilter > 0 && kind != null && kind != managementStatFilters[openStatFilter] &&
            Array.IndexOf(managementStatFilters, kind, 1) >= 1;

        private void ResetManagementFilters()
        {
            Array.Clear(managementStatFilters, 0, managementStatFilters.Length);
            CloseManagementFilter();
        }

        private bool CloseManagementFilter()
        {
            if (openStatFilter < 0) return false;
            openStatFilter = -1;
            Get<GameObject>("manageStatMenu").SetActive(false);
            PaintManagementFilters();
            return true;
        }

        private string ManagementStatCaption(string kind)
        {
            if (kind == null) return L("manageFilterAll");
            return kind.EndsWith("Pct", StringComparison.Ordinal) ? F("manageFilterPercent", L(kind)) : L(kind);
        }

        private void PaintManagementFilters()
        {
            for (int i = 0; i < managementStatFilters.Length; i++)
            {
                var button = Get<GameButton>("manageStatFilter" + i);
                var label = button.GetComponentInChildren<GameText>(true);
                string heading = i == 0 ? L("manageFilterMain") : F("manageFilterSub", i);
                label.text = F("manageFilterChoice", heading, ManagementStatCaption(managementStatFilters[i]));
                label.color = i == openStatFilter || managementStatFilters[i] != null ? new Color32(254,217,133,255) : Color.white;
            }
            if (openStatFilter < 0) return;
            foreach (string option in ManagementStatKinds)
            {
                string kind = option == "all" ? null : option;
                var button = Get<GameButton>("manageStatOption_" + option);
                var label = button.GetComponentInChildren<GameText>(true);
                label.text = ManagementStatCaption(kind);
                button.interactable = !UsedByOtherSubfilter(kind);
                label.color = managementStatFilters[openStatFilter] == kind ? new Color32(254,217,133,255) :
                    button.interactable ? Color.white : new Color32(133,142,156,255);
            }
        }

        private IEnumerable<EquipmentInventoryItem> FilterManagementCandidates(IEnumerable<EquipmentInventoryItem> candidates)
        {
            PaintManagementFilters();
            if (managementStatFilters[0] != null)
                candidates = candidates.Where(item => item.mainStat?.kind == managementStatFilters[0]);
            var required = managementStatFilters.Skip(1).Where(kind => kind != null).ToArray();
            if (required.Length == 0) return candidates;
            return candidates.Where(item => item.substats != null &&
                required.All(kind => item.substats.Any(stat => stat?.kind == kind)));
        }
    }
}
