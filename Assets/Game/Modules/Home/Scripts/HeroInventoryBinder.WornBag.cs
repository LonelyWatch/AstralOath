using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed partial class HeroInventoryBinder
    {
        private static readonly string[] WornElements = { "fire", "ice", "wind", "light", "dark" };
        private static readonly string[] WornSorts = { "stars", "level", "element", "name", "acquired", "ability" };
        private readonly HashSet<string> wornElements = new HashSet<string>(), wornStars = new HashSet<string>();
        private BindingMap wornFilters;
        private string wornSort = "ability";
        private bool wornAscending, hideMax, hideFull, visitingWearer;
        private Vector2 wornScrollPosition;

        private void BindRosterPresentation(BindingScope owner)
        {
            rosterStage = view.RosterDetail;
            focusedRoster = FocusedRosterScroll.Bind(view.RosterScroll, owner, null, SelectFocusedInstance);
            Canvas.willRenderCanvases += OnRosterResized;
            var current = portrait = new HeroRosterPortrait(view.DetailPortrait, portraitSources);
            Canvas.willRenderCanvases += current.Tick;
            bool released = false;
            void Release()
            {
                if (released) return;
                released = true;
                Canvas.willRenderCanvases -= OnRosterResized;
                Canvas.willRenderCanvases -= current.Tick;
                current.Dispose();
                if (portrait == current) portrait = null;
            }
            var onDestroy = view.Root.GetComponent<BindingMap>().destroyCancellationToken.Register(Release);
            owner.Add(() =>
            {
                onDestroy.Dispose();
                Release();
            });
        }

        private void BindWornRow(Card card)
        {
            var map = card.Root.GetComponent<BindingMap>();
            map.Require<GameButton>("bagManage").onClick.AddListener(() => VisitWearer(card.Item, true));
            map.Require<GameButton>("bagPortrait").onClick.AddListener(() => VisitWearer(card.Item, false));
            map.Require<GameButton>("bagArtifact").onClick.AddListener(() => artifacts?.OpenWornFromBag(card.Item));
            for (int index = 0; index < EquipmentInventoryBinder.Slots.Length; index++)
            {
                int slotIndex = index;
                map.Require<GameButton>("bagInspect" + index).onClick.AddListener(() =>
                {
                    if (card.Item == null || equipment == null || !equipment.HasWornItem(card.Item.instanceId, slotIndex)) return;
                    selected = card.Item;
                    equipment.Show(true, EquipmentInventoryBinder.Slots[slotIndex]);
                });
            }
        }

        private void VisitWearer(HeroInventoryItem wearer, bool manage)
        {
            if (wearer == null || (manage && equipment == null)) return;
            wornScrollPosition = Scroll.content.anchoredPosition;
            Scroll.StopMovement();
            visitingWearer = roster = true;
            view.Bag.SetActive(false);
            view.Roster.SetActive(true);
            view.FitInventoryLayout(true);
            Scroll = view.RosterScroll;
            if (portrait == null)
            {
                BindRosterPresentation(screen);
                var scroll = Scroll;
                scroll.onValueChanged.AddListener(OnScroll);
                screen.Add(() => { if (scroll) scroll.onValueChanged.RemoveListener(OnScroll); });
            }
            view.Search.SetTextWithoutNotify("");
            selected = wearer;
            preferredRosterId = wearer.instanceId;
            ResetRosterFilterValues();
            PaintRosterFilters();
            SetExpanded(false);
            Refresh(resetScroll: true, revealSelected: true);
            if (manage) equipment.Show(true, null);
        }

        private bool ReturnToWornBag()
        {
            if (!visitingWearer) return false;
            visitingWearer = roster = false;
            portrait.SetVisible(false);
            view.Roster.SetActive(false);
            view.Bag.SetActive(true);
            view.FitInventoryLayout(false);
            Scroll = view.BagScroll;
            Refresh(resetScroll: false);
            float maximum = Mathf.Max(0f, Scroll.content.rect.height - Scroll.viewport.rect.height);
            Scroll.content.anchoredPosition = new Vector2(0f, Mathf.Clamp(wornScrollPosition.y, 0f, maximum));
            ShowCards();
            return true;
        }

        private void BindWornFilters(BindingScope owner)
        {
            wornFilters = view.WornFilters;
            wornFilters.gameObject.SetActive(false);
            void Listen(GameButton button, UnityAction action)
            {
                button.onClick.AddListener(action);
                owner.Add(() => { if (button) button.onClick.RemoveListener(action); });
            }
            Listen(view.WornSort, () => wornFilters.gameObject.SetActive(true));
            Listen(wornFilters.Require<GameButton>("close"), () => CloseWornFilters());
            foreach (var group in new[] { ("element", WornElements, wornElements), ("stars", new[] { "6","5","4","3","2" }, wornStars) })
            {
                foreach (string value in group.Item2.Prepend("all"))
                {
                    var choice = wornFilters.Require<BindingMap>(group.Item1 + "_" + value);
                    Listen(choice.Require<GameButton>("button"), () =>
                    {
                        if (value == "all") group.Item3.Clear();
                        else if (!group.Item3.Add(value)) group.Item3.Remove(value);
                        Refilter();
                    });
                }
            }
            foreach (string key in WornSorts)
            {
                Listen(wornFilters.Require<GameButton>("sort_" + key), () =>
                {
                    wornAscending = key == wornSort ? !wornAscending : key == "name";
                    wornSort = key;
                    Refilter();
                });
            }
            Listen(wornFilters.Require<GameButton>("reverse"), () => { wornAscending = !wornAscending; Refilter(); });
            Listen(wornFilters.Require<GameButton>("hideMax"), () => { hideMax = !hideMax; Refilter(); });
            Listen(wornFilters.Require<GameButton>("hideFull"), () => { hideFull = !hideFull; Refilter(); });
            locale.TextChanged += PaintWornFilters;
            owner.Add(() => locale.TextChanged -= PaintWornFilters);
            PaintWornFilters();
        }

        private bool CloseWornFilters()
        {
            if (!wornFilters || !wornFilters.gameObject.activeInHierarchy) return false;
            wornFilters.gameObject.SetActive(false);
            return true;
        }

        private void Refilter()
        {
            PaintWornFilters();
            Refresh(resetScroll: true);
        }

        private void PaintWornFilters()
        {
            Color ink = new Color32(24, 32, 39, 255);
            void PaintGroup(string group, string[] values, HashSet<string> selectedValues)
            {
                foreach (string value in values.Prepend("all"))
                {
                    bool active = value == "all" ? selectedValues.Count == 0 : selectedValues.Contains(value);
                    var choice = wornFilters.Require<BindingMap>(group + "_" + value);
                    choice.Require<Image>("on").enabled = active;
                    choice.Require<GameText>("label").color = active ? ink : Color.white;
                }
            }
            PaintGroup("element", WornElements, wornElements);
            PaintGroup("stars", new[] { "6","5","4","3","2" }, wornStars);
            wornFilters.Require<Image>("hideMaxCheck").enabled = hideMax;
            wornFilters.Require<Image>("hideFullCheck").enabled = hideFull;
            bool ability = wornSort == "ability";
            var caption = view.BagCaption("Sort");
            caption.text = locale.Resolve(ability ? TextPrefix + "sort_power" : "arena.draft.filter." + wornSort);
            caption.rectTransform.anchorMax = new Vector2(ability ? .76f : .98f, caption.rectTransform.anchorMax.y);
            view.WornSortAttack.enabled = ability;
            wornFilters.Require<Image>("sort_attackIcon").color = ability ? ink : Color.white;
            foreach (string key in WornSorts)
            {
                bool active = key == wornSort;
                wornFilters.Require<Image>("sort_" + key + "_on").enabled = active;
                wornFilters.Require<GameText>("sort_" + key + "_label").color = active ? ink : Color.white;
            }
            var reverse = (RectTransform)wornFilters.Require<GameButton>("reverse").transform;
            float bottom = .20f - .1515f * (Array.IndexOf(WornSorts, wornSort) - 5);
            reverse.anchorMax = new Vector2(reverse.anchorMax.x, bottom);
            reverse.anchorMin = new Vector2(reverse.anchorMin.x, bottom - .111f);
            wornFilters.Require<Image>("up").color = wornAscending ? Color.white : Color.gray;
            wornFilters.Require<Image>("down").color = wornAscending ? Color.gray : Color.white;
        }

        private IEnumerable<HeroInventoryItem> WornHeroes(HeroInventoryItem[] owned)
        {
            var filtered = owned.Where(hero => (classFilter == "all" || ClassForTemplate(hero.templateId) == classFilter)
                && (wornElements.Count == 0 || artwork.TryGetValue(hero.templateId, out var art) && wornElements.Contains(art.elementId))
                && (wornStars.Count == 0 || wornStars.Contains(hero.stars.ToString(CultureInfo.InvariantCulture)))
                && (!hideMax || hero.level < (hero.stars == 1 ? 15 : hero.stars * 10))
                && (!hideFull || equipment?.Pieces(hero.instanceId).Length != EquipmentInventoryBinder.Slots.Length));
            if (wornSort == "acquired") return wornAscending ? filtered : filtered.Reverse();
            if (wornSort == "name")
            {
                var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("zh-Hans"), true);
                return wornAscending ? filtered.OrderBy(Name, comparer) : filtered.OrderByDescending(Name, comparer);
            }
            return wornAscending ? filtered.OrderBy(WornSortValue) : filtered.OrderByDescending(WornSortValue);
        }

        private int WornSortValue(HeroInventoryItem hero)
        {
            if (wornSort == "level") return hero.level;
            if (wornSort == "stars") return hero.stars;
            if (wornSort == "element")
                return artwork.TryGetValue(hero.templateId, out var art) ? Array.IndexOf(WornElements, art.elementId) : -1;
            var stats = BaseStats(hero);
            if (stats != null && equipment != null)
                stats = EquipmentStatCalculator.Calculate(stats, equipment.Pieces(hero.instanceId));
            return (stats?.Attack ?? 0) + (artifacts?.EquippedStats(hero.instanceId).Attack ?? 0);
        }
    }
}
