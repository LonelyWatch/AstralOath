using AstralOath.Server.Entry;

namespace AstralOath.Server.Inventory;

internal sealed record EquipmentEnhancementStep(int Milestone, string Kind, int Before, int After, bool Added);
internal sealed record EquipmentEnhancementPlan(EquipmentEnhancementResult Result, OwnedEquipment After,
    EquipmentEnhancementMaterial[] Materials, EquipmentEnhancementStep[] Steps);

internal static class EquipmentEnhancementEngine
{
    internal static EquipmentEnhancementPlan Calculate(OwnedEquipment item, EquipmentEnhancementMaterial?[]? materials,
        Func<int, int> random)
    {
        if (item.EnhanceLevel >= 15) throw new EntryProblem(409, "equipment_enhancement_cap");
        var profile = Profile(item);
        if (!item.EnhancementSupported || profile is not { Supported: true } ||
            item.EnhanceLevel < 0 || item.MainStat.Value != profile.MainValues[item.EnhanceLevel] ||
            item.Xp < profile.XpThresholds[item.EnhanceLevel] || item.Xp >= profile.XpThresholds[item.EnhanceLevel + 1])
            throw new EntryProblem(409, "equipment_profile_unsupported");
        if (materials is not { Length: >= 1 and <= 32 }) throw InvalidMaterial();
        var consumed = new List<EquipmentEnhancementMaterial>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        long experience = 0, gold = 0;
        foreach (var selection in materials)
        {
            var known = EquipmentEnhancementCatalog.Rules.Materials.FirstOrDefault(m => m.Id == selection?.Kind);
            if (selection is not { Quantity: >= 1 and <= 999 } || known == null ||
                !known.Slots.Contains(item.Slot) || !used.Add(known.Id)) throw InvalidMaterial();
            consumed.Add(selection);
            experience += (long)known.Experience * selection.Quantity;
            gold += (long)known.GoldPerUnit * selection.Quantity;
        }
        if (consumed.Sum(m => m.Quantity) > 1000 || gold > int.MaxValue) throw InvalidMaterial();
        int roll = random(10000);
        var success = roll < 8900 ? EquipmentEnhancementCatalog.Rules.Successes[0]
            : roll < 9900 ? EquipmentEnhancementCatalog.Rules.Successes[1] : EquipmentEnhancementCatalog.Rules.Successes[2];
        long granted = experience * success.ExperienceMultiplierBp / 10000;
        int returnedPowder = checked((int)(Math.Max(0, experience - (profile.XpThresholds[15] - item.Xp)) / 100));
        string? powderKind = returnedPowder == 0 ? null : item.Slot is "necklace" or "ring" ? "ma_est1j" : "ma_est1e";
        long afterXp = Math.Min(profile.XpThresholds[15], checked(item.Xp + granted));
        int afterLevel = item.EnhanceLevel;
        while (afterLevel < 15 && afterXp >= profile.XpThresholds[afterLevel + 1]) afterLevel++;
        var substats = item.Substats.ToList();
        var steps = new List<EquipmentEnhancementStep>();
        int[] unlocks = item.Grade switch { "rare" => [9, 12], "heroic" => [12], "epic" => [], _ => [] };
        for (int level = item.EnhanceLevel + 1; level <= afterLevel; level++)
        {
            if (level % 3 != 0) continue;
            if (unlocks.Contains(level))
            {
                string[] available = LegalSubstats(item.Slot, item.MainStat.Kind).Except(substats.Select(s => s.Kind)).ToArray();
                string kind = available[random(available.Length)];
                int value = RollValue(item.ItemLevel, item.Grade, kind, random);
                substats.Add(new(kind, value));
                steps.Add(new(level, kind, 0, value, true));
            }
            else
            {
                int index = random(substats.Count);
                var stat = substats[index];
                int value = checked(stat.Value + RollValue(item.ItemLevel, item.Grade, stat.Kind, random));
                substats[index] = stat with { Value = value };
                steps.Add(new(level, stat.Kind, stat.Value, value, false));
            }
        }
        var after = item with { EnhanceLevel = afterLevel, Xp = afterXp,
            MainStat = item.MainStat with { Value = profile.MainValues[afterLevel] }, Substats = substats };
        var changes = new List<EquipmentEnhancementChange>();
        if (after.MainStat.Value != item.MainStat.Value)
            changes.Add(new(item.MainStat.Kind, item.MainStat.Value, after.MainStat.Value, false));
        foreach (var stat in substats)
        {
            var before = item.Substats.FirstOrDefault(s => s.Kind == stat.Kind);
            if (before == null || before.Value != stat.Value)
                changes.Add(new(stat.Kind, before?.Value ?? 0, stat.Value, before == null));
        }
        var result = new EquipmentEnhancementResult(item.InstanceId, item.EnhanceLevel, afterLevel, item.Xp, afterXp,
            granted, gold, success.Tier, changes.ToArray(), powderKind, returnedPowder);
        return new(result, after, consumed.OrderBy(m => m.Kind, StringComparer.Ordinal).ToArray(), steps.ToArray());
    }

    internal static EquipmentEnhancementProfile? Profile(OwnedEquipment item) =>
        EquipmentEnhancementCatalog.Rules.Profiles.FirstOrDefault(p => p.ItemLevel == item.ItemLevel && p.Grade == item.Grade &&
            p.Slot == item.Slot && p.MainKind == item.MainStat.Kind);

    internal static string[] LegalSubstats(string slot, string mainKind)
    {
        string[] all = ["attack", "health", "defense", "speed", "attackPct", "healthPct", "defensePct",
            "critChanceBp", "critDamageBp", "effectivenessBp", "resistanceBp"];
        string[] excluded = slot switch
        {
            "weapon" => ["attack", "defense", "defensePct"],
            "armor" => ["defense", "attack", "attackPct"], "helmet" => ["health"], _ => []
        };
        return all.Except(excluded).Where(k => k != mainKind).ToArray();
    }

    private static readonly EquipmentEnhancementRoll[] Rolls = EquipmentEnhancementSource.Rolls();
    internal static EquipmentEnhancementRoll[] Distribution(int level, string grade, string kind)
    {
        string family = kind switch
        {
            "attack" or "health" or "defense" or "speed" => kind,
            "critChanceBp" => "crit_chance_percent", "critDamageBp" => "crit_damage_percent", _ => "percent"
        };
        return Rolls.Where(r => r.ItemLevel == level && r.Grade == grade && r.Family == family).ToArray();
    }

    private static int RollValue(int level, string grade, string kind, Func<int, int> random)
    {
        var choices = Distribution(level, grade, kind);
        int number = random(choices.Sum(c => c.Weight));
        foreach (var choice in choices)
        {
            number -= choice.Weight;
            if (number < 0) return choice.Value * (kind is "attack" or "health" or "defense" or "speed" ? 1 : 100);
        }
        throw new InvalidDataException("Invalid enhancement probability distribution.");
    }

    private static EntryProblem InvalidMaterial() => new(400, "invalid_equipment_material");
}
