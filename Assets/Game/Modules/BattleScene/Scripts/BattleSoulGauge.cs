using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleSoulGauge : IDisposable
    {
        private const float CanvasPixelsPerSourcePixel = 1.5f;
        private const float SpineExportScale = 2.24f;
        private const float SoulScale = .8f;
        private const float ParticleDelay = .2f;
        private static readonly float[] SlotX = { 526f, 568.5f, 611f, 653.5f, 696f, 738.5f, 780.1714f, 822.6714f };
        private readonly Slot[] slots = new Slot[SlotX.Length];
        private readonly Image fill;
        private readonly GameText count;
        private readonly int capacity;
        private readonly GameObject root;
        private int displayed = -1;
        private int occupied;

        public BattleSoulGauge(Image fill, GameText count, int capacity, BattleHudPreview.SpineEffect effect,
            TextAsset particles, IReadOnlyDictionary<string, Texture2D> textures)
        {
            this.fill = fill;
            this.count = count;
            this.capacity = capacity;
            root = new GameObject("Soul Indicators", typeof(RectTransform));
            var anchor = (RectTransform)root.transform;
            anchor.SetParent(fill.transform.parent, false);
            anchor.anchorMin = anchor.anchorMax = Vector2.zero;
            anchor.anchoredPosition = Vector2.zero;
            var canvas = fill.GetComponentInParent<Canvas>();
            try
            {
                var factory = new SkeletonPlayerFactory();
                float scale = CanvasPixelsPerSourcePixel / SpineExportScale;
                var source = new SkeletonPlayerSource(effect.skeleton.text, effect.atlas.text,
                    new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { effect.page.name + ".sct", effect.page } },
                    premultipliedAlpha: true, scale: scale * SoulScale);
                factory.Prepare(source);
                for (int i = 0; i < slots.Length; i++)
                {
                    var player = factory.Create(source, anchor);
                    // The source Burn/Cancel overlays the right-hand soul slots when available.
                    int order = canvas.sortingOrder + 1;
                    slots[i] = new Slot(player);
                    player.Root.name = "Soul " + (i + 1);
                    player.Root.transform.localPosition = new Vector3(SlotX[i], i < 6 ? 44f : 44.0599f, 0f) * CanvasPixelsPerSourcePixel;
                    var renderer = player.Root.GetComponent<MeshRenderer>();
                    renderer.sortingLayerID = canvas.sortingLayerID;
                    renderer.sortingOrder = order;
                    slots[i].Particles = new BattleHudParticles(particles, textures, player.Root.transform,
                        new Vector2(0, 30f), scale, order);
                    player.Root.SetActive(false);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Show(int amount)
        {
            amount = Mathf.Clamp(amount, 0, capacity);
            if (amount == displayed) return;
            int nextOccupied = Mathf.Min(amount / BattleFlowRules.SoulPerSlot, slots.Length);
            for (int i = occupied; i < nextOccupied; i++) slots[i].Show(animate: displayed >= 0);
            for (int i = nextOccupied; i < occupied; i++) slots[i].Hide();
            occupied = nextOccupied;
            displayed = amount;
            count.text = $"{amount} / {capacity}";
            fill.fillAmount = nextOccupied < slots.Length
                ? (amount % BattleFlowRules.SoulPerSlot) / (float)BattleFlowRules.SoulPerSlot : 0f;
        }

        public void Tick(float delta)
        {
            for (int i = 0; i < occupied; i++) slots[i].Tick(delta);
        }

        public void Dispose()
        {
            foreach (var slot in slots) slot?.Dispose();
            if (!root) return;
            root.SetActive(false);
            root.transform.SetParent(null, false);
            Object.Destroy(root);
        }

        private sealed class Slot
        {
            private readonly ISkeletonPlayer player;
            private float particleDelay;
            public BattleHudParticles Particles { get; set; }

            public Slot(ISkeletonPlayer player) => this.player = player;

            public void Show(bool animate)
            {
                player.Play(animate ? "intro" : "loop", !animate);
                player.Root.SetActive(true);
                if (animate) particleDelay = ParticleDelay;
                else
                {
                    particleDelay = 0f;
                    Particles.Play();
                }
            }

            public void Hide()
            {
                player.Root.SetActive(false);
                Particles.Stop();
            }

            public void Tick(float delta)
            {
                if (player.CurrentAnimation == "intro")
                {
                    float introStep = Mathf.Min(delta, player.Duration - player.Time);
                    player.Tick(introStep);
                    if (player.Time >= player.Duration)
                    {
                        player.Play("loop", true);
                        player.Tick(delta - introStep);
                    }
                }
                else player.Tick(delta);

                if (particleDelay > 0f)
                {
                    float delayStep = Mathf.Min(delta, particleDelay);
                    particleDelay -= delayStep;
                    if (particleDelay > 0f) return;
                    Particles.Play();
                    delta -= delayStep;
                }
                Particles.Tick(delta);
            }

            public void Dispose()
            {
                Particles?.Dispose();
                player.Dispose();
            }
        }
    }
}
