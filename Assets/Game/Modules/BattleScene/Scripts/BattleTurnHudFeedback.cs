using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleTurnHud
    {
        private const float FeedbackDuration = 0.86f;
        private readonly List<Feedback> impactLabels = new List<Feedback>();

        internal void ShowImpact(BattleFlowEvent item, RectTransform anchor, Vector3 targetWorld, Camera worldCamera)
        {
            if (!anchor || item.Amount <= 0)
                return;
            var popup = impactLabels.Find(candidate => !candidate.Root.gameObject.activeSelf);
            if (popup == null)
            {
                var root = Rect(feedbackLayer, "Feedback", Vector2.zero, new Vector2(160f, 65f));
                var amount = Text(root, "Impact", "", new Vector2(80f, 25f), new Vector2(160f, 56f),
                    42f, numberSource);
                if (damageFont)
                {
                    amount.font = damageFont;
                    amount.fontSharedMaterial = damageFont.material;
                }
                var caption = Text(root, "Impact Caption", "", new Vector2(80f, 57f), new Vector2(160f, 25f),
                    23f, textSource);
                popup = new Feedback(root, amount, caption, root.gameObject.AddComponent<CanvasGroup>());
                popup.Alpha.blocksRaycasts = false;
                popup.Alpha.interactable = false;
                impactLabels.Add(popup);
            }

            int existing = 0;
            foreach (var candidate in impactLabels)
                if (candidate.TargetId == item.TargetId && candidate.Root.gameObject.activeSelf)
                    existing++;
            float offset = existing == 0 ? 0f : existing % 2 == 0 ? -18f : 18f;

            popup.TargetId = item.TargetId;
            popup.Root.name = item.TargetId;
            // The camera may be panning or zooming during a skill. Static healthbar authoring
            // coordinates no longer describe the rendered target's position in that frame.
            Vector2 screenPoint = worldCamera.WorldToScreenPoint(targetWorld);
            popup.Root.position = RectTransformUtility.ScreenPointToWorldPointInRectangle(
                feedbackLayer, screenPoint, worldCamera, out var uiPoint) ? uiPoint : anchor.position;
            popup.Origin = popup.Root.anchoredPosition + new Vector2(offset, 43f + (existing % 3) * 16f);
            popup.Elapsed = 0f;
            bool healing = item.Kind == BattleFlowEventKind.Healing;
            popup.Amount.text = healing ? "+" + item.Amount : item.Amount.ToString();
            popup.Caption.text = item.HitKind == BattleHitKind.Miss ? "未命中" : "";
            popup.Amount.color = healing ? new Color(0.58f, 1f, 0.35f) : item.HitKind == BattleHitKind.Critical
                ? new Color(1f, 0.43f, 0.29f) : Color.white;
            popup.Amount.fontSize = item.HitKind switch
            {
                BattleHitKind.Critical => 52f,
                BattleHitKind.Crushing => 47f,
                BattleHitKind.Miss => 35f,
                _ => 41f
            };
            popup.Root.SetAsLastSibling();
            popup.Root.gameObject.SetActive(true);
            Animate(popup);
        }

        private void TickFeedback(float delta)
        {
            foreach (var popup in impactLabels)
            {
                if (!popup.Root.gameObject.activeSelf)
                    continue;
                popup.Elapsed += delta;
                if (popup.Elapsed >= FeedbackDuration)
                {
                    popup.Root.gameObject.SetActive(false);
                    continue;
                }
                Animate(popup);
            }
        }

        private static void Animate(Feedback popup)
        {
            float progress = popup.Elapsed / FeedbackDuration;
            float y = 54f * (1f - Mathf.Pow(1f - progress, 2f));
            popup.Root.anchoredPosition = popup.Origin + new Vector2(0f, y);
            float scale = popup.Elapsed < 0.10f
                ? Mathf.Lerp(0.83f, 1.18f, popup.Elapsed / 0.10f)
                : Mathf.Lerp(1.18f, 1f, (popup.Elapsed - 0.10f) / 0.13f);
            popup.Root.localScale = Vector3.one * scale;
            popup.Alpha.alpha = 1f - Mathf.InverseLerp(0.52f, 1f, progress);
        }

        private sealed class Feedback
        {
            internal Feedback(RectTransform root, GameText amount, GameText caption, CanvasGroup alpha)
            {
                Root = root;
                Amount = amount;
                Caption = caption;
                Alpha = alpha;
            }

            internal RectTransform Root { get; }
            internal GameText Amount { get; }
            internal GameText Caption { get; }
            internal CanvasGroup Alpha { get; }
            internal string TargetId { get; set; }
            internal Vector2 Origin { get; set; }
            internal float Elapsed { get; set; }
        }
    }
}
