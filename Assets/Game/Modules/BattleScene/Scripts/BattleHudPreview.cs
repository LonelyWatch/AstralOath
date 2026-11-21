using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using Unity.Profiling;

namespace AstralOath.Client.BattleScene
{
    // Scene-only host. BattleHud.prefab contains no feature-specific MonoBehaviour.
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class BattleHudPreview : MonoBehaviour
    {
        [Serializable]
        internal sealed class SpineEffect
        {
            public TextAsset skeleton;
            public TextAsset atlas;
            public Texture2D page;

            internal SkeletonPlayerSource CreateSource(float scale)
            {
                var pages = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { page.name + ".sct", page } };
                return new SkeletonPlayerSource(skeleton.text, atlas.text, pages, premultipliedAlpha: true, scale: scale);
            }

            internal ISkeletonPlayer Create(Transform anchor, string name, float scale, int sortingOrder)
            {
                var player = new SkeletonPlayerFactory().Create(CreateSource(scale), anchor);
                player.Root.name = name;
                var renderer = player.Root.GetComponent<MeshRenderer>();
                var canvas = anchor.GetComponentInParent<Canvas>();
                renderer.sortingLayerID = canvas.sortingLayerID;
                renderer.sortingOrder = sortingOrder;
                player.Root.SetActive(false);
                return player;
            }
        }

        [SerializeField] private BindingMap hud;
        [SerializeField] private GameObject inspectionPrefab;
        [SerializeField] private GameObject confirmPrefab;
        [SerializeField] private bool autoStartBattle = true;
        [SerializeField] private Sprite burnPressed;
        [SerializeField] private Sprite cancelNormal;
        [SerializeField] private Sprite cancelPressed;
        [SerializeField] private SpineEffect burnButton;
        [SerializeField] private SpineEffect skillSelection;
        [SerializeField] private SpineEffect burnIntro;
        [SerializeField] private SpineEffect soulUse;
        [SerializeField] private SpineEffect soulDisplay;
        [SerializeField] private SpineEffect soulConsume;
        [SerializeField] private BattleSoulFlights.Art soulGain;
        [SerializeField] private SpineEffect readyDust;
        [SerializeField] private TextAsset[] particles;
        [SerializeField] private Texture2D[] particleTextures;
        [SerializeField] private TextAsset battleCatalog;
        [SerializeField] private TMP_FontAsset battleDamageFont;
        [SerializeField] private TextAsset[] battleGraphs;
        [SerializeField] private TextAsset skillTimelineOverrides;
        [SerializeField] private Sprite[] battleSprites;
        [SerializeField] private BattleSkillPresentationAsset[] skillAssets;
        [SerializeField] private TextAsset audioCatalog;
        [SerializeField] private TextAsset[] audioBanks;
        [SerializeField] private TextAsset impactBank;
        [SerializeField] private GameObject stageBackground;
        [Serializable]
        internal sealed class ModelForm
        {
            public string status;
            public string skin;
            public string[] graphs;
            public BattleSkillPresentationAsset skeleton;
            [NonSerialized] internal BattlePreviewActor.SharedSource prepared;
            [NonSerialized] internal BattleSkeletonMetadata metadata;
        }

        [Serializable]
        private sealed class HeroModel
        {
            public string id;
            public string label;
            public string skin;
            public BattleSkillPresentationAsset skeleton;
            public ModelForm[] forms;
        }

        public const int StandardSoulCapacity = BattleFlowRules.StandardMaximumSoul;

        public static IReadOnlyList<string> DefaultAllyHeroIds { get; } = Array.AsReadOnly(new[] { "c1001", "c1062", "c1005", "c1002" });
        public static IReadOnlyList<string> DefaultEnemyHeroIds { get; } = Array.AsReadOnly(new[] { "c1003", "c1004", "c1006", "c1007" });

