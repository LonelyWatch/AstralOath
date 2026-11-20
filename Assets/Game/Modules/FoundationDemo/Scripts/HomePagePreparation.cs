using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Home;
using AstralOath.Client.Summon;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    // The current login owns the three hidden prefab instances and their four shared source assets.
    // Binders still own actors, render targets, event handlers and rows for an open page.
    internal sealed class HomePagePreparation
    {
        internal sealed class Resource<T>
        {
            private readonly Func<Task<T>> load;
            private Task<T> resident;
            internal Resource(Func<Task<T>> load) => this.load = load;
            internal Task<T> Get() => resident == null || resident.IsCanceled || resident.IsFaulted ? resident = load() : resident;
        }

        internal readonly Resource<HomeMoreMenu> More;
        internal readonly Resource<HeroInventory> Inventory;
        internal readonly Resource<SummonWindow> Summon;
        internal readonly Resource<HomeHeroArt> Heroes;
        internal readonly Resource<ArtifactArtCatalog> Artifacts;
        internal readonly Resource<SummonPresentation> Presentation;
        internal readonly Resource<SummonSkillLoop> SkillLoop;

        internal HomePagePreparation(UIViewRegistry views, IAssetService assets, Transform parent, BindingScope owner)
        {
            More = new Resource<HomeMoreMenu>(() => views.CreateAsync<HomeMoreMenu>(assets, parent, owner));
            Inventory = new Resource<HeroInventory>(() => views.CreateAsync<HeroInventory>(assets, parent, owner));
            Summon = new Resource<SummonWindow>(() => views.CreateAsync<SummonWindow>(assets, parent, owner));
            Heroes = new Resource<HomeHeroArt>(() => assets.LoadAsync<HomeHeroArt>(HomeHeroArt.Address, owner));
            Artifacts = new Resource<ArtifactArtCatalog>(() => assets.LoadAsync<ArtifactArtCatalog>(ArtifactArtCatalog.Address, owner));
            Presentation = new Resource<SummonPresentation>(() => assets.LoadAsync<SummonPresentation>(SummonPresentation.Address, owner));
            SkillLoop = new Resource<SummonSkillLoop>(() => assets.LoadAsync<SummonSkillLoop>(SummonSkillLoop.Address, owner));
        }

        internal async Task PrepareAsync(CancellationToken token)
        {
            try
            {
                await Task.WhenAll(More.Get(), Inventory.Get(), Summon.Get(), Heroes.Get(), Artifacts.Get(), Presentation.Get(), SkillLoop.Get());
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                // A failed optional page retries its own load when selected.
                Debug.LogWarning("Home page preparation: " + error.GetType().Name);
            }
            token.ThrowIfCancellationRequested();
        }
    }

    internal sealed class HomeFirstReads
    {
        private const double FreshSeconds = 30;
        private double started, summonReceived;
        private HeroInventorySnapshot heroes;
        private ArtifactInventorySnapshot artifacts;
        private SummonState summon;

        internal async Task PrepareAsync(Func<CancellationToken, Task<string>> session, HeroInventoryApi heroApi,
            ArtifactInventoryApi artifactApi, SummonApi summonApi, CancellationToken lifetime)
        {
            started = Time.realtimeSinceStartupAsDouble;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                // For development guests establish one identity before dispatching the three GETs.
                await session(deadline.Token);
                var heroRead = Optional(heroApi.FetchAsync, deadline.Token);
                var artifactRead = Optional(artifactApi.FetchAsync, deadline.Token);
                var summonRead = ReadSummon();
                await Task.WhenAll(heroRead, artifactRead, summonRead);
                heroes = await heroRead;
                artifacts = await artifactRead;
                summon = await summonRead;
            }
            catch (Exception error) when (!lifetime.IsCancellationRequested)
            {
                Debug.LogWarning("Home account preparation: " + error.GetType().Name);
            }
            lifetime.ThrowIfCancellationRequested();

            async Task<SummonState> ReadSummon()
            {
                var state = await Optional(summonApi.GetAsync, deadline.Token);
                summonReceived = Time.realtimeSinceStartupAsDouble;
                return state;
            }
        }

        private static async Task<T> Optional<T>(Func<CancellationToken, Task<T>> read, CancellationToken token) where T : class
        {
            try { return await read(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
            catch (Exception error)
            {
                Debug.LogWarning("Home account read: " + error.GetType().Name);
                return null;
            }
        }

        private bool Fresh => Time.realtimeSinceStartupAsDouble - started <= FreshSeconds;
        internal HeroInventorySnapshot Heroes => Fresh ? heroes : null;
        internal ArtifactInventorySnapshot Artifacts => Fresh ? artifacts : null;

        internal SummonState TakeSummon()
        {
            var state = summon;
            summon = null; // The binder mutates this snapshot after an authoritative receipt.
            if (!Fresh || state == null) return null;
            if (Parse(state.serverTimeUtc, out var serverTime))
            {
                var now = serverTime.AddSeconds(Time.realtimeSinceStartupAsDouble - summonReceived);
                bool Changed(string boundary) => Parse(boundary, out var instant) && serverTime < instant && instant <= now;
                if (Changed(state.freeResetUtc) || Array.Exists(state.banners ?? Array.Empty<SummonBanner>(),
                    banner => Changed(banner.startUtc) || Changed(banner.endUtc))) return null;
                state.serverTimeUtc = now.ToString("O", CultureInfo.InvariantCulture);
            }
            return state;
        }

        private static bool Parse(string value, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);

        internal void Invalidate()
        {
            heroes = null;
            artifacts = null;
            summon = null;
        }
    }
}
