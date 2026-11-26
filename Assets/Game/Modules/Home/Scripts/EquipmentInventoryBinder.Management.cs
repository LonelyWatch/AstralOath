using System;
using System.Collections.Generic;
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
        private static readonly string[] ManagementSets = { null, "speed", "critical" };
        private int managementSet;
        private bool managementSortByScore;
        private static string PanelValue(int index, int value) => index < 4 ? Num(value) : Percent(value);

        private void NextManagementSet()
        {
            if (busy) return;
            managementSet = (managementSet + 1) % ManagementSets.Length;
            Refresh();
        }

        private void ToggleManagementSort()
        {
            if (busy) return;
            managementSortByScore = !managementSortByScore;
            Refresh();
        }

        private IEnumerable<EquipmentInventoryItem> ManagementCandidates(IEnumerable<EquipmentInventoryItem> candidates)
        {
            var set = ManagementSets[managementSet];
            Get<GameText>("manageSetFilterLabel").text = set == null ? L("manageAllSets") : SetName(set);
            Get<Image>("manageSetFilterSymbol").sprite = set == null ? Get<Image>("manageFilterAllArt").sprite : Get<Image>("set_" + set).sprite;
            Get<GameText>("manageSortLabel").text = (managementSortByScore ? L("manageScoreSort") : L("sortItemLevel")) + " ↓";
            if (set != null) candidates = candidates.Where(item => item.setId == set);
            var ordered = managementSortByScore ? candidates.OrderByDescending(ItemScore).ThenByDescending(item => item.itemLevel) :
                candidates.OrderByDescending(item => item.itemLevel);
            return FilterManagementCandidates(ordered.ThenByDescending(item => item.enhanceLevel).ThenBy(item => item.instanceId, StringComparer.Ordinal));
        }

        private void PaintManagementQuote(int[] before, int[] after, List<string> notes)
        {
            Get<GameText>("manageSummary").text = L("manageChangeSummary");
            Get<GameText>("manageQuoteBeforeTitle").text = L("combatPower");
            Get<GameText>("manageQuoteAfterTitle").text = L("combatPower");
            bool known = TryCombatPowerQuote(before, after, out int current, out int proposed);
            Get<GameText>("manageQuoteBefore").text = known ? CombatPowerNumber(current) : "—";
            Get<GameText>("manageQuoteAfter").text = known ? CombatPowerNumber(proposed) : "—";
            int delta = proposed - current;
            var change = Get<GameText>("manageQuoteDelta");
            change.text = known ? CombatPowerNumber(Math.Abs(delta)) : "—";
            var color = delta < 0 ? new Color32(56,148,240,255) : new Color32(255,120,0,255);
            change.color = known && delta != 0 ? color : Color.white;
            var direction = Get<Image>("manageQuoteDirection");
            direction.enabled = known;
            direction.color = color;
            var arrow = Get<Image>("manageQuoteDeltaArrow");
            arrow.enabled = known && delta != 0;
            arrow.color = color;
            arrow.rectTransform.localEulerAngles = new Vector3(0,0,delta < 0 ? 180f : 0f);
            Get<GameText>("manageChange").text = string.Join("\n",notes);
        }

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
            var apply = Get<GameButton>("apply");
            apply.interactable = choosing && hero != null && hero.instanceId != selected.equippedHeroId && !loadFailed;
            apply.targetGraphic.color = apply.interactable ? Color.white : new Color(.46f,.46f,.46f,1f);
            Get<GameText>("applyLabel").color = apply.interactable ? Color.white : new Color32(139,150,159,255);
            var reset = Get<GameButton>("manageReset");
            reset.interactable = choosing;
            ((Image)reset.targetGraphic).sprite = Get<Image>(choosing ? "manageFilterWhiteArt" : "manageFilterBlackArt").sprite;
            Get<Image>("manageResetIcon").color = choosing ? Color.black : new Color32(255,255,255,76);
            Get<GameObject>("manageCurrentGroup").SetActive(!choosing);
            Get<GameText>("managePrompt").gameObject.SetActive(!choosing);
            Get<GameText>("manageHeroName").text = hero == null ? "" : heroName(hero.instanceId);
            for (int i = 0; i < 6; i++) Get<Image>("manageHeroStar" + i).enabled = hero != null && i < hero.stars;
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
            var proposed = plain == null ? null : PanelValues(hero.instanceId,plain,previewPieces);
            PaintLoadout("manageCurrent",current,currentPieces,original,null);
            PaintLoadout("managePreview",preview,previewPieces,proposed,original,choosing);
            if (!choosing) return;

            BindSlotArt(new EquipmentItemCardBinder(Get<BindingMap>("manageThumbnail")),selected);
            Get<Image>("manageDetailPlate").sprite = Get<Image>("manageDetail_" + selected.grade).sprite;
            Get<Image>("manageMainStatIcon").sprite = StatArt(selected.mainStat);
            Get<Image>("manageMainStatIcon").enabled = true;
            Get<GameText>("name").text = ItemName(selected);
            Get<GameText>("manageDescription").text = ItemDescription(selected);
            PaintEquipmentScore("manageScore",selected);
            Get<GameText>("quality").text = L(selected.grade) + L(selected.slot);
            Get<GameText>("quality").color = selected.grade switch
            {
                "good" => new Color32(121,203,104,255), "rare" => new Color32(116,181,243,255),
                "heroic" => new Color32(218,122,234,255), "epic" => new Color32(238,87,62,255),
                _ => new Color32(200,193,180,255)
            };
            Get<GameText>("wearer").text = string.IsNullOrEmpty(selected.equippedHeroId) ? L("unworn") : F("owner",heroName(selected.equippedHeroId));
            int proposedSetCount = preview.Count(item => item?.setId == selected.setId);
            Get<GameText>("manageSet").text = SetName(selected.setId) + " (" + Num(proposedSetCount) + "/" +
                (selected.setId == "speed" ? "4" : "2") + ")";
            Get<GameText>("manageSetEffect").text = L(selected.setId + "SetEffect");
            var setIcon = Get<Image>("manageSetIcon");
            setIcon.sprite = SetArt(selected);
            setIcon.enabled = setIcon.sprite;
            for (int i = 0; i < 5; ++i)
            {
                var row = Get<BindingMap>("manageItemStat"+i);
                var stat = i == 0 ? selected.mainStat : selected.substats.ElementAtOrDefault(i-1);
                row.gameObject.SetActive(stat != null);
                if (stat != null) new EquipmentStatRowBinder(row).Set(L(stat.kind),StatValue(stat));
            }
        }

        private void PaintLoadout(string group, EquipmentInventoryItem[] gear, EquipmentPiece[] pieces, int[] values, int[] baseline, bool showValues = true)
        {
            var targets = group == "managePreview" ? managerPreview : managerCurrent;
            for (int i = 0; i < targets.Length; ++i)
            {
                // The editor authors these slots before the Canvas has a measurable width.
                // Activate width-driven layout only after the page is on its runtime Canvas.
                Get<BindingMap>(group + "_" + Slots[i]).GetComponent<AspectRatioFitter>().enabled = true;
                PaintWornSlot(targets[i], gear[i]);
                targets[i].Root.GetComponent<CanvasGroup>().alpha = group == "managePreview" && !showValues ? .45f : 1f;
            }
            for (int i = 0; i < PanelKeys.Length; ++i)
            {
                var map = Get<BindingMap>(group + "_" + PanelKeys[i]);
                map.gameObject.SetActive(values != null && showValues);
                if (group == "managePreview")
                {
                    var change = Get<GameText>("manageDelta_" + PanelKeys[i]);
                    change.gameObject.SetActive(showValues);
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
