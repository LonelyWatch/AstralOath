using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaDraftRosterFilter
    {
        private readonly bool preban;
        private string Overlay => preban ? "arena/draft-preban-filter" : "arena/draft-roster-filter";
        private const string LabelPrefix = "arena.draft.filter.";
        private static readonly string[] SortKeys = { "stars", "level", "name", "attack", "defense", "health", "speed" };
        private readonly ArenaDraft view;
        private readonly ILocaleService locale;
        private readonly WindowCoordinator windows;
        private readonly Action refreshRoster;
        private readonly BindingMap panel;
        private readonly Dictionary<string, HashSet<string>> selections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private string sort;
        private bool ascending, hideMax;

        internal ArenaDraftRosterFilter(ArenaDraft view, ILocaleService locale, WindowCoordinator windows,
            Action refreshRoster, bool preban = false)
        {
            this.view = view;
            this.locale = locale;
            this.windows = windows;
            this.refreshRoster = refreshRoster;
            this.preban = preban;
            sort = preban ? "stars" : "level";
            panel = view.Node("rosterFilters").GetComponent<BindingMap>();
        }

        internal void Bind(BindingScope scope)
        {
            view.Button(preban ? "preSort" : "openRosterFilters").Bind(scope, _ =>
            {
                if (windows.IsOverlayOpen(Overlay)) Close();
                else windows.ShowOverlay(Overlay, Open);
                return Task.CompletedTask;
            });
            DrawToolbar();
        }

        internal bool Compact { get; private set; }

        internal void Close() => windows.CloseOverlay(Overlay);

        private void Open(BindingScope scope)
        {
            panel.transform.SetAsLastSibling();
            panel.gameObject.SetActive(true);
            scope.Add(() => { if (panel) panel.gameObject.SetActive(false); });
            Listen(scope, panel.Require<GameButton>("close"), Close);
            Listen(scope, panel.Require<GameButton>("resetEffects"), () =>
            {
                foreach (var group in new[] { "enhance", "weaken", "general" })
                {
                    selections.Remove(group);
                    DrawGroup(group, panel.Require<BindingMap>(group));
                }
                Refresh();
            });
            foreach (var group in new[] { "element", "class", "rarity", "enhance", "weaken", "general" })
            {
                BindingMap groupMap = panel.Require<BindingMap>(group);
                foreach (var item in Options(groupMap))
                {
                    string key = item.gameObject.name;
                    Listen(scope, item.Require<GameButton>("button"), () =>
                    {
                        if (key == "all") selections.Remove(group);
                        else
                        {
                            var set = GetSelection(group);
                            if (!set.Add(key)) set.Remove(key);
                        }
                        DrawGroup(group, groupMap);
                        Refresh();
                    });
                }
                DrawGroup(group, groupMap);
            }
            var effectList = panel.Require<GameObject>("effectList");
            foreach (var group in new[] { "enhance", "weaken", "general" })
            {
                Listen(scope, panel.Require<GameButton>("show_" + group), () =>
                {
                    bool alreadyOpen = effectList.activeSelf && panel.Require<BindingMap>(group).gameObject.activeSelf;
                    foreach (var item in new[] { "enhance", "weaken", "general" })
                        panel.Require<BindingMap>(item).gameObject.SetActive(item == group);
                    effectList.SetActive(!alreadyOpen);
                });
            }
            effectList.SetActive(false);
            Listen(scope, panel.Require<GameButton>("hideMax"), () =>
            {
                hideMax = !hideMax;
                panel.Require<Image>("hideMaxCheck").enabled = hideMax;
                Refresh();
            });
            panel.Require<Image>("hideMaxCheck").enabled = hideMax;
            var compactCheck = panel.Require<Image>("compactCheck");
            compactCheck.enabled = Compact;
            Listen(scope, panel.Require<GameButton>("compact"), () =>
            {
                Compact = !Compact;
                compactCheck.enabled = Compact;
                Refresh();
            });
            foreach (var key in SortKeys)
            {
                Listen(scope, panel.Require<GameButton>("sort_" + key), () =>
                {
                    ascending = key == sort ? !ascending : key == "name";
                    sort = key;
                    DrawSort();
                    Refresh();
                });
            }
            DrawSort();
        }

        internal bool Matches(ArenaDraftArt.Hero hero)
        {
            if (hideMax && hero.Level >= 60) return false;
            return Accept("element", hero.Element) && Accept("class", hero.Class) &&
                Accept("rarity", hero.BaseStars.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
                AcceptEffect("enhance", hero.FilterEffects) && AcceptEffect("weaken", hero.FilterEffects) &&
                AcceptEffect("general", hero.FilterEffects);
        }

        internal IEnumerable<T> Order<T>(IEnumerable<T> rows, Func<T, ArenaDraftArt.Hero> hero)
        {
            if (sort == "name")
            {
                var names = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-Hans"), true);
                return (ascending ? rows.OrderBy(row => hero(row).Name, names) : rows.OrderByDescending(row => hero(row).Name, names))
                    .ThenBy(row => hero(row).Id, StringComparer.Ordinal);
            }
            return (ascending ? rows.OrderBy(row => SortValue(hero(row))) : rows.OrderByDescending(row => SortValue(hero(row))))
                .ThenBy(row => hero(row).Id, StringComparer.Ordinal);
        }

        private int SortValue(ArenaDraftArt.Hero hero) => sort switch
        {
            "stars" => hero.BaseStars,
            "attack" => hero.Attack,
            "defense" => hero.Defense,
            "health" => hero.Health,
            "speed" => hero.Speed,
            _ => hero.Level
        };

        private bool Accept(string group, string value) => !selections.TryGetValue(group, out var set) || set.Count == 0 || set.Contains(value);
        private bool AcceptEffect(string group, string[] values) => !selections.TryGetValue(group, out var set) || set.Count == 0 ||
            (values != null && set.Overlaps(values));

        private HashSet<string> GetSelection(string group)
        {
            if (!selections.TryGetValue(group, out var set))
                selections[group] = set = new HashSet<string>(StringComparer.Ordinal);
            return set;
        }

        private void DrawGroup(string group, BindingMap groupMap)
        {
            var chosen = GetSelection(group);
            foreach (var item in Options(groupMap))
            {
                string key = item.gameObject.name;
                var label = item.Require<GameText>("label");
                bool active = key == "all" ? chosen.Count == 0 : chosen.Contains(key);
                item.Require<Image>("on").enabled = active;
                label.color = active ? new Color32(25, 34, 43, 255) : Color.white;
            }
            if (group is "enhance" or "weaken" or "general")
            {
                var label = panel.Require<GameText>("count_" + group);
                label.text = chosen.Count == 0 ? "All" : chosen.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static IEnumerable<BindingMap> Options(BindingMap group) => group.Require<RectTransform>("options")
            .Cast<Transform>().Select(node => node.GetComponent<BindingMap>());

        private void DrawToolbar()
        {
            var label = view.Label(preban ? "preSortLabel" : "rosterFilterLabel");
            label.text = locale.Resolve(LabelPrefix + sort);
            label.color = selections.Values.Any(items => items.Count > 0) || hideMax ? Mist : Color.white;
        }

        private static readonly Color Mist = new Color32(130, 210, 255, 255);

        private void DrawSort()
        {
            foreach (var key in SortKeys)
            {
                bool active = key == sort;
                panel.Require<Image>("sort_" + key + "_on").enabled = active;
                panel.Require<GameText>("sort_" + key + "_label").color = active ? new Color32(25, 34, 43, 255) : Color.white;
                panel.Require<Image>("sort_" + key + "_down").enabled = active && !ascending;
                panel.Require<Image>("sort_" + key + "_up").enabled = active && ascending;
            }
        }

        private void Refresh()
        {
            DrawToolbar();
            refreshRoster();
        }

        private static void Listen(BindingScope scope, GameButton button, Action handle)
        {
            UnityAction click = () => handle();
            button.onClick.AddListener(click);
            scope.Add(() => { if (button) button.onClick.RemoveListener(click); });
        }
    }
}
