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
            Get<GameText>("manageSortLabel").text = (managementSortByScore ? L("manageScoreSort") : L("sortItemLevel")) + " ↓";
            if (set != null) candidates = candidates.Where(item => item.setId == set);
            var ordered = managementSortByScore ? candidates.OrderByDescending(ItemScore).ThenByDescending(item => item.itemLevel) :
                candidates.OrderByDescending(item => item.itemLevel);
            return FilterManagementCandidates(ordered.ThenByDescending(item => item.enhanceLevel).ThenBy(item => item.instanceId, StringComparer.Ordinal));
        }

        private void PaintManagementQuote(int[] before, int[] after, List<string> notes)
        {
            var changed = before == null ? Array.Empty<int>() : Enumerable.Range(0, before.Length)
                .Where(index => before[index] != after[index]).ToArray();
            Get<GameText>("manageQuoteMetric").text = string.Join("\n", changed.Select(i => L(PanelKeys[i])));
            Get<GameText>("manageQuoteBefore").text = string.Join("\n", changed.Select(i => PanelValue(i,before[i])));
            Get<GameText>("manageQuoteAfter").text = string.Join("\n", changed.Select(i => PanelValue(i,after[i])));
            Get<GameText>("manageQuoteDelta").text = string.Join("\n", changed.Select(i =>
            {
                int delta = after[i] - before[i];
                return delta > 0 ? "<color=#FFA652>+" + PanelValue(i,delta) + "</color>" :
                    "<color=#7AB8FF>" + PanelValue(i,delta) + "</color>";
            }));
            Get<GameText>("manageChange").text = notes.Count != 0 ? string.Join("\n",notes) :
                changed.Length == 0 ? L("noStatChange") : "";
            var panel = (RectTransform)Get<GameObject>("managementQuote").transform;
            var noteText = Get<GameText>("manageChange");
            float width = ((RectTransform)panel.parent).rect.width * .60f * .80f;
            float notesHeight = string.IsNullOrEmpty(noteText.text) ? 0f :
                Mathf.Max(30f, noteText.GetPreferredValues(noteText.text, width, float.PositiveInfinity).y + 12f);
            float valuesHeight = Mathf.Max(1,changed.Length) * 34f;
            float notesTop = 194f + valuesHeight + 18f;
            float panelHeight = Mathf.Max(500f,notesTop + notesHeight + 120f);
            float height = panelHeight / 1080f;
            panel.anchorMin = new Vector2(.20f,.50f - height / 2f);
            panel.anchorMax = new Vector2(.80f,.50f + height / 2f);
            void PlaceVertically(RectTransform rect, float top, float extent)
            {
                rect.anchorMin = new Vector2(rect.anchorMin.x,1f-(top+extent)/panelHeight);
                rect.anchorMax = new Vector2(rect.anchorMax.x,1f-top/panelHeight);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            PlaceVertically(Get<GameText>("manageQuoteTitle").rectTransform,24f,48f);
            PlaceVertically(Get<GameText>("manageSummary").rectTransform,86f,50f);
            PlaceVertically((RectTransform)Get<GameObject>("manageQuoteHeadings").transform,154f,34f);
            foreach (string column in new[] {"Metric","Before","After","Delta"})
            {
                PlaceVertically(Get<GameText>("manageQuoteHeading"+column).rectTransform,158f,30f);
                PlaceVertically(Get<GameText>("manageQuote"+column).rectTransform,194f,valuesHeight);
            }
            PlaceVertically(noteText.rectTransform,notesTop,notesHeight);
            PlaceVertically((RectTransform)Get<GameButton>("manageCancel").transform,panelHeight-90f,64f);
            PlaceVertically((RectTransform)Get<GameButton>("manageConfirm").transform,panelHeight-90f,64f);
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
            Get<GameButton>("manageReset").gameObject.SetActive(choosing);
            Get<GameObject>("manageCurrentGroup").SetActive(!choosing);
            Get<GameText>("managePrompt").gameObject.SetActive(!choosing);
            Get<GameText>("manageHeroName").text = hero == null ? "" : heroName(hero.instanceId);
            Get<GameText>("manageHeroStars").text = hero == null ? "" : new string('★',hero.stars);
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
            PaintLoadout("managePreview",preview,previewPieces,proposed,original);
            if (!choosing) return;

            BindSlotArt(new EquipmentItemCardBinder(Get<BindingMap>("manageThumbnail")),selected);
            Get<GameText>("name").text = ItemName(selected);
            Get<GameText>("manageDescription").text = ItemDescription(selected);
            PaintEquipmentScore("manageScore",selected);
            Get<GameText>("quality").text = L(selected.grade) + L(selected.slot);
            Get<GameText>("wearer").text = string.IsNullOrEmpty(selected.equippedHeroId) ? L("unworn") : F("owner",heroName(selected.equippedHeroId));
            Get<GameText>("manageSet").text = SetProgress(selected);
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

        private void PaintLoadout(string group, EquipmentInventoryItem[] gear, EquipmentPiece[] pieces, int[] values, int[] baseline)
        {
            var targets = group == "managePreview" ? managerPreview : managerCurrent;
            for (int i = 0; i < targets.Length; ++i)
            {
                // The editor authors these slots before the Canvas has a measurable width.
                // Activate width-driven layout only after the page is on its runtime Canvas.
                Get<BindingMap>(group + "_" + Slots[i]).GetComponent<AspectRatioFitter>().enabled = true;
                PaintWornSlot(targets[i], gear[i]);
            }
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
