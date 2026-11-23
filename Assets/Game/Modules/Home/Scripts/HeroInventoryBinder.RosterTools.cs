using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed partial class HeroInventoryBinder
    {
        private readonly HashSet<string> rosterElements = new HashSet<string>();
        private readonly HashSet<string> rosterRoles = new HashSet<string>();
        private BindingMap rosterTools;
        private string preferredRosterId, rosterSort;
        private bool ascendingRoster;

        private void BindRosterTools(BindingScope owner)
        {
            rosterTools = view.RosterTools;
            preferredRosterId = null;
            ResetRosterFilterValues();
            void Listen(GameButton button, UnityAction action)
            {
                button.onClick.AddListener(action);
                owner.Add(() => { if (button) button.onClick.RemoveListener(action); });
            }
            var filters = rosterTools.Require<UnityEngine.GameObject>("filters");
            var search = rosterTools.Require<UnityEngine.GameObject>("searchPanel");
            search.SetActive(false);
            filters.SetActive(false);
            Listen(view.RosterSearchButton, () =>
            {
                filters.SetActive(false);
                search.SetActive(true);
                view.Search.Select();
                view.Search.ActivateInputField();
            });
            Listen(view.RosterFilterButton, () =>
            {
                view.Search.DeactivateInputField();
                search.SetActive(false);
                filters.SetActive(true);
            });
            Listen(rosterTools.Require<GameButton>("filtersDismiss"), () => CloseRosterTools());
            Listen(rosterTools.Require<GameButton>("searchPanelDismiss"), () => CloseRosterTools());
            Listen(rosterTools.Require<GameButton>("submit"), () => CloseRosterTools());
            var field = view.Search;
            Listen(rosterTools.Require<GameButton>("clear"), () => { field.text = ""; field.ActivateInputField(); });
            void Submit(string _) => CloseRosterTools();
            field.onSubmit.AddListener(Submit);
            owner.Add(() => { if (field) field.onSubmit.RemoveListener(Submit); });
            Listen(rosterTools.Require<GameButton>("reset"), () =>
            {
                ResetRosterFilterValues();
                RefreshRosterFilters();
            });
            foreach (string key in new[] { "level", "acquired" })
                Listen(rosterTools.Require<GameButton>("sort_" + key), () =>
                {
                    ascendingRoster = key == rosterSort && !ascendingRoster;
                    rosterSort = key;
                    RefreshRosterFilters();
                });
            Listen(rosterTools.Require<GameButton>("direction"), () => { ascendingRoster = !ascendingRoster; RefreshRosterFilters(); });
            void Choices(string prefix, IEnumerable<string> values, HashSet<string> active)
            {
                foreach (string value in values)
                    Listen(rosterTools.Require<GameButton>(prefix + "_" + value), () =>
                    {
                        if (!active.Add(value)) active.Remove(value);
                        RefreshRosterFilters();
                    });
            }
            Choices("element", WornElements, rosterElements);
            Choices("role", HeroInventory.ClassNames.Skip(1), rosterRoles);
            locale.TextChanged += PaintRosterFilters;
            owner.Add(() => locale.TextChanged -= PaintRosterFilters);
            PaintRosterFilters();
        }

        private void ResetRosterFilterValues()
        {
            rosterSort = "level";
            ascendingRoster = false;
            rosterElements.Clear();
            rosterRoles.Clear();
        }

        private void RefreshRosterFilters()
        {
            PaintRosterFilters();
            Refresh(resetScroll: true, revealSelected: true);
        }

        private void PaintRosterFilters()
        {
            foreach (string value in WornElements)
                rosterTools.Require<Image>("element_" + value + "Mark").enabled = rosterElements.Contains(value);
            foreach (string value in HeroInventory.ClassNames.Skip(1))
                rosterTools.Require<Image>("role_" + value + "Mark").enabled = rosterRoles.Contains(value);
            foreach (string key in new[] { "level", "acquired" })
                rosterTools.Require<Image>("sort_" + key + "Mark").enabled = rosterSort == key;
            rosterTools.Require<GameText>("directionLabel").text = locale.Resolve(TextPrefix + (ascendingRoster ? "roster_sort_asc" : "roster_sort_desc"));
            view.RosterSortCaption.text = locale.Resolve("arena.draft.filter." + rosterSort);
            PaintRosterSearchCaption();
        }

        private void PaintRosterSearchCaption() => view.RosterSearchCaption.text =
            string.IsNullOrWhiteSpace(view.Search.text) ? locale.Resolve(TextPrefix + "roster_search") : view.Search.text;

        private bool CloseRosterTools()
        {
            if (!roster || !rosterTools) return false;
            var filter = rosterTools.Require<UnityEngine.GameObject>("filters");
            var search = rosterTools.Require<UnityEngine.GameObject>("searchPanel");
            if (!filter.activeSelf && !search.activeSelf) return false;
            if (search.activeSelf) view.Search.DeactivateInputField();
            filter.SetActive(false);
            search.SetActive(false);
            return true;
        }

        private IEnumerable<HeroInventoryItem> RosterHeroes(IEnumerable<HeroInventoryItem> owned, string query)
        {
            var matches = owned.Where(item =>
            {
                artwork.TryGetValue(item.templateId, out var art);
                return (query.Length == 0 || Name(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    && (rosterElements.Count == 0 || art != null && rosterElements.Contains(art.elementId))
                    && (rosterRoles.Count == 0 || art != null && rosterRoles.Contains(art.heroClass));
            });
            // The inventory endpoint sends acquisition_order ascending, including different copies of one hero.
            if (rosterSort == "acquired") return ascendingRoster ? matches : matches.Reverse();
            return ascendingRoster ? matches.OrderBy(item => item.level) : matches.OrderByDescending(item => item.level);
        }
    }
}
