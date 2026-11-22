using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            internal T Loaded => resident?.Status == TaskStatus.RanToCompletion ? resident.Result : default;
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
        private readonly Snapshot<HeroInventorySnapshot> heroes;
        private readonly Snapshot<ArtifactInventorySnapshot> artifacts;
        private readonly Snapshot<EquipmentInventorySnapshot> equipment;
        private long lastEquipmentVersion = -1;
        private SummonState summon;

        internal HomeFirstReads(HeroInventoryApi heroApi, ArtifactInventoryApi artifactApi, EquipmentInventoryApi equipmentApi, CancellationToken login)
        {
            heroes = new Snapshot<HeroInventorySnapshot>(heroApi.FetchAsync, login);
            artifacts = new Snapshot<ArtifactInventorySnapshot>(artifactApi.FetchAsync, login);
            equipment = new Snapshot<EquipmentInventorySnapshot>(equipmentApi.FetchAsync, login);
        }

        // A page can stop waiting without cancelling another page's read of the same account.
        // A write detaches the pending read so it cannot repopulate the cache after invalidation.
        private sealed class Snapshot<T> where T : class
        {
            private readonly Func<CancellationToken, Task<T>> fetch;
            private readonly CancellationToken login;
            private Task<T> current;
            internal Snapshot(Func<CancellationToken, Task<T>> fetch, CancellationToken login)
            {
                this.fetch = fetch;
                this.login = login;
            }
            internal T Value => current?.Status == TaskStatus.RanToCompletion ? current.Result : null;
            internal bool IsReading => current != null && !current.IsCompleted;
            internal void Invalidate() => current = null;
            internal void Replace(T snapshot) => current = Task.FromResult(snapshot);
            internal void Reconcile()
            {
                Invalidate();
                if (!login.IsCancellationRequested) _ = Optional(GetAsync, login);
            }

            internal async Task<T> GetAsync(CancellationToken caller)
            {
                while (true)
                {
                    caller.ThrowIfCancellationRequested();
                    login.ThrowIfCancellationRequested();
                    if (current == null || current.IsCanceled || current.IsFaulted) current = fetch(login);
                    var read = current;
                    if (!read.IsCompleted)
                    {
                        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        using var registration = caller.Register(() => cancelled.TrySetResult(true));
                        await Task.WhenAny(read, cancelled.Task);
                        caller.ThrowIfCancellationRequested();
                    }
                    if (read == current) return await read;
                }
            }
        }

        internal Task<HeroInventorySnapshot> ReadHeroesAsync(CancellationToken token) => heroes.GetAsync(token);
        internal Task<ArtifactInventorySnapshot> ReadArtifactsAsync(CancellationToken token) => artifacts.GetAsync(token);
        internal async Task<EquipmentInventorySnapshot> ReadEquipmentAsync(CancellationToken token)
        {
            var value = await equipment.GetAsync(token);
            lastEquipmentVersion = Math.Max(lastEquipmentVersion, value.version);
            return value;
        }
        internal EquipmentInventorySnapshot Equipment => equipment.Value;

        internal void ApplyEquipment(EquipmentInventorySnapshot updated)
        {
            // A replayed request can return an older, already committed receipt.
            if (updated.version < Math.Max(lastEquipmentVersion, equipment.Value?.version ?? -1) ||
                (updated.version == lastEquipmentVersion && equipment.Value == null)) return;
            lastEquipmentVersion = updated.version;
            // If another client forced reconciliation, the outstanding GET might predate this write.
            if (equipment.IsReading) equipment.Reconcile();
            else equipment.Replace(updated);
        }

        internal void ReconcileEquipment() => equipment.Reconcile();

        internal async Task PrepareAsync(Func<CancellationToken, Task<string>> session, SummonApi summonApi, CancellationToken lifetime)
        {
            started = Time.realtimeSinceStartupAsDouble;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                // Establish one guest identity before launching this account's reads in parallel.
                await session(deadline.Token);
                var heroRead = Optional(ReadHeroesAsync, deadline.Token);
                var artifactRead = Optional(ReadArtifactsAsync, deadline.Token);
                var equipmentRead = Optional(ReadEquipmentAsync, deadline.Token);
                var summonRead = ReadSummon();
                await Task.WhenAll(heroRead, artifactRead, equipmentRead, summonRead);
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
        internal HeroInventorySnapshot Heroes => heroes.Value;
        internal ArtifactInventorySnapshot Artifacts => artifacts.Value;

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

        internal void ApplyGrants(SummonReward[] rewards)
        {
            summon = null;
            if (rewards.Length == 0) return;
            if (Array.Exists(rewards, reward => reward.kind == "hero"))
            {
                var roster = heroes.Value;
                if (roster == null) heroes.Reconcile();
                else
                {
                    var ids = new HashSet<string>(roster.heroes.Select(item => item.instanceId), StringComparer.Ordinal);
                    var added = rewards.Where(r => r.kind == "hero" && ids.Add(r.instanceId)).Select(r =>
                        new HeroInventoryItem { instanceId = r.instanceId, templateId = r.templateId, stars = r.stars, level = 1 }).ToArray();
                    if (roster.used + added.Length > roster.capacity) heroes.Reconcile();
                    else if (added.Length > 0) heroes.Replace(new HeroInventorySnapshot { capacity = roster.capacity,
                        used = roster.used + added.Length, heroes = roster.heroes.Concat(added).ToArray() });
                }
            }
            if (Array.Exists(rewards, reward => reward.kind == "artifact"))
            {
                var bag = artifacts.Value;
                if (bag == null) artifacts.Reconcile();
                else
                {
                    var ids = new HashSet<string>(bag.artifacts.Select(item => item.instanceId), StringComparer.Ordinal);
                    var added = rewards.Where(r => r.kind == "artifact" && ids.Add(r.instanceId)).Select(r =>
                        new ArtifactInventoryItem { instanceId = r.instanceId, templateId = r.templateId, source = "summon" }).ToArray();
                    if (bag.used + added.Length > bag.capacity) artifacts.Reconcile();
                    else if (added.Length > 0) artifacts.Replace(new ArtifactInventorySnapshot { capacity = bag.capacity,
                        used = bag.used + added.Length, artifacts = bag.artifacts.Concat(added).ToArray(), materials = bag.materials });
                }
            }
        }

        internal void ReconcileInventories()
        {
            summon = null;
            heroes.Reconcile();
            artifacts.Reconcile();
        }

        internal void InvalidateArtifacts()
        {
            artifacts.Invalidate();
            summon = null;
        }
    }
}
