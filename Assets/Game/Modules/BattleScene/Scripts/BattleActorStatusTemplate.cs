using System;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    /// <summary>Actor-owned authoring data. Visible markers are owned by the battle View under its shared Canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class BattleActorStatusTemplate : MonoBehaviour
    {
        [SerializeField] private RectTransform markerTemplate;
        [SerializeField] private BattleUnitPresentationProfile profile;

        public BattleUnitPresentationProfile Profile => profile;

        public GameObject CreateMarker(Transform team, string slotName)
        {
            if (!markerTemplate || !markerTemplate.IsChildOf(transform) || markerTemplate.gameObject.activeSelf)
                throw new InvalidOperationException("BattleActor requires an inactive child status template.");
            if (!(team is RectTransform) || !team.GetComponentInParent<Canvas>())
                throw new ArgumentException("Actor status markers require the battle's shared Canvas.", nameof(team));
            if (string.IsNullOrWhiteSpace(slotName)) throw new ArgumentException("A slot name is required.", nameof(slotName));

            var marker = Instantiate(markerTemplate.gameObject, team, false);
            marker.name = slotName;
            marker.SetActive(true);
            return marker;
        }
    }

    internal sealed class BattleTargetBadge
    {
        private readonly Image background;
        private readonly Image digit;
        private readonly GameButton button;
        private readonly UnityAction click;

        internal BattleTargetBadge(RectTransform parent, Vector2 position, Vector2 size, UnityAction click = null,
            Vector2? digitOffset = null)
        {
            this.click = click;
            var root = new GameObject("Target Number", typeof(RectTransform), typeof(Image));
            root.SetActive(false);
            Root = (RectTransform)root.transform;
            Root.SetParent(parent, false);
            Root.anchoredPosition = position;
            Root.sizeDelta = size;
            background = root.GetComponent<Image>();
            background.raycastTarget = click != null;
            background.preserveAspect = true;
            var number = new GameObject("Number", typeof(RectTransform), typeof(Image));
            var numberRect = (RectTransform)number.transform;
            numberRect.SetParent(Root, false);
            numberRect.anchoredPosition = digitOffset ?? new Vector2(0, 1f);
            numberRect.sizeDelta = new Vector2(size.x * .65f, size.y * .68f);
            digit = number.GetComponent<Image>();
            digit.preserveAspect = true;
            digit.raycastTarget = false;
            if (click == null) return;
            button = root.AddComponent<GameButton>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.FeedbackSound = GameUiFeedback.Sound.Silent;
            button.onClick.AddListener(click);
        }

        internal RectTransform Root { get; }
        internal bool IsShown => Root.gameObject.activeSelf;

        internal void Show(Sprite art, Sprite number)
        {
            bool visible = art && number;
            if (visible)
            {
                background.sprite = art;
                digit.sprite = number;
            }
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        internal void RemoveClick()
        {
            if (button) button.onClick.RemoveListener(click);
        }
    }
}
