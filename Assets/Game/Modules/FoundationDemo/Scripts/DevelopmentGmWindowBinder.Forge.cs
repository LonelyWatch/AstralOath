using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine.Events;
using static AstralOath.Client.FoundationDemo.GmForgeRules;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class DevelopmentGmWindowBinder
    {
        private string forgeSlot = "weapon", forgeGrade = "epic", forgeSet = "speed", forgeMain;
        private int forgeLevel = 85, lastBudget = -1;
        private bool sendingForge;
        private readonly string[] subKinds = new string[4];
        private GmForgeRules Rules => snapshot?.forgeRules;
        private bool HasRules => Rules?.tiers?.Length > 0 && Rules.slots?.Length > 0 &&
            Rules.grades?.Length > 0 && Rules.grades.All(g => g?.unlockEnhancements?.Length == subKinds.Length) &&
            Rules.sets?.Length > 0;
        private Tier CurrentTier => Rules.tiers.First(t => t.itemLevel == forgeLevel);
        private Slot CurrentSlot => Rules.slots.First(s => s.slot == forgeSlot);

        private static T Next<T>(IEnumerable<T> options, T current)
        {
            var values = options.ToArray();
            return values[(Array.IndexOf(values, current) + 1) % values.Length];
        }

        private void BindForge(BindingScope scope)
        {
            void Cycle(string key, Action change) => Listen(view.Button(key), scope, () =>
            {
                if (!HasRules || pending != null) return;
                change();
                DrawForge();
            });
            void Watch(string field, UnityAction<string> callback)
            {
                var input = view.Field(field);
                input.onValueChanged.AddListener(callback);
                scope.Add(() => input.onValueChanged.RemoveListener(callback));
            }
            Cycle("forgeSlot", () => forgeSlot = Next(Rules.slots.Select(x => x.slot), forgeSlot));
            Cycle("forgeTier", () =>
            {
                forgeLevel = Next(Rules.tiers.Select(x => x.itemLevel), forgeLevel);
                var tier = CurrentTier;
                int.TryParse(view.Field("forgeEnhance").text, out int current);
                view.Field("forgeEnhance").SetTextWithoutNotify(Math.Clamp(current, tier.minEnhance, tier.maxEnhance).ToString());
                lastBudget = -1;
            });
            Cycle("forgeGrade", () => forgeGrade = Next(Rules.grades.Select(x => x.grade), forgeGrade));
            Cycle("forgeSet", () => forgeSet = Next(Rules.sets, forgeSet));
            Cycle("forgeMain", () => forgeMain = Next(CurrentSlot.mainKinds, forgeMain));
            Watch("forgeEnhance", _ => DrawForge());
            Watch("forgeCount", _ => UpdateForgeValues());
            for (int i = 0; i < 4; i++)
            {
                int row = i;
                Cycle("forgeSub" + row, () =>
                {
                    var allowed = CurrentSlot.subKinds.Where(key => key != forgeMain &&
                        !subKinds.Where((_, index) => index != row).Contains(key));
                    subKinds[row] = Next(allowed, subKinds[row]);
                    view.Field("forgeValue" + row).SetTextWithoutNotify("");
                });
                Watch("forgeRolls" + row, _ => UpdateForgeValues());
                Watch("forgeValue" + row, _ => UpdateForgeValues());
            }
            if (view.Field("forgeCount").text == "") view.Field("forgeCount").SetTextWithoutNotify("1");
            if (view.Field("forgeEnhance").text == "") view.Field("forgeEnhance").SetTextWithoutNotify("0");
        }

        private void Caption(string button, string text) =>
            view.Button(button).GetComponentInChildren<GameText>(true).text = text;
        private string Gear(string id) => locale.Resolve("home.gear." + id);
        private string StatName(string id) => Gear(id) + (id.EndsWith("Pct", StringComparison.Ordinal) ? " %" : "");
        private static bool Percent(string kind) =>
            kind.EndsWith("Pct", StringComparison.Ordinal) || kind.EndsWith("Bp", StringComparison.Ordinal);
        private static string Display(string kind, int raw) => Percent(kind) ?
            (raw / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%" : raw.ToString(CultureInfo.InvariantCulture);
        private static bool Number(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        private void DrawForge()
        {
            if (kind != "forgeEquipment") return;
            bool ready = HasRules;
            bool editing = ready && pending == null && !sendingForge;
            foreach (string key in new[] { "forgeSlot", "forgeTier", "forgeGrade", "forgeSet", "forgeMain" })
                view.Button(key).interactable = editing;
            view.Field("forgeCount").interactable = view.Field("forgeEnhance").interactable = editing;
            view.Button("forgeMake").interactable = !sendingForge && (ready || pending != null);
            Caption("forgeMake", T(sendingForge ? "sending" : pending == null ? "forgeMake" : "retry"));
            if (!ready)
            {
                for (int index = 0; index < subKinds.Length; index++) SetRowEnabled(index, false);
                view.Label("forgeBudget").text = T("forgeUnavailable");
                return;
            }
            if (!Rules.tiers.Any(x => x.itemLevel == forgeLevel)) forgeLevel = Rules.tiers[0].itemLevel;
            if (!Rules.slots.Any(x => x.slot == forgeSlot)) forgeSlot = Rules.slots[0].slot;
            if (!Rules.grades.Any(x => x.grade == forgeGrade)) forgeGrade = Rules.grades[0].grade;
            if (!Rules.sets.Contains(forgeSet)) forgeSet = Rules.sets[0];
            var slot = CurrentSlot;
            var tier = CurrentTier;
            if (!slot.mainKinds.Contains(forgeMain)) forgeMain = slot.mainKinds[0];
            var main = tier.stats.First(x => x.kind == forgeMain);
            bool validEnhance = Number(view.Field("forgeEnhance").text, out int enhance) &&
                enhance >= tier.minEnhance && enhance <= tier.maxEnhance;
            string mainValue = validEnhance ? Display(main.kind,
                main.mainValues != null && enhance < main.mainValues.Length ? main.mainValues[enhance] : main.mainBase + main.mainStep * enhance) : "—";
            Caption("forgeSlot", T("forgeSlot", Gear(forgeSlot)));
            Caption("forgeTier", T("forgeTier", forgeLevel));
            Caption("forgeGrade", T("forgeGrade", Gear(forgeGrade)));
            Caption("forgeSet", T("forgeSet", Gear(forgeSet + "Set")));
            Caption("forgeMain", T("forgeMain", StatName(forgeMain) + "  " + mainValue));

            if (!validEnhance)
            {
                // Keep a partially edited recipe while the enhancement field is empty or incomplete.
                for (int index = 0; index < subKinds.Length; index++) SetRowEnabled(index, false);
                UpdateForgeValues();
                return;
            }
            var grade = Rules.grades.First(x => x.grade == forgeGrade);
            int budget = grade.initialSubstats + enhance / 3;
            int count = grade.unlockEnhancements.Count(at => at <= enhance);
            var used = new HashSet<string>(StringComparer.Ordinal) { forgeMain };
            for (int i = 0; i < 4; i++)
            {
                SetRowEnabled(i, i < count && editing);
                if (i >= count)
                {
                    subKinds[i] = null;
                    Caption("forgeSub" + i, "—");
                    view.Field("forgeRolls" + i).SetTextWithoutNotify("");
                    view.Field("forgeValue" + i).SetTextWithoutNotify("");
                    view.Label("forgeRange" + i).text = "—";
                    continue;
                }
                bool replace = !slot.subKinds.Contains(subKinds[i]) || used.Contains(subKinds[i]);
                if (replace) subKinds[i] = slot.subKinds.First(k => !used.Contains(k));
                used.Add(subKinds[i]);
                Caption("forgeSub" + i, StatName(subKinds[i]));
                bool reset = budget != lastBudget;
                if (reset) view.Field("forgeRolls" + i).SetTextWithoutNotify((i == 0 ? 1 + budget - count : 1).ToString());
                var valueField = view.Field("forgeValue" + i);
                if (reset || replace || valueField.text == "")
                {
                    var rule = tier.stats.First(s => s.kind == subKinds[i]);
                    Number(view.Field("forgeRolls" + i).text, out int rolls);
                    valueField.SetTextWithoutNotify((RollRange(rule).rollMin * Math.Clamp(rolls, 1, budget) /
                        (Percent(rule.kind) ? 100 : 1)).ToString());
                }
            }
            lastBudget = budget;
            UpdateForgeValues();
        }

        private void SetRowEnabled(int index, bool enabled)
        {
            var row = view.ForgeRow(index);
            row.interactable = row.blocksRaycasts = enabled;
            row.alpha = enabled ? 1f : .32f;
        }

        private void UpdateForgeValues()
        {
            if (kind != "forgeEquipment" || !HasRules) return;
            ForgeRequest(out string error, false);
            if (error != null) view.Label("forgeBudget").text = error;
            view.Button("forgeMake").interactable = !sendingForge && (pending != null || error == null);
        }

        private GmForgeAction ForgeRequest(out string error, bool create = true)
        {
            error = null;
            if (!HasRules) { error = T("forgeUnavailable"); return null; }
            var tier = CurrentTier;
            if (!Number(view.Field("forgeEnhance").text, out int enhance) || enhance < tier.minEnhance || enhance > tier.maxEnhance)
            { error = T("forgeEnhanceError", tier.minEnhance, tier.maxEnhance); return null; }
            if (!Number(view.Field("forgeCount").text, out int quantity) || quantity < 1 || quantity > 10)
            { error = T("grantAmount"); return null; }
            var grade = Rules.grades.First(x => x.grade == forgeGrade);
            int budget = grade.initialSubstats + enhance / 3;
            int count = grade.unlockEnhancements.Count(at => at <= enhance);
            int total = 0, laterUpgrades = 0;
            var stats = create ? new GmForgeSubstat[count] : null;
            for (int i = count - 1; i >= 0; i--)
            {
                var rule = tier.stats.First(s => s.kind == subKinds[i]);
                int unlock = grade.unlockEnhancements[i];
                int laterUnlocks = grade.unlockEnhancements.Count(at => at > unlock && at <= enhance);
                int maxRolls = 1 + (enhance - unlock) / 3 - laterUnlocks - laterUpgrades;
                if (!Number(view.Field("forgeRolls" + i).text, out int rolls) || rolls < 1 || rolls > maxRolls)
                {
                    error ??= T("forgeRollError", i + 1, maxRolls);
                    view.Label("forgeRange" + i).text = "—";
                    continue;
                }
                total += rolls;
                laterUpgrades += rolls - 1;
                var range = RollRange(rule);
                string lo = Display(rule.kind, range.rollMin * rolls), hi = Display(rule.kind, range.rollMax * rolls);
                view.Label("forgeRange" + i).text = lo + " – " + hi;
                long unit = Percent(rule.kind) ? 100L : 1L;
                if (!Number(view.Field("forgeValue" + i).text, out int value) ||
                    value * unit < range.rollMin * rolls || value * unit > range.rollMax * rolls ||
                    range.step < 1 || value * unit % range.step != 0)
                { error ??= T("forgeValueError", i + 1, lo, hi); continue; }
                if (create) stats[i] = new GmForgeSubstat { kind = rule.kind, rolls = rolls, value = (int)(value * unit) };
            }
            if (total != budget) error ??= T("forgeBudgetError", budget);
            view.Label("forgeBudget").text = error ?? T("forgeBudget", total, budget);
            if (!create || error != null) return null;
            return new GmForgeAction { requestId = Guid.NewGuid().ToString("N"), action = "forgeEquipment", key = forgeSlot,
                value = quantity, forge = new GmForgeRecipe { itemLevel = tier.itemLevel, enhanceLevel = enhance,
                    grade = forgeGrade, setId = forgeSet, mainKind = forgeMain, substats = stats,
                    rulesVersion = Rules.enhancementRulesVersion } };
        }

        private GmForgeRules.RollRange RollRange(GmForgeRules.Stat stat) =>
            stat.gradeRanges?.FirstOrDefault(range => range.grade == forgeGrade) ??
            new GmForgeRules.RollRange { grade = forgeGrade, rollMin = stat.rollMin, rollMax = stat.rollMax, step = stat.step };
    }
}
