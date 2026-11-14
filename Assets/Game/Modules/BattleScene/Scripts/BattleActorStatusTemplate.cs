using System;
using UnityEngine;

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
}
