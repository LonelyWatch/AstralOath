using System;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private static readonly string[] PanelKeys =
        {
            "attack", "defense", "health", "speed", "critChanceBp", "critDamageBp", "effectivenessBp", "resistanceBp"
        };
        private bool Managing => picker && slotName == null;
        private static string PanelValue(int index, int value) => index < 4 ? Num(value) : Percent(value);

        private int[] PanelValues(string hero, BattleStats plain, EquipmentPiece[] pieces)
        {
            var values = EquipmentStatCalculator.Calculate(plain,pieces);
            var artifact = artifactStats(hero);
            return new[] { values.Attack + artifact.attack, values.Defense, values.MaxHealth + artifact.health,
                values.Speed, values.CritChanceBp, values.CritDamageBp, values.EffectivenessBp, values.EffectResistanceBp };
        }

        private void PaintManager()
        {
            var hero = currentHero();
            bool choosing = selected != null;
            Get<GameButton>("apply").interactable = choosing && hero != null && hero.instanceId != selected.equippedHeroId && !loadFailed;
            Get<GameButton>("manageReset").gameObject.SetActive(choosing);
            Get<GameObject>("manageCurrentGroup").SetActive(!choosing);
            Get<GameText>("managePrompt").gameObject.SetActive(!choosing);
            Get<GameText>("manageHeroName").text = hero == null ? "" : heroName(hero.instanceId);
            var portrait = Get<Image>("manageHeroIcon");
            portrait.sprite = hero == null ? null : heroPortrait(hero.instanceId);
            portrait.enabled = portrait.sprite;
            if (hero == null) return;

            var current = loadouts.TryGetValue(hero.instanceId,out var worn) ? worn : new EquipmentInventoryItem[Slots.Length];
            var preview = (EquipmentInventoryItem[])current.Clone();
            if (choosing) preview[Array.IndexOf(Slots,selected.slot)] = selected;
            var plain = baseStats(hero);
            var currentPieces = current.Where(item => item != null).Select(item => item.ToPiece()).ToArray();
            var previewPieces = preview.Where(item => item != null).Select(item => item.ToPiece()).ToArray();
            var original = plain == null ? null : PanelValues(hero.instanceId,plain,currentPieces);
            var proposed = !choosing || plain == null ? null : PanelValues(hero.instanceId,plain,previewPieces);
            PaintLoadout("manageCurrent",current,currentPieces,original,null);
            PaintLoadout("managePreview",preview,previewPieces,proposed,original);
            if (!choosing) return;

            Get<Image>("manageDetailPlate").sprite = Get<Image>("managePlate_" + selected.grade).sprite;
            BindSlotArt(new EquipmentItemCardBinder(Get<BindingMap>("manageThumbnail")),selected);
            Get<GameText>("name").text = ItemName(selected);
            Get<GameText>("manageDescription").text = ItemDescription(selected);
            PaintEquipmentScore("manageScore",selected);
            Get<GameText>("quality").text = L(selected.grade) + L(selected.slot);
            Get<GameText>("wearer").text = string.IsNullOrEmpty(selected.equippedHeroId) ? L("unworn") : F("owner",heroName(selected.equippedHeroId));
            Get<GameText>("manageSet").text = SetProgress(selected);
            for (int i = 0; i < 5; ++i)
            {
                var row = Get<BindingMap>("manageItemStat"+i);
                var stat = i == 0 ? selected.mainStat : selected.substats.ElementAtOrDefault(i-1);
                row.gameObject.SetActive(stat != null);
                if (stat != null) new EquipmentStatRowBinder(row).Set(L(stat.kind),StatValue(stat));
            }
        }

        private void PaintLoadout(string group, EquipmentInventoryItem[] gear, EquipmentPiece[] pieces, int[] values, int[] baseline)
        {
            var targets = group == "managePreview" ? managerPreview : managerCurrent;
            for (int i = 0; i < targets.Length; ++i)
                PaintWornSlot(targets[i], gear[i]);
            for (int i = 0; i < PanelKeys.Length; ++i)
            {
                var map = Get<BindingMap>(group + "_" + PanelKeys[i]);
                map.gameObject.SetActive(values != null);
                if (group == "managePreview")
                {
                    var change = Get<GameText>("manageDelta_" + PanelKeys[i]);
                    int delta = values == null || baseline == null ? 0 : values[i]-baseline[i];
                    change.text = delta == 0 ? "" : (delta > 0 ? "+" : "") + PanelValue(i,delta);
                    change.color = delta > 0 ? new Color32(255,149,41,255) : new Color32(56,148,240,255);
                }
                if (values != null) new EquipmentStatRowBinder(map).Set(L(PanelKeys[i]),PanelValue(i,values[i]));
            }
            var sets = EquipmentStatCalculator.ActiveSets(pieces);
            Get<GameText>(group + "Set").text = values == null ? "" : sets.Count == 0 ?
                locale.Resolve("home.hero_inventory.roster_no_set") :
                string.Join("  ",sets.Select(pair => F("setCount",SetName(pair.Key),pair.Value)));
        }
    }
}
