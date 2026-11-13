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

            internal ISkeletonPlayer Create(Transform anchor, string name, float scale, int sortingOrder)
            {
                var pages = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { page.name + ".sct", page } };
                var player = new SkeletonPlayerFactory().Create(
                    new SkeletonPlayerSource(skeleton.text, atlas.text, pages, premultipliedAlpha: true, scale: scale), anchor);
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
        [SerializeField] private bool autoStartBattle = true;
        [SerializeField] private Sprite burnPressed;
        [SerializeField] private Sprite cancelNormal;
        [SerializeField] private Sprite cancelPressed;
        [SerializeField] private SpineEffect burnButton;
        [SerializeField] private SpineEffect skillSelection;
        [SerializeField] private SpineEffect burnIntro;
        [SerializeField] private SpineEffect soulUse;
        [SerializeField] private TextAsset[] particles;
        [SerializeField] private Texture2D[] particleTextures;
        [SerializeField] private TextAsset battleCatalog;
        [SerializeField] private TMP_FontAsset battleDamageFont;
        [SerializeField] private TextAsset[] battleGraphs;
        [SerializeField] private Sprite[] battleSprites;
        [SerializeField] private BattleSkillPresentationAsset[] skillAssets;
        [SerializeField] private TextAsset audioCatalog;
        [SerializeField] private TextAsset[] audioBanks;
        [SerializeField] private TextAsset impactBank;
        [SerializeField] private GameObject stageBackground;
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
            if (simulation != null) ClearBinding();
            UpdateBinding();
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            simulation = new BattleSimulationPreview(gameObject.scene, hud, binder, catalog, battleSprites, battleGraphs,
                skillAssets, audioCatalog, audioBanks, battleDamageFont, impactBank, stageBackground);
            binder.AttachSimulation(simulation);
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
                key => messages.TryGetValue(key, out var message) ? message : key);
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