        [SerializeField, HideInInspector] private string[] allyHeroIds;
        [SerializeField, HideInInspector] private string[] enemyHeroIds;
        [SerializeField, HideInInspector] private HeroModel[] heroModels;
        private BattleHudBinder binder;
        private BattleSimulationPreview simulation;
        private Func<string, (string Name, Sprite Icon)> artifactCue;
        private BattleOnlineSession online;
        private BattleOnlineArrival arrival;
        private CanvasGroup entryHud, entryUnitStatus;
        private GameText stageTitle;
        private BattleOnlineTransitionAssets.Copy onlineCopy;
        private readonly SemaphoreSlim modelWorkers = new SemaphoreSlim(2);
        private readonly Dictionary<string, Task<BattlePreviewActor.SharedSource>> preparedModels =
            new Dictionary<string, Task<BattlePreviewActor.SharedSource>>(StringComparer.Ordinal);

        private static readonly ProfilerMarker StartMarker = new ProfilerMarker("Astral.Battle.Start");
        private static readonly ProfilerMarker FrameMarker = new ProfilerMarker("Astral.Battle.Frame");

        public bool AutoStartBattle { get => autoStartBattle; set => autoStartBattle = value; }
        public bool IsPaused => simulation?.IsPaused ?? false;
        public void PauseBattle() => binder?.PauseBattle();
        public void ResumeBattle() => binder?.ResumeBattle();
        public void RestartBattle() => StartBattle();
        public void SetArtifactCueLookup(Func<string, (string Name, Sprite Icon)> lookup) => artifactCue = lookup;

        public BattleSnapshot SimulationSnapshot => simulation?.Snapshot;
        public int StartedSkillVoices => simulation?.StartedSkillVoices ?? 0;
        public int StartedSkillSounds => simulation?.StartedSkillSounds ?? 0;
        public int StartedHitSounds => simulation?.StartedHitSounds ?? 0;
        public bool IsCinematicVisible => simulation?.IsCinematicVisible ?? false;

        public void StartBattle()
        {
            using var sample = StartMarker.Auto();
            if (online != null) return;
            if (!Application.isPlaying || !hud || !hud.isActiveAndEnabled || !battleCatalog)
                throw new InvalidOperationException("Open the battle scene in Play mode before starting.");
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            var units = gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var allyActors = GetTeamActors(units.Find("Allies"), DefaultAllyHeroIds.Count);
            var enemyActors = GetTeamActors(units.Find("Enemies"), DefaultEnemyHeroIds.Count);
            var allies = SelectHeroes(allyActors, allyHeroIds, DefaultAllyHeroIds);
            var enemies = SelectHeroes(enemyActors, enemyHeroIds, DefaultEnemyHeroIds);
            var nextMatch = BattlePreviewFactory.Create("local", 271828UL, catalog, allyHeroIds: allies, enemyHeroIds: enemies);
            InstallBattle(catalog, allyActors, enemyActors, allies, enemies, nextMatch);
        }

        internal BattleCatalogData AcceptOnlineSnapshot(BattleOnlineSession session,
            (JObject Payload, double SentAt, double ReceivedAt) opening)
        {
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            session.Open(battleCatalog, catalog, opening);
            return catalog;
        }

        internal Task WarmOnlineModelsAsync(IEnumerable<string> heroIds, CancellationToken token) =>
            Task.WhenAll(QueueOnlineModels(heroIds, ReadHeroModels(), token).Values);

