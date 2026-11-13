using System;
using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private BattleUnitPresentationProfile unitPresentation;
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
            if (!Application.isPlaying || !hud || !hud.isActiveAndEnabled || !battleCatalog)
                throw new InvalidOperationException("Open the battle scene in Play mode before starting.");
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            var units = gameObject.scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var allyActors = GetTeamActors(units.Find("Allies"), DefaultAllyHeroIds.Count);
            var enemyActors = GetTeamActors(units.Find("Enemies"), DefaultEnemyHeroIds.Count);
            var allies = SelectHeroes(allyActors, allyHeroIds, DefaultAllyHeroIds);
            var enemies = SelectHeroes(enemyActors, enemyHeroIds, DefaultEnemyHeroIds);
            var nextMatch = BattlePreviewFactory.Create("local", 271828UL, catalog, allyHeroIds: allies, enemyHeroIds: enemies);
            var models = (heroModels ?? Array.Empty<HeroModel>()).ToDictionary(model => model.id, StringComparer.Ordinal);
            var allySlots = ResolveTeam(allyActors, allies, models);
            var enemySlots = ResolveTeam(enemyActors, enemies, models);
            if (simulation != null) ClearBinding();
            ApplyTeam(allySlots);
            ApplyTeam(enemySlots);
            UpdateBinding();
            simulation = new BattleSimulationPreview(gameObject.scene, hud, binder, catalog, battleSprites, battleGraphs,
                skillAssets, audioCatalog, audioBanks, battleDamageFont, impactBank, stageBackground, unitPresentation,
                soulDisplay, particles, particleTextures, nextMatch, inspectionPrefab, readyDust: readyDust,
                skillTimelineOverrides: skillTimelineOverrides);
            binder.AttachSimulation(simulation);
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
                return string.IsNullOrEmpty(actor.HeroId) ? defaults[index] : actor.HeroId;
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
                actor.UseModel(model.skeleton, model.skin, $"{model.label} [{model.id}]");
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

        private void OnDisable() => ClearBinding();

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
