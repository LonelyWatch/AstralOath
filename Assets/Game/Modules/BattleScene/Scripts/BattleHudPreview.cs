using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
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
        private sealed class HeroModel
        {
            public string id;
            public string label;
            public string skin;
            public BattleSkillPresentationAsset skeleton;
        }

        public const int StandardSoulCapacity = BattleFlowRules.StandardMaximumSoul;

        public static IReadOnlyList<string> DefaultAllyHeroIds { get; } = Array.AsReadOnly(new[] { "c1001", "c1062", "c1005", "c1002" });
        public static IReadOnlyList<string> DefaultEnemyHeroIds { get; } = Array.AsReadOnly(new[] { "c1003", "c1004", "c1006", "c1007" });

        [SerializeField, HideInInspector] private string[] allyHeroIds;
        [SerializeField, HideInInspector] private string[] enemyHeroIds;
        [SerializeField, HideInInspector] private HeroModel[] heroModels;
        private BattleHudBinder binder;
        private BattleSimulationPreview simulation;
        private BattleOnlineSession online;
        private BattleOnlineArrival arrival;
        private CanvasGroup entryHud;
        private GameText stageTitle;
        private BattleOnlineTransitionAssets.Copy onlineCopy;

        private static readonly ProfilerMarker StartMarker = new ProfilerMarker("Astral.Battle.Start");
        private static readonly ProfilerMarker FrameMarker = new ProfilerMarker("Astral.Battle.Frame");

        public bool AutoStartBattle { get => autoStartBattle; set => autoStartBattle = value; }
        public bool IsPaused => simulation?.IsPaused ?? false;
        public void PauseBattle() => binder?.PauseBattle();
        public void ResumeBattle() => binder?.ResumeBattle();
        public void RestartBattle() => StartBattle();

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

        internal async Task PrepareOnlineAsync(BattleOnlineSession session, BattleOnlineTransitionAssets assets, CancellationToken token)
        {
            autoStartBattle = false;
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            await session.OpenAsync(battleCatalog, catalog, token);
            token.ThrowIfCancellationRequested();
            var units = gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var allyActors = GetTeamActors(units.Find("Allies"), DefaultAllyHeroIds.Count);
            var enemyActors = GetTeamActors(units.Find("Enemies"), DefaultEnemyHeroIds.Count);
            string[] Team(BattleSide side) => session.Snapshot.Units.Where(unit => unit.Slot.Side == side)
                .OrderBy(unit => unit.Slot.Position).Select(unit => unit.DefinitionId).ToArray();
            online = session;
            onlineCopy = BattleOnlineTransitionAssets.Copy.Load(assets.zhHans);
            InstallOnlineBackdrop(assets.arenaBackdrop);
            entryHud = hud.gameObject.AddComponent<CanvasGroup>();
            entryHud.alpha = 0f;
            entryHud.blocksRaycasts = false;
            InstallBattle(catalog, allyActors, enemyActors, Team(BattleSide.Ally), Team(BattleSide.Enemy), null);
            arrival = new BattleOnlineArrival(units, allyActors.Concat(enemyActors), readyDust);
            arrival.HoldAboveStage();
        }

        internal void BeginOnlineArrival()
        {
            arrival?.Begin();
            var title = new GameObject("PvP Arena Title", typeof(RectTransform), typeof(GameText));
            title.transform.SetParent(transform, false);
            var rect = (RectTransform)title.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .26f);
            rect.sizeDelta = new Vector2(720f, 160f);
            rect.anchoredPosition = Vector2.zero;
            stageTitle = title.GetComponent<GameText>();
            var template = hud.Require<GameText>("activeName");
            stageTitle.font = template.font;
            stageTitle.fontSharedMaterial = template.fontSharedMaterial;
            stageTitle.fontSize = 52f;
            stageTitle.fontStyle = FontStyles.Bold;
            stageTitle.alignment = TextAlignmentOptions.Center;
            stageTitle.text = "<size=34><color=#D8AD66>" + onlineCopy.arenaMode + "</color></size>\n" + onlineCopy.arenaTitle;
            stageTitle.raycastTarget = false;
        }

        private void InstallOnlineBackdrop(Sprite sprite)
        {
            if (stageBackground) stageBackground.SetActive(false);
            stageBackground = new GameObject("RTA 021 Stage", typeof(SpriteRenderer));
            var renderer = stageBackground.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -70;
            var canvas = GetComponent<Canvas>();
            canvas.worldCamera.GetComponent<BattlePreviewViewport>().UseOnlineBackdrop(renderer);
        }
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
            entryHud.alpha = 1f;
            entryHud.blocksRaycasts = true;
            simulation?.FinishOnlineEntry();
        }

        private void InstallBattle(BattleCatalogData catalog, BattlePreviewActor[] allyActors, BattlePreviewActor[] enemyActors,
            string[] allies, string[] enemies, BattleMatch nextMatch)
        {
            var models = (heroModels ?? Array.Empty<HeroModel>()).ToDictionary(model => model.id, StringComparer.Ordinal);
            var allySlots = ResolveTeam(allyActors, allies, models);
            var enemySlots = ResolveTeam(enemyActors, enemies, models);
            if (simulation != null) ClearBinding();
            ApplyTeam(allySlots);
            ApplyTeam(enemySlots);
            UpdateBinding();
            simulation = new BattleSimulationPreview(gameObject.scene, hud, binder, catalog, battleSprites, battleGraphs,
                skillAssets, audioCatalog, audioBanks, battleDamageFont, impactBank, stageBackground,
                soulDisplay, soulConsume, particles, particleTextures, nextMatch, inspectionPrefab, readyDust: readyDust,
                skillTimelineOverrides: skillTimelineOverrides, online: online, onlineCopy: onlineCopy);
            binder.AttachSimulation(simulation, online != null);
        }

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

        private static void ApplyTeam((BattlePreviewActor Actor, HeroModel Model)[] slots)
        {
            foreach (var (actor, model) in slots)
                actor.UseModel(model.skeleton, model.skin, $"{model.label} [{model.id}]", model.id);
        }

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

        private void UpdateBinding()
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
                ? JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text).messages
                    .ToDictionary(item => item.key, item => item.zhCn, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            binder = new BattleHudBinder(hud, burnPressed, cancelNormal, cancelPressed,
                burnButton, skillSelection, burnIntro, soulUse, particles, particleTextures, battleCatalog ? StartBattle : null,
                key => messages.TryGetValue(key, out var message) ? message : key, confirmPrefab);
        }

        private void OnDisable()
        {
            arrival?.Dispose();
            arrival = null;
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