        private Dictionary<string, Task<BattlePreviewActor.SharedSource>> QueueOnlineModels(
            IEnumerable<string> heroIds, IReadOnlyDictionary<string, HeroModel> models, CancellationToken token)
        {
            var requested = new Dictionary<string, Task<BattlePreviewActor.SharedSource>>(StringComparer.Ordinal);
            foreach (string id in heroIds.Distinct(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (!models.TryGetValue(id, out var model))
                    throw new InvalidOperationException("Missing battle model for " + id);
                if (!preparedModels.TryGetValue(id, out var preparation))
                {
                    preparation = BattlePreviewActor.WarmModelAsync(gameObject.scene, model.skeleton, model.skin, model.label, modelWorkers, token);
                    preparedModels.Add(id, preparation);
                }
                requested.Add(id, preparation);
                foreach (var form in model.forms ?? Array.Empty<ModelForm>())
                {
                    string key = form.skeleton.source;
                    if (!preparedModels.TryGetValue(key, out var alternate))
                    {
                        alternate = BattlePreviewActor.WarmModelAsync(gameObject.scene, form.skeleton, form.skin, model.label, modelWorkers, token);
                        preparedModels.Add(key, alternate);
                    }
                    requested[key] = alternate;
                }
            }
            return requested;
        }

        internal async Task ConfigureOnlineRosterAsync(BattleSnapshot snapshot, CancellationToken token)
        {
            var models = ReadHeroModels();
            string[] allies = OnlineTeam(snapshot, BattleSide.Ally);
            string[] enemies = OnlineTeam(snapshot, BattleSide.Enemy);
            var requested = QueueOnlineModels(allies.Concat(enemies), models, token);
            await Task.WhenAll(requested.Values);
            token.ThrowIfCancellationRequested();
            var loaded = requested.ToDictionary(entry => entry.Key, entry => entry.Value.Result, StringComparer.Ordinal);
            preparedModels.Clear();

            var units = gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            ApplyTeam(ResolveTeam(UseOnlineSlots(units.Find("Allies"), allies.Length), allies, models), loaded);
            ApplyTeam(ResolveTeam(UseOnlineSlots(units.Find("Enemies"), enemies.Length), enemies, models), loaded);
        }

        private static string[] OnlineTeam(BattleSnapshot snapshot, BattleSide side) =>
            snapshot.Units.Where(unit => unit.Slot.Side == side)
                .OrderBy(unit => unit.Slot.Position).Select(unit => unit.DefinitionId).ToArray();

        private static BattlePreviewActor[] UseOnlineSlots(Transform team, int count)
        {
            var slots = GetTeamActors(team, 4);
            for (int index = count; index < slots.Length; index++)
                team.GetChild(index).gameObject.SetActive(false);
            return slots.Take(count).ToArray();
        }

        internal void PrepareOnline(BattleOnlineSession session, BattleOnlineTransitionAssets assets, BattleCatalogData catalog)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            autoStartBattle = false;
            var units = gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            string[] allies = OnlineTeam(session.Snapshot, BattleSide.Ally);
            string[] enemies = OnlineTeam(session.Snapshot, BattleSide.Enemy);
            var allyActors = UseOnlineSlots(units.Find("Allies"), allies.Length);
            var enemyActors = UseOnlineSlots(units.Find("Enemies"), enemies.Length);
            online = session;
            onlineCopy = BattleOnlineTransitionAssets.Copy.Load(assets.zhHans);
            var viewport = GetComponent<Canvas>().worldCamera.GetComponent<BattlePreviewViewport>();
            viewport.UseOnlineBackdrop(InstallOnlineBackdrop(assets.arenaBackdrop));
            viewport.PositionOnlineFormation(allyActors, enemyActors);
            entryHud = hud.gameObject.AddComponent<CanvasGroup>();
            entryHud.alpha = 0f;
            entryHud.blocksRaycasts = false;
            double layoutReady = Time.realtimeSinceStartupAsDouble;
            var timings = InstallBattle(catalog, allyActors, enemyActors, allies, enemies, null, assets);
            if (session.IsOwnedTrial) binder.ShowOnlineReturn();
            double battleReady = Time.realtimeSinceStartupAsDouble;
            arrival = new BattleOnlineArrival(units, viewport, allyActors, enemyActors, assets);
            arrival.Prepare();
            if (Debug.isDebugBuild)
            {
                static string Ms(double start, double end) => ((end - start) * 1000d).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                Debug.Log("ASTRAL_ARENA_HOST layoutMs=" + Ms(started, layoutReady) +
                    " modelsMs=" + Ms(layoutReady, timings.ModelsReady) + " binderMs=" + Ms(timings.ModelsReady, timings.BinderReady) +
                    " simulationMs=" + Ms(timings.BinderReady, battleReady) + " arrivalMs=" + Ms(battleReady, Time.realtimeSinceStartupAsDouble));
            }
        }

