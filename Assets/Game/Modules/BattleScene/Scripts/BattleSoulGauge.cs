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
        // hud_soul_consume.cfx: source Spine scale 1 / speed .6, particle scale 1.2.
        private const float ConsumeSpineSpeed = .6f;
        private const float ConsumeParticleScale = 1.2f;
        // Last soul_aura burst at .5s plus a maximum particle lifetime of 1.5s.
        private const float ConsumeDuration = 2f;
        private static readonly float[] SlotX = { 526f, 568.5f, 611f, 653.5f, 696f, 738.5f, 780.1714f, 822.6714f };
        private readonly Slot[] slots = new Slot[SlotX.Length];
        private readonly Consumption[] consumption = new Consumption[SlotX.Length];
        private readonly Image fill;
        private readonly GameText count;
        private readonly int capacity;
        private readonly GameObject root;
        private readonly GameObject consumptionRoot;
        private int displayed = -1;
        private int occupied;

        public BattleSoulGauge(Image fill, GameText count, int capacity, BattleHudPreview.SpineEffect effect,
            BattleHudPreview.SpineEffect consumeEffect, TextAsset particles, TextAsset consumeParticles,
            IReadOnlyDictionary<string, Texture2D> textures)
        {
            this.fill = fill;
            this.count = count;
            this.capacity = capacity;
            root = CreateAnchor("Soul Indicators", fill.transform.parent);
            consumptionRoot = CreateAnchor("Soul Consumption Effects", fill.transform.parent);
            var canvas = fill.GetComponentInParent<Canvas>();
            try
            {
                var factory = new SkeletonPlayerFactory();
                float scale = CanvasPixelsPerSourcePixel / SpineExportScale;
                var source = effect.CreateSource(scale * SoulScale);
                var consumeSource = consumeEffect.CreateSource(scale);
                factory.Prepare(source);
                factory.Prepare(consumeSource);
                for (int i = 0; i < slots.Length; i++)
                {
                    var position = new Vector3(SlotX[i], i < 6 ? 44f : 44.0599f, 0f) * CanvasPixelsPerSourcePixel;
                    var player = factory.Create(source, root.transform);
                    slots[i] = new Slot(player);
                    player.Root.name = "Soul " + (i + 1);
                    player.Root.transform.localPosition = position;
                    var renderer = player.Root.GetComponent<MeshRenderer>();
                    renderer.sortingLayerID = canvas.sortingLayerID;
                    // The source Burn/Cancel overlays the right-hand soul slots when available.
                    renderer.sortingOrder = canvas.sortingOrder + 1;
                    slots[i].Particles = new BattleHudParticles(particles, textures, player.Root.transform,
                        new Vector2(0, 30f), scale, renderer.sortingOrder);
                    player.Root.SetActive(false);

                    var consumeAnchor = new GameObject("Soul Consume " + (i + 1));
                    consumeAnchor.layer = consumptionRoot.layer;
                    consumeAnchor.transform.SetParent(consumptionRoot.transform, false);
                    consumeAnchor.transform.localPosition = position;
                    consumeAnchor.SetActive(false);
                    var consumePlayer = factory.Create(consumeSource, consumeAnchor.transform);
                    consumption[i] = new Consumption(consumeAnchor, consumePlayer);
                    consumePlayer.Root.GetComponent<MeshRenderer>().sortingOrder = canvas.sortingOrder + 2;
                    consumption[i].Particles = new BattleHudParticles(consumeParticles, textures, consumeAnchor.transform,
                        Vector2.zero, scale * ConsumeParticleScale, canvas.sortingOrder + 1);
                    foreach (var consumeRenderer in consumeAnchor.GetComponentsInChildren<MeshRenderer>(true))
                        consumeRenderer.sortingLayerID = canvas.sortingLayerID;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Show(int amount, bool animateConsumption = false)
        {
            amount = Mathf.Clamp(amount, 0, capacity);
            if (amount == displayed) return;
            int nextOccupied = Mathf.Min(amount / BattleFlowRules.SoulPerSlot, slots.Length);
            for (int i = occupied; i < nextOccupied; i++) slots[i].Show(animate: displayed >= 0);
            for (int i = nextOccupied; i < occupied; i++)
            {
                slots[i].Hide();
                if (animateConsumption) consumption[i].Play();
            }
            occupied = nextOccupied;
            displayed = amount;
            count.text = $"{amount} / {capacity}";
            fill.fillAmount = nextOccupied < slots.Length
                ? (amount % BattleFlowRules.SoulPerSlot) / (float)BattleFlowRules.SoulPerSlot : 0f;
        }

        public void Tick(float delta)
        {
            for (int i = 0; i < occupied; i++) slots[i].Tick(delta);
            foreach (var effect in consumption) effect.Tick(delta);
        }

        public void Dispose()
        {
            foreach (var slot in slots) slot?.Dispose();
            foreach (var effect in consumption) effect?.Dispose();
            DestroyRoot(root);
            DestroyRoot(consumptionRoot);
        }

        private static GameObject CreateAnchor(string name, Transform parent)
        {
            var anchor = new GameObject(name, typeof(RectTransform));
            anchor.layer = parent.gameObject.layer;
            var rect = (RectTransform)anchor.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return anchor;
        }

        private static void DestroyRoot(GameObject anchor)
        {
            if (!anchor) return;
            anchor.SetActive(false);
            anchor.transform.SetParent(null, false);
            Object.Destroy(anchor);
        }

        private sealed class Consumption
        {
            private readonly GameObject anchor;
            private readonly ISkeletonPlayer player;
            private float elapsed;
            public BattleHudParticles Particles { get; set; }

            public Consumption(GameObject anchor, ISkeletonPlayer player)
            {
                this.anchor = anchor;
                this.player = player;
            }

            public void Play()
            {
                elapsed = 0f;
                anchor.SetActive(true);
                player.Play("animation", false);
                player.Root.SetActive(true);
                Particles.Play();
            }

            public void Tick(float delta)
            {
                if (!anchor.activeSelf) return;
                elapsed += delta;
                if (elapsed >= ConsumeDuration)
                {
                    Particles.Stop();
                    anchor.SetActive(false);
                    return;
                }

                if (player.Root.activeSelf)
                {
                    player.Tick(Mathf.Min(delta * ConsumeSpineSpeed, player.Duration - player.Time));
                    if (player.Time >= player.Duration)
                        player.Root.SetActive(false);
                }
                Particles.Tick(delta);
            }

            public void Dispose()
            {
                Particles?.Dispose();
                player.Dispose();
            }
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
