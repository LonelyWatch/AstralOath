using System;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed class DevelopmentGmEntryDrag
    {
        private readonly RectTransform entry;
        private readonly RectTransform area;
        private PointerEventData pressed;
        private Vector2 grabOffset;
        private bool dragging;

        internal DevelopmentGmEntryDrag(GameButton button, BindingScope scope)
        {
            entry = (RectTransform)button.transform;
            area = (RectTransform)entry.parent;
            var events = button.gameObject.AddComponent<EventTrigger>();
            void Bind(EventTriggerType type, Action<PointerEventData> handle)
            {
                var binding = new EventTrigger.Entry { eventID = type };
                binding.callback.AddListener(data => handle((PointerEventData)data));
                events.triggers.Add(binding);
            }
            Bind(EventTriggerType.PointerDown, Press);
            Bind(EventTriggerType.BeginDrag, Begin);
            Bind(EventTriggerType.Drag, Move);
            Bind(EventTriggerType.PointerUp, Release);
            Bind(EventTriggerType.EndDrag, Release);
            Application.focusChanged += OnFocus;
            scope.Add(() =>
            {
                Application.focusChanged -= OnFocus;
                Cancel();
                if (events)
                {
                    events.triggers.Clear();
                    UnityEngine.Object.Destroy(events);
                }
            });
        }

        internal void Cancel()
        {
            if (pressed != null) pressed.eligibleForClick = false;
            pressed = null;
            dragging = false;
        }

        private void OnFocus(bool focused)
        {
            if (!focused) Cancel();
        }

        private void Press(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            if (pressed != null)
            {
                data.eligibleForClick = false;
                return;
            }
            if (!LocalPoint(data, out var point)) return;
            pressed = data;
            grabOffset = (Vector2)entry.localPosition - point;
        }

        private void Begin(PointerEventData data)
        {
            if (pressed != data) return;
            // UGUI keeps a press eligible if the drag handler and button are on the same object.
            data.eligibleForClick = false;
            dragging = true;
        }

        private void Move(PointerEventData data)
        {
            if (!dragging || pressed != data || !LocalPoint(data, out var point)) return;
            Rect bounds = area.rect;
            Vector2 size = entry.rect.size;
            Vector2 lower = bounds.min + Vector2.Scale(size, entry.pivot);
            Vector2 upper = bounds.max - Vector2.Scale(size, Vector2.one - entry.pivot);
            Vector2 position = point + grabOffset;
            var anchor = new Vector2(Mathf.InverseLerp(lower.x, upper.x, position.x),
                Mathf.InverseLerp(lower.y, upper.y, position.y));
            // Anchors preserve the fraction of the available travel as the safe area changes.
            entry.anchorMin = entry.anchorMax = anchor;
            entry.anchoredPosition = Vector2.Scale(entry.pivot - anchor, size);
        }

        private bool LocalPoint(PointerEventData data, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, data.position, data.pressEventCamera, out point);

        private void Release(PointerEventData data)
        {
            if (pressed != data) return;
            if (dragging) Move(data);
            pressed = null;
            dragging = false;
        }
    }
}
