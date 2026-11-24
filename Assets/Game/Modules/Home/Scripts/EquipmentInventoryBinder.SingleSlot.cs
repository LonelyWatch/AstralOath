using System;
using System.Linq;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private SingleSlotEquipment singleView;
        private SingleSlotEquipmentBinder singleCards;
        private string slotHero, slotName, candidateId;
        private string slotFeedback;
        private long slotVersion = -1;
        private bool showingWornPopup;
        private CancellationToken slotToken;

        private void BindSingleSlot(BindingScope scope)
        {
            singleView = new SingleSlotEquipment(Get<BindingMap>("singleSlot"));
            singleCards = new SingleSlotEquipmentBinder(singleView, BindSlotArt, ItemName, StatArt, StatValue);
            slotToken = scope.Token;
            void Hook(string key, UnityAction handler)
            {
                var button = singleView.Map.Require<GameButton>(key);
                button.onClick.AddListener(handler);
                scope.Add(() => { if (button) button.onClick.RemoveListener(handler); });
            }
            singleView.Map.Require<GameButton>("back").FeedbackSound = GameUiFeedback.Sound.PageBack;
            Hook("back", () => Back());
            Hook("dismiss", () => Back());
            Hook("change", () =>
            {
                if (busy) return;
                showingWornPopup = false;
                candidateId = null;
                SetSingleSlotFeedback("");
                RefreshSingleSlot();
            });
            Hook("equip", () => SubmitSlotItem("equip"));
            Hook("unequip", () => SubmitSlotItem("unequip"));
            Hook("popupRemove", () => SubmitSlotItem("unequip"));
            Hook("retry", () => { if (!busy) _ = RetryAsync(slotToken); });
            scope.Add(ResetSingleSlot);
        }

        private void BindSlotArt(EquipmentItemCardBinder card, EquipmentInventoryItem item) =>
            card.Set(Icon(item), GradeArt(item), SetArt(item), null, WearerArt(item), item.itemLevel, item.enhanceLevel, null);

        private EquipmentInventoryItem WornSlotItem() =>
            Items.FirstOrDefault(item => item.equippedHeroId == slotHero && item.slot == slotName);

        internal bool HasWornItem(string hero, int index) =>
            hero != null && loadouts.TryGetValue(hero, out var worn) && worn[index] != null;

        private EquipmentInventoryItem CandidateSlotItem() =>
            Items.FirstOrDefault(item => item.instanceId == candidateId && item.slot == slotName && item.equippedHeroId != slotHero);

        private bool TryShowSingleSlot(bool fromHero, string slot)
        {
            if (busy) return true;
            ResetSingleSlot();
            string hero = currentHero()?.instanceId;
            if (!fromHero || string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(hero)) return false;
            slotHero = hero;
            slotName = slot;
            picker = true;
            showingWornPopup = WornSlotItem() != null;
            selected = null;
            ui.gameObject.SetActive(true);
            Get<GameObject>("pickerSurface").SetActive(false);
            Get<GameButton>("shade").gameObject.SetActive(false);
            bagPane.gameObject.SetActive(false);
            CloseDialog();
            singleView.Show();
            RefreshSingleSlot();
            return true;
        }

        private void ResetSingleSlot()
        {
            slotHero = slotName = candidateId = null;
            slotVersion = -1;
            slotFeedback = "";
            showingWornPopup = false;
            if (singleView?.Root) singleView.Hide();
            singleCards?.ClearList();
        }

        private bool BackSingleSlot()
        {
            if (slotName == null) return false;
            if (!busy) Hide();
            return true;
        }

        private void OnSingleSlotApplied()
        {
            if (slotName != null) Hide();
        }

        private void SetSingleSlotFeedback(string message)
        {
            if (slotName == null) return;
            slotFeedback = message;
            singleView.Map.Require<GameText>("feedback").text = message;
        }

        private bool RefreshSingleSlot()
        {
            if (slotName == null) return false;
            long version = snapshot?.version ?? -1;
            if (slotVersion >= 0 && version != slotVersion && (candidateId != null || requestId != null))
            {
                candidateId = null;
                selected = null;
                CloseDialog();
                SetSingleSlotFeedback(L("conflict"));
            }
            slotVersion = version;
            var worn = WornSlotItem();
            var candidate = CandidateSlotItem();
            if (candidate == null) candidateId = null;
            if (worn == null) showingWornPopup = false;
            singleView.Comparison.SetActive(!showingWornPopup);
            singleView.Popup.SetActive(showingWornPopup);
            singleView.Map.Require<GameButton>("retry").gameObject.SetActive(loadFailed);
            singleView.Map.Require<GameText>("feedback").text = loadFailed ? L("loadFailed") : slotFeedback;
            if (showingWornPopup)
            {
                singleView.Map.Require<Image>("popupPlate").sprite = singleView.Map.Require<Image>("popup_" + worn.grade).sprite;
                PaintSlotColumn("popupItem", worn);
            }
            else
            {
                singleCards.ShowList(snapshot, slotName, slotHero, candidateId, ChooseSlotItem);
                PaintSlotColumn("current", worn);
                PaintSlotColumn("candidate", candidate);
                singleView.Map.Require<GameButton>("equip").gameObject.SetActive(candidate != null);
                singleView.Map.Require<GameButton>("unequip").gameObject.SetActive(worn != null);
            }
            return true;
        }

        private void ChooseSlotItem(string id)
        {
            if (busy || slotName == null || Get<GameObject>("dialog").activeSelf) return;
            if (!Items.Any(i => i.instanceId == id && i.slot == slotName && i.equippedHeroId != slotHero)) return;
            if (candidateId != id) CloseDialog();
            candidateId = id;
            SetSingleSlotFeedback("");
            RefreshSingleSlot();
        }

        private void PaintSlotColumn(string key, EquipmentInventoryItem item)
        {
            var map = singleView.Map.Require<BindingMap>(key);
            map.Require<GameObject>("empty").SetActive(item == null);
            map.Require<GameObject>("detail").SetActive(item != null);
            if (item == null) return;
            BindSlotArt(new EquipmentItemCardBinder(map.Require<BindingMap>("card")),item);
            var grade = map.Require<GameText>("grade");
            grade.text = L(item.grade) + L(item.slot);
            grade.color = item.grade switch
            {
                "good" => new Color32(121,203,104,255), "rare" => new Color32(116,181,243,255),
                "heroic" => new Color32(218,122,234,255), "epic" => new Color32(238,87,62,255),
                _ => new Color32(200,193,180,255)
            };
            map.Require<GameText>("name").text = ItemName(item);
            map.Require<GameText>("description").text = ItemDescription(item);
            if (key != "popupItem")
            {
                var image=map.Require<Image>("mainIcon");
                image.sprite = StatArt(item.mainStat);
                image.enabled = image.sprite;
            }
            new EquipmentStatRowBinder(map.Require<BindingMap>("main")).Set(L(item.mainStat.kind),StatValue(item.mainStat));
            for (int i=0; i<4; ++i)
            {
                var row = map.Require<BindingMap>("sub"+i);
                bool hasStat = i < item.substats.Length;
                row.gameObject.SetActive(hasStat);
                if (hasStat) new EquipmentStatRowBinder(row).Set(L(item.substats[i].kind),StatValue(item.substats[i]));
            }
            new EquipmentStatRowBinder(map.Require<BindingMap>("score")).Set(
                locale.Resolve("home.hero_inventory.sort_equipment_score"), Num(ItemScore(item)));
            var emblem = map.Require<Image>("setIcon");
            emblem.sprite = SetArt(item);
            emblem.enabled = emblem.sprite;
            map.Require<GameText>("set").text = SetProgress(item, emphasizeName: true);
        }

        private void SubmitSlotItem(string operation)
        {
            if (busy || slotName == null || snapshot == null || loadFailed || Get<GameObject>("dialog").activeSelf) return;
            var item = operation == "equip" ? CandidateSlotItem() : WornSlotItem();
            if (item == null) return;
            bool retry = action == operation && selected?.instanceId == item.instanceId && targetId == slotHero
                && requestId != null && quotedVersion == snapshot.version;
            selected = item;
            if (operation == "equip" && !string.IsNullOrEmpty(item.equippedHeroId))
            {
                Prepare(operation);
                return;
            }
            if (!retry)
            {
                action = operation;
                targetId = slotHero;
                quotedVersion = snapshot.version;
                requestId = Guid.NewGuid().ToString("N");
            }
            SetSingleSlotFeedback("");
            _ = SubmitAsync(slotToken);
        }
    }
}
