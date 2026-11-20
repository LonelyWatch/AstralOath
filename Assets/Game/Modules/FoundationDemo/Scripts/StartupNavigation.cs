using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Client.Summon;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed class StartupNavigation
    {
        private readonly WindowCoordinator windows;
        private readonly HomeWindowBinder home;
        private readonly ArenaDirectoryBinder directory;
        private readonly ArenaRoomBinder room;
        private readonly UIViewRegistry views;
        private readonly IAssetService assets;
        private readonly ISceneAssetService scenes;
        private readonly Transform parent;
        private readonly BindingScope lifetime;
        private readonly ILocaleService locale;
        private readonly HomePagePreparation pages;
        private readonly HomeFirstReads firstReads = new HomeFirstReads();
        private const string MoreOverlay = "home-more";
        private readonly ArenaApi api;
        private readonly HeroInventoryApi inventory;
        private readonly ArtifactInventoryApi artifacts;
        private readonly SummonApi summon;
        private readonly Uri endpoint;
        private readonly CancellationToken token;
        private string roomId, activeDraftId;
        private string returnedRoomId, returnedDraftId;

        private StartupNavigation(BindingScope lifetime, HomeWindow homeView, ArenaDirectory directoryView,
            ArenaCreateRoom createView, ArenaRoom roomView, UIViewRegistry views, IAssetService assets, ISceneAssetService scenes, Transform parent,
            ILocaleService locale, ArenaApi api, HeroInventoryApi inventory, ArtifactInventoryApi artifacts,
            SummonApi summon, Uri endpoint, string playerName, DesktopDisplayMode display)
        {
            this.api = api;
            this.inventory = inventory;
            this.artifacts = artifacts;
            this.summon = summon;
            this.endpoint = endpoint;
            this.views = views;
            this.assets = assets;
            this.scenes = scenes;
            this.parent = parent;
            this.lifetime = lifetime;
            this.locale = locale;
            pages = new HomePagePreparation(views, assets, parent, lifetime);
            summon.InventoryMayChange += firstReads.Invalidate;
            artifacts.InventoryMayChange += firstReads.Invalidate;
            lifetime.Add(() =>
            {
                summon.InventoryMayChange -= firstReads.Invalidate;
                artifacts.InventoryMayChange -= firstReads.Invalidate;
            });
            token = lifetime.Token;
            windows = new WindowCoordinator(token);
            lifetime.Add(windows.Dispose);
            home = new HomeWindowBinder(homeView, locale, OpenDirectory, OpenMore,
                ct => OpenInventory(false, ct), display);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, windows, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, windows, ReturnToDirectory, OpenDraft);
            BattleOnlineLaunch.Returning += ReturnFromBattleAsync;
            lifetime.Add(() => BattleOnlineLaunch.Returning -= ReturnFromBattleAsync);
        }

        internal static async Task<StartupNavigation> StartAsync(GameLaunchContext context, BindingScope owner)
        {
            var curtain = new GameObject("Home loading curtain", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            var overlay = curtain.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = short.MaxValue;
            curtain.GetComponent<Image>().color = Color.black;
            void CloseCurtain()
            {
                if (!curtain) return;
                curtain.SetActive(false);
                UnityEngine.Object.Destroy(curtain);
            }
            var detach = owner.Add(CloseCurtain);
            try
            {
                await Task.Yield();
                owner.Token.ThrowIfCancellationRequested();
                return await LoadHomeAsync(context, owner);
            }
            finally { detach(); CloseCurtain(); }
        }

        private static async Task<StartupNavigation> LoadHomeAsync(GameLaunchContext context, BindingScope owner)
        {
            var views = new UIViewRegistry();
            views.Register<HomeWindow>();
            views.Register<HomeMoreMenu>();
            views.Register<HeroInventory>();
            views.Register<SummonWindow>();
            views.Register<ArenaDirectory>();
            views.Register<ArenaCreateRoom>();
            views.Register<ArenaRoom>();
            views.Register<ArenaDraft>();
            var assets = context.Services.Get<IAssetService>();
            var locale = await ArenaLabels.LoadAsync(assets, owner, context.TextLocale, context.VoiceLocale);
            var home = await views.CreateAsync<HomeWindow>(assets, context.ViewRoot, owner);
            var directory = await views.CreateAsync<ArenaDirectory>(assets, context.ViewRoot, owner);
            var create = await views.CreateAsync<ArenaCreateRoom>(assets, context.ViewRoot, owner);
            var room = await views.CreateAsync<ArenaRoom>(assets, context.ViewRoot, owner);
            context.Services.TryGet<GameAccountSession>(out var account);
            var api = new ArenaApi(context.Services.Get<GameServerEndpoint>().Address, owner.Token, account);
            owner.Add(api.Dispose);
            string name = account?.DisplayName ?? Environment.GetEnvironmentVariable("ASTRAL_ARENA_PLAYER_NAME");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = PlayerPrefs.GetString("arena.guest-name", "");
                if (string.IsNullOrEmpty(name))
                {
                    name = GameText.Format(locale, "arena.guest_name", Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant());
                    PlayerPrefs.SetString("arena.guest-name", name);
                    PlayerPrefs.Save();
                }
            }
            var display = context.ViewRoot.GetComponentInParent<DesktopDisplayMode>();
            var scenes = context.Services.Get<ISceneAssetService>();
            var endpoint = context.Services.Get<GameServerEndpoint>().Address;
            Task<string> Bearer(CancellationToken ct) =>
                account == null ? api.GetSessionTokenAsync(name, ct) : Task.FromResult(account.Token);

            var inventory = new HeroInventoryApi(endpoint, Bearer, owner.Token);
            var artifacts = new ArtifactInventoryApi(endpoint, Bearer, owner.Token);
            var summon = new SummonApi(endpoint, Bearer, owner.Token);
            owner.Add(inventory.Dispose);
            owner.Add(artifacts.Dispose);
            owner.Add(summon.Dispose);
            var navigation = new StartupNavigation(owner, home, directory, create, room, views, assets, scenes,
                context.ViewRoot, locale, api, inventory, artifacts, summon, endpoint, name, display);
            await Task.WhenAll(navigation.pages.PrepareAsync(owner.Token),
                navigation.firstReads.PrepareAsync(Bearer, inventory, artifacts, summon, owner.Token));
            owner.Token.ThrowIfCancellationRequested();
            await navigation.ShowHome();
            context.Services.Add<ILocaleService>(locale);
            context.OpenButton.gameObject.SetActive(false);
            context.StartupStatus.gameObject.SetActive(false);
            return navigation;
        }

        private async Task OpenMore(CancellationToken cancellation)
        {
            if (windows.IsOverlayOpen(MoreOverlay)) { windows.CloseOverlay(MoreOverlay); return; }
            var view = await pages.More.Get();
            cancellation.ThrowIfCancellationRequested();
            windows.ShowOverlay(MoreOverlay, scope =>
            {
                home.HideArenaWhile(scope);
                var binder = new HomeMoreMenuBinder(view, locale, ct => OpenInventory(true, ct), OpenSummon,
                    () => windows.CloseOverlay(MoreOverlay));
                binder.Open(scope);
            });
        }

        internal async Task VerifySummonAsync(CancellationToken cancellation, SummonApi isolatedGuest = null)
        {
            await OpenMore(cancellation);
            var view = await ShowSummonPageAsync(cancellation, isolatedGuest);
            var bindings = view.Root.GetComponent<BindingMap>();
            bool covenant = bindings.Require<GameButton>("covenant").interactable;
            bool iseria = bindings.Require<GameButton>("iseria").interactable;
            var published = await (isolatedGuest ?? summon).GetAsync(cancellation);
            bool expectIseria = Array.Exists(published.banners, banner => banner.id == "iseria");
            if (!view.Root.activeInHierarchy || !covenant || iseria != expectIseria)
                throw new InvalidOperationException($"Packaged banner availability disagrees with the service. Active={view.Root.activeInHierarchy}, covenant={covenant}, iseria={iseria}, publishedIseria={expectIseria}.");

            var preview = bindings.Require<UnityEngine.UI.RawImage>("featuredSkillCanvas");
            if (expectIseria)
            {
                var poster = bindings.Require<CanvasGroup>("featuredActorLayer");
                while (!preview.enabled || !preview.texture || poster.alpha > .05f || preview.color.a < .95f)
                    await Task.Delay(50, cancellation);
            }
            bindings.Require<GameButton>("covenant").onClick.Invoke();
            if (preview.enabled || !bindings.Require<GameObject>("wishRoster").activeInHierarchy)
                throw new InvalidOperationException("The covenant roster did not replace the featured preview.");
            bindings.Require<GameButton>("wish").onClick.Invoke();
            bindings.Require<GameButton>("wishArtifact").onClick.Invoke();
            if (!bindings.Require<GameObject>("wishArtifactHighlight").activeInHierarchy ||
                bindings.Require<RectTransform>("wishSelectedArtifacts").childCount < 7)
                throw new InvalidOperationException("The packaged artifact wishlist did not display its selected items.");
            bindings.Require<GameButton>("wishClose").onClick.Invoke();
            if (isolatedGuest != null) await SummonPlayerSmoke.VerifyResultResizeAsync(bindings, cancellation);
        }

        private Task OpenSummon(CancellationToken cancellation) => ShowSummonPageAsync(cancellation);

        private async Task<SummonWindow> ShowSummonPageAsync(CancellationToken cancellation, SummonApi isolatedGuest = null)
        {
            var view = await pages.Summon.Get();
            var art = await pages.Heroes.Get();
            var artifactArt = await pages.Artifacts.Get();
            var presentation = await pages.Presentation.Get();
            var featuredLoop = await pages.SkillLoop.Get();
            cancellation.ThrowIfCancellationRequested();
            await windows.ShowPageAsync(scope =>
            {
                var binder = new SummonWindowBinder(view, isolatedGuest ?? summon, art, artifactArt, presentation, featuredLoop, locale, ShowHome);
                return binder.OpenAsync(scope, isolatedGuest == null ? firstReads.TakeSummon() : null);
            });
            return view;
        }

        private async Task OpenInventory(bool roster, CancellationToken cancellation)
        {
            var view = await pages.Inventory.Get();
            var art = await pages.Heroes.Get();
            var artifactArt = await pages.Artifacts.Get();
            cancellation.ThrowIfCancellationRequested();
            await windows.ShowPageAsync(scope =>
            {
                Func<string, CancellationToken, Task> startTrial = null;
                if (Debug.isDebugBuild && endpoint.IsLoopback)
                    startTrial = (hero, ct) => StartArtifactTrialAsync(hero, artifactArt, ct);
                var binder = new HeroInventoryBinder(view, art, locale, inventory.FetchAsync, ShowHome, artifactArt, artifacts, startTrial);
                return binder.OpenAsync(roster, scope, firstReads.Heroes, firstReads.Artifacts);
            });
        }

        private async Task StartArtifactTrialAsync(string heroInstanceId, ArtifactArtCatalog art, CancellationToken cancellation)
        {
            if (BattleOnlineLaunch.IsActive) return;
            var invitation = await artifacts.StartTrialAsync(heroInstanceId, cancellation);
            await BattleOnlineLaunch.LoadOwnedTrialAsync(assets, scenes, endpoint, invitation.roomId,
                invitation.opening.snapshot.battleId, invitation.contentHash, invitation.battleContentHash,
                invitation.allyToken, cancellation, id =>
                {
                    var item = art.Find(id);
                    return (item?.template.Name, item?.combatIcon);
                });
        }

        private Task OpenDirectory(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ReturnToDirectory();
        }

        private Task OpenRoom(ArenaRoomState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (state?.room == null) throw new InvalidOperationException("Arena room response is missing.");
            roomId = state.room.id;
            activeDraftId = null;
            return windows.ShowPageAsync(scope => room.OpenAsync(state, scope));
        }

        private async Task OpenDraft(ArenaDraftState state)
        {
            if (state.roomId != roomId || state.phase == "battle" && returnedDraftId != null &&
                state.roomId == returnedRoomId && state.id == returnedDraftId) return;
            var loading = new BindingScope(token);
            var release = lifetime.Add(loading.Dispose);
            try
            {
                // Wait for the draft art without adding its model pages to initial home loading.
                var draftView = await views.CreateAsync<ArenaDraft>(assets, parent, loading);
                var draftArt = await assets.LoadAsync<ArenaDraftArt>(ArenaDraftArt.Address, loading);
                loading.Token.ThrowIfCancellationRequested();
                if (roomId != state.roomId) return;
                await windows.ShowPageAsync(scope =>
                {
                    activeDraftId = state.id;
                    scope.Add(loading.Dispose);
                    scope.Add(release);
                    var draft = new ArenaDraftBinder(draftView, api, locale, draftArt, windows, assets, scenes, ReopenRoomAsync);
                    scope.Add(draft.Dispose);
                    return draft.OpenAsync(state, scope);
                });
                loading = null;
            }
            finally
            {
                if (loading != null) { release(); loading.Dispose(); }
            }
        }

        private async Task ReopenRoomAsync()
        {
            try { await OpenRoom(await api.GetAsync(roomId, token), token); }
            catch (ArenaRequestException) { await ReturnToDirectory(); }
        }

        private async Task ReturnFromBattleAsync()
        {
            if (token.IsCancellationRequested || string.IsNullOrEmpty(roomId)) return;
            if (!string.IsNullOrEmpty(activeDraftId))
            {
                returnedRoomId = roomId;
                returnedDraftId = activeDraftId;
            }
            try
            {
                try { await api.LeaveAsync(roomId, token); }
                catch (ArenaRequestException) { /* The directory checks the current room again. */ }
                token.ThrowIfCancellationRequested();
                await ShowDirectory(directory.OpenAfterBattleAsync);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        private Task ReturnToDirectory() => ShowDirectory(directory.OpenAsync);

        private Task ShowDirectory(Func<BindingScope, Task> open)
        {
            roomId = null;
            activeDraftId = null;
            return windows.ShowPageAsync(open);
        }

        private Task ShowHome() => windows.ShowPageAsync(home.OpenAsync);
    }
}
