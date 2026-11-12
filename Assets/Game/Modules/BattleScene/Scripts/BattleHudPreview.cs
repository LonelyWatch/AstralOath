using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
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
        private BattleHudBinder binder;

        private void OnEnable() => UpdateBinding();

        private void Update()
        {
            UpdateBinding();
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
            var binding = binder;
            binder = null;
            binding?.Dispose();
        }
    }
}
