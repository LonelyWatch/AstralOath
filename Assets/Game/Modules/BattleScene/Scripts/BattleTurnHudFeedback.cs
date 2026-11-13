using System;
using System.Collections.Generic;
using System.Globalization;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleTurnHud
    {
        private const float GlyphBounce = 0.16f;
        private const float GlyphStagger = 0.036f;
        private const float NumberHold = 0.29f;
        private const float NumberFade = 0.14f;
        private const float ComboHold = 0.46f;
        private const float ComboFade = 0.19f;
        private static readonly Color CriticalTop = new Color32(255, 68, 58, 255);
        private static readonly Color CriticalBottom = new Color32(255, 255, 99, 255);
        private static readonly Color HealingTop = new Color32(176, 255, 104, 255);
        private static readonly Color HealingBottom = new Color32(82, 212, 30, 255);
        private readonly List<Feedback> impactLabels = new List<Feedback>();
        private readonly Dictionary<string, DamageTotal> damageTotals = new Dictionary<string, DamageTotal>(StringComparer.Ordinal);
        private string missFeedbackLabel = "";
        private int hitCount;
        private HitCombo hitCombo;

        internal void SetMissFeedbackLabel(string label) => missFeedbackLabel = label;

        internal void BeginFeedbackAction() => damageTotals.Clear();

        internal void ResetFeedbackCombo()
        {
            hitCount = 0;
            if (hitCombo != null)
                hitCombo.Root.gameObject.SetActive(false);
        }

        internal void ShowImpact(BattleFlowEvent item, RectTransform anchor, Vector3 targetWorld, Camera worldCamera)
        {
            if (item.Kind == BattleFlowEventKind.Damage)
                ShowDamageHit(item.TargetId, item.Amount, item.HitKind, anchor, targetWorld, worldCamera);
            else if (item.Kind == BattleFlowEventKind.Healing && anchor && item.Amount > 0)
                ShowNumber(AcquireFeedback(), item.TargetId, item.Amount, BattleHitKind.None, true, anchor, targetWorld, worldCamera);
        }

        internal void ShowPeriodicDamage(BattleFlowEvent item, RectTransform anchor, Vector3 targetWorld, Camera worldCamera)
        {
            if (!anchor || item.Amount <= 0) return;
            ShowNumber(AcquireFeedback(), item.TargetId, item.Amount, BattleHitKind.None, false, anchor, targetWorld, worldCamera);
        }

        internal void ShowDamageHit(string targetId, int amount, BattleHitKind hitKind,
            RectTransform anchor, Vector3 targetWorld, Camera worldCamera)
        {
            if (!anchor || amount < 0)
                return;
            RecordComboHit();
            if (amount == 0 && hitKind != BattleHitKind.Miss)
                return;
            if (!damageTotals.TryGetValue(targetId, out var total))
            {
                total = new DamageTotal();
                damageTotals.Add(targetId, total);
            }

            total.Amount += amount;
            if ((int)hitKind > (int)total.Kind)
                total.Kind = hitKind;
            if (total.Label == null)
            {
                total.Label = AcquireFeedback();
                total.Label.Total = total;
            }

            ShowNumber(total.Label, targetId, total.Amount, total.Kind, false, anchor, targetWorld, worldCamera,
                showMissCaption: hitKind == BattleHitKind.Miss);
        }

        internal void ShowStatusFeedback(string message, RectTransform anchor)
        {
            if (!anchor || string.IsNullOrEmpty(message)) return;
            var popup = AcquireFeedback();
            popup.Root.name = "Status Feedback";
            popup.Root.SetAsLastSibling();
            popup.Anchor = anchor;
            popup.Camera = null;
            popup.Elapsed = 0f;
            popup.FadeStart = NumberHold;
            popup.Amount.gameObject.SetActive(false);
            for (int index = 0; index < popup.GlyphCount; index++)
                popup.Glyphs[index].gameObject.SetActive(false);
            popup.GlyphCount = 0;
            popup.Caption.text = message;
            popup.Root.gameObject.SetActive(true);
            AnimateFeedback(popup);
        }

        private Feedback AcquireFeedback()
        {
            var popup = impactLabels.Find(candidate => !candidate.Root.gameObject.activeSelf);
            if (popup != null)
                return popup;

            var root = Rect(feedbackLayer, "Feedback", Vector2.zero, Vector2.zero);
            var amount = Text(root, "Impact", "", Vector2.zero, new Vector2(450f, 110f), 54f, numberSource);
            if (damageFont)
            {
                amount.font = damageFont;
                amount.fontSharedMaterial = damageFont.material;
            }
            var caption = Text(root, "Impact Caption", "", new Vector2(0f, 54f), new Vector2(240f, 40f), 27f, textSource);
            popup = new Feedback(root, amount, caption, AddFeedbackAlpha(root));
            impactLabels.Add(popup);
            return popup;
        }

        private void ShowNumber(Feedback popup, string targetId, long amount, BattleHitKind kind, bool healing,
            RectTransform anchor, Vector3 targetWorld, Camera worldCamera, bool showMissCaption = false)
        {
            popup.Root.name = targetId;
            popup.Root.SetAsLastSibling();
            popup.Anchor = anchor;
            popup.WorldPoint = targetWorld;
            popup.Camera = worldCamera;
            popup.Elapsed = 0f;

            bool critical = kind == BattleHitKind.Critical;
            bool miss = kind == BattleHitKind.Miss;
            float scale = critical ? 1.5f : kind == BattleHitKind.Crushing ? 1.24f : miss ? 0.88f : 1.08f;
            popup.BounceHeight = critical ? 79f : 64f;
            popup.Top = healing ? HealingTop : critical ? CriticalTop : Color.white;
            popup.Bottom = healing ? HealingBottom : critical ? CriticalBottom : Color.white;
            string digits = showMissCaption && amount == 0
                ? ""
                : (healing ? "+" : "") + amount.ToString(CultureInfo.InvariantCulture);
            popup.GlyphCount = digits.Length != 0 && LayoutGlyphs(popup, digits, scale) ? digits.Length : 0;
            for (int index = popup.GlyphCount; index < popup.Glyphs.Count; index++)
                popup.Glyphs[index].gameObject.SetActive(false);

            float lastDigitDelay = Mathf.Max(DigitDelay(0, popup.GlyphCount), DigitDelay(popup.GlyphCount - 1, popup.GlyphCount));
            popup.FadeStart = GlyphBounce + lastDigitDelay + NumberHold;
            popup.Amount.gameObject.SetActive(popup.GlyphCount == 0 && digits.Length != 0);
            popup.Amount.text = popup.GlyphCount == 0 ? digits : "";
            popup.Amount.fontSize = 54f * scale;
            popup.Amount.color = Color.Lerp(popup.Top, popup.Bottom, 0.5f);
            popup.Caption.text = showMissCaption ? missFeedbackLabel : "";
            popup.Root.gameObject.SetActive(true);
            AnimateFeedback(popup);
        }

        private bool LayoutGlyphs(Feedback popup, string digits, float scale)
        {
            if (!damageFont || !damageFont.atlasTexture)
                return false;

            var characters = damageFont.characterLookupTable;
            float width = 0f;
            for (int index = 0; index < digits.Length; index++)
            {
                if (!characters.TryGetValue(digits[index], out var character))
                    return false;
                var metrics = character.glyph.metrics;
                width += index < digits.Length - 1 ? metrics.horizontalAdvance : metrics.horizontalBearingX + metrics.width;
            }

            var atlas = damageFont.atlasTexture;
            float x = -width * 0.5f;
            for (int index = 0; index < digits.Length; index++)
            {
                if (index == popup.Glyphs.Count)
                {
                    var rect = Rect(popup.Root, "Damage Glyph", Vector2.zero, Vector2.zero);
                    var graphic = rect.gameObject.AddComponent<DamageFeedbackGlyph>();
                    graphic.raycastTarget = false;
                    popup.Glyphs.Add(graphic);
                }

                var glyph = characters[digits[index]].glyph;
                var image = popup.Glyphs[index];
                var bounds = glyph.glyphRect;
                var metrics = glyph.metrics;
                image.texture = atlas;
                image.uvRect = new UnityEngine.Rect((float)bounds.x / atlas.width, (float)bounds.y / atlas.height,
                    (float)bounds.width / atlas.width, (float)bounds.height / atlas.height);
                image.rectTransform.sizeDelta = new Vector2(metrics.width, metrics.height) * scale;
                image.RestPosition = new Vector2((x + metrics.horizontalBearingX + metrics.width * 0.5f) * scale, 0f);
                image.gameObject.SetActive(true);
                x += metrics.horizontalAdvance;
            }
            return true;
        }

        private void TickFeedback(float delta)
        {
            foreach (var popup in impactLabels)
            {
                if (!popup.Root.gameObject.activeSelf)
                    continue;
                popup.Elapsed += delta;
                if (popup.Elapsed >= popup.FadeStart + NumberFade)
                {
                    popup.Root.gameObject.SetActive(false);
                    if (popup.Total != null)
                        popup.Total.Label = null;
                    popup.Total = null;
                    popup.Anchor = null;
                    popup.Camera = null;
                    continue;
                }
                AnimateFeedback(popup);
            }
            if (hitCombo != null && hitCombo.Root.gameObject.activeSelf)
            {
                hitCombo.Elapsed += delta;
                hitCombo.Alpha.alpha = 1f - Mathf.InverseLerp(ComboHold, ComboHold + ComboFade, hitCombo.Elapsed);
                hitCombo.Root.localScale = Vector3.one * Mathf.Lerp(1.13f, 1f, hitCombo.Elapsed / 0.10f);
                if (hitCombo.Elapsed >= ComboHold + ComboFade)
                    hitCombo.Root.gameObject.SetActive(false);
            }
        }

        private void AnimateFeedback(Feedback popup)
        {
            if (popup.Camera && RectTransformUtility.ScreenPointToWorldPointInRectangle(feedbackLayer,
                popup.Camera.WorldToScreenPoint(popup.WorldPoint), popup.Camera, out var uiPoint))
                popup.Root.position = uiPoint;
            else if (popup.Anchor)
                popup.Root.position = popup.Anchor.position;

            popup.Alpha.alpha = 1f - Mathf.InverseLerp(popup.FadeStart, popup.FadeStart + NumberFade, popup.Elapsed);
            for (int index = 0; index < popup.GlyphCount; index++)
            {
                var image = popup.Glyphs[index];
                float phase = Mathf.Clamp01((popup.Elapsed - DigitDelay(index, popup.GlyphCount)) / GlyphBounce);
                float rise = Mathf.InverseLerp(0f, 0.40f, phase);
                float fall = Mathf.InverseLerp(0.40f, 1f, phase);
                float y = phase < 0.40f
                    ? Mathf.Lerp(-12f, popup.BounceHeight, rise * (2f - rise))
                    : Mathf.Lerp(popup.BounceHeight, 0f, fall * (2f - fall));
                image.rectTransform.anchoredPosition = image.RestPosition + Vector2.up * y;
                image.rectTransform.localScale = Vector3.one * (Mathf.Lerp(0.86f, 1f, phase) + 0.17f * Mathf.Sin(phase * Mathf.PI));
                float tint = Mathf.InverseLerp(0.20f, 0.85f, phase);
                image.SetGradient(Color.Lerp(Color.white, popup.Top, tint), Color.Lerp(Color.white, popup.Bottom, tint));
            }
        }

        private static float DigitDelay(int index, int count)
        {
            if (count <= 1)
                return 0f;
            float distanceFromCenter = Mathf.Abs(index - (count - 1) * 0.5f);
            float outsideDelay = Mathf.Min(4f, Mathf.Floor(distanceFromCenter)) * GlyphStagger;
            return outsideDelay + (index % 2 == 0 ? 0f : GlyphStagger * 0.55f);
        }

        private void RecordComboHit()
        {
            hitCount++;
            if (hitCount < 2 || !sprites.TryGetValue("game_eff_dmg_hits", out var caption) || !caption)
                return;
            if (hitCombo == null)
            {
                var root = Rect(feedbackLayer, "Hit Combo", Vector2.zero, Vector2.zero);
                root.anchorMin = root.anchorMax = Vector2.up;
                root.anchoredPosition = new Vector2(0f, -276f);
                hitCombo = new HitCombo(root, AddFeedbackAlpha(root));
                var backdrop = ComboImage(root, "Combo Backdrop", new Vector2(205f, 50f));
                SetComboSprite(backdrop, sprites.GetValueOrDefault("ef_blood03"));
                SetComboSprite(ComboImage(root, "Combo Caption", new Vector2(215f, 42f)), caption);
            }

            string number = hitCount.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < number.Length; index++)
            {
                if (index == hitCombo.Digits.Count)
                    hitCombo.Digits.Add(ComboImage(hitCombo.Root, "Combo Digit", new Vector2(130f - index * 40f, 42f)));
                var digit = hitCombo.Digits[index];
                SetComboSprite(digit, sprites.GetValueOrDefault("game_eff_dmg_p" + number[number.Length - index - 1]));
                digit.gameObject.SetActive(true);
            }
            for (int index = number.Length; index < hitCombo.Digits.Count; index++)
                hitCombo.Digits[index].gameObject.SetActive(false);
            hitCombo.Elapsed = 0f;
            hitCombo.Alpha.alpha = 1f;
            hitCombo.Root.localScale = Vector3.one * 1.13f;
            hitCombo.Root.SetAsLastSibling();
            hitCombo.Root.gameObject.SetActive(true);
        }

        private static Image ComboImage(RectTransform root, string name, Vector2 sourcePosition)
        {
            var image = Rect(root, name, sourcePosition * SourceScale, Vector2.zero).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void SetComboSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
            if (sprite)
                image.rectTransform.sizeDelta = sprite.rect.size * SourceScale;
        }

        private static CanvasGroup AddFeedbackAlpha(RectTransform root)
        {
            var alpha = root.gameObject.AddComponent<CanvasGroup>();
            alpha.blocksRaycasts = false;
            alpha.interactable = false;
            return alpha;
        }

        private sealed class DamageTotal
        {
            internal long Amount;
            internal BattleHitKind Kind;
            internal Feedback Label;
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
            internal List<DamageFeedbackGlyph> Glyphs { get; } = new List<DamageFeedbackGlyph>();
            internal RectTransform Anchor;
            internal Vector3 WorldPoint;
            internal Camera Camera;
            internal DamageTotal Total;
            internal int GlyphCount;
            internal float Elapsed;
            internal float FadeStart;
            internal float BounceHeight;
            internal Color Top;
            internal Color Bottom;
        }

        private sealed class HitCombo
        {
            internal HitCombo(RectTransform root, CanvasGroup alpha)
            {
                Root = root;
                Alpha = alpha;
            }

            internal RectTransform Root { get; }
            internal CanvasGroup Alpha { get; }
            internal List<Image> Digits { get; } = new List<Image>();
            internal float Elapsed;
        }
    }
}