        internal void BeginOnlineArrival()
        {
            arrival?.Begin();
            // Level and HP follow the characters during the entry without enabling the action HUD.
            entryUnitStatus = hud.transform.Find("Unit Status").gameObject.AddComponent<CanvasGroup>();
            entryUnitStatus.ignoreParentGroups = true;
            entryUnitStatus.blocksRaycasts = false;
            if (online.IsOwnedTrial) return;
            var title = new GameObject("PvP Arena Title", typeof(RectTransform), typeof(GameText));
            title.transform.SetParent(transform, false);
            var rect = (RectTransform)title.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .615f);
            rect.sizeDelta = new Vector2(720f, 160f);
            rect.anchoredPosition = Vector2.zero;
            stageTitle = title.GetComponent<GameText>();
            var template = hud.Require<GameText>("wave");
            stageTitle.font = template.font;
            stageTitle.fontSharedMaterial = template.fontSharedMaterial;
            stageTitle.fontSize = 43f;
            stageTitle.fontStyle = FontStyles.Bold;
            stageTitle.alignment = TextAlignmentOptions.Center;
            stageTitle.text = "<size=32><color=#D8AD66>" + onlineCopy.arenaMode + "</color></size>\n" + onlineCopy.arenaTitle;
            stageTitle.raycastTarget = false;
        }

        private SpriteRenderer InstallOnlineBackdrop(Sprite sprite)
        {
            if (stageBackground) stageBackground.SetActive(false);
            stageBackground = new GameObject("RTA 021 Stage", typeof(SpriteRenderer));
            var renderer = stageBackground.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -70;
            return renderer;
        }

        internal bool OnlineArrivalIgnitionReady => arrival?.IgnitionReady ?? true;
        internal bool OnlineArrivalActorsReady => arrival?.ActorsReady ?? true;
        internal bool OnlineArrivalCameraBlending => arrival?.CameraBlending == true;
        internal bool OnlineArrivalComplete => arrival?.Complete ?? true;
        internal bool OnlineArrivalEffectsComplete => arrival?.EffectsComplete ?? true;

        internal void FinishOnlineEntry()
        {
            arrival?.Dispose();
            arrival = null;
            if (stageTitle)
            {
                stageTitle.gameObject.SetActive(false);
                Destroy(stageTitle.gameObject);
                stageTitle = null;
            }
            ReleaseEntryUnitStatus();
            entryHud.alpha = 1f;
            entryHud.blocksRaycasts = true;
            simulation?.FinishOnlineEntry();
        }

        private (double ModelsReady, double BinderReady) InstallBattle(BattleCatalogData catalog,
            BattlePreviewActor[] allyActors, BattlePreviewActor[] enemyActors,
            string[] allies, string[] enemies, BattleMatch nextMatch,
            BattleOnlineTransitionAssets onlineAssets = null)
        {
            var models = ReadHeroModels();
            var allySlots = ResolveTeam(allyActors, allies, models);
            var enemySlots = ResolveTeam(enemyActors, enemies, models);
            if (simulation != null) ClearBinding();
            ApplyTeam(allySlots);
            ApplyTeam(enemySlots);
            if (onlineAssets != null)
            {
                var viewport = GetComponent<Canvas>().worldCamera.GetComponent<BattlePreviewViewport>();
                viewport.ApplyOnlineActorScale(allyActors);
                viewport.ApplyOnlineActorScale(enemyActors);
            }
            double modelsReady = Time.realtimeSinceStartupAsDouble;
            UpdateBinding(catalog);
            double binderReady = Time.realtimeSinceStartupAsDouble;
            simulation = new BattleSimulationPreview(gameObject.scene, hud, binder, catalog, battleSprites, battleGraphs,
                skillAssets, audioCatalog, audioBanks, battleDamageFont, impactBank, stageBackground,
                soulDisplay, soulConsume, soulGain, particles, particleTextures, nextMatch, inspectionPrefab, readyDust: readyDust,
                skillTimelineOverrides: skillTimelineOverrides, online: online, onlineCopy: onlineCopy, onlineAssets: onlineAssets,
                artifactCue: artifactCue);
            binder.AttachSimulation(simulation, online != null);
            return (modelsReady, binderReady);
        }

        private Dictionary<string, HeroModel> ReadHeroModels() =>
            (heroModels ?? Array.Empty<HeroModel>()).ToDictionary(model => model.id, StringComparer.Ordinal);

        private static BattlePreviewActor[] GetTeamActors(Transform team, int size)
        {
            if (!team || team.childCount != size)
                throw new InvalidOperationException("Battle preview requires four slots per team.");
            var actors = new BattlePreviewActor[size];
            for (int index = 0; index < size; index++)
                actors[index] = team.GetChild(index).GetComponentsInChildren<BattlePreviewActor>(true).Single();
            return actors;
        }

        private static string[] SelectHeroes(BattlePreviewActor[] actors, string[] overrides, IReadOnlyList<string> defaults)
        {
            if (overrides is { Length: > 0 } && overrides.Length != actors.Length)
                throw new InvalidOperationException("Battle preview requires four roster overrides.");
            return actors.Select((actor, index) =>
            {
                string selected = overrides is { Length: > 0 } ? overrides[index] : null;
                if (!string.IsNullOrEmpty(selected)) return selected;
                return string.IsNullOrEmpty(actor.AuthoredHeroId) ? defaults[index] : actor.AuthoredHeroId;
            }).ToArray();
        }

        private static (BattlePreviewActor Actor, HeroModel Model)[] ResolveTeam(BattlePreviewActor[] actors,
            IReadOnlyList<string> ids, IReadOnlyDictionary<string, HeroModel> models)
        {
            var slots = new (BattlePreviewActor, HeroModel)[ids.Count];
            for (int index = 0; index < ids.Count; index++)
            {
                var actor = actors[index];
                if (!models.TryGetValue(ids[index], out var model))
                    throw new InvalidOperationException("Missing battle model for " + ids[index]);
                slots[index] = (actor, model);
            }
            return slots;
        }

        private static void ApplyTeam((BattlePreviewActor Actor, HeroModel Model)[] slots,
            IReadOnlyDictionary<string, BattlePreviewActor.SharedSource> prepared = null)
        {
            foreach (var (actor, model) in slots)
            {
                BattlePreviewActor.SharedSource source = null;
                prepared?.TryGetValue(model.id, out source);
                foreach (var form in model.forms ?? Array.Empty<ModelForm>())
                    if (prepared != null) prepared.TryGetValue(form.skeleton.source, out form.prepared);
                actor.UseBattleModel(model.skeleton, model.skin, $"{model.label} [{model.id}]", model.id, source);
                actor.ConfigureForms(model.skeleton, model.skin, model.forms);
            }
        }

        internal void ConfigureActorModel(BattlePreviewActor actor, string id) =>
            ApplyTeam(new[] { (actor, ReadHeroModels()[id]) });

        public bool TrySelectBattleTarget(string instanceId) => simulation?.ChooseTarget(instanceId) == true;

        private void Update()
        {
            UpdateBinding();
            if (autoStartBattle && simulation == null && binder != null && battleCatalog)
                StartBattle();
        }

        private void LateUpdate()
        {
            using var sample = FrameMarker.Auto();
            // EventSystem.Update must use the camera and screen-space HUD pose from the same
            // rendered frame. Advance the cinematic camera only after this frame's input.
            arrival?.Tick(Time.unscaledDeltaTime);
            simulation?.Tick(Time.unscaledDeltaTime);
            if (!IsPaused) binder?.Tick(Time.unscaledDeltaTime);
        }

        private void UpdateBinding(BattleCatalogData catalog = null)
        {
            if (!hud || !hud.isActiveAndEnabled)
            {
                ClearBinding();
                return;
            }
            if (binder != null)
                return;

            UIViewRegistry.ValidatePrefab<BattleHud>(hud.gameObject);
            var messages = battleCatalog
                ? (catalog ?? JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text)).messages
                    .ToDictionary(item => item.key, item => item.zhCn, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            binder = new BattleHudBinder(hud, burnPressed, cancelNormal, cancelPressed,
                burnButton, skillSelection, burnIntro, soulUse, particles, particleTextures, battleCatalog ? StartBattle : null,
                key => messages.TryGetValue(key, out var message) ? message : key, confirmPrefab);
        }

        private void ReleaseEntryUnitStatus()
        {
            if (!entryUnitStatus) return;
            entryUnitStatus.ignoreParentGroups = false;
            Destroy(entryUnitStatus);
            entryUnitStatus = null;
        }

        private void OnDisable()
        {
            arrival?.Dispose();
            arrival = null;
            ReleaseEntryUnitStatus();
            ClearBinding();
            online?.Dispose();
            online = null;
        }

        private void ClearBinding()
        {
            var battle = simulation;
            simulation = null;
            battle?.Dispose();
            var binding = binder;
            binder = null;
            binding?.Dispose();
        }
    }
}
