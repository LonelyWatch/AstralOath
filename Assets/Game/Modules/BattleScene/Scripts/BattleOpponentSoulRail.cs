using System;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleOpponentSoulRail : IDisposable
    {
        private const int SlotCount = BattleFlowRules.StandardMaximumSoul / BattleFlowRules.SoulPerSlot;
        private readonly RectTransform root;
        private readonly Canvas canvas;
        private readonly RectTransform[] anchors = new RectTransform[SlotCount];
        private readonly Image[] dots = new Image[SlotCount];
        private readonly ISkeletonPlayer[] souls = new ISkeletonPlayer[SlotCount];
        private readonly SkeletonPlayerFactory factory = new SkeletonPlayerFactory();
        private readonly SkeletonPlayerSource source;
        private int occupied;

        internal BattleOpponentSoulRail(RectTransform parent, BattleHudPreview.SpineEffect effect, Sprite dot)
        {
            // pvplive_global_hp.csb n_gauge_right/n_soul_burn. Source starts at x988/y640
            // and moves left in 23px steps; align the row to the opponent avatar at 82% of the safe area.
            root = (RectTransform)new GameObject("Opponent Soul Rail", typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(.82f, 1f);
            root.anchoredPosition = new Vector2(-92.4f, -120f);
            root.sizeDelta = Vector2.zero;
            canvas = root.GetComponentInParent<Canvas>();
            source = effect.CreateSource(1.5f / 2.24f * .6f);
            for (int index = 0; index < SlotCount; ++index)
            {
                var slot = new GameObject("Soul " + (index + 1), typeof(RectTransform), typeof(Image));
                var anchor = anchors[index] = (RectTransform)slot.transform;
                anchor.SetParent(root, false);
                anchor.anchoredPosition = new Vector2(-34.5f * index, 0f);
                anchor.sizeDelta = new Vector2(10.5f, 9f);
                var placeholder = dots[index] = slot.GetComponent<Image>();
                placeholder.sprite = dot;
                placeholder.enabled = dot;
                placeholder.raycastTarget = false;
            }
        }

        internal void Show(int amount, bool animateGain)
        {
            int next = Mathf.Clamp(amount / BattleFlowRules.SoulPerSlot, 0, SlotCount);
            for (int index = occupied; index < next; ++index)
            {
                var player = souls[index] ??= CreateSoul(index);
                player.Play(animateGain ? "intro" : "loop", !animateGain);
                player.Root.SetActive(true);
                dots[index].enabled = false;
            }
            for (int index = next; index < occupied; ++index)
            {
                souls[index].Root.SetActive(false);
                dots[index].enabled = dots[index].sprite;
            }
            occupied = next;
        }

        internal void Restore(int amount)
        {
            Show(amount, animateGain: false);
            for (int index = 0; index < occupied; ++index)
                if (souls[index].CurrentAnimation == "intro") souls[index].Play("loop", true);
        }

        internal void Tick(float delta)
        {
            for (int index = 0; index < occupied; ++index)
            {
                var player = souls[index];
                if (player.CurrentAnimation == "intro")
                {
                    float introStep = Mathf.Min(delta, Mathf.Max(0f, player.Duration - player.Time));
                    player.Tick(introStep);
                    if (player.Time < player.Duration) continue;
                    player.Play("loop", true);
                    player.Tick(delta - introStep);
                }
                else player.Tick(delta);
            }
        }

        private ISkeletonPlayer CreateSoul(int index)
        {
            var player = factory.Create(source, anchors[index]);
            player.Root.name = "Soul";
            var renderer = player.Root.GetComponent<MeshRenderer>();
            renderer.sortingLayerID = canvas.sortingLayerID;
            renderer.sortingOrder = canvas.sortingOrder + 1;
            return player;
        }

        public void Dispose()
        {
            foreach (var soul in souls) soul?.Dispose();
            if (!root) return;
            root.gameObject.SetActive(false);
            root.name = "Retired Opponent Soul Rail";
            Object.Destroy(root.gameObject);
        }
    }
}
