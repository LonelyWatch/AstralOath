using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleActivePortrait
    {
        private const float ExitSeconds = .09f;
        private const float EnterDelaySeconds = .025f;
        private const float EnterSeconds = .17f;
        private readonly Image image;
        private readonly RectTransform rect;
        private readonly Sprite originalSprite;
        private readonly bool originalEnabled;
        private readonly Vector2 originalSize;
        private readonly Vector2 originalPosition;
        private readonly Vector2 originalBottomLeft;
        private readonly float authoredHeight;
        private readonly (Graphic Graphic, Vector2 Position, float Alpha)[] details;
        private readonly List<(Graphic Graphic, Vector2 Position, float Alpha)> outgoing = new List<(Graphic, Vector2, float)>(6);
        private readonly float originalAlpha;
        private Vector2 restingPosition;
        private string currentActor;
        private float elapsed;
        private bool transitioning;

        public BattleActivePortrait(Image image)
        {
            this.image = image;
            rect = image.rectTransform;
            originalSprite = image.sprite;
            originalEnabled = image.enabled;
            originalSize = rect.sizeDelta;
            originalPosition = rect.anchoredPosition;
            originalBottomLeft = originalPosition + rect.rect.min;
            authoredHeight = rect.rect.height;
            restingPosition = originalPosition;
            originalAlpha = image.canvasRenderer.GetAlpha();

            var parts = new List<(Graphic, Vector2, float)>(5);
            foreach (string name in new[] { "Active Name", "Active HP", "Active HP Fill", "Element", "Class" })
            {
                var graphic = rect.parent ? rect.parent.Find(name)?.GetComponent<Graphic>() : null;
                if (graphic) parts.Add((graphic, graphic.rectTransform.anchoredPosition, graphic.canvasRenderer.GetAlpha()));
            }
            details = parts.ToArray();
        }

        public void Show(Sprite sprite)
        {
            StopTransition();
            SetSprite(sprite);
            ApplyPose(0f, 1f);
        }

        public void ShowTurn(Sprite sprite, string actorId, Action applyDetails)
        {
            if (currentActor == null || actorId == null)
            {
                currentActor = actorId;
                Show(sprite);
                applyDetails?.Invoke();
                return;
            }
            if (actorId == currentActor)
            {
                if (image.sprite != sprite)
                {
                    SetSprite(sprite);
                    if (transitioning) ApplyIncomingPose();
                    else ApplyPose(0f, 1f);
                }
                applyDetails?.Invoke();
                return;
            }

            StopTransition();
            try
            {
                CaptureOutgoing(image);
                foreach (var detail in details) CaptureOutgoing(detail.Graphic);
                currentActor = actorId;
                SetSprite(sprite);
                applyDetails?.Invoke();
                elapsed = 0f;
                transitioning = true;
                ApplyIncomingPose();
            }
            catch
            {
                StopTransition();
                ApplyPose(0f, 1f);
                throw;
            }
        }

        public void Tick(float delta)
        {
            if (!transitioning || delta <= 0f) return;
            elapsed += delta;
            if (elapsed >= ExitSeconds) ClearOutgoing();
            else
            {
                float exit = Mathf.Clamp01(elapsed / ExitSeconds);
                var movement = Vector2.right * (authoredHeight * .34f * Mathf.SmoothStep(0f, 1f, exit));
                foreach (var part in outgoing)
                {
                    part.Graphic.rectTransform.anchoredPosition = part.Position + movement;
                    part.Graphic.canvasRenderer.SetAlpha(part.Alpha * (1f - exit));
                }
            }
            ApplyIncomingPose();
            transitioning = elapsed < EnterDelaySeconds + EnterSeconds;
        }

        private void ApplyIncomingPose()
        {
            float enter = Mathf.Clamp01((elapsed - EnterDelaySeconds) / EnterSeconds);
            ApplyPose(Mathf.Lerp(-authoredHeight * .28f, 0f, Mathf.SmoothStep(0f, 1f, enter)), enter);
        }

        // Capture only passive visuals. The active role always owns the original
        // graphics, and its slot, event bindings and raycast state are untouched.
        private void CaptureOutgoing(Graphic graphic)
        {
            if (!graphic || !graphic.enabled || graphic.canvasRenderer.GetAlpha() <= 0f) return;
            var sourceRect = graphic.rectTransform;
            var ghost = new GameObject("Outgoing " + graphic.name, typeof(RectTransform));
            var ghostRect = (RectTransform)ghost.transform;
            ghostRect.SetParent(sourceRect.parent, false);
            ghostRect.SetSiblingIndex(sourceRect.GetSiblingIndex());
            ghostRect.anchorMin = sourceRect.anchorMin;
            ghostRect.anchorMax = sourceRect.anchorMax;
            ghostRect.pivot = sourceRect.pivot;
            ghostRect.sizeDelta = sourceRect.sizeDelta;
            ghostRect.anchoredPosition3D = sourceRect.anchoredPosition3D;
            ghostRect.localRotation = sourceRect.localRotation;
            ghostRect.localScale = sourceRect.localScale;

            try
            {
                MaskableGraphic copy = graphic is Image sprite ? CopyImage(ghost, sprite) : CopyText(ghost, (TMP_Text)graphic);
                copy.color = graphic.color;
                copy.maskable = ((MaskableGraphic)graphic).maskable;
                copy.raycastTarget = false;
                float alpha = graphic.canvasRenderer.GetAlpha();
                copy.canvasRenderer.SetAlpha(alpha);
                outgoing.Add((copy, ghostRect.anchoredPosition, alpha));
            }
            catch { DestroyGhost(ghost); throw; }
        }

        private static Image CopyImage(GameObject ghost, Image source)
        {
            var copy = source is BattleActiveHealthBar ? ghost.AddComponent<BattleActiveHealthBar>() : ghost.AddComponent<Image>();
            copy.sprite = source.overrideSprite;
            copy.material = source.material;
            copy.type = source.type;
            copy.preserveAspect = source.preserveAspect;
            copy.fillCenter = source.fillCenter;
            copy.fillMethod = source.fillMethod;
            copy.fillOrigin = source.fillOrigin;
            copy.fillClockwise = source.fillClockwise;
            copy.fillAmount = source.fillAmount;
            return copy;
        }

        private static TMP_Text CopyText(GameObject ghost, TMP_Text source)
        {
            var copy = ghost.AddComponent<TextMeshProUGUI>();
            copy.font = source.font;
            copy.fontSharedMaterial = source.fontSharedMaterial;
            copy.fontSize = source.fontSize;
            copy.fontStyle = source.fontStyle;
            copy.fontWeight = source.fontWeight;
            copy.alignment = source.alignment;
            copy.margin = source.margin;
            copy.characterSpacing = source.characterSpacing;
            copy.enableWordWrapping = source.enableWordWrapping;
            copy.overflowMode = source.overflowMode;
            copy.richText = source.richText;
            copy.text = source.text;
            return copy;
        }

        private void ClearOutgoing()
        {
            foreach (var part in outgoing)
                if (part.Graphic) DestroyGhost(part.Graphic.gameObject);
            outgoing.Clear();
        }

        private static void DestroyGhost(GameObject ghost)
        {
            ghost.SetActive(false);
            if (Application.isPlaying) Object.Destroy(ghost);
            else Object.DestroyImmediate(ghost);
        }

        private void StopTransition()
        {
            ClearOutgoing();
            transitioning = false;
        }

        private void SetSprite(Sprite sprite)
        {
            image.enabled = sprite != null;
            if (!sprite) return;

            image.sprite = sprite;
            float referenceHeight = originalSprite ? originalSprite.rect.height : sprite.rect.height;
            Vector2 size = sprite.rect.size * (authoredHeight / referenceHeight);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            restingPosition = originalBottomLeft - rect.rect.min;
        }

        private void ApplyPose(float horizontal, float alpha)
        {
            var movement = new Vector2(horizontal, 0f);
            rect.anchoredPosition = restingPosition + movement;
            image.canvasRenderer.SetAlpha(originalAlpha * alpha);
            foreach (var part in details)
            {
                if (!part.Graphic) continue;
                part.Graphic.rectTransform.anchoredPosition = part.Position + movement;
                part.Graphic.canvasRenderer.SetAlpha(part.Alpha * alpha);
            }
        }

        public void Restore()
        {
            if (!image) return;

            StopTransition();
            currentActor = null;
            image.sprite = originalSprite;
            rect.sizeDelta = originalSize;
            restingPosition = originalPosition;
            ApplyPose(0f, 1f);
            image.enabled = originalEnabled;
        }
    }
}
