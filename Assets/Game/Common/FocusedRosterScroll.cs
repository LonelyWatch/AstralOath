using System;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    // The binder supplies the visible rows and decides which focused item is selectable.
    [DisallowMultipleComponent]
    public sealed class FocusedRosterScroll : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        private const float RightOverhang = 37f;
        public static readonly Vector2 CardSize = new Vector2(361f, 112f);
        private ScrollRect scroll;
        private BindingScope owner;
        private Predicate<int> selectable;
        private Action<int> select;
        private bool dragging, moving;
        private float observed, movedAt, snapFrom, snapTime, step;
        private int count, snapIndex = -1;
        private Vector2 measuredViewport;
        private float cardScale, originalSensitivity;
        private bool measuredCompact;

        public static FocusedRosterScroll Bind(ScrollRect scroll, BindingScope owner, Predicate<int> selectable, Action<int> select)
        {
            if (!scroll.TryGetComponent(out FocusedRosterScroll list))
                list = scroll.gameObject.AddComponent<FocusedRosterScroll>();
            if (list.owner == null) list.originalSensitivity = scroll.scrollSensitivity;
            list.scroll = scroll;
            list.owner = owner;
            list.selectable = selectable;
            list.select = select;
            list.count = 0;
            list.step = 0f;
            list.measuredViewport = Vector2.zero;
            list.cardScale = 0f;
            list.enabled = true;
            list.ResetMotion();
            owner.Add(() =>
            {
                if (!list || list.owner != owner) return;
                list.enabled = false;
                list.owner = null;
                list.selectable = null;
                list.select = null;
            });
            return list;
        }

        // Keep the top of the second preceding card inside the mask, including its portrait.
        public float FocusLine => 2f * step + .5f * CardSize.y * cardScale * ScaleAt(2f);
        public int Nearest => count > 0 && step > 0f ? Mathf.Clamp(Mathf.RoundToInt(Offset / step), 0, count - 1) : -1;
        private float Offset => scroll.content.anchoredPosition.y;

        public void Layout(int itemCount, float rowStep)
        {
            if (count != itemCount || step != rowStep) ResetMotion();
            count = Mathf.Max(0, itemCount);
            step = Mathf.Max(0f, rowStep);
            float maximum = Mathf.Max(0, count - 1) * step;
            float extent = scroll.viewport.rect.height + maximum;
            if (!Mathf.Approximately(scroll.content.rect.height, extent))
                scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, extent);
            float offset = Mathf.Clamp(Offset, 0, maximum);
            if (!Mathf.Approximately(offset, Offset)) scroll.content.anchoredPosition = new Vector2(0, offset);
        }

        public float LayoutCards(int itemCount, float stageHeight, bool compact = false)
        {
            Vector2 size = scroll.viewport.rect.size;
            if (size.y <= 0f || stageHeight <= 0f) return 0f;
            float pitch = compact ? 52f : 59f;
            float threeCards = 2f * pitch + .5f * CardSize.y * (ScaleAt(2f) + 1f);
            float scale = Mathf.Min(stageHeight / 720f * (compact ? .86f : 1f), size.y / threeCards);
            // unit_bar extends beyond the screen on the right. The viewport must expose its left edge.
            var frame = (RectTransform)scroll.viewport.parent;
            var inset = scroll.viewport.offsetMin;
            float left = Mathf.Min(0f, frame.rect.width - (CardSize.x - RightOverhang) * scale);
            if (!Mathf.Approximately(inset.x, left)) scroll.viewport.offsetMin = new Vector2(left, inset.y);
            size = scroll.viewport.rect.size;
            if (measuredViewport != size || measuredCompact != compact || !Mathf.Approximately(cardScale, scale))
            {
                bool wasMoving = moving || dragging || snapIndex >= 0 || Mathf.Abs(scroll.velocity.y) > .1f;
                float focus = step > 0f ? (wasMoving ? Offset : observed) / step : 0f;
                int target = snapIndex;
                // The 112 px texture contains an 89 px solid plate. In the 720p
                // reference the distant plates meet at roughly 59 px intervals.
                cardScale = scale;
                measuredViewport = size;
                measuredCompact = compact;
                Layout(itemCount, pitch * cardScale);
                scroll.StopMovement();
                scroll.scrollSensitivity = step;
                scroll.content.anchoredPosition = new Vector2(0f, Mathf.Clamp(focus, 0f, Mathf.Max(0, count - 1)) * step);
                ResetMotion();
                if (target >= 0 && target < count && (selectable == null || selectable(target)))
                {
                    snapIndex = target;
                    snapFrom = Offset;
                    snapTime = 0f;
                }
                else if (wasMoving)
                {
                    moving = true;
                    movedAt = Time.unscaledTime;
                }
            }
            else Layout(itemCount, step);
            return step;
        }

        private static float ScaleAt(float distance) => Mathf.Max(.73f, 1f - .09f * distance);

        public void PlaceCard(RectTransform card, int index)
        {
            if (step <= 0f || count == 0) return;
            float scale = cardScale * ScaleAt(Mathf.Abs(index - Offset / step));
            card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, .5f);
            card.anchoredPosition = new Vector2(RightOverhang * scale, -(FocusLine + index * step));
            card.sizeDelta = CardSize;
            card.localScale = new Vector3(scale, scale, 1f);
        }

        public static void RaiseNearest(int itemCount, int nearest, Action<int> raise)
        {
            if (nearest < 0 || nearest >= itemCount) return;
            for (int distance = Mathf.Max(nearest, itemCount - 1 - nearest); distance > 0; distance--)
            {
                if (nearest - distance >= 0) raise(nearest - distance);
                if (nearest + distance < itemCount) raise(nearest + distance);
            }
            raise(nearest);
        }

        public void ResetMotion()
        {
            observed = scroll ? Offset : 0f;
            moving = false;
            snapIndex = -1;
        }

        public void Focus(int index)
        {
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(index, 0, Mathf.Max(0, count - 1)) * step);
            ResetMotion();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) dragging = true;
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) dragging = false;
        }
        private void OnEnable()
        {
            if (scroll && cardScale > 0f) scroll.scrollSensitivity = step;
        }
        private void OnDisable()
        {
            if (scroll) scroll.scrollSensitivity = originalSensitivity;
            dragging = false;
            ResetMotion();
        }

        private void LateUpdate()
        {
            if (!scroll || count == 0 || step <= 0f) return;
            Layout(count, step);
            float offset = Offset;
            bool changed = Mathf.Abs(offset - observed) > .1f;
            observed = offset;
            if (changed || dragging)
            {
                if (changed) { moving = true; snapIndex = -1; }
                movedAt = Time.unscaledTime;
                return;
            }
            if (snapIndex >= 0)
            {
                snapTime += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(snapTime / .12f);
                scroll.content.anchoredPosition = new Vector2(0, Mathf.SmoothStep(snapFrom, snapIndex * step, progress));
                observed = Offset;
                if (progress < 1f) return;
                int index = snapIndex;
                ResetMotion();
                if (selectable == null || selectable(index)) select(index);
                return;
            }
            if (!moving || Time.unscaledTime - movedAt < .12f) return;
            scroll.StopMovement();
            int nearest = Nearest;
            snapIndex = nearest;
            if (selectable != null && !selectable(nearest))
            {
                for (int distance = 1; distance < count; distance++)
                {
                    int after = nearest + distance, before = nearest - distance;
                    if (after < count && selectable(after)) { snapIndex = after; break; }
                    if (before >= 0 && selectable(before)) { snapIndex = before; break; }
                }
            }
            snapFrom = offset;
            snapTime = 0f;
            moving = false;
        }
    }
}
