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
        private readonly EquipmentInventoryBinder equipment;
        private readonly Func<string, CancellationToken, Task> startTrial;
        private readonly List<Card> cards = new List<Card>();

        private BindingScope screen;
        private HeroInventorySnapshot snapshot;
        private HeroInventoryItem[] visible = Array.Empty<HeroInventoryItem>();
        private HeroInventoryItem selected;
        private FocusedRosterScroll focusedRoster;
        private Vector2 compactViewport;
        private RectTransform rosterStage;
        private float RosterCardStageHeight => Mathf.Min(rosterStage.rect.height, rosterStage.rect.width * (10f / 16f));
        private ScrollRect Scroll { get; set; }
        private bool roster, expanded, loading, failed;
        private Tab tab;
        private string classFilter;
        private bool trialPending;
        private string failedTrialHero;
        private HeroRosterPortrait portrait;
        private readonly HeroPortraitSources portraitSources;

        public HeroInventoryBinder(HeroInventory view, HomeHeroArt art, ILocaleService locale,
            Func<CancellationToken, Task<HeroInventorySnapshot>> fetch, Func<Task> goHome,
            ArtifactArtCatalog artifactArt = null, ArtifactInventoryApi artifactApi = null,
            Func<string, CancellationToken, Task> startTrial = null,
            Func<CancellationToken, Task<ArtifactInventorySnapshot>> fetchArtifacts = null, HeroPortraitSources portraitSources = null,
            EquipmentInventoryApi equipmentApi = null, Func<CancellationToken,Task<EquipmentInventorySnapshot>> readEquipment = null)
        {
            this.view = view;
            this.locale = locale;
            this.fetch = fetch;
            this.goHome = goHome;
            this.startTrial = startTrial;
            this.portraitSources = portraitSources;
            artwork = art.heroes.ToDictionary(item => item.id, StringComparer.Ordinal);
            star = art.star;
            if (artifactArt != null && artifactApi != null)
                artifacts = new ArtifactInventoryBinder(view.Artifacts, artifactApi, artifactArt, locale,
                    () => selected, ClassForTemplate, HeroNameForInstance, OnArtifactsChanged, fetchArtifacts);
            if (equipmentApi != null)
                equipment = new EquipmentInventoryBinder(view.Root.GetComponent<BindingMap>(),locale,equipmentApi,readEquipment,
                    () => snapshot?.heroes ?? Array.Empty<HeroInventoryItem>(), () => selected, EquipmentHeroName,
                    BaseStats, id => artifacts?.EquippedStats(id) ?? (0,0), EquipmentWearerPortrait, OnArtifactsChanged);
        }

        public Task OpenAsync(bool roster, BindingScope scope) => OpenAsync(roster, scope, null, null);

        public async Task OpenAsync(bool roster, BindingScope scope, HeroInventorySnapshot initialHeroes,
            ArtifactInventorySnapshot initialArtifacts, EquipmentInventorySnapshot initialEquipment = null)
        {
            screen = scope;
            this.roster = roster;
            expanded = false;
            tab = Tab.Heroes;
            classFilter = "all";
            view.Show();
            view.FitInventoryLayout(roster);
            Scroll = roster ? view.RosterScroll : view.BagScroll;
            if (roster)
            {
                rosterStage = view.RosterDetail;
                focusedRoster = FocusedRosterScroll.Bind(Scroll, scope, null, SelectFocusedInstance);
                Canvas.willRenderCanvases += OnRosterResized;
                scope.Add(() => Canvas.willRenderCanvases -= OnRosterResized);
                var currentPortrait = new HeroRosterPortrait(view.DetailPortrait, portraitSources);
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
            view.Back.BindBack(scope, () => equipment?.Back() == true || artifacts?.Back() == true ? Task.CompletedTask : expanded ? ToggleExpanded() : goHome());
            view.Expand.Bind(scope, _ => ToggleExpanded());
            view.Retry.Bind(scope, ReloadAsync);
            view.TabHeroes.Bind(scope, ct => SelectTab(Tab.Heroes, ct));
            view.TabEquipment.Bind(scope, ct => SelectTab(Tab.Equipment, ct));
            view.TabArtifacts.Bind(scope, ct => SelectTab(Tab.Artifacts, ct));
            foreach (string name in HeroInventory.ClassNames)
                view.ClassFilter(name).Bind(scope, _ => SelectClass(name));
            SetExpanded(false);
            artifacts?.Bind(scope);
            equipment?.Bind(scope);
            await Task.WhenAll(ReloadAsync(scope.Token,initialHeroes),
                artifacts?.PreloadAsync(scope.Token,initialArtifacts) ?? Task.CompletedTask,
                equipment?.LoadAsync(initialEquipment,scope.Token) ?? Task.CompletedTask);
        }

        private int Columns => roster && expanded ? 4 : 1;
        private float GridRowHeight => roster ? 142.5f : 217.5f;

        private Task ReloadAsync(CancellationToken token) => ReloadAsync(token, null);

        private async Task ReloadAsync(CancellationToken token, HeroInventorySnapshot initial)
        {
            if (initial != null)
            {
                snapshot = initial;
                failed = loading = false;
                Refresh(resetScroll: true);
                return;
            }
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
            if (next != Tab.Equipment) equipment?.Hide();
            Refresh(resetScroll: true);
            if (next == Tab.Artifacts && artifacts != null) await artifacts.ShowTabAsync(token);
            if (next == Tab.Equipment) equipment?.Show(false,null);
        }

        private string ClassForTemplate(string templateId) =>
            artwork.TryGetValue(templateId, out var item) ? item.heroClass : "";

        private string HeroNameForInstance(string instanceId)
        {
            var item = snapshot?.heroes?.FirstOrDefault(hero => hero.instanceId == instanceId);
            return item == null ? locale.Resolve(TextPrefix + "unknown_hero") : Name(item);
        }

        private Sprite EquipmentWearerPortrait(string instanceId)
        {
            var hero = snapshot?.heroes?.FirstOrDefault(item => item.instanceId == instanceId);
            return hero != null && artwork.TryGetValue(hero.templateId,out var art) ? art.portrait : null;
        }

        private string EquipmentHeroName(string instanceId)
        {
            var all = snapshot?.heroes ?? Array.Empty<HeroInventoryItem>();
            var hero = all.FirstOrDefault(h => h.instanceId == instanceId);
            if (hero == null) return HeroNameForInstance(instanceId);
            var same = all.Where(h => h.templateId == hero.templateId).OrderBy(h => h.instanceId,StringComparer.Ordinal).ToArray();
            return same.Length > 1 ? Name(hero) + " #" + (Array.IndexOf(same,hero) + 1) : Name(hero);
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
            if (roster)
            {
                focusedRoster.enabled = !enabled;
                if (enabled) Scroll.viewport.offsetMin = Vector2.zero;
            }
            view.RosterDetail.gameObject.SetActive(!expanded);
            view.RosterShading.gameObject.SetActive(!expanded);
            view.RosterSidebar.anchorMin = expanded ? new Vector2(.012f, 0) : new Vector2(.780f, .129f);
            view.RosterSidebar.anchorMax = expanded ? new Vector2(.820f, .810f) : new Vector2(1f, .840f);
            view.RosterCollapsedFooter.gameObject.SetActive(!expanded);
            view.RosterExpandedHint.SetActive(expanded);
            view.RosterExpandIcon.SetActive(!expanded);
            view.RosterCollapseIcon.SetActive(expanded);
            view.ExpandLabel.text = locale.Resolve(TextPrefix + (expanded ? "collapse" : "expand"));
        }

        private void OnSearch(string _) => Refresh(resetScroll: true);
        private void OnScroll(Vector2 _) => ShowCards();
        private void OnRosterResized()
        {
            view.FitInventoryLayout(true);
            if (!expanded && compactViewport != Scroll.viewport.rect.size) ShowCards();
        }
        private void SelectFocusedInstance(int index)
        {
            if (selected != visible[index]) Select(visible[index]);
        }
        private void Select(HeroInventoryItem hero)
        {
            selected = hero;
            if (expanded)
            {
                SetExpanded(false);
                Refresh(resetScroll: true, revealSelected: true);
                return;
            }
            focusedRoster.Focus(Array.IndexOf(visible, hero));
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
            view.EmptyEquipment.SetActive(!roster && tab == Tab.Equipment && equipment == null);
            view.EmptyArtifacts.SetActive(!roster && tab == Tab.Artifacts);
            string status = !heroes ? null
                : loading ? "loading" : failed ? "load_failed" : visible.Length == 0 ? "empty_heroes" : null;
            view.Status.gameObject.SetActive(status != null);
            if (status != null) view.Status.text = locale.Resolve(TextPrefix + status);
            view.Retry.gameObject.SetActive(heroes && failed);
            bool compact = roster && !expanded;
            if (compact)
                focusedRoster.LayoutCards(visible.Length, RosterCardStageHeight);
            else
                Scroll.content.sizeDelta = new Vector2(0f, Mathf.CeilToInt((float)visible.Length / Columns) * GridRowHeight);
            if (resetScroll)
            {
                Scroll.StopMovement();
                if (compact) focusedRoster.Focus(revealSelected ? Array.IndexOf(visible, selected) : 0);
                else Scroll.content.anchoredPosition = Vector2.zero;
            }
            SetDetail();
            ShowCards();
        }

        private void ShowCards()
        {
            int columns = Columns;
            bool compact = roster && !expanded;
            float height = compact ? focusedRoster.LayoutCards(visible.Length, RosterCardStageHeight) : GridRowHeight;
            if (compact) compactViewport = Scroll.viewport.rect.size;
            if (height <= 0f) return;
            float scrollY = Scroll.content.anchoredPosition.y - (compact ? focusedRoster.FocusLine : 0f);
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
                bool active = compact && itemIndex == selectedIndex;
                if (compact)
                    focusedRoster.PlaceCard(card.Root, itemIndex);
                else
                {
                    card.Root.anchorMin = new Vector2((float)column / columns, 1);
                    card.Root.anchorMax = new Vector2((float)(column + 1) / columns, 1);
                    card.Root.pivot = new Vector2(.5f, 1f);
                    card.Root.sizeDelta = new Vector2(roster ? -8f : 0f, roster ? height - 22.5f : height);
                    card.Root.anchoredPosition = new Vector2(0f, -(itemIndex / columns) * height);
                    card.Root.localScale = Vector3.one;
                }
                card.Display(item, Name(item), Level(item), art, star, active);
                if (!roster)
                {
                    card.DisplayArtifact(artifacts?.EquippedCard(item.instanceId) ?? (null, 0));
                    if (equipment != null) card.DisplayGear(item.instanceId,equipment);
                }
            }
            if (compact && count > 0) FocusedRosterScroll.RaiseNearest(count, focusedRoster.Nearest - first, RaiseCard);
        }

        private void RaiseCard(int index) => cards[index].Root.SetAsLastSibling();

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
            equipment?.PaintSlots(selected?.instanceId);
            PaintTrial();
            SetDetailStats();
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

        private BattleStats BaseStats(HeroInventoryItem hero)
        {
            if (hero == null || !artwork.TryGetValue(hero.templateId,out var art) || art.growth == null) return null;
            var growth = art.growth;
            if (hero.stars < growth.nativeStars || hero.level > (hero.stars == 1 ? 15 : hero.stars * 10)) return null;
            var preset = new HeroGrowthPreset(growth.id, growth.nativeStars, growth.attackCoefficientMilli,
                growth.healthCoefficientMilli, growth.defenseCoefficientMilli, growth.speedMilli);
            return HeroStatCalculator.Calculate(preset,hero.level,hero.stars,0,Array.Empty<HeroAwakeningStep>());
        }

        private void SetDetailStats()
        {
            var stats = BaseStats(selected);
            if (stats != null && equipment != null)
                stats = EquipmentStatCalculator.Calculate(stats,equipment.Pieces(selected.instanceId));
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
            private readonly EquipmentSlotBinder artifactSlot;
            private readonly EquipmentSlotBinder[] gearSlots;
            private readonly bool roster;

            internal readonly RectTransform Root;
            internal readonly GameButton Button;
            internal HeroInventoryItem Item { get; private set; }

            internal Card(GameObject instance, bool roster)
            {
                this.roster = roster;
                Root = (RectTransform)instance.transform;
                var map = instance.GetComponent<BindingMap>();
                name = map.Require<GameText>("name");
                portrait = map.Require<Image>("portrait");
                selection = map.Require<Image>("selection");
                element = map.Require<Image>("element");
                role = map.Require<Image>("role");
                stars = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                Button = instance.GetComponent<GameButton>();
                string prefix = roster ? "rosterLevel" : "bagLevel";
                firstLevelDigit = map.Require<Image>(prefix + "First");
                secondLevelDigit = map.Require<Image>(prefix + "Second");
                maximumLevel = map.Require<Image>(prefix + "Max");
                levelDigits = Enumerable.Range(0, 10).Select(index => map.Require<Image>(prefix + "Digit" + index).sprite).ToArray();
                if (!roster)
                {
                    level = map.Require<GameText>("level");
                    artifactSlot = new EquipmentSlotBinder(map.Require<BindingMap>("bagArtifactSlot"));
                    gearSlots = Enumerable.Range(0,6).Select(i => new EquipmentSlotBinder(map.Require<Image>("emptyGear" + i)
                        .GetComponentInParent<BindingMap>(true))).ToArray();
                }
            }

            internal void DisplayArtifact((Sprite Portrait, int Level) equipped)
            {
                artifactSlot.Set(equipped.Portrait, equipped.Level);
            }

            internal void DisplayGear(string heroId, EquipmentInventoryBinder gear)
            {
                for (int i = 0; i < gearSlots.Length; ++i)
                {
                    var (icon,level) = gear.Equipped(heroId,EquipmentInventoryBinder.Slots[i]);
                    gearSlots[i].Set(icon,level);
                }
            }

            internal void Display(HeroInventoryItem item, string title, string levelText, HomeHeroArt.Hero art, Sprite star,
                bool selected)
            {
                Item = item;
                Root.name = item.instanceId;
                name.text = title;
                if (level) level.text = levelText;
                SetSprite(portrait, roster && art?.cardPortrait ? art.cardPortrait : art?.portrait);
                if (roster && portrait.sprite)
                {
                    var rect = portrait.rectTransform;
                    rect.anchorMin = new Vector2(rect.anchorMax.x - portrait.sprite.rect.width / FocusedRosterScroll.CardSize.x,
                        rect.anchorMin.y);
                }
                bool max = item.level == (item.stars == 1 ? 15 : item.stars * 10);
                maximumLevel.enabled = max;
                // The bag reads from the left. The roster's rank anchor reads units from the right.
                bool tensFirst = !roster && item.level >= 10;
                int first = tensFirst ? item.level / 10 : item.level % 10;
                int second = tensFirst ? item.level % 10 : item.level / 10;
                SetSprite(firstLevelDigit, max ? null : levelDigits[first]);
                SetSprite(secondLevelDigit, !max && item.level >= 10 ? levelDigits[second] : null);
                SetSprite(element, roster && art?.cardElement ? art.cardElement : art?.element);
                SetSprite(role, art?.role);
                selection.enabled = selected;
                for (int index = 0; index < stars.Length; ++index)
                    SetSprite(stars[index], index < item.stars ? star : null);
                Root.gameObject.SetActive(true);
            }
        }
    }
}
