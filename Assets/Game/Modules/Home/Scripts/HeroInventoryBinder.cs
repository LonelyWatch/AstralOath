using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class HeroInventoryBinder
    {
        private const string TextPrefix = "home.hero_inventory.";
        private enum Tab { Heroes, Equipment, Artifacts }

        private readonly HeroInventory view;
        private readonly ILocaleService locale;
        private readonly Func<CancellationToken, Task<HeroInventorySnapshot>> fetch;
        private readonly Func<Task> goHome;
        private readonly Dictionary<string, HomeHeroArt.Hero> artwork;
        private readonly Sprite star;
        private readonly List<Card> cards = new List<Card>();

        private BindingScope screen;
        private HeroInventorySnapshot snapshot;
        private HeroInventoryItem[] visible = Array.Empty<HeroInventoryItem>();
        private HeroInventoryItem selected;
        private bool roster, expanded, loading, failed;
        private Tab tab;
        private string classFilter;
        private HeroRosterPortrait portrait;

        public HeroInventoryBinder(HeroInventory view, HomeHeroArt art, ILocaleService locale,
            Func<CancellationToken, Task<HeroInventorySnapshot>> fetch, Func<Task> goHome)
        {
            this.view = view;
            this.locale = locale;
            this.fetch = fetch;
            this.goHome = goHome;
            artwork = art.heroes.ToDictionary(item => item.id, StringComparer.Ordinal);
            star = art.star;
        }

        public async Task OpenAsync(bool roster, BindingScope scope)
        {
            screen = scope;
            this.roster = roster;
            expanded = false;
            tab = Tab.Heroes;
            classFilter = "all";
            view.Show();
            if (roster)
            {
                var currentPortrait = new HeroRosterPortrait(view.DetailPortrait);
                portrait = currentPortrait;
                Canvas.willRenderCanvases += currentPortrait.Tick;
                scope.Add(() =>
                {
                    Canvas.willRenderCanvases -= currentPortrait.Tick;
                    currentPortrait.Dispose();
                    portrait = null;
                });
            }
            scope.Add(() =>
            {
                foreach (var card in cards)
                {
                    if (!card.Root) continue;
                    card.Root.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(card.Root.gameObject);
                }
                cards.Clear();
                if (view.Root) view.Hide();
                screen = null;
            });

            view.Title.Bind(locale, scope, TextPrefix + (roster ? "heroes" : "bag"));
            view.TabHeroesLabel.Bind(locale, scope, TextPrefix + "heroes");
            view.TabEquipmentLabel.Bind(locale, scope, TextPrefix + "equipment");
            view.TabArtifactsLabel.Bind(locale, scope, TextPrefix + "artifacts");
            view.SearchHint.Bind(locale, scope, TextPrefix + "search");
            view.RetryLabel.Bind(locale, scope, TextPrefix + "retry");
            var search = view.Search;
            search.SetTextWithoutNotify("");
            search.onValueChanged.AddListener(OnSearch);
            scope.Add(() => { if (search) search.onValueChanged.RemoveListener(OnSearch); });
            var scroll = Scroll;
            scroll.onValueChanged.AddListener(OnScroll);
            scope.Add(() => { if (scroll) scroll.onValueChanged.RemoveListener(OnScroll); });

            view.Bag.SetActive(!roster);
            view.Roster.SetActive(roster);
            view.Back.Bind(scope, _ => expanded ? ToggleExpanded() : goHome());
            view.Expand.Bind(scope, _ => ToggleExpanded());
            view.Retry.Bind(scope, ReloadAsync);
            view.TabHeroes.Bind(scope, _ => SelectTab(Tab.Heroes));
            view.TabEquipment.Bind(scope, _ => SelectTab(Tab.Equipment));
            view.TabArtifacts.Bind(scope, _ => SelectTab(Tab.Artifacts));
            foreach (string name in HeroInventory.ClassNames)
                view.ClassFilter(name).Bind(scope, _ => SelectClass(name));
            SetExpanded(false);
            await ReloadAsync(scope.Token);
        }

        private ScrollRect Scroll => roster ? view.RosterScroll : view.BagScroll;
        private int Columns => roster && expanded ? 3 : 1;
        private float RowHeight => roster ? 116f : 175f;

        private async Task ReloadAsync(CancellationToken token)
        {
            snapshot = null;
            selected = null;
            failed = false;
            loading = true;
            Refresh(resetScroll: true);
            try
            {
                snapshot = await fetch(token);
                token.ThrowIfCancellationRequested();
                failed = snapshot == null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // The error body may contain service diagnostics. Only present a localized status.
                Debug.LogWarning("Unable to load hero inventory: " + error.GetType().Name);
                failed = true;
            }
            loading = false;
            if (!token.IsCancellationRequested)
                Refresh(resetScroll: true);
        }

        private Task SelectTab(Tab next)
        {
            if (tab != next)
            {
                tab = next;
                classFilter = "all";
                Refresh(resetScroll: true);
            }
            return Task.CompletedTask;
        }

        private Task SelectClass(string next)
        {
            classFilter = next;
            Refresh(resetScroll: true);
            return Task.CompletedTask;
        }

        private Task ToggleExpanded()
        {
            SetExpanded(!expanded);
            Refresh(resetScroll: true);
            return Task.CompletedTask;
        }

        private void SetExpanded(bool enabled)
        {
            expanded = enabled;
            view.RosterDetail.gameObject.SetActive(!expanded);
            view.RosterSidebar.anchorMin = new Vector2(expanded ? .055f : .738f, .045f);
            view.ExpandLabel.text = locale.Resolve(TextPrefix + (expanded ? "collapse" : "expand"));
        }

        private void OnSearch(string _) => Refresh(resetScroll: true);
        private void OnScroll(Vector2 _) => ShowCards();
        private void Select(HeroInventoryItem hero)
        {
            selected = hero;
            if (expanded)
            {
                SetExpanded(false);
                Refresh(resetScroll: true);
                return;
            }
            SetDetail();
            ShowCards();
        }

        private void Refresh(bool resetScroll)
        {
            bool heroes = roster || tab == Tab.Heroes;
            string query = roster ? view.Search.text.Trim() : "";
            visible = !heroes || snapshot?.heroes == null
                ? Array.Empty<HeroInventoryItem>()
                : snapshot.heroes.Where(item =>
                    (query.Length == 0 || Name(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (roster || classFilter == "all" || artwork.TryGetValue(item.templateId, out var art) && art.heroClass == classFilter))
                    .ToArray();
            selected = visible.FirstOrDefault(item => item.instanceId == selected?.instanceId) ?? visible.FirstOrDefault();
            view.Title.text = roster && selected != null ? Name(selected) : locale.Resolve(TextPrefix + (roster ? "heroes" : "bag"));
            view.Capacity.gameObject.SetActive(heroes && snapshot != null);
            if (heroes && snapshot != null)
                view.Capacity.text = GameText.Format(locale, TextPrefix + "capacity", snapshot.used, snapshot.capacity);

            view.TabHeroes.interactable = tab != Tab.Heroes;
            view.TabEquipment.interactable = tab != Tab.Equipment;
            view.TabArtifacts.interactable = tab != Tab.Artifacts;
            view.TabSelection("heroes").enabled = tab == Tab.Heroes;
            view.TabSelection("equipment").enabled = tab == Tab.Equipment;
            view.TabSelection("artifacts").enabled = tab == Tab.Artifacts;
            view.BagFilter.SetActive(!roster && heroes);
            foreach (string name in HeroInventory.ClassNames)
                view.ClassSelection(name).enabled = name == classFilter;
            view.BagScroll.gameObject.SetActive(!roster && heroes);
            string status = !heroes ? tab == Tab.Equipment ? "empty_equipment" : "empty_artifacts"
                : loading ? "loading" : failed ? "load_failed" : visible.Length == 0 ? "empty_heroes" : null;
            view.Status.gameObject.SetActive(status != null);
            if (status != null) view.Status.text = locale.Resolve(TextPrefix + status);
            view.Retry.gameObject.SetActive(heroes && failed);
            Scroll.content.sizeDelta = new Vector2(0f, Mathf.CeilToInt((float)visible.Length / Columns) * RowHeight);
            if (resetScroll)
            {
                Scroll.StopMovement();
                Scroll.content.anchoredPosition = Vector2.zero;
            }
            SetDetail();
            ShowCards();
        }

        private void ShowCards()
        {
            int columns = Columns;
            float height = RowHeight;
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(Scroll.content.anchoredPosition.y / height));
            int viewRows = Mathf.CeilToInt(Mathf.Max(Scroll.viewport.rect.height, 1080f) / height) + 2;
            int first = firstRow * columns;
            int count = Mathf.Clamp(visible.Length - first, 0, viewRows * columns);
            while (cards.Count < count)
            {
                var card = new Card(UnityEngine.Object.Instantiate(roster ? view.RosterRow : view.BagRow, Scroll.content, false));
                if (roster) card.Button.onClick.AddListener(() => Select(card.Item));
                cards.Add(card);
            }
            for (int index = 0; index < cards.Count; ++index)
            {
                var card = cards[index];
                if (index >= count)
                {
                    card.Root.gameObject.SetActive(false);
                    continue;
                }
                int itemIndex = first + index;
                HeroInventoryItem item = visible[itemIndex];
                artwork.TryGetValue(item.templateId, out var art);
                int column = itemIndex % columns;
                card.Root.anchorMin = new Vector2((float)column / columns, 1);
                card.Root.anchorMax = new Vector2((float)(column + 1) / columns, 1);
                card.Root.pivot = new Vector2(.5f, 1f);
                card.Root.sizeDelta = new Vector2(-12f, height - 8f);
                card.Root.anchoredPosition = new Vector2(0, -(itemIndex / columns) * height);
                card.Display(item, Name(item), Level(item), art, star, roster && selected == item);
            }
        }

        private void SetDetail()
        {
            if (!roster) return;
            HomeHeroArt.Hero art = null;
            if (selected != null) artwork.TryGetValue(selected.templateId, out art);
            view.Title.text = selected == null ? locale.Resolve(TextPrefix + "heroes") : Name(selected);
            view.DetailName.text = selected == null ? "" : Name(selected);
            view.DetailLevel.text = selected == null ? "" : Level(selected);
            view.DetailStars.text = selected == null ? "" : new string('★', Mathf.Clamp(selected.stars, 0, 6));
            SetSprite(view.DetailIllustration, art?.illustration ? art.illustration : art?.portrait);
            bool native = expanded ? portrait.SetVisible(false) : portrait.Show(art);
            if (native) view.DetailIllustration.enabled = false;
            SetSprite(view.DetailElement, art?.element);
            SetSprite(view.DetailRole, art?.role);
        }

        private string Name(HeroInventoryItem item) =>
            artwork.TryGetValue(item.templateId, out var art) ? art.name : locale.Resolve(TextPrefix + "unknown_hero");

        private string Level(HeroInventoryItem item) => GameText.Format(locale, TextPrefix + "level",
            item.level.ToString(CultureInfo.InvariantCulture));

        private static void SetSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
        }

        private sealed class Card
        {
            private readonly GameText name, level;
            private readonly Image portrait, selection, element, role;
            private readonly Image[] stars;

            internal readonly RectTransform Root;
            internal readonly GameButton Button;
            internal HeroInventoryItem Item { get; private set; }

            internal Card(GameObject instance)
            {
                Root = (RectTransform)instance.transform;
                var map = instance.GetComponent<BindingMap>();
                name = map.Require<GameText>("name");
                level = map.Require<GameText>("level");
                portrait = map.Require<Image>("portrait");
                selection = map.Require<Image>("selection");
                element = map.Require<Image>("element");
                role = map.Require<Image>("role");
                stars = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                Button = instance.GetComponent<GameButton>();
            }

            internal void Display(HeroInventoryItem item, string title, string levelText, HomeHeroArt.Hero art, Sprite star,
                bool selected)
            {
                Item = item;
                Root.name = item.instanceId;
                name.text = title;
                level.text = levelText;
                SetSprite(portrait, art?.portrait);
                SetSprite(element, art?.element);
                SetSprite(role, art?.role);
                selection.enabled = selected;
                for (int index = 0; index < stars.Length; ++index)
                    SetSprite(stars[index], index < item.stars ? star : null);
                Root.gameObject.SetActive(true);
            }
        }
    }
}
