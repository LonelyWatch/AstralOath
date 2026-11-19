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
        private const string MoreOverlay = "home-more";
        private readonly ArenaApi api;
        private readonly HeroInventoryApi inventory;
        private readonly ArtifactInventoryApi artifacts;
        private readonly SummonApi summon;
        private readonly Uri endpoint;
        private readonly CancellationToken token;
        private string roomId;

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
            token = lifetime.Token;
            windows = new WindowCoordinator(token);
            lifetime.Add(windows.Dispose);
            home = new HomeWindowBinder(homeView, locale, OpenDirectory, OpenMore,
                ct => OpenInventory(false, ct), display);
            directory = new ArenaDirectoryBinder(directoryView, createView, locale, api, windows, OpenRoom, ShowHome, playerName);
            room = new ArenaRoomBinder(roomView, locale, api, windows, ReturnToDirectory, OpenDraft);
            BattleOnlineLaunch.Exited += OnBattleExited;
            lifetime.Add(() => BattleOnlineLaunch.Exited -= OnBattleExited);
        }

        internal static async Task<StartupNavigation> StartAsync(GameLaunchContext context, BindingScope owner)
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
            await navigation.ShowHome();
            context.Services.Add<ILocaleService>(locale);
            context.OpenButton.gameObject.SetActive(false);
            context.StartupStatus.gameObject.SetActive(false);
            return navigation;
        }

        private async Task OpenMore(CancellationToken cancellation)
        {
            if (windows.IsOverlayOpen(MoreOverlay)) { windows.CloseOverlay(MoreOverlay); return; }
            var loading = new BindingScope(token);
            var detach = lifetime.Add(loading.Dispose);
            try
            {
                var view = await views.CreateAsync<HomeMoreMenu>(assets, parent, loading);
                cancellation.ThrowIfCancellationRequested();
                windows.ShowOverlay(MoreOverlay, scope =>
                {
                    home.HideArenaWhile(scope);
                    scope.Add(detach);
                    scope.Add(loading.Dispose);
                    var binder = new HomeMoreMenuBinder(view, locale, ct => OpenInventory(true, ct), OpenSummon,
                        () => windows.CloseOverlay(MoreOverlay));
                    binder.Open(scope);
                });
            }
            catch
            {
                detach();
                loading.Dispose();
                throw;
            }
        }

        internal async Task VerifySummonAsync(CancellationToken cancellation)
        {
            await OpenMore(cancellation);
            var view = await ShowSummonPageAsync(cancellation);
            var bindings = view.Root.GetComponent<BindingMap>();
            bool covenant = bindings.Require<GameButton>("covenant").interactable;
            bool iseria = bindings.Require<GameButton>("iseria").interactable;
            if (!view.Root.activeInHierarchy || !covenant || !iseria)
                throw new InvalidOperationException($"The packaged Summon page failed to activate both banners. Active={view.Root.activeInHierarchy}, covenant={covenant}, iseria={iseria}.");

            var preview = bindings.Require<UnityEngine.UI.RawImage>("featuredSkillCanvas");
            var poster = bindings.Require<CanvasGroup>("featuredActorLayer");
            while (!preview.enabled || !preview.texture || poster.alpha > .05f || preview.color.a < .95f)
                await Task.Delay(50, cancellation);
            bindings.Require<GameButton>("covenant").onClick.Invoke();
            if (preview.enabled || !bindings.Require<GameObject>("wishRoster").activeInHierarchy)
                throw new InvalidOperationException("The covenant roster did not replace the featured preview.");
            bindings.Require<GameButton>("wish").onClick.Invoke();
            bindings.Require<GameButton>("wishArtifact").onClick.Invoke();
            if (!bindings.Require<GameObject>("wishArtifactHighlight").activeInHierarchy ||
                bindings.Require<RectTransform>("wishSelectedArtifacts").childCount < 7)
                throw new InvalidOperationException("The packaged artifact wishlist did not display its selected items.");
            bindings.Require<GameButton>("wishClose").onClick.Invoke();
        }

        private Task OpenSummon(CancellationToken cancellation) => ShowSummonPageAsync(cancellation);

        private async Task<SummonWindow> ShowSummonPageAsync(CancellationToken cancellation)
        {
            var loading = new BindingScope(token);
            var detach = lifetime.Add(loading.Dispose);
            bool opened = false;
            try
            {
                var view = await views.CreateAsync<SummonWindow>(assets, parent, loading);
                var art = await assets.LoadAsync<HomeHeroArt>(HomeHeroArt.Address, loading);
                var artifacts = await assets.LoadAsync<ArtifactArtCatalog>(ArtifactArtCatalog.Address, loading);
                var presentation = await assets.LoadAsync<SummonPresentation>(SummonPresentation.Address, loading);
                var featuredLoop = await assets.LoadAsync<SummonSkillLoop>(SummonSkillLoop.Address, loading);
                cancellation.ThrowIfCancellationRequested();
                await windows.ShowPageAsync(scope =>
                {
                    scope.Add(detach);
                    scope.Add(loading.Dispose);
                    var binder = new SummonWindowBinder(view, summon, art, artifacts, presentation, featuredLoop, locale, ShowHome);
                    opened = true;
                    return binder.OpenAsync(scope);
                });
                return view;
            }
            finally
            {
                if (!opened) { detach(); loading.Dispose(); }
            }
        }

        private async Task OpenInventory(bool roster, CancellationToken cancellation)
        {
            var loading = new BindingScope(token);
            var detach = lifetime.Add(loading.Dispose);
            bool opened = false;
            try
            {
                var view = await views.CreateAsync<HeroInventory>(assets, parent, loading);
                var art = await assets.LoadAsync<HomeHeroArt>(HomeHeroArt.Address, loading);
                var artifactArt = await assets.LoadAsync<ArtifactArtCatalog>(ArtifactArtCatalog.Address, loading);
                cancellation.ThrowIfCancellationRequested();
                await windows.ShowPageAsync(scope =>
                {
                    scope.Add(detach);
                    scope.Add(loading.Dispose);
                    Func<string, CancellationToken, Task> startTrial = null;
                    if (Debug.isDebugBuild && endpoint.IsLoopback)
                        startTrial = (hero, ct) => StartArtifactTrialAsync(hero, artifactArt, ct);
                    var binder = new HeroInventoryBinder(view, art, locale, inventory.FetchAsync, ShowHome,
                        artifactArt, artifacts, startTrial);
                    opened = true;
                    return binder.OpenAsync(roster, scope);
                });
            }
            finally
            {
                if (!opened) { detach(); loading.Dispose(); }
            }
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
            return windows.ShowPageAsync(scope => room.OpenAsync(state, scope));
        }

        private async Task OpenDraft(ArenaDraftState state)
        {
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

        private async Task LeaveDraftRoomAsync(CancellationToken cancellation)
        {
            await api.LeaveAsync(roomId, cancellation);
            await ReturnToDirectory();
        }

        private async void OnBattleExited()
        {
            if (token.IsCancellationRequested || string.IsNullOrEmpty(roomId)) return;
            try { await LeaveDraftRoomAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ArenaRequestException) { await ReturnToDirectory(); }
        }

        private Task ReturnToDirectory()
        {
            roomId = null;
            return windows.ShowPageAsync(directory.OpenAsync);
        }

        private Task ShowHome() => windows.ShowPageAsync(home.OpenAsync);
    }
}
