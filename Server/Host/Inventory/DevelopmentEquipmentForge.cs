using System.Text.Json.Serialization;
using AstralOath.Server.Entry;

namespace AstralOath.Server.Inventory;

internal sealed record GmForgeInput(
    [property: JsonRequired] int ItemLevel, [property: JsonRequired] int EnhanceLevel,
    string? Grade, string? SetId, string? MainKind, GmForgeSubstat?[]? Substats, string? RulesVersion = null);
internal sealed record GmForgeSubstat(string? Kind, int Value, int Rolls);
internal sealed record ForgeStatRule(string Kind, int MainBase, int MainStep, int RollMin, int RollMax, int Step,
    int[]? MainValues = null, ForgeRollRange[]? GradeRanges = null);
internal sealed record ForgeRollRange(string Grade, int RollMin, int RollMax, int Step);
internal sealed record ForgeTier(int ItemLevel, int MinEnhance, int MaxEnhance, ForgeStatRule[] Stats);
internal sealed record ForgeGrade(string Grade, int[] UnlockEnhancements)
{
    public int InitialSubstats => UnlockEnhancements.Count(level => level == 0);
}
internal sealed record ForgeSlot(string Slot, string[] MainKinds, string[] SubKinds);
internal sealed record ForgeRules(ForgeTier[] Tiers, ForgeSlot[] Slots, ForgeGrade[] Grades, string[] Sets,
    string? EnhancementRulesVersion = null);

internal static class DevelopmentEquipmentForge
{
    private static readonly ForgeRules LegacyRules = BuildRules();
    internal static readonly ForgeRules Rules = BuildOriginalRules();

    private static ForgeRules BuildOriginalRules()
    {
        var tiers = LegacyRules.Tiers.Select(tier => tier with { Stats = tier.Stats.Select(stat =>
        {
            var main = EquipmentEnhancementCatalog.Rules.Profiles.FirstOrDefault(p => p.ItemLevel == tier.ItemLevel && p.MainKind == stat.Kind);
            var ranges = new[] { "rare", "heroic", "epic" }.Select(grade =>
            {
                var values = EquipmentEnhancementEngine.Distribution(tier.ItemLevel == 90 ? 85 : tier.ItemLevel, grade, stat.Kind);
                int step = stat.Kind is "attack" or "health" or "defense" or "speed" ? 1 : 100;
                return new ForgeRollRange(grade, values.Min(v => v.Value) * step, values.Max(v => v.Value) * step, step);
            }).ToArray();
            return stat with { MainBase = main?.MainValues[0] ?? stat.MainBase, MainStep = 0,
                MainValues = main?.MainValues, GradeRanges = ranges };
        }).ToArray() }).ToArray();
        return LegacyRules with { Tiers = tiers.Where(t => t.ItemLevel != 90).ToArray(),
            Grades = LegacyRules.Grades.Where(g => g.Grade is "rare" or "heroic" or "epic").ToArray(),
            EnhancementRulesVersion = EquipmentEnhancementCatalog.Rules.RulesVersion };
    }

    private static ForgeRules BuildRules()
    {
        // AstralOath development-only progression. Percentages use 100 units per percentage point.
        ForgeTier Tier(int level, int rank)
        {
            ForgeStatRule Rule(string kind, int basis, int growth, int low, int high, int step = 1) =>
                new(kind, basis, growth, low, high, step);
            var stats = new List<ForgeStatRule>
            {
                Rule("attack", 60 + 20 * rank, 22 + 4 * rank, 12 + 2 * rank, 22 + 4 * rank),
                Rule("health", 280 + 80 * rank, 110 + 20 * rank, 65 + 10 * rank, 115 + 15 * rank),
                Rule("defense", 40 + 10 * rank, 15 + 3 * rank, 10 + 2 * rank, 20 + 2 * rank),
                Rule("speed", 6 + 2 * rank, 2, 1 + rank, 3 + rank)
            };
            foreach (string kind in new[] { "attackPct", "healthPct", "defensePct", "effectivenessBp", "resistanceBp" })
                stats.Add(Rule(kind, 800 + 200 * rank, 300, 300 + 100 * rank, 600 + 100 * rank, 100));
            stats.Add(Rule("critChanceBp", 400 + 100 * rank, 300, 200 + 100 * rank, 400 + 100 * rank, 100));
            stats.Add(Rule("critDamageBp", 1000 + 200 * rank, 400, 300 + 100 * rank, 600 + 100 * rank, 100));
            return new(level, level == 90 ? 15 : 0, 15, stats.ToArray());
        }
        ForgeTier[] tiers = [Tier(70, 0), Tier(85, 1), Tier(90, 2)];
        var kinds = tiers[0].Stats.Select(stat => stat.Kind).ToArray();
        string[] accessory = ["attack", "health", "defense", "attackPct", "healthPct", "defensePct"];
        ForgeSlot[] slots = [
            new("weapon", ["attack"], kinds.Except(["defense", "defensePct"]).ToArray()),
            new("helmet", ["health"], kinds),
            new("armor", ["defense"], kinds.Except(["attack", "attackPct"]).ToArray()),
            new("necklace", [.. accessory, "critChanceBp", "critDamageBp"], kinds),
            new("ring", [.. accessory, "effectivenessBp", "resistanceBp"], kinds),
            new("boots", [.. accessory, "speed"], kinds)
        ];
        ForgeGrade[] grades = [new("normal", [3, 6, 9, 12]), new("good", [0, 3, 6, 9]),
            new("rare", [0, 0, 9, 12]), new("heroic", [0, 0, 0, 12]), new("epic", [0, 0, 0, 0])];
        return new(tiers, slots, grades, ["speed", "critical"]);
    }

