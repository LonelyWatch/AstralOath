using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Summon
{
    public sealed partial class SummonWindowBinder
    {
        private const string Prefix = "summon.";
        private const string Covenant = "covenant";
        private const string Up = "iseria";
        private const string Bookmarks = "bookmarks";
        private const string EpicBookmarks = "epic_bookmarks";
        private const string Free = "free";

        private readonly SummonWindow view;
        private readonly SummonApi api;
        private readonly Dictionary<string, HomeHeroArt.Hero> heroes;
        private readonly ArtifactArtCatalog artifactArt;
        private readonly SummonPresentation presentation;
        private readonly SummonSkillLoop featuredLoop;
        private readonly ILocaleService locale;
        private readonly Func<Task> goHome;
        private readonly HashSet<string> wishHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> wishArtifacts = new HashSet<string>(StringComparer.Ordinal);

        private BindingScope screen;
        private GameToastBinder notice;
        private HeroRosterPortrait featuredPortrait, revealPortrait;
        private SummonReward displayedReward;
        private SummonSkillLoop.Player featuredSkill;
        private UIPageLayers layers;
        private string wishRoleFilter;
        private HeroRosterPortrait[] wishPortraits;
        private SummonPresentation.Player player;
        private SummonState state;
        private SummonRequest quote;
        private SummonReceipt lastReceipt;
        private SummonStorage storage;
        private CancellationTokenSource playback;
        private TaskCompletionSource<bool> next;
        private string bannerId = Up;
        private bool busy, epic, wantsArtifacts, onlyUnowned, showingDetail;

        public SummonWindowBinder(SummonWindow view, SummonApi api, HomeHeroArt heroArt,
            ArtifactArtCatalog artifactArt, SummonPresentation presentation, SummonSkillLoop featuredLoop, ILocaleService locale, Func<Task> goHome)
        {
            this.view = view;
            this.api = api;
            this.artifactArt = artifactArt;
            this.presentation = presentation;
            this.featuredLoop = featuredLoop;
            this.locale = locale;
            this.goHome = goHome;
            heroes = heroArt.heroes.ToDictionary(hero => hero.id, StringComparer.Ordinal);
        }

        public Task OpenAsync(BindingScope scope) => OpenAsync(scope, null);

        public async Task OpenAsync(BindingScope scope, SummonState initial)
        {
            screen = scope;
            notice = new GameToastBinder(GameToast.ForInstance(view.Panel("noticeToast")));
            WatchBannerSchedule(scope);
            view.Show();
            view.BindCardLayout(scope);
            layers = new UIPageLayers(
                new[] { view.Panel("mainPage"), view.Panel("resultPanel"), view.Panel("revealPanel") },
                new[] { view.Panel("confirmPanel"), view.Panel("infoPanel"), view.Panel("wishPanel"), view.Panel("storagePanel"), view.Panel("settingsPanel") });
            ShowPanel(null);
            player = presentation.CreatePlayer(view.Effects, view.Audio);
            featuredSkill = featuredLoop.CreatePlayer(view.Raw("featuredSkillCanvas"), view.FeaturedSkillAudio, scope,
                view.Group("featuredScenery"), view.Group("featuredActorLayer"),
                view.Group("featuredOfferLayer"));
            featuredPortrait = new HeroRosterPortrait(view.Raw("upPortrait"), renderSize: 1024, positionOffset: 2000, framing: 1.04f, headFocus: .23f);
            revealPortrait = new HeroRosterPortrait(view.Raw("revealHero"), renderSize: 1024, positionOffset: 3000, framing: .90f, headFocus: .26f);
            wishPortraits = Enumerable.Range(0, 7).Select(index => new HeroRosterPortrait(view.Raw("wishHero" + index),
                renderSize: 480, animated: true, positionOffset: 4000 + index * 1000, framing: .83f, headFocus: .30f)).ToArray();
            Canvas.willRenderCanvases += featuredPortrait.Tick;
            Canvas.willRenderCanvases += revealPortrait.Tick;
            foreach (var portrait in wishPortraits) Canvas.willRenderCanvases += portrait.Tick;
            scope.Add(() =>
            {
                playback?.Cancel();
                Canvas.willRenderCanvases -= featuredPortrait.Tick;
                Canvas.willRenderCanvases -= revealPortrait.Tick;
                featuredPortrait.Dispose();
                revealPortrait.Dispose();
                foreach (var portrait in wishPortraits)
                {
                    Canvas.willRenderCanvases -= portrait.Tick;
                    portrait.Dispose();
                }
                player.Dispose();
                if (!view.Root) return;
                foreach (string list in new[] { "infoHeroColumn", "infoArtifactColumn", "wishItems", "wishSelectedHeroes", "wishSelectedArtifacts", "storageItems", "resultItems" })
                    Clear(list);
                if (view.Root) view.Hide();
            });

            view.Button("back").BindBack(scope, () => busy ? Task.CompletedTask : layers.BackAsync(goHome));
            Hook("covenant", _ => ChooseBanner(Covenant));
            Hook("iseria", _ => ChooseBanner(Up));
            Hook("single", _ => Quote(1, epic ? EpicBookmarks : Bookmarks));
            Hook("ten", _ => Quote(10, epic ? EpicBookmarks : Bookmarks));
            Hook("free", _ => Quote(1, Free));
            Hook("epic", _ => { epic = !epic; RenderMain(); return Task.CompletedTask; });
            Hook("info", _ => ShowInfo());
            Hook("infoNotesTab", _ => ShowRateTab(false));
            Hook("infoRatesTab", _ => ShowRateTab(true));
            Hook("wish", _ => ShowWishes());
            Hook("help", _ => ShowInfo());
            Hook("storage", OpenStorage);
            Hook("settings", _ => { ShowPanel("settingsPanel"); return Task.CompletedTask; });
            Hook("previewToggle", _ =>
            {
                PreviewEnabled = !PreviewEnabled;
                RefreshSummonSettings();
                UpdateFeaturedSkill();
                return Task.CompletedTask;
            });
            Hook("settingsClose", ClosePanel);
            Hook("recovery", Recover);
            RefreshSummonSettings();
            Hook("confirmYes", Confirm);
            Hook("confirmNo", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("infoClose", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("wishHero", _ => ShowWishCategory(false));
            Hook("wishArtifact", _ => ShowWishCategory(true));
            Hook("wishSave", SaveWishes);
            Hook("wishUnowned", _ => { onlyUnowned = !onlyUnowned; return ShowWishCategory(wantsArtifacts); });
            Hook("wishClose", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("wishDismiss", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("wishInfo", _ => ToggleWishOverlay("wishHelp"));
            Hook("wishFilter", _ => ToggleWishOverlay("wishFilters"));
            foreach (string role in new[] { "all", "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" })
                Hook("wishRole_" + role, _ =>
                {
                    wishRoleFilter = role == "all" ? null : role;
                    return ShowWishCategory(wantsArtifacts);
                });
            Hook("wishRecommend", _ =>
            {
                wishHeroes.Clear();
                wishArtifacts.Clear();
                wishHeroes.UnionWith(state.wishHeroCandidates.Take(7));
                wishArtifacts.UnionWith(state.wishArtifactCandidates.Take(7));
                return ShowWishCategory(wantsArtifacts);
            });
            Hook("storageClose", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("resultClose", _ => layers.BackAsync(() => ClosePanel(screen.Token)));
            Hook("resultAgain", _ => Quote(lastReceipt.count, RepeatPayment(lastReceipt)));
            Hook("skip", _ => { playback?.Cancel(); return Task.CompletedTask; }, allowBusy: true);
            Hook("next", Next, allowBusy: true);
            Hook("revealDismiss", _ => { CloseRewardDialogue(); return Task.CompletedTask; }, allowBusy: true);
            Hook("revealDetails", _ => ShowRewardDialogue(voice: false), allowBusy: true);
            Hook("revealVoice", _ => ShowRewardDialogue(voice: true), allowBusy: true);

            view.Text("title").text = L("title");
            view.Text("infoTitle").text = L("info.title");
            view.Text("wishTitle").text = L("wish.title");
            view.Text("resultTitle").text = L("result.title");
            view.Text("skipLabel").text = L("reveal.skip");
            if (view.Music) player.PlayTheme(view.Music, ceremony: false);
            SetStatus("status.loading");
            await ReadState(scope.Token, initial);
            if (api.Pending != null && !scope.Token.IsCancellationRequested)
                SetStatus("status.recover");
        }

        private SummonBanner Banner => state?.banners?.FirstOrDefault(item => item.id == bannerId);

        private async Task ReadState(CancellationToken token, SummonState initial = null)
        {
            try
            {
                state = initial ?? await api.GetAsync(token);
                token.ThrowIfCancellationRequested();
                if (Banner == null)
                    bannerId = state.banners?.FirstOrDefault()?.id ?? Covenant;
                SetStatus(null);
            }
            catch (IOException error) when (!token.IsCancellationRequested)
            {
                Debug.LogWarning("Could not read summon state: " + error.GetType().Name + ". " + error.Message);
                SetStatus("status.error");
            }
            RenderMain();
        }

        private Task ChooseBanner(string id)
        {
            if (state?.banners?.Any(candidate => candidate.id == id) == true)
            {
                bannerId = id;
                if (Banner?.allowsEpicBookmarks != true) epic = false;
                SetStatus(null);
                RenderMain();
            }
            return Task.CompletedTask;
        }

        private void RenderMain()
        {
            RefreshBannerSchedule();
            var banner = Banner;
            void Balance(string key, int? value) =>
                view.Text(key).text = value?.ToString("#,0", CultureInfo.InvariantCulture) ?? "—";
            Balance("balance", state?.bookmarks);
            Balance("transmitBalance", state?.goldTransmitStones);
            Balance("moonBalance", state?.galaxyBookmarks);
            Balance("mysticBalance", state?.mysticMedals);
            Balance("skyBalance", state?.skystones);
            view.Text("bannerTitle").text = banner == null ? "" : bannerId == Covenant ? banner.title : L("banner.pickup_tag");
            view.Text("featuredHero").text = string.IsNullOrEmpty(banner?.featuredHero) ? "" : Name("hero", banner.featuredHero);
            view.Text("featuredHeroShadow").text = view.Text("featuredHero").text;
            view.Text("featuredArtifact").text = string.IsNullOrEmpty(banner?.featuredArtifact) ? "" : Name("artifact", banner.featuredArtifact);
            view.Text("pity").text = banner?.guaranteeAfter > 0 ?
                F("remaining", Math.Max(0, banner.guaranteeAfter - banner.misses)) : "";

            bool covenant = bannerId == Covenant;
            view.Panel("covenantBackdrop").SetActive(covenant);
            view.Panel("upBackdrop").SetActive(!covenant);
            view.Panel("wishRoster").SetActive(covenant);
            view.Text("bannerTitle").gameObject.SetActive(!covenant);
            bool selectedUp = !covenant && !string.IsNullOrEmpty(banner?.featuredHero);
            HomeHeroArt.Hero featured = null;
            if (selectedUp && heroes.TryGetValue(banner.featuredHero, out featured)) featuredPortrait.Show(featured);
            else featuredPortrait.SetVisible(false);
            view.Panel("featuredBadges").SetActive(featured != null);
            SetImage(view.Image("featuredElement"), featured?.element);
            SetImage(view.Image("featuredRole"), featured?.role);
            var artifact = covenant ? null : artifactArt.Find(banner?.featuredArtifact);
            SetImage(view.Image("artifactArt"), artifact?.card);
            SetImage(view.Image("artifactEmblem"), artifact?.combatIcon);
            var artifactRole = artifact == null ? null :
                heroes.Values.FirstOrDefault(hero => hero.heroClass == artifact.template.HeroClass)?.role;
            var roleIcon = view.Image("artifactRole");
            SetImage(roleIcon, artifactRole);
            roleIcon.gameObject.SetActive(artifactRole != null);
            for (int star = 0; star < 6; star++)
                view.Panel("artifactStar" + star).SetActive(artifact != null && star < artifact.template.Stars);
            view.Panel("artifactFrame").SetActive(artifact != null && artifact.card);
            StyleBanner(Covenant, covenant);
            StyleBanner(Up, !covenant && banner != null);
            UpdateFeaturedSkill();
            RenderWishRoster(covenant);

            int perDraw = banner?.bookmarksPerDraw ?? 0;
            view.Text("singleLabel").text = L("draw.single");
            view.Text("tenLabel").text = L("draw.ten");
            view.Text("singlePrice").text = perDraw.ToString(CultureInfo.InvariantCulture);
            view.Text("tenPrice").text = (perDraw * 10).ToString(CultureInfo.InvariantCulture);
            SetImage(view.Image("singleCoin"), view.Coin(epic));
            SetImage(view.Image("tenCoin"), view.Coin(epic));
            view.Text("freeLabel").text = L("free");

            bool usable = banner != null && !busy && api.Pending == null;
            view.Button("single").interactable = usable;
            view.Button("ten").interactable = usable;
            view.Button("free").interactable = usable && banner.dailyFree && state.freeAvailable;
            view.Button("epic").gameObject.SetActive(banner?.allowsEpicBookmarks == true);
            view.Button("epic").interactable = usable && banner.allowsEpicBookmarks;
            view.Button("free").gameObject.SetActive(banner?.dailyFree == true && state.freeAvailable);
            view.Button("info").interactable = banner != null;
            view.Button("help").interactable = banner != null;
            view.Button("wish").gameObject.SetActive(covenant);
            view.Button("wish").interactable = banner?.usesWishlist == true;
            int wishCount = (state?.wishHeroes?.Length ?? 0) + (state?.wishArtifacts?.Length ?? 0);
            string countText = wishCount.ToString(CultureInfo.CurrentCulture);
            view.Text("wishButtonLabel").text = F("wish.total", wishCount == 14 ? "<color=#9DCC49>" + countText + "</color>" : countText);
            view.Button("covenant").interactable = state?.banners?.Any(item => item.id == Covenant) == true;
            view.Button("iseria").interactable = state?.banners?.Any(item => item.id == Up) == true;
        }

        private void StyleBanner(string id, bool selected)
        {
            var button = view.Button(id);
            button.transform.localScale = Vector3.one * (selected ? 1.1f : 1f);
            view.Image(id + "Poster").color = selected ? Color.white : new Color32(160, 160, 160, 255);
            view.Panel(id + "Selected").SetActive(selected);
            if (id != Up) return;
            string hero = state?.banners?.FirstOrDefault(item => item.id == id)?.featuredHero;
            bool ownershipKnown = state?.wishHeroCandidates?.Contains(hero) == true && state.ownedWishIds != null;
            view.Panel("pickupUnowned").SetActive(ownershipKnown && !state.ownedWishIds.Contains(hero));
        }

        private bool PreviewEnabled
        {
            get => PlayerPrefs.GetInt("Summon.FeaturedSkillPreview", 1) != 0;
            set { PlayerPrefs.SetInt("Summon.FeaturedSkillPreview", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private void RefreshSummonSettings() =>
            view.Text("previewState").text = L(PreviewEnabled ? "settings.preview_on" : "settings.preview_off");

        private void UpdateFeaturedSkill() =>
            featuredSkill?.SetState(PreviewEnabled && bannerId == Up && Banner != null, !view.Panel("mainPage").activeSelf);

        private void RenderWishRoster(bool visible)
        {
            var wish = state?.wishHeroes ?? Array.Empty<string>();
            var owned = state?.ownedWishIds ?? Array.Empty<string>();
            for (int index = 0; index < wishPortraits.Length; index++)
            {
                string key = "wishHero" + index;
                var card = view.Panel(key + "Card");
                if (!visible || index >= wish.Length || !heroes.TryGetValue(wish[index], out var selected))
                {
                    card.SetActive(false);
                    wishPortraits[index].SetVisible(false);
                    continue;
                }
                card.SetActive(true);
                wishPortraits[index].Show(selected);
                var ownership = view.Text(key + "Owned");
                ownership.richText = true;
                ownership.text = "<i angle=18>" + L("wish.unowned_badge") + "</i>";
                ownership.transform.parent.gameObject.SetActive(!owned.Contains(selected.id));
                var name = view.Text(key + "Name");
                name.richText = true;
                // gacha_wish.csb uses a local ten-degree skew. Keep the main font's
                // default italic angle intact for every other screen.
                name.text = "<i angle=18>" + selected.name + "</i>";
                SetImage(view.Image(key + "Role"), selected.role);
                SetImage(view.Image(key + "Element"), selected.element);
            }
        }

        private Task Quote(int count, string payment)
        {
            var banner = Banner;
            if (banner == null || api.Pending != null)
            {
                SetStatus(api.Pending == null ? "status.unavailable" : "status.recover");
                return Task.CompletedTask;
            }
            if (payment == Free && (!banner.dailyFree || !state.freeAvailable))
            {
                SetStatus("status.unavailable");
                return Task.CompletedTask;
            }
            int cost = payment == Free ? 0 : banner.bookmarksPerDraw * count;
            int balance = Balance(payment);
            quote = new SummonRequest
            {
                requestId = Guid.NewGuid().ToString("N"), bannerId = banner.id, revision = banner.revision,
                count = count, payment = payment
            };
            view.Text("confirmTitle").text = L(count == 10 ? "confirm.ten" : "confirm.single");
            string currency = L("payment." + payment);
            view.Text("confirmPrompt").text = F(count == 10 ? "confirm.prompt_ten" : "confirm.prompt_single", currency);
            view.Text("confirmCurrency").text = currency;
            view.Text("confirmCost").text = cost.ToString(CultureInfo.InvariantCulture);
            view.Text("confirmBody").text = balance < cost ? L("status.insufficient") : F("confirm.owned", currency, balance);
            SetImage(view.Image("confirmCoin"), payment == Free ? null : view.Coin(payment == EpicBookmarks));
            bool showFeatured = !string.IsNullOrEmpty(banner.featuredHero);
            view.Panel("confirmHeroRow").SetActive(showFeatured);
            SetImage(view.Image("confirmHeroPortrait"), showFeatured && heroes.TryGetValue(banner.featuredHero, out var previewHero) ? previewHero.portrait : null);
            view.Text("confirmHeroName").text = showFeatured ? Name("hero", banner.featuredHero) : "";
            view.Text("confirmHeroPity").text = showFeatured ?
                F("remaining", Math.Max(0, banner.guaranteeAfter - banner.misses)) : "";
            view.LayoutConfirmation(showFeatured);
            ShowPanel("confirmPanel");
            return Task.CompletedTask;
        }

        private async Task Confirm(CancellationToken token)
        {
            var request = quote;
            if (request == null) return;
            if (request.payment != Free && Banner != null && Balance(request.payment) < Banner.bookmarksPerDraw * request.count)
            {
                notice.Show(L("status.insufficient"), screen);
                return;
            }
            quote = null;
            api.Pending = request;
            ReturnFromConfirmation();
            await DrawPending(request, token);
        }

        private int Balance(string payment) => payment == EpicBookmarks ? state.epicBookmarks : payment == Free ? 1 : state.bookmarks;

        private async Task DrawPending(SummonRequest request, CancellationToken token)
        {
            try
            {
                var receipt = await api.DrawAsync(request, token);
                await ShowReceipt(receipt, token);
            }
            catch (SummonRequestException error) when (error.Status >= 400 && error.Status < 500)
            {
                // The server returned a definitive rejection. A new quote must reflect the new state.
                api.Pending = null;
                await ReadState(token);
                if (error.Code == "insufficient_bookmarks")
                {
                    await Quote(request.count, request.payment);
                    notice.Show(L("status.insufficient"), screen);
                    return;
                }
                SetStatus(error.Status == 409 ? "status.changed" : "status.rejected");
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                SetStatus("status.recover");
            }
        }

        private async Task Recover(CancellationToken token)
        {
            try
            {
                var pending = api.Pending;
                var receipt = pending == null ? await api.LatestAsync(token) : await api.ReceiptAsync(pending.requestId, token);
                if (receipt != null)
                    await ShowReceipt(receipt, token, recovered: true);
                else if (pending != null)
                    await DrawPending(pending, token);
                else if (state == null) await ReadState(token);
                else SetStatus("status.none");
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                SetStatus(api.Pending == null ? "status.error" : "status.recover");
            }
        }

        private Task ShowWishes()
        {
            if (state == null) return Task.CompletedTask;
            wishHeroes.Clear();
            wishHeroes.UnionWith(state.wishHeroes);
            wishArtifacts.Clear();
            wishArtifacts.UnionWith(state.wishArtifacts);
            onlyUnowned = false;
            wishRoleFilter = null;
            ShowPanel("wishPanel");
            return ShowWishCategory(false);
        }

        private Task ToggleWishOverlay(string key)
        {
            bool open = !view.Panel(key).activeSelf;
            view.Panel("wishHelp").SetActive(false);
            view.Panel("wishFilters").SetActive(false);
            view.Panel(key).SetActive(open);
            return Task.CompletedTask;
        }

        private Task ShowWishCategory(bool artifacts)
        {
            wantsArtifacts = artifacts;
            view.Panel("wishHelp").SetActive(false);
            view.Panel("wishFilters").SetActive(false);
            Canvas.ForceUpdateCanvases();
            RenderSelectedWishes("wishSelectedHeroes", "hero", wishHeroes);
            RenderSelectedWishes("wishSelectedArtifacts", "artifact", wishArtifacts);
            Clear("wishItems");
            ResizeWishGrid("wishItems", false);
            var selected = artifacts ? wishArtifacts : wishHeroes;
            string kind = artifacts ? "artifact" : "hero";
            var candidates = artifacts ? state.wishArtifactCandidates : state.wishHeroCandidates;
            var known = state.ownedWishIds ?? Array.Empty<string>();
            foreach (string id in candidates.OrderBy(id => selected.Contains(id)))
            {
                if (onlyUnowned && known.Contains(id)) continue;
                if (wishRoleFilter != null)
                {
                    string role = artifacts ? artifactArt.Find(id)?.template.HeroClass :
                        heroes.TryGetValue(id, out var candidate) ? candidate.heroClass : null;
                    if (role != wishRoleFilter) continue;
                }
                AddWishCard("wishItems", kind, id, selected.Contains(id), false);
            }
            foreach (bool artifactTab in new[] { false, true })
            {
                var tab = view.Button(artifactTab ? "wishArtifact" : "wishHero");
                bool active = artifactTab == artifacts;
                tab.image.color = active ? new Color32(255, 255, 255, 229) : Color.clear;
                tab.GetComponentInChildren<GameText>(true).color = active ? Color.black : new Color32(184, 188, 191, 255);
                view.Panel((artifactTab ? "wishArtifact" : "wishHero") + "Highlight").SetActive(active);
            }
            view.Text("wishCount").text = F("wish.count", wishHeroes.Count, wishArtifacts.Count);
            view.Image("wishUnownedCheck").enabled = onlyUnowned;
            view.Button("wishFilter").GetComponentInChildren<GameText>(true).text = locale.Resolve(
                wishRoleFilter == null ? "arena.draft.filter.button" : "home.hero_inventory.role." + wishRoleFilter);
            bool dirty = !wishHeroes.SetEquals(state.wishHeroes) || !wishArtifacts.SetEquals(state.wishArtifacts);
            view.Text("wishCount").gameObject.SetActive(dirty);
            var save = view.Button("wishSave");
            save.interactable = wishHeroes.Count == 7 && wishArtifacts.Count == 7;
            save.gameObject.SetActive(dirty);
            bool recommended = wishHeroes.SetEquals(state.wishHeroCandidates.Take(7)) &&
                wishArtifacts.SetEquals(state.wishArtifactCandidates.Take(7));
            var recommend = view.Button("wishRecommend");
            recommend.interactable = !recommended;
            recommend.GetComponentInChildren<GameText>(true).color = recommended ? new Color32(255, 255, 255, 127) : Color.white;
            return Task.CompletedTask;
        }

        private void RenderSelectedWishes(string list, string kind, HashSet<string> selected)
        {
            Clear(list);
            ResizeWishGrid(list, true);
            foreach (string id in selected.OrderBy(value => value, StringComparer.Ordinal))
                AddWishCard(list, kind, id, false, true);
        }

        private void ResizeWishGrid(string key, bool chosen)
        {
            var items = view.Items(key);
            var grid = items.GetComponent<GridLayoutGroup>();
            float side = (items.rect.width - (grid.constraintCount - 1) * grid.spacing.x) / grid.constraintCount;
            if (chosen) side = Mathf.Min(side, (items.rect.height - 3f * grid.spacing.y) / 4f);
            if (side > 0) grid.cellSize = new Vector2(side, side);
        }

        private void AddWishCard(string list, string kind, string id, bool selected, bool chosen)
        {
            bool isArtifact = kind == "artifact";
            var entry = Object.Instantiate(view.WishTemplate, view.Items(list), false);
            var bindings = entry.GetComponent<BindingMap>();
            var button = bindings.Require<GameButton>("button");
            button.onClick.AddListener(() => RunClick(_ => ToggleWish(id, isArtifact), false));
            heroes.TryGetValue(id, out var hero);
            var artifact = isArtifact ? artifactArt.Find(id) : null;
            var icon = bindings.Require<Image>("icon");
            SetImage(icon, isArtifact ? artifact?.combatIcon : hero?.portrait);
            bindings.Require<Image>("heroFrame").enabled = !isArtifact;
            icon.color = selected ? new Color32(150, 150, 150, 255) : Color.white;
            var role = isArtifact && artifact != null
                ? heroes.Values.FirstOrDefault(item => item.heroClass == artifact.template.HeroClass)?.role : hero?.role;
            SetImage(bindings.Require<Image>("role"), role);
            SetImage(bindings.Require<Image>("element"), hero?.element);
            bindings.Require<Image>("selected").enabled = selected;
            bindings.Require<GameObject>("remove").SetActive(chosen && wantsArtifacts == isArtifact);
            bindings.Require<GameText>("name").text = Name(kind, id);
            entry.SetActive(true);
        }

        private Task ToggleWish(string id, bool isArtifact)
        {
            var selected = isArtifact ? wishArtifacts : wishHeroes;
            if (!selected.Remove(id))
            {
                if (selected.Count >= 7)
                {
                    view.Text("wishCount").gameObject.SetActive(true);
                    view.Text("wishCount").text = L("wish.full");
                    return Task.CompletedTask;
                }
                selected.Add(id);
            }
            SetStatus(null);
            return ShowWishCategory(wantsArtifacts);
        }

        private async Task SaveWishes(CancellationToken token)
        {
            if (wishHeroes.Count != 7 || wishArtifacts.Count != 7)
            {
                SetStatus("status.wish_complete");
                return;
            }
            state = await api.SaveWishesAsync(wishHeroes.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                wishArtifacts.OrderBy(id => id, StringComparer.Ordinal).ToArray(), token);
            token.ThrowIfCancellationRequested();
            ShowPanel(null);
            RenderMain();
        }

        private async Task OpenStorage(CancellationToken token)
        {
            storage = await api.StorageAsync(token);
            token.ThrowIfCancellationRequested();
            ShowPanel("storagePanel");
            RenderStorage();
        }

        private void RenderStorage()
        {
            Clear("storageItems");
            var items = storage.items ?? Array.Empty<SummonReward>();
            view.Text("storageTitle").text = F("storage.title", items.Length, storage.capacity);
            if (items.Length == 0)
                AddRow("storageItems", L("storage.empty"), "", null, null);
            foreach (var item in items)
                AddRow("storageItems", Name(item.kind, item.templateId), F("entry.stars", item.stars) + "   " + L("storage.claim"),
                    SpriteFor(item.kind, item.templateId), async token =>
                    {
                        storage = await api.ClaimAsync(new[] { item.instanceId }, token);
                        token.ThrowIfCancellationRequested();
                        RenderStorage();
                    });
        }

        private async Task ShowReceipt(SummonReceipt receipt, CancellationToken windowToken, bool recovered = false)
        {
            if (receipt?.results == null || receipt.results.Length != receipt.count || receipt.count is not (1 or 10))
                throw new IOException("Invalid summon receipt.");
            windowToken.ThrowIfCancellationRequested();
            api.Pending = null;
            SetStatus(null);
            lastReceipt = receipt;
            if (recovered)
            {
                bannerId = receipt.bannerId;
                await ReadState(windowToken);
                windowToken.ThrowIfCancellationRequested();
                if (view.Music) player.PlayTheme(view.Music, ceremony: true);
                player.PlaySummary();
                ShowSummary();
                return;
            }
            if (state != null)
            {
                state.bookmarks = receipt.bookmarks;
                state.epicBookmarks = receipt.epicBookmarks;
                state.freeAvailable = receipt.freeAvailable;
                bannerId = receipt.bannerId;
                if (Banner != null) Banner.misses = receipt.misses;
            }
            RenderMain();

            using var sequence = CancellationTokenSource.CreateLinkedTokenSource(windowToken);
            playback = sequence;
            var token = sequence.Token;
            ShowPanel("revealPanel");
            view.Button("skip").gameObject.SetActive(true);
            view.Button("next").gameObject.SetActive(false);
            ResetReveal();
            if (view.Music) player.PlayTheme(view.Music, ceremony: true);
            try
            {
                await player.PlayOpeningAsync(receipt.results.Select(EffectGrade).Max(), token);
                foreach (var result in receipt.results.OrderBy(item => item.position))
                {
                    bool isArtifact = result.kind == "artifact";
                    heroes.TryGetValue(result.templateId, out var hero);
                    await player.PlayRevealAsync(result.stars, isArtifact, token, () =>
                    {
                        FillReveal(result);
                        player.PlayCardFlip(result.stars >= 4, result.isNew);
                        if (hero != null)
                            player.ShowHeroMiniature(hero.modelSkeleton, hero.modelAtlas, hero.modelPage);
                    }, view.Image("revealArt").rectTransform, hero?.heroClass,
                        opacity => view.Group("revealCaptionFade").alpha = opacity,
                        moonlight: EffectGrade(result) == SummonEffectGrade.VioletStars);
                    player.PlayCardReady();
                    token.ThrowIfCancellationRequested();
                    var step = new TaskCompletionSource<bool>();
                    next = step;
                    view.Button("next").gameObject.SetActive(true);
                    view.Text("nextLabel").text = L("reveal.next");
                    if (result.isNew && hero != null)
                        await ShowRewardDialogue(voice: true);
                    using var cancel = token.Register(() => step.TrySetCanceled(token));
                    await step.Task;
                    next = null;
                    view.Button("next").gameObject.SetActive(false);
                    ResetReveal();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Debug.LogWarning("Summon presentation failed: " + error.GetType().Name);
            }
            finally
            {
                playback = null;
                next = null;
            }
            if (windowToken.IsCancellationRequested) return;
            player.PlaySummary();
            ShowSummary();
        }

        private Task Next(CancellationToken token)
        {
            if (view.Panel("revealSpeech").activeSelf)
            {
                CloseRewardDialogue();
                return Task.CompletedTask;
            }
            if (showingDetail)
            {
                showingDetail = false;
                ResetReveal();
                player.PlaySummary(playCue: false);
                ShowPanel("resultPanel");
            }
            else next?.TrySetResult(true);
            return Task.CompletedTask;
        }

        private SummonEffectGrade EffectGrade(SummonReward reward)
        {
            if (reward.stars < 4) return SummonEffectGrade.Blue;
            if (reward.stars == 4) return SummonEffectGrade.Gold;
            return reward.kind == "hero" && heroes.TryGetValue(reward.templateId, out var hero)
                && (hero.elementId is "light" or "dark") ? SummonEffectGrade.VioletStars : SummonEffectGrade.GoldStars;
        }

        private void ResetReveal()
        {
            displayedReward = null;
            CloseRewardDialogue();
            view.Panel("revealCaption").SetActive(false);
            view.Panel("heroCaption").SetActive(false);
            view.Panel("artifactCaption").SetActive(false);
            view.Text("revealName").text = "";
            view.Text("revealStars").text = "";
            view.Text("revealLabel").text = "";
            revealPortrait.SetVisible(false);
            SetImage(view.Image("revealArt"), null);
        }

        private void FillReveal(SummonReward reward)
        {
            displayedReward = reward;
            view.Group("revealCaptionFade").alpha = 1f;
            CloseRewardDialogue();
            heroes.TryGetValue(reward.templateId, out var hero);
            var artifact = reward.kind == "artifact" ? artifactArt.Find(reward.templateId) : null;
            bool isHero = reward.kind == "hero" && hero != null;
            string name = Name(reward.kind, reward.templateId);

            view.Panel("revealCaption").SetActive(true);
            view.Panel("heroCaption").SetActive(isHero);
            view.Panel("artifactCaption").SetActive(!isHero);
            view.Panel("revealNew").SetActive(reward.isNew);
            view.Button("revealVoice").gameObject.SetActive(isHero);
            view.Text("revealName").text = name;
            view.Text("revealStars").text = isHero ? "" : L("entry.artifact");
            view.Text("revealLabel").text = isHero ? L("reveal." + reward.templateId + ".intro") : artifact?.template.DescriptionAt(1);
            var grade = view.Items("revealGrade");
            var label = view.Text("revealName");
            // Text length varies independently of the name box. Place the first star at the rendered name edge.
            grade.anchoredPosition = new Vector2(31f * 1.5f + label.GetPreferredValues(name).x + 8f, grade.anchoredPosition.y);
            for (int i = 0; i < 6; i++) view.Panel("revealStar" + i).SetActive(i < reward.stars);
            if (isHero)
            {
                SetImage(view.Image("revealElement"), hero.element);
                SetImage(view.Image("revealRole"), hero.role);
                SetImage(view.Image("revealZodiac"), hero.zodiacIcon);
                view.Text("revealElementText").text = L("reveal.element." + hero.elementId);
                view.Text("revealRoleText").text = L("reveal.role." + hero.heroClass);
                view.Text("revealZodiacText").text = L("reveal.zodiac." + hero.zodiac);
                view.Text("revealCV").text = L("reveal." + hero.id + ".cv");
            }

            bool animated = isHero && revealPortrait.Show(hero);
            if (!animated) revealPortrait.SetVisible(false);
            SetImage(view.Image("revealArt"), animated ? null : SpriteFor(reward.kind, reward.templateId, fullArt: true));
        }

        private Task ShowRewardDialogue(bool voice)
        {
            if (displayedReward == null) return Task.CompletedTask;
            var reward = displayedReward;
            bool isHero = reward.kind == "hero";
            view.Text("revealSpeaker").text = Name(reward.kind, reward.templateId);
            view.Text("revealQuote").text = isHero
                ? L("reveal." + reward.templateId + (voice ? ".line" : ".story"))
                : artifactArt.Find(reward.templateId)?.template.DescriptionAt(1);
            view.Panel("revealSpeech").SetActive(true);
            if (voice && isHero) player.PlayHeroVoice(reward.templateId);
            return Task.CompletedTask;
        }

        private void CloseRewardDialogue()
        {
            view.Panel("revealSpeech").SetActive(false);
            player?.StopHeroVoice();
        }

        private void ShowSummary()
        {
            Clear("resultItems");
            ShowPanel("resultPanel");
            var template = view.Panel("resultCardTemplate");
            var parent = view.Items("resultItems");
            int position = 0;
            foreach (var result in lastReceipt.results.OrderBy(item => item.position))
            {
                var row = Object.Instantiate(template, parent);
                var bindings = row.GetComponent<BindingMap>();
                heroes.TryGetValue(result.templateId, out var hero);
                var artifact = result.kind == "artifact" ? artifactArt.Find(result.templateId) : null;
                SummonResultCard.Present(bindings, result, Name(result.kind, result.templateId), hero, artifact);
                SummonResultCard.Place((RectTransform)row.transform, ++position, lastReceipt.count);
                bindings.Require<GameButton>("button").onClick.AddListener(() => RunClick(_ =>
                {
                    showingDetail = true;
                    player.PlayCardFlip(result.stars >= 4, isNew: false);
                    ShowPanel("revealPanel");
                    player.ShowRewardDetail(result.kind == "artifact", view.Image("revealArt").rectTransform, hero);
                    FillReveal(result);
                    view.Button("skip").gameObject.SetActive(false);
                    view.Button("next").gameObject.SetActive(true);
                    view.Text("nextLabel").text = L("reveal.close");
                    return Task.CompletedTask;
                }, allowBusy: false));
                row.SetActive(true);
            }

            var banner = Banner;
            bool canRepeat = banner != null && banner.id == lastReceipt.bannerId;
            bool usesEpicBookmarks = canRepeat && RepeatPayment(lastReceipt) == EpicBookmarks;
            view.Button("resultAgain").interactable = canRepeat;
            view.Image("resultAgainCurrency").enabled = canRepeat && !usesEpicBookmarks;
            view.Image("resultAgainEpicCurrency").enabled = usesEpicBookmarks;
            view.Text("resultAgainPrice").text = canRepeat
                ? (banner.bookmarksPerDraw * lastReceipt.count).ToString(CultureInfo.CurrentCulture) : "";
        }

        private static string RepeatPayment(SummonReceipt receipt) => receipt.payment == Free ? Bookmarks : receipt.payment;

        private RectTransform AddRow(string parent, string name, string detail, Sprite icon, Func<CancellationToken, Task> click)
        {
            var instance = Object.Instantiate(view.RowTemplate, view.Items(parent));
            var bindings = instance.GetComponent<BindingMap>();
            bindings.Require<GameText>("name").text = name;
            bindings.Require<GameText>("detail").text = detail;
            SetImage(bindings.Require<Image>("icon"), icon);
            var button = bindings.Require<GameButton>("button");
            button.interactable = click != null;
            if (click != null) button.onClick.AddListener(() => RunClick(click, allowBusy: false));
            instance.SetActive(true);
            return (RectTransform)instance.transform;
        }

        private void Clear(string list)
        {
            var content = view.Items(list);
            if (!content) return;
            for (int i = content.childCount - 1; i >= 0; --i)
            {
                var child = content.GetChild(i).gameObject;
                if (child == view.RowTemplate) continue;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        private void Hook(string key, Func<CancellationToken, Task> click, bool allowBusy = false)
        {
            var button = view.Button(key);
            void Call()
            {
                if (button && button.isActiveAndEnabled && button.IsInteractable())
                    RunClick(click, allowBusy);
            }
            UnityAction call = Call;
            button.onClick.AddListener(call);
            screen.Add(() => { if (button) button.onClick.RemoveListener(call); });
        }

        private async void RunClick(Func<CancellationToken, Task> click, bool allowBusy)
        {
            CancellationToken token = screen.Token;
            if (token.IsCancellationRequested || !allowBusy && busy) return;
            if (!allowBusy) busy = true;
            try { await click(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (IOException) when (!token.IsCancellationRequested) { SetStatus("status.error"); }
            finally
            {
                if (!allowBusy)
                {
                    busy = false;
                    if (!token.IsCancellationRequested && view.Root) RenderMain();
                }
            }
        }

        private Task ClosePanel(CancellationToken token)
        {
            showingDetail = false;
            quote = null;
            if (lastReceipt != null)
            {
                lastReceipt = null;
                player.Stop();
                if (view.Music) player.PlayTheme(view.Music, ceremony: false);
            }
            ShowPanel(null);
            SetStatus(null);
            return Task.CompletedTask;
        }

        private void ShowPanel(string active)
        {
            notice?.Hide();
            switch (active)
            {
                case null:
                    layers.ShowPage(view.Panel("mainPage"));
                    break;
                case "resultPanel":
                case "revealPanel":
                    layers.ShowPage(view.Panel(active));
                    break;
                case "confirmPanel":
                    layers.ShowOverlay(view.Panel(active), () =>
                    {
                        quote = null;
                        ReturnFromConfirmation();
                        return Task.CompletedTask;
                    });
                    break;
                default:
                    layers.ShowOverlay(view.Panel(active), () => ClosePanel(screen.Token));
                    break;
            }
            UpdateFeaturedSkill();
            if (active != "revealPanel") revealPortrait?.SetVisible(false);
        }

        private void ReturnFromConfirmation() => ShowPanel(lastReceipt == null ? null : "resultPanel");

        private string Name(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return L("entry.unknown");
            if (kind == "hero" && heroes.TryGetValue(id, out var hero)) return hero.name;
            if (kind == "artifact") return artifactArt.Find(id)?.template.Name ?? L("entry.unknown");
            return L("entry.unknown");
        }

        private Sprite SpriteFor(string kind, string id, bool fullArt = false)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (kind == "artifact")
            {
                var entry = artifactArt.Find(id);
                return fullArt ? entry?.card : entry?.portrait;
            }
            if (!heroes.TryGetValue(id, out var hero)) return null;
            return fullArt && hero.illustration ? hero.illustration : hero.cardPortrait ? hero.cardPortrait : hero.portrait;
        }

        private static void SetImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
        }

        private void SetStatus(string key) => view.Text("status").text = key == null ? "" : L(key);
        private string L(string key) => locale.Resolve(Prefix + key);
        private string F(string key, params object[] args) => GameText.Format(locale, Prefix + key, args);
    }
}
