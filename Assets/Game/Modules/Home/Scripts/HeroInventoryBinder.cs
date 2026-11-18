using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Battle;
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
        private readonly ArtifactInventoryBinder artifacts;
        private readonly Func<string, CancellationToken, Task> startTrial;
        private readonly List<Card> cards = new List<Card>();

        private BindingScope screen;
        private HeroInventorySnapshot snapshot;
        private HeroInventoryItem[] visible = Array.Empty<HeroInventoryItem>();
        private HeroInventoryItem selected;
        private bool roster, expanded, loading, failed;
        private Tab tab;
        private string classFilter;
        private bool trialPending;
        private string failedTrialHero;
        private HeroRosterPortrait portrait;

        public HeroInventoryBinder(HeroInventory view, HomeHeroArt art, ILocaleService locale,
            Func<CancellationToken, Task<HeroInventorySnapshot>> fetch, Func<Task> goHome,
            ArtifactArtCatalog artifactArt = null, ArtifactInventoryApi artifactApi = null,
            Func<string, CancellationToken, Task> startTrial = null)
        {
            this.view = view;
            this.locale = locale;
            this.fetch = fetch;
            this.goHome = goHome;
            this.startTrial = startTrial;
            artwork = art.heroes.ToDictionary(item => item.id, StringComparer.Ordinal);
            star = art.star;
            if (artifactArt != null && artifactApi != null)
                artifacts = new ArtifactInventoryBinder(view.Artifacts, artifactApi, artifactArt, locale,
                    () => selected, ClassForTemplate, HeroNameForInstance, OnArtifactsChanged);
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
            view.SearchHint.Bind(locale, scope, TextPrefix + "roster_search");
            view.RosterSortCaption.Bind(locale, scope, TextPrefix + "roster_sort_level");
            view.BagCaption("Sort").Bind(locale, scope, TextPrefix + "sort_level");
            view.BagCaption("EquipmentSort").Bind(locale, scope, TextPrefix + "sort_equipment_score");
            view.BagCaption("ArtifactSort").Bind(locale, scope, TextPrefix + "sort_upgrade");
            view.BagCaption("EquipmentEmpty").Bind(locale, scope, TextPrefix + "empty_equipment");
            view.BagCaption("ArtifactEmpty").Bind(locale, scope, TextPrefix + "empty_artifacts");
            view.BagCaption("EquipmentOrganize").Bind(locale, scope, TextPrefix + "manage");
            view.BagCaption("ArtifactOrganize").Bind(locale, scope, TextPrefix + "organize");
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
            var trialButton = view.ArtifactTrial;
            trialButton.gameObject.SetActive(roster && startTrial != null);
            if (roster && startTrial != null)
            {
                view.ArtifactTrialLabel.text = locale.Resolve(TextPrefix + "artifact_trial");
                view.ArtifactTrialStatus.text = locale.Resolve(TextPrefix + "artifact_trial_failed");
                PaintTrial();
                trialButton.onClick.AddListener(StartTrial);
                scope.Add(() => { if (trialButton) trialButton.onClick.RemoveListener(StartTrial); });
            }
            view.Back.Bind(scope, _ => artifacts?.Back() == true ? Task.CompletedTask : expanded ? ToggleExpanded() : goHome());
            view.Expand.Bind(scope, _ => ToggleExpanded());
            view.Retry.Bind(scope, ReloadAsync);
            view.TabHeroes.Bind(scope, ct => SelectTab(Tab.Heroes, ct));
            view.TabEquipment.Bind(scope, ct => SelectTab(Tab.Equipment, ct));
            view.TabArtifacts.Bind(scope, ct => SelectTab(Tab.Artifacts, ct));
            foreach (string name in HeroInventory.ClassNames)
                view.ClassFilter(name).Bind(scope, _ => SelectClass(name));
            SetExpanded(false);
            if (artifacts != null)
            {
                artifacts.Bind(scope);
                await Task.WhenAll(ReloadAsync(scope.Token), artifacts.PreloadAsync(scope.Token));
            }
            else await ReloadAsync(scope.Token);
        }

        private ScrollRect Scroll => roster ? view.RosterScroll : view.BagScroll;
        private int Columns => roster && expanded ? 4 : 1;
        private float RowHeight => roster ? (expanded ? 142.5f : 86f) : 217.5f;
        private float CompactInset => roster && !expanded ? 145f : 0f;

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

        private async Task SelectTab(Tab next, CancellationToken token)
        {
            if (tab == next) return;
            tab = next;
            classFilter = "all";
            if (next != Tab.Artifacts) artifacts?.HideTab();
            Refresh(resetScroll: true);
            if (next == Tab.Artifacts && artifacts != null) await artifacts.ShowTabAsync(token);
        }

        private string ClassForTemplate(string templateId) =>
            artwork.TryGetValue(templateId, out var item) ? item.heroClass : "";

        private string HeroNameForInstance(string instanceId)
        {
            var item = snapshot?.heroes?.FirstOrDefault(hero => hero.instanceId == instanceId);
            return item == null ? locale.Resolve(TextPrefix + "unknown_hero") : Name(item);
        }

        private void OnArtifactsChanged()
        {
            SetDetail();
            ShowCards();
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
            Refresh(resetScroll: true, revealSelected: !expanded);
            return Task.CompletedTask;
        }

        private void SetExpanded(bool enabled)
        {
            expanded = enabled;
            view.RosterDetail.gameObject.SetActive(!expanded);
            view.RosterSidebar.anchorMin = expanded ? new Vector2(.012f, 0) : new Vector2(.780f, .129f);
            view.RosterSidebar.anchorMax = new Vector2(expanded ? .820f : 1f, .810f);
            view.RosterCollapsedFooter.gameObject.SetActive(!expanded);
            view.RosterExpandedHint.SetActive(expanded);
            view.RosterExpandIcon.SetActive(!expanded);
            view.RosterCollapseIcon.SetActive(expanded);
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
                Refresh(resetScroll: true, revealSelected: true);
                return;
            }
            SetDetail();
            ShowCards();
        }

        private void Refresh(bool resetScroll, bool revealSelected = false)
        {
            bool heroes = roster || tab == Tab.Heroes;
            string query = roster ? view.Search.text.Trim() : "";
            visible = !heroes || snapshot?.heroes == null
                ? Array.Empty<HeroInventoryItem>()
                : snapshot.heroes.Where(item =>
                    (query.Length == 0 || Name(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (roster || classFilter == "all" || artwork.TryGetValue(item.templateId, out var art) && art.heroClass == classFilter))
                    .OrderByDescending(item => item.level)
                    .ToArray();
            selected = visible.FirstOrDefault(item => item.instanceId == selected?.instanceId) ?? visible.FirstOrDefault();
            view.Title.text = roster && selected != null ? Name(selected) : locale.Resolve(TextPrefix + (roster ? "heroes" : "bag"));
            var capacity = roster ? view.RosterCapacity : view.Capacity;
            capacity.gameObject.SetActive(heroes && snapshot != null);
            if (heroes && snapshot != null)
                capacity.text = GameText.Format(locale, TextPrefix + "capacity", snapshot.used, snapshot.capacity);

            view.TabHeroes.interactable = tab != Tab.Heroes;
            view.TabEquipment.interactable = tab != Tab.Equipment;
            view.TabArtifacts.interactable = tab != Tab.Artifacts;
            view.TabSelection("heroes").gameObject.SetActive(tab == Tab.Heroes);
            view.TabSelection("equipment").gameObject.SetActive(tab == Tab.Equipment);
            view.TabSelection("artifacts").gameObject.SetActive(tab == Tab.Artifacts);
            view.BagFilter.SetActive(!roster && heroes);
            foreach (string name in HeroInventory.ClassNames)
                view.ClassSelection(name).gameObject.SetActive(name == classFilter);
            view.BagScroll.gameObject.SetActive(!roster && heroes);
            view.EmptyEquipment.SetActive(!roster && tab == Tab.Equipment);
            view.EmptyArtifacts.SetActive(!roster && tab == Tab.Artifacts);
            string status = !heroes ? null
                : loading ? "loading" : failed ? "load_failed" : visible.Length == 0 ? "empty_heroes" : null;
            view.Status.gameObject.SetActive(status != null);
            if (status != null) view.Status.text = locale.Resolve(TextPrefix + status);
            view.Retry.gameObject.SetActive(heroes && failed);
            float overlapTail = roster && !expanded && visible.Length != 0 ? 48f : 0f;
            Scroll.content.sizeDelta = new Vector2(0f, CompactInset +
                Mathf.CeilToInt((float)visible.Length / Columns) * RowHeight + overlapTail);
            if (resetScroll)
            {
                Scroll.StopMovement();
                float offset = 0;
                if (revealSelected && selected != null)
                {
                    Canvas.ForceUpdateCanvases();
                    float maxOffset = Mathf.Max(0, Scroll.content.rect.height - Scroll.viewport.rect.height);
                    offset = Mathf.Min(Array.IndexOf(visible, selected) * RowHeight, maxOffset);
                }
                Scroll.content.anchoredPosition = new Vector2(0, offset);
            }
            SetDetail();
            ShowCards();
        }

        private void ShowCards()
        {
            int columns = Columns;
            float height = RowHeight;
            bool compact = roster && !expanded;
            float scrollY = Scroll.content.anchoredPosition.y - CompactInset;
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / height) - (compact ? 1 : 0));
            int viewRows = Mathf.CeilToInt(Mathf.Max(Scroll.viewport.rect.height, 1080f) / height) + 2;
            int first = firstRow * columns;
            int count = Mathf.Clamp(visible.Length - first, 0, viewRows * columns);
            while (cards.Count < count)
            {
                var card = new Card(UnityEngine.Object.Instantiate(roster ? view.RosterRow : view.BagRow, Scroll.content, false), roster);
                if (roster) card.Button.onClick.AddListener(() => Select(card.Item));
                cards.Add(card);
            }
            int selectedIndex = Array.IndexOf(visible, selected);
            RectTransform selectedCard = null;
            for (int index = cards.Count - 1; index >= 0; --index)
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
                bool active = compact && itemIndex == selectedIndex;
                float visualHeight = compact ? (active ? 134f : 111f) : roster ? height - 22.5f : height;
                float x = compact ? Mathf.Min(Mathf.Abs(itemIndex - selectedIndex), 3) * 27f : 0f;
                card.Root.sizeDelta = new Vector2(roster ? -8f : 0f, visualHeight);
                card.Root.anchoredPosition = new Vector2(x, -CompactInset - (itemIndex / columns) * height);
                card.Display(item, Name(item), Level(item), art, star, active);
                if (!roster) card.DisplayArtifact(artifacts?.EquippedCard(item.instanceId) ?? (null, 0));
                if (compact)
                {
                    card.Root.SetAsLastSibling();
                    if (active) selectedCard = card.Root;
                }
            }
            if (selectedCard) selectedCard.SetAsLastSibling();
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
            SetSprite(view.DetailZodiac, art?.zodiacIcon);
            view.DetailElementName.text = DetailLabel("element", art?.elementId);
            view.DetailRoleName.text = DetailLabel("role", art?.heroClass);
            view.DetailZodiacName.text = DetailLabel("zodiac", art?.zodiac);
            for (int index = 0; index < 6; ++index)
                SetSprite(view.DetailStar(index), selected != null && index < selected.stars ? star : null);
            artifacts?.PaintSlot(selected);
            PaintTrial();
            SetDetailStats(art?.growth);
        }

        private void PaintTrial()
        {
            if (!roster || startTrial == null) return;
            string id = selected?.instanceId;
            view.ArtifactTrial.interactable = id != null && !trialPending;
            view.ArtifactTrialStatus.enabled = id != null && failedTrialHero == id;
        }

        private async void StartTrial()
        {
            if (screen == null || selected == null || trialPending || !view.ArtifactTrial.IsInteractable()) return;
            var scope = screen;
            string hero = selected.instanceId;
            trialPending = true;
            failedTrialHero = null;
            PaintTrial();
            try { await startTrial(hero, scope.Token); }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception error)
            {
                failedTrialHero = hero;
                Debug.LogWarning("Artifact trial could not start: " + error.GetType().Name);
            }
            finally
            {
                trialPending = false;
                if (screen == scope) PaintTrial();
            }
        }

        private string DetailLabel(string category, string value) => string.IsNullOrEmpty(value)
            ? "" : locale.Resolve(TextPrefix + category + "." + value);

        private void SetDetailStats(BattleCatalogData.GrowthRow growth)
        {
            BattleStats stats = null;
            if (selected != null && growth != null && selected.stars >= growth.nativeStars &&
                selected.level <= (selected.stars == 1 ? 15 : selected.stars * 10))
            {
                var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
                    growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
                stats = HeroStatCalculator.Calculate(preset, selected.level, selected.stars, 0,
                    Array.Empty<HeroAwakeningStep>());
            }
            var (artifactAttack, artifactHealth) = selected == null
                ? (0, 0) : artifacts?.EquippedStats(selected.instanceId) ?? (0, 0);
            view.DetailStat("attack").text = StatNumber(stats?.Attack + artifactAttack);
            view.DetailStat("defense").text = StatNumber(stats?.Defense);
            view.DetailStat("health").text = StatNumber(stats?.MaxHealth + artifactHealth);
            view.DetailStat("speed").text = StatNumber(stats?.Speed);
            view.DetailStat("critChance").text = StatPercent(stats?.CritChanceBp);
            view.DetailStat("critDamage").text = StatPercent(stats?.CritDamageBp);
            view.DetailStat("effectiveness").text = StatPercent(stats?.EffectivenessBp);
            view.DetailStat("resistance").text = StatPercent(stats?.EffectResistanceBp);
        }

        private static string StatNumber(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        private static string StatPercent(int? basisPoints) =>
            basisPoints == null ? "" : (basisPoints.Value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

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
            private readonly Image firstLevelDigit, secondLevelDigit, maximumLevel;
            private readonly Sprite[] levelDigits;
            private readonly Image artifact;
            private readonly GameText artifactLevel;

            internal readonly RectTransform Root;
            internal readonly GameButton Button;
            internal HeroInventoryItem Item { get; private set; }

            internal Card(GameObject instance, bool roster)
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
                if (!roster)
                {
                    firstLevelDigit = map.Require<Image>("bagLevelFirst");
                    secondLevelDigit = map.Require<Image>("bagLevelSecond");
                    maximumLevel = map.Require<Image>("bagLevelMax");
                    artifact = map.Require<Image>("bagEquippedArtifact");
                    artifactLevel = map.Require<GameText>("bagEquippedArtifactLevel");
                    levelDigits = Enumerable.Range(0, 10).Select(index => map.Require<Image>("bagLevelDigit" + index).sprite).ToArray();
                }
            }

            internal void DisplayArtifact((Sprite Portrait, int Level) equipped)
            {
                SetSprite(artifact, equipped.Portrait);
                artifactLevel.text = equipped.Portrait ? "+" + equipped.Level.ToString(CultureInfo.InvariantCulture) : "";
            }

            internal void Display(HeroInventoryItem item, string title, string levelText, HomeHeroArt.Hero art, Sprite star,
                bool selected)
            {
                Item = item;
                Root.name = item.instanceId;
                name.text = title;
                level.text = levelText;
                SetSprite(portrait, levelDigits == null && art?.cardPortrait ? art.cardPortrait : art?.portrait);
                if (levelDigits != null)
                {
                    bool max = item.level == (item.stars == 1 ? 15 : item.stars * 10);
                    maximumLevel.enabled = max;
                    SetSprite(firstLevelDigit, max ? null : levelDigits[item.level >= 10 ? item.level / 10 : item.level]);
                    SetSprite(secondLevelDigit, !max && item.level >= 10 ? levelDigits[item.level % 10] : null);
                }
                SetSprite(element, levelDigits == null && art?.cardElement ? art.cardElement : art?.element);
                SetSprite(role, art?.role);
                selection.enabled = selected;
                for (int index = 0; index < stars.Length; ++index)
                    SetSprite(stars[index], index < item.stars ? star : null);
                Root.gameObject.SetActive(true);
            }
        }
    }
}
