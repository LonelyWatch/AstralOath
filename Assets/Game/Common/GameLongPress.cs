using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    public sealed class GameLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private const float HoldSeconds = .45f;
        private PointerEventData pointer;
        private Vector2 origin;
        private double started;
        private int? consumed;

        public Action Held { get; set; }
        public bool IsTracking => pointer != null;

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || pointer != null || Held == null) return;
            consumed = null;
            pointer = data;
            origin = data.position;
            started = Time.unscaledTimeAsDouble;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (pointer?.pointerId == data.pointerId) pointer = null;
            if (consumed == data.pointerId) data.eligibleForClick = false;
        }

        public void OnPointerExit(PointerEventData data)
        {
            if (pointer?.pointerId == data.pointerId) Cancel();
        }

        private void Update()
        {
            if (pointer == null) return;
            float threshold = Mathf.Max(10f, EventSystem.current ? EventSystem.current.pixelDragThreshold : 0f);
            if ((pointer.position - origin).sqrMagnitude > threshold * threshold)
            {
                Cancel();
                return;
            }
            if (Time.unscaledTimeAsDouble - started >= HoldSeconds)
            {
                Cancel();
                Held?.Invoke();
            }
        }

        public bool ConsumedClick(PointerEventData data) => consumed == data.pointerId;

        public void Cancel()
        {
            if (pointer == null) return;
            consumed = pointer.pointerId;
            pointer.eligibleForClick = false;
            pointer = null;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        private void OnDisable() => Cancel();
    }
}
