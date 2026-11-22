using System;
using System.Linq;
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
        private readonly ScreenFadeTransition fade;
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
        private readonly HomeFirstReads firstReads;
        private readonly HeroPortraitSources portraits = new HeroPortraitSources();
        private const string MoreOverlay = "home-more";
        private readonly ArenaApi api;
        private readonly ArtifactInventoryApi artifacts;
        private readonly EquipmentInventoryApi equipment;
        private readonly SummonApi summon;
        private readonly Uri endpoint;
        private readonly CancellationToken token;
        private string roomId, activeDraftId;
        private string returnedRoomId, returnedDraftId;
        private bool lostFocus;
        private Task equipmentFocusRead;
        private Func<Task> refreshGmPage;

        private StartupNavigation(BindingScope lifetime, ScreenFadeTransition fade, HomeWindow homeView, ArenaDirectory directoryView,
            ArenaCreateRoom createView, ArenaRoom roomView, UIViewRegistry views, IAssetService assets, ISceneAssetService scenes, Transform parent,
            ILocaleService locale, ArenaApi api, HeroInventoryApi inventory, ArtifactInventoryApi artifacts,
            EquipmentInventoryApi equipment, SummonApi summon, Uri endpoint, string playerName, DesktopDisplayMode display)
        {
            this.api = api;
            this.artifacts = artifacts;
            this.equipment = equipment;
            this.summon = summon;
            this.endpoint = endpoint;
            this.views = views;
            this.assets = assets;
            this.scenes = scenes;
            this.parent = parent;
            this.lifetime = lifetime;
            this.fade = fade;
            this.locale = locale;
            pages = new HomePagePreparation(views, assets, parent, lifetime);
            firstReads = new HomeFirstReads(inventory, artifacts, equipment, lifetime.Token);
            lifetime.Add(portraits.Clear);
            summon.InventoryGranted += firstReads.ApplyGrants;
            summon.InventoryUncertain += firstReads.ReconcileInventories;
            artifacts.InventoryMayChange += firstReads.InvalidateArtifacts;
            equipment.InventoryChanged += firstReads.ApplyEquipment;
            equipment.InventoryUncertain += firstReads.ReconcileEquipment;
            lifetime.Add(() =>
            {
                summon.InventoryGranted -= firstReads.ApplyGrants;
                summon.InventoryUncertain -= firstReads.ReconcileInventories;
                artifacts.InventoryMayChange -= firstReads.InvalidateArtifacts;
                equipment.InventoryChanged -= firstReads.ApplyEquipment;
                equipment.InventoryUncertain -= firstReads.ReconcileEquipment;
            });
            token = lifetime.Token;
            Application.focusChanged += OnFocusChanged;
            lifetime.Add(() => Application.focusChanged -= OnFocusChanged);
            windows = new WindowCoordinator(token);
            lifetime.Add(windows.Dispose);
            home = new HomeWindowBinder(homeView, locale, OpenDirectory, OpenMore,
                ct => OpenInventory(false, ct), display);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, windows, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, windows, ReturnToDirectory, OpenDraft);
            BattleOnlineLaunch.Returning += ReturnFromBattleAsync;
            lifetime.Add(() => BattleOnlineLaunch.Returning -= ReturnFromBattleAsync);
        }

        private void OnFocusChanged(bool focused)
        {
            if (!focused) { lostFocus = true; return; }
            if (!lostFocus) return;
            lostFocus = false;
            if (equipmentFocusRead == null || equipmentFocusRead.IsCompleted)
                equipmentFocusRead = RefreshEquipmentOnReturnAsync();
        }

        private async Task RefreshEquipmentOnReturnAsync()
        {
            var current = firstReads.Equipment;
            if (current == null || token.IsCancellationRequested) return;
            try { await equipment.RefreshIfChangedAsync(current.version, token); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                Debug.LogWarning("Equipment version check: " + error.GetType().Name);
            }
        }

        internal static async Task<StartupNavigation> StartAsync(GameLaunchContext context, BindingScope owner)
        {
            var fade = new ScreenFadeTransition(owner);
            StartupNavigation navigation = null;
            await fade.RunAsync(async () => { navigation = await LoadHomeAsync(context, owner, fade); }, owner.Token);
            return navigation;
        }

        private static async Task<StartupNavigation> LoadHomeAsync(GameLaunchContext context, BindingScope owner, ScreenFadeTransition fade)
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
            var equipment = new EquipmentInventoryApi(endpoint, Bearer, owner.Token);
            var summon = new SummonApi(endpoint, Bearer, owner.Token);
            owner.Add(inventory.Dispose);
            owner.Add(artifacts.Dispose);
            owner.Add(equipment.Dispose);
            owner.Add(summon.Dispose);
            var navigation = new StartupNavigation(owner, fade, home, directory, create, room, views, assets, scenes,
                context.ViewRoot, locale, api, inventory, artifacts, equipment, summon, endpoint, name, display);
            await Task.WhenAll(navigation.pages.PrepareAsync(owner.Token),
                navigation.firstReads.PrepareAsync(Bearer, summon, owner.Token));
            var first = navigation.firstReads.Heroes?.heroes?.OrderByDescending(hero => hero.level).FirstOrDefault();
            if (first != null)
            {
                try
                {
                    var catalog = await navigation.pages.Heroes.Get();
                    await navigation.portraits.PrepareAsync(catalog.heroes.FirstOrDefault(hero => hero.id == first.templateId), owner.Token);
                }
                catch (Exception error) when (!owner.Token.IsCancellationRequested)
                {
                    Debug.LogWarning("Initial hero portrait preparation: " + error.GetType().Name);
                }
            }
            owner.Token.ThrowIfCancellationRequested();
            await navigation.ShowHome();
            if (Application.isEditor || Debug.isDebugBuild)
                await navigation.InstallDevelopmentGmAsync(Bearer);
            context.Services.Add<ILocaleService>(locale);
            context.OpenButton.gameObject.SetActive(false);
            context.StartupStatus.gameObject.SetActive(false);
            return navigation;
        }

        private async Task InstallDevelopmentGmAsync(Func<CancellationToken, Task<string>> bearer)
        {
            views.Register<DevelopmentGmWindow>();
            var gmView = await views.CreateAsync<DevelopmentGmWindow>(assets, parent, lifetime);
            var client = new DevelopmentGmApi(endpoint, bearer);
            lifetime.Add(client.Dispose);
            string Name(string kind, string id)
            {
                if (kind == "hero") return pages.Heroes.Loaded?.heroes.FirstOrDefault(item => item.id == id)?.name;
                if (kind == "artifact") return pages.Artifacts.Loaded?.Find(id)?.template.Name;
                if (kind == "equipment" && id.StartsWith("training_", StringComparison.Ordinal))
                {
                    var slot = id.Substring(id.LastIndexOf('_') + 1);
                    try { return GameText.Format(locale, "home.gear.title", locale.Resolve("home.gear." + slot)); }
                    catch (System.Collections.Generic.KeyNotFoundException) { return id; }
                }
                return null;
            }
            void Reconcile()
            {
                firstReads.ReconcileInventories();
                firstReads.ReconcileEquipment();
            }
            new DevelopmentGmWindowBinder(gmView, windows, client, locale, Name, Reconcile,
                () => refreshGmPage?.Invoke() ?? Task.CompletedTask).Install(lifetime);
        }

        private async Task OpenMore(CancellationToken cancellation)
        {
            if (windows.IsOverlayOpen(MoreOverlay)) { windows.CloseOverlay(MoreOverlay); return; }
            var view = await pages.More.Get();
            cancellation.ThrowIfCancellationRequested();
            windows.ShowOverlay(MoreOverlay, scope =>
            {
                home.HideArenaWhile(scope);
                var binder = new HomeMoreMenuBinder(view, locale, OpenHeroes, OpenSummon,
                    () => windows.CloseOverlay(MoreOverlay));
                binder.Open(scope);
            });
        }

        private Task OpenHeroes(CancellationToken request)
        {
            request.ThrowIfCancellationRequested();
            windows.CloseOverlay(MoreOverlay);
            return fade.RunAsync(() => OpenInventory(true, token), token);
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
                if (!(preview.texture is RenderTexture live) || !live.IsCreated())
                    throw new InvalidOperationException("The packaged skill preview has no live battle render target.");
                Debug.Log($"ASTRAL_SUMMON_FEATURED_LIVE target={live.width}x{live.height} screen={Screen.width}x{Screen.height}");
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
                refreshGmPage = () => ShowSummonPageAsync(token, isolatedGuest);
                scope.Add(() => refreshGmPage = null);
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
                refreshGmPage = () => OpenInventory(roster, token);
                scope.Add(() => refreshGmPage = null);
                Func<string, CancellationToken, Task> startTrial = null;
                if (Debug.isDebugBuild && endpoint.IsLoopback)
                    startTrial = (hero, ct) => StartArtifactTrialAsync(hero, artifactArt, ct);
                var binder = new HeroInventoryBinder(view, art, locale, firstReads.ReadHeroesAsync, ShowHome,
                    artifactArt, artifacts, startTrial, firstReads.ReadArtifactsAsync, portraits, equipment, firstReads.ReadEquipmentAsync);
                return binder.OpenAsync(roster, scope, firstReads.Heroes, firstReads.Artifacts, firstReads.Equipment);
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
                var membership = await api.CurrentAsync(token);
                token.ThrowIfCancellationRequested();
                if (membership?.room != null)
                    await OpenRoom(membership, token);
                else
                    await ShowDirectory(directory.OpenAfterBattleAsync);
            }
            catch (ArenaRequestException) when (!token.IsCancellationRequested)
            {
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
