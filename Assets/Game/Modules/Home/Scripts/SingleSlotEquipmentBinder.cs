using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Home
{
    internal sealed class SingleSlotEquipmentBinder
    {
        private readonly SingleSlotEquipment view;
        private readonly Action<EquipmentItemCardBinder, EquipmentInventoryItem> bindCard;
        private readonly Func<EquipmentInventoryItem, string> itemName;
        private readonly Func<EquipmentStatValue, Sprite> statArt;
        private readonly Func<EquipmentStatValue, string> statValue;
        private readonly List<GameObject> instances = new List<GameObject>();
        private readonly Dictionary<string, Image> outlines = new Dictionary<string, Image>();
        private long listVersion = -1;
        private string listedHero, listedSlot;

        internal SingleSlotEquipmentBinder(SingleSlotEquipment view,
            Action<EquipmentItemCardBinder, EquipmentInventoryItem> bindCard,
            Func<EquipmentInventoryItem, string> itemName, Func<EquipmentStatValue, Sprite> statArt,
            Func<EquipmentStatValue, string> statValue)
        {
            this.view = view;
            this.bindCard = bindCard;
            this.itemName = itemName;
            this.statArt = statArt;
            this.statValue = statValue;
        }

        internal void ShowList(EquipmentInventorySnapshot snapshot, string slot, string hero, string selectedId,
            Action<string> choose)
        {
            if (listVersion != (snapshot?.version ?? 0) || listedHero != hero || listedSlot != slot)
            {
                bool sameSlot = listedHero == hero && listedSlot == slot;
                float position = sameSlot ? view.Candidates.verticalNormalizedPosition : 1;
                ClearList();
                listedHero = hero;
                listedSlot = slot;
                listVersion = snapshot?.version ?? 0;
                var candidates = (snapshot?.items ?? Array.Empty<EquipmentInventoryItem>())
                    .Where(item => item.slot == slot && item.equippedHeroId != hero)
                    .OrderByDescending(item => item.enhanceLevel).ThenByDescending(item => item.itemLevel)
                    .ThenBy(item => item.instanceId, StringComparer.Ordinal).ToArray();
                view.Map.Require<GameText>("capacity").text = snapshot == null ? "" : candidates.Length + " / " + snapshot.capacity;
                foreach (var item in candidates)
                {
                    var row = Object.Instantiate(view.Row, view.Candidates.content, false);
                    instances.Add(row);
                    row.name = item.instanceId;
                    var map = row.GetComponent<BindingMap>();
                    bindCard(new EquipmentItemCardBinder(map.Require<BindingMap>("card")), item);
                    var icon = map.Require<Image>("statIcon");
                    icon.sprite = statArt(item.mainStat);
                    icon.enabled = icon.sprite;
                    map.Require<GameText>("stat").text = statValue(item.mainStat);
                    map.Require<GameText>("name").text = itemName(item);
                    map.Require<GameButton>("select").onClick.AddListener(() => choose(item.instanceId));
                    outlines.Add(item.instanceId, map.Require<Image>("highlight"));
                    row.SetActive(true);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(view.Candidates.content);
                view.Candidates.verticalNormalizedPosition = position;
            }
            foreach (var line in outlines) line.Value.enabled = line.Key == selectedId;
        }

        internal void ClearList()
        {
            foreach (var row in instances)
            {
                if (!row) continue;
                row.SetActive(false);
                Object.Destroy(row);
            }
            instances.Clear();
            outlines.Clear();
            listVersion = -1;
            listedHero = listedSlot = null;
        }
    }
}
