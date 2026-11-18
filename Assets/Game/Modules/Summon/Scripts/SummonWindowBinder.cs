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
    public sealed class SummonWindowBinder
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
        private HeroRosterPortrait featuredPortrait, revealPortrait;
        private SummonSkillLoop.Player featuredSkill;
        private string activePanel;
        private HeroRosterPortrait[] wishPortraits;
        private SummonPresentation.Player player;
        private SummonState state;
        private SummonRequest quote;
        private SummonReceipt lastReceipt;
        private SummonStorage storage;
        private CancellationTokenSource playback;
        private TaskCompletionSource<bool> next;
        private string bannerId = Covenant;
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

        public async Task OpenAsync(BindingScope scope)
        {
            screen = scope;
            view.Show();
            ShowPanel(null);
            player = presentation.CreatePlayer(view.Effects, view.Audio);
            featuredSkill = featuredLoop.CreatePlayer(view.Raw("featuredSkillCanvas"), view.FeaturedSkillAudio, scope);
            featuredPortrait = new HeroRosterPortrait(view.Raw("upPortrait"), positionOffset: 2000);
            revealPortrait = new HeroRosterPortrait(view.Raw("revealHero"), positionOffset: 3000);
            wishPortraits = Enumerable.Range(0, 7).Select(index => new HeroRosterPortrait(view.Raw("wishHero" + index),
                renderSize: 480, animated: false, positionOffset: 4000 + index * 1000, framing: .83f, headFocus: .30f)).ToArray();
            Canvas.willRenderCanvases += featuredPortrait.Tick;
            Canvas.willRenderCanvases += revealPortrait.Tick;
            scope.Add(() =>
            {
                playback?.Cancel();
                Canvas.willRenderCanvases -= featuredPortrait.Tick;
                Canvas.willRenderCanvases -= revealPortrait.Tick;
                featuredPortrait.Dispose();
                revealPortrait.Dispose();
                foreach (var portrait in wishPortraits) portrait.Dispose();
                player.Dispose();
                if (view.Music) view.Music.Stop();
                foreach (string list in new[] { "infoItems", "wishItems", "wishSelectedItems", "storageItems", "resultItems" })
                    Clear(list);
                if (view.Root) view.Hide();
            });

            Hook("back", _ => goHome());
            Hook("covenant", _ => ChooseBanner(Covenant));
            Hook("iseria", _ => ChooseBanner(Up));
            Hook("single", _ => Quote(1, epic ? EpicBookmarks : Bookmarks));
            Hook("ten", _ => Quote(10, epic ? EpicBookmarks : Bookmarks));
            Hook("free", _ => Quote(1, Free));
            Hook("epic", _ => { epic = !epic; RenderMain(); return Task.CompletedTask; });
            Hook("info", _ => ShowInfo());
            Hook("wish", _ => ShowWishes());
            Hook("help", _ => ShowInfo());
            Hook("storage", OpenStorage);
            Hook("recovery", Recover);
            Hook("confirmYes", Confirm);
            Hook("confirmNo", _ => { quote = null; ReturnFromConfirmation(); return Task.CompletedTask; });
            Hook("infoClose", ClosePanel);
            Hook("wishHero", _ => ShowWishCategory(false));
            Hook("wishArtifact", _ => ShowWishCategory(true));
            Hook("wishSave", SaveWishes);
            Hook("wishUnowned", _ => { onlyUnowned = !onlyUnowned; return ShowWishCategory(wantsArtifacts); });
            Hook("wishClose", ClosePanel);
            Hook("storageClose", ClosePanel);
            Hook("resultClose", ClosePanel);
            Hook("resultAgain", _ => Quote(lastReceipt.count, RepeatPayment(lastReceipt)));
            Hook("skip", _ => { playback?.Cancel(); return Task.CompletedTask; }, allowBusy: true);
            Hook("next", Next, allowBusy: true);

            view.Text("title").text = L("title");
            view.Text("infoTitle").text = L("info.title");
            view.Text("wishTitle").text = L("wish.title");
            view.Text("resultTitle").text = L("result.title");
            view.Text("skipLabel").text = L("reveal.skip");
            if (view.Music) player.PlayTheme(view.Music, ceremony: false);
            SetStatus("status.loading");
            await ReadState(scope.Token);
            if (api.Pending != null && !scope.Token.IsCancellationRequested)
                SetStatus("status.recover");
        }

        private SummonBanner Banner => state?.banners?.FirstOrDefault(item => item.id == bannerId);

        private async Task ReadState(CancellationToken token)
        {
            try
            {
                state = await api.GetAsync(token);
                token.ThrowIfCancellationRequested();
                if (Banner == null)
                    bannerId = state.banners?.FirstOrDefault()?.id ?? Covenant;
                SetStatus(null);
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
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
            var banner = Banner;
            view.Text("balance").text = state?.bookmarks.ToString(CultureInfo.InvariantCulture) ?? "";
            view.Text("epicBalance").text = state?.epicBookmarks.ToString(CultureInfo.InvariantCulture) ?? "";
            view.Text("bannerTitle").text = banner?.title ?? "";
            view.Text("featuredHero").text = string.IsNullOrEmpty(banner?.featuredHero) ? "" : Name("hero", banner.featuredHero);
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
            SetImage(view.Image("artifactArt"), SpriteFor("artifact", banner?.featuredArtifact, fullArt: true));
            view.Panel("artifactFrame").SetActive(!covenant && view.Image("artifactArt").enabled);
            view.Panel("covenantSelected").SetActive(covenant);
            view.Panel("iseriaSelected").SetActive(!covenant);
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
            view.Text("wishButtonLabel").text = F("wish.total", (state?.wishHeroes?.Length ?? 0) + (state?.wishArtifacts?.Length ?? 0));
            view.Button("covenant").interactable = state?.banners?.Any(item => item.id == Covenant) == true;
            view.Button("iseria").interactable = state?.banners?.Any(item => item.id == Up) == true;
        }

        private void UpdateFeaturedSkill() =>
            featuredSkill?.SetState(bannerId == Up && Banner != null, activePanel != null || busy);

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
                view.Text(key + "Owned").text = L(owned.Contains(selected.id) ? "wish.owned_badge" : "wish.unowned_badge");
                view.Text(key + "Name").text = selected.name;
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
            int balance = payment == EpicBookmarks ? state.epicBookmarks : payment == Free ? 1 : state.bookmarks;
            view.Button("confirmYes").interactable = balance >= cost;
            quote = new SummonRequest
            {
                requestId = Guid.NewGuid().ToString("N"), bannerId = banner.id, revision = banner.revision,
                count = count, payment = payment
            };
            view.Text("confirmTitle").text = L(count == 10 ? "confirm.ten" : "confirm.single");
            view.Text("confirmPrompt").text = L(count == 10 ? "confirm.prompt_ten" : "confirm.prompt_single");
            view.Text("confirmCurrency").text = L("payment." + payment);
            view.Text("confirmCost").text = cost.ToString(CultureInfo.InvariantCulture);
            view.Text("confirmBody").text = balance < cost ? L("status.insufficient") : F("confirm.owned", balance);
            SetImage(view.Image("confirmCoin"), payment == Free ? null : view.Coin(payment == EpicBookmarks));
            bool showFeatured = !string.IsNullOrEmpty(banner.featuredHero);
            view.Panel("confirmHeroRow").SetActive(showFeatured);
            SetImage(view.Image("confirmHeroPortrait"), SpriteFor("hero", banner.featuredHero));
            view.Text("confirmHeroName").text = showFeatured ? Name("hero", banner.featuredHero) : "";
            view.Text("confirmHeroPity").text = showFeatured ?
                F("remaining", Math.Max(0, banner.guaranteeAfter - banner.misses)) : "";
            view.ConfirmPriceRow.anchoredPosition = new Vector2(0, showFeatured ? 0 : view.ConfirmPriceRow.rect.height * .48f);
            ShowPanel("confirmPanel");
            return Task.CompletedTask;
        }

        private async Task Confirm(CancellationToken token)
        {
            var request = quote;
            if (request == null) return;
            quote = null;
            api.Pending = request;
            ShowPanel(null);
            await DrawPending(request, token);
        }

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

        private Task ShowInfo()
        {
            var banner = Banner;
            if (banner == null) return Task.CompletedTask;
            Clear("infoItems");
            if (banner.guaranteeAfter > 0)
                AddRow("infoItems", L("info.title"), F("info.pity", banner.guaranteeAfter), null, null);
            foreach (var entry in banner.entries.OrderByDescending(item => item.stars).ThenBy(item => item.kind).ThenByDescending(item => item.featured))
            {
                string probability = entry.probability.ToString("0.#####", CultureInfo.InvariantCulture) + "%";
                AddRow("infoItems", Name(entry.kind, entry.templateId),
                    F("entry.stars", entry.stars) + "   " + probability, SpriteFor(entry.kind, entry.templateId), null);
            }
            ShowPanel("infoPanel");
            return Task.CompletedTask;
        }

        private Task ShowWishes()
        {
            if (state == null) return Task.CompletedTask;
            wishHeroes.Clear();
            wishHeroes.UnionWith(state.wishHeroes);
            wishArtifacts.Clear();
            wishArtifacts.UnionWith(state.wishArtifacts);
            onlyUnowned = false;
            ShowPanel("wishPanel");
            return ShowWishCategory(false);
        }

        private Task ShowWishCategory(bool artifacts)
        {
            wantsArtifacts = artifacts;
            Clear("wishItems");
            Clear("wishSelectedItems");
            var selected = artifacts ? wishArtifacts : wishHeroes;
            string kind = artifacts ? "artifact" : "hero";
            var candidates = artifacts ? state.wishArtifactCandidates : state.wishHeroCandidates;
            Canvas.ForceUpdateCanvases();
            ResizeWishGrid("wishSelectedItems");
            ResizeWishGrid("wishItems");
            foreach (string id in selected.OrderBy(value => value, StringComparer.Ordinal))
                AddWishCard("wishSelectedItems", kind, id, false);
            var known = state.ownedWishIds ?? Array.Empty<string>();
            foreach (string id in candidates)
            {
                if (onlyUnowned && known.Contains(id)) continue;
                AddWishCard("wishItems", kind, id, selected.Contains(id));
            }
            view.Text("wishCount").text = F("wish.count", wishHeroes.Count, wishArtifacts.Count);
            var filter = view.Button("wishUnowned");
            var caption = filter.GetComponentInChildren<GameText>(true);
            if (caption) caption.text = (onlyUnowned ? "✓ " : "") + L("wish.unowned");
            view.Button("wishSave").interactable = wishHeroes.Count == 7 && wishArtifacts.Count == 7;
            return Task.CompletedTask;
        }

        private void ResizeWishGrid(string key)
        {
            var items = view.Items(key);
            var grid = items.GetComponent<GridLayoutGroup>();
            float side = (items.rect.width - (grid.constraintCount - 1) * grid.spacing.x) / grid.constraintCount;
            if (side > 24) grid.cellSize = new Vector2(side, side);
        }

        private void AddWishCard(string list, string kind, string id, bool selected)
        {
            bool isArtifact = kind == "artifact";
            var entry = Object.Instantiate(view.WishTemplate, view.Items(list), false);
            var bindings = entry.GetComponent<BindingMap>();
            var button = bindings.Require<GameButton>("button");
            button.image.sprite = view.WishFrame(isArtifact);
            button.onClick.AddListener(() => RunClick(_ => ToggleWish(id), false));
            var icon = isArtifact ? artifactArt.Find(id)?.combatIcon :
                heroes.TryGetValue(id, out var hero) ? hero.portrait : null;
            SetImage(bindings.Require<Image>("icon"), icon);
            bindings.Require<Image>("selected").enabled = selected;
            bindings.Require<GameText>("name").text = Name(kind, id);
            entry.SetActive(true);
        }

        private Task ToggleWish(string id)
        {
            var selected = wantsArtifacts ? wishArtifacts : wishHeroes;
            if (!selected.Remove(id))
            {
                if (selected.Count >= 7)
                {
                    SetStatus("wish.full");
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
                await player.PlayOpeningAsync(receipt.results.Max(item => item.stars), token);
                foreach (var result in receipt.results.OrderBy(item => item.position))
                {
                    bool isArtifact = result.kind == "artifact";
                    heroes.TryGetValue(result.templateId, out var hero);
                    await player.PlayRevealAsync(result.stars, isArtifact, token, () =>
                    {
                        FillReveal(result);
                        player.PlayCardFlip(result.stars >= 4, result.isNew);
                        if (!isArtifact)
                        {
                            if (hero != null) player.ShowHeroMiniature(hero.skeleton, hero.atlas, hero.page);
                            player.PlayHeroVoice(result.templateId);
                        }
                    }, view.Image("revealArt").rectTransform, hero?.heroClass);
                    player.PlayCardReady();
                    token.ThrowIfCancellationRequested();
                    var step = new TaskCompletionSource<bool>();
                    next = step;
                    view.Button("next").gameObject.SetActive(true);
                    view.Text("nextLabel").text = L("reveal.next");
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
            if (showingDetail)
            {
                showingDetail = false;
                ShowPanel("resultPanel");
            }
            else next?.TrySetResult(true);
            return Task.CompletedTask;
        }

        private void ResetReveal()
        {
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
            view.Panel("heroCaption").SetActive(reward.kind == "hero");
            view.Panel("artifactCaption").SetActive(reward.kind == "artifact");
            view.Text("revealName").text = Name(reward.kind, reward.templateId);
            view.Text("revealStars").text = F("entry.stars", reward.stars);
            view.Text("revealLabel").text = L(reward.isNew ? "result.new" : "entry." + reward.kind);
            bool shown = reward.kind == "hero" && heroes.TryGetValue(reward.templateId, out var hero) && revealPortrait.Show(hero);
            if (!shown) revealPortrait.SetVisible(false);
            SetImage(view.Image("revealArt"), shown ? null : SpriteFor(reward.kind, reward.templateId, fullArt: true));
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
            lastReceipt = null;
            player.Stop();
            if (view.Music) player.PlayTheme(view.Music, ceremony: false);
            ShowPanel(null);
            SetStatus(null);
            return Task.CompletedTask;
        }

        private void ShowPanel(string active)
        {
            activePanel = active;
            UpdateFeaturedSkill();
            bool confirmFromResults = active == "confirmPanel" && lastReceipt != null;
            foreach (string key in SummonWindow.PanelKeys)
                view.Panel(key).SetActive(key == active || confirmFromResults && key == "resultPanel");
            if (active != "revealPanel") revealPortrait?.SetVisible(false);
            if (active == "confirmPanel") view.Panel(active).transform.SetAsLastSibling();
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