    internal static EquipmentGrant Create(string? slotId, GmForgeInput? input)
    {
        if (input?.RulesVersion != null) return CreateOriginal(slotId, input);
        static EntryProblem Invalid() => new(400, "invalid_gm_forge");
        if (input is null || input.Substats is null || input.Substats.Length > 4) throw Invalid();
        var slot = LegacyRules.Slots.FirstOrDefault(entry => entry.Slot == slotId);
        var tier = LegacyRules.Tiers.FirstOrDefault(entry => entry.ItemLevel == input.ItemLevel);
        var grade = LegacyRules.Grades.FirstOrDefault(entry => entry.Grade == input.Grade);
        if (slot == null || tier == null || grade == null || !LegacyRules.Sets.Contains(input.SetId) ||
            input.EnhanceLevel < tier.MinEnhance || input.EnhanceLevel > tier.MaxEnhance ||
            !slot.MainKinds.Contains(input.MainKind)) throw Invalid();

        var main = tier.Stats.Single(rule => rule.Kind == input.MainKind);
        int enhance = input.EnhanceLevel;
        int unlocked = grade.UnlockEnhancements.Count(at => at <= enhance);
        if (input.Substats.Length != unlocked) throw Invalid();
        var used = new HashSet<string>(StringComparer.Ordinal) { main.Kind };
        var substats = new OwnedEquipmentStat[unlocked];
        int laterUpgrades = 0;
        // Newer rows compete for the same remaining upgrades. A milestone that opens another row cannot roll twice.
        for (int index = unlocked - 1; index >= 0; index--)
        {
            var stat = input.Substats[index];
            int unlock = grade.UnlockEnhancements[index];
            int laterUnlocks = grade.UnlockEnhancements.Count(at => at > unlock && at <= enhance);
            int available = (enhance - unlock) / 3 - laterUnlocks - laterUpgrades;
            if (stat?.Kind == null || !slot.SubKinds.Contains(stat.Kind) || !used.Add(stat.Kind) ||
                stat.Rolls < 1 || stat.Rolls > 1 + available) throw Invalid();
            var rule = tier.Stats.Single(row => row.Kind == stat.Kind);
            if (stat.Value < rule.RollMin * stat.Rolls || stat.Value > rule.RollMax * stat.Rolls ||
                stat.Value % rule.Step != 0) throw Invalid();
            laterUpgrades += stat.Rolls - 1;
            substats[index] = new(stat.Kind, stat.Value, stat.Rolls - 1);
        }
        if (unlocked + laterUpgrades != grade.InitialSubstats + enhance / 3) throw Invalid();
        return new("gm_forge_" + slot.Slot, slot.Slot, input.SetId!, input.ItemLevel, input.EnhanceLevel,
            grade.Grade, new(main.Kind, main.MainBase + main.MainStep * input.EnhanceLevel), substats);
    }

    private static EquipmentGrant CreateOriginal(string? slotId, GmForgeInput input)
    {
        static EntryProblem Invalid() => new(400, "invalid_gm_forge");
        if (input.RulesVersion != Rules.EnhancementRulesVersion || input.Substats == null) throw Invalid();
        var slot = Rules.Slots.FirstOrDefault(s => s.Slot == slotId);
        var tier = Rules.Tiers.FirstOrDefault(t => t.ItemLevel == input.ItemLevel);
        var grade = Rules.Grades.FirstOrDefault(g => g.Grade == input.Grade);
        var profile = EquipmentEnhancementCatalog.Rules.Profiles.FirstOrDefault(p => p.ItemLevel == input.ItemLevel &&
            p.Grade == input.Grade && p.Slot == slotId && p.MainKind == input.MainKind && p.Supported);
        if (slot == null || tier == null || grade == null || profile == null || !Rules.Sets.Contains(input.SetId) ||
            input.EnhanceLevel is < 0 or > 15 || !slot.MainKinds.Contains(input.MainKind)) throw Invalid();
        int enhance = input.EnhanceLevel;
        int unlocked = grade.UnlockEnhancements.Count(at => at <= enhance);
        if (input.Substats.Length != unlocked) throw Invalid();
        var allowed = EquipmentEnhancementEngine.LegalSubstats(slot.Slot, input.MainKind!);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var stats = new OwnedEquipmentStat[unlocked];
        int laterUpgrades = 0;
        for (int index = unlocked - 1; index >= 0; index--)
        {
            var stat = input.Substats[index];
            int unlock = grade.UnlockEnhancements[index];
            int laterUnlocks = grade.UnlockEnhancements.Count(at => at > unlock && at <= enhance);
            int available = (enhance - unlock) / 3 - laterUnlocks - laterUpgrades;
            if (stat?.Kind == null || !allowed.Contains(stat.Kind) || !used.Add(stat.Kind) ||
                stat.Rolls < 1 || stat.Rolls > 1 + available) throw Invalid();
            var range = tier.Stats.Single(s => s.Kind == stat.Kind).GradeRanges!.Single(r => r.Grade == grade.Grade);
            if (stat.Value < range.RollMin * stat.Rolls || stat.Value > range.RollMax * stat.Rolls ||
                stat.Value % range.Step != 0) throw Invalid();
            laterUpgrades += stat.Rolls - 1;
            stats[index] = new(stat.Kind, stat.Value, stat.Rolls - 1);
        }
        if (unlocked + laterUpgrades != grade.InitialSubstats + enhance / 3) throw Invalid();
        return new("gm_forge_" + slot.Slot, slot.Slot, input.SetId!, input.ItemLevel, enhance, grade.Grade,
            new(input.MainKind!, profile.MainValues[enhance]), stats, profile.Id);
    }
}
