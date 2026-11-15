using UnityEngine;
using UnityEngine.EventSystems;

namespace AstralOath.Client.Arena
{
    // Attached at runtime to the ScrollRect so the roster only snaps after release.
    internal sealed class ArenaDraftRosterGesture : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        internal bool Dragging { get; private set; }
        internal void ResetDrag() => Dragging = false;
        public void OnBeginDrag(PointerEventData eventData) => Dragging = true;
        public void OnEndDrag(PointerEventData eventData) => ResetDrag();
        private void OnDisable() => ResetDrag();
    }
}
