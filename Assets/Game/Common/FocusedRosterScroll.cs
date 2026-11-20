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
        private ScrollRect scroll;
        private BindingScope owner;
        private Predicate<int> selectable;
        private Action<int> select;
        private bool dragging, moving;
        private float observed, movedAt, snapFrom, snapTime, step;
        private int count, snapIndex = -1;

        public static FocusedRosterScroll Bind(ScrollRect scroll, BindingScope owner, Predicate<int> selectable, Action<int> select)
        {
            if (!scroll.TryGetComponent(out FocusedRosterScroll list))
                list = scroll.gameObject.AddComponent<FocusedRosterScroll>();
            list.scroll = scroll;
            list.owner = owner;
            list.selectable = selectable;
            list.select = select;
            list.count = 0;
            list.step = 0f;
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

        public float FocusLine => Mathf.Min(scroll.viewport.rect.height * .23f, 2.5f * step);
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

        public void PlaceCard(RectTransform card, Vector2 designSize, int index, float widthScale = 1f)
        {
            if (step <= 0f || count == 0 || scroll.viewport.rect.width <= 10f) return;
            float distance = Mathf.Abs(index - Offset / step);
            float scale = (scroll.viewport.rect.width - 10f) / designSize.x * widthScale *
                Mathf.Lerp(.70f, 1f, Mathf.Exp(-distance * .58f));
            card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, .5f);
            card.anchoredPosition = new Vector2(10f, -(FocusLine + index * step));
            card.sizeDelta = designSize;
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
        private void OnDisable() { dragging = false; ResetMotion(); }

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
