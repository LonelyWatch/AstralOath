using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using UnityEngine;

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
        [SerializeField] private TextAsset[] battleGraphs;
        [SerializeField] private Sprite[] battleSprites;
        private BattleHudBinder binder;
        private BattleSimulationPreview simulation;

        public BattleSnapshot SimulationSnapshot => simulation?.Snapshot;

        public void StartBattle()
        {
            if (!Application.isPlaying || !hud || !hud.isActiveAndEnabled || !battleCatalog)
                throw new InvalidOperationException("Open the battle scene in Play mode before starting.");
            ClearBinding();
            UpdateBinding();
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(battleCatalog.text);
            simulation = new BattleSimulationPreview(gameObject.scene, hud, binder, catalog, battleSprites, battleGraphs);
            binder.Simulation = simulation;
        }

        public bool TrySelectBattleTarget(string instanceId) => simulation?.ChooseTarget(instanceId) == true;

        private void OnEnable() => UpdateBinding();

        private void Update()
        {
            UpdateBinding();
            simulation?.Tick(Time.unscaledDeltaTime);
            binder?.Tick(Time.unscaledDeltaTime);
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
            binder = new BattleHudBinder(hud, burnPressed, cancelNormal, cancelPressed,
                burnButton, skillSelection, burnIntro, soulUse, particles, particleTextures);
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
