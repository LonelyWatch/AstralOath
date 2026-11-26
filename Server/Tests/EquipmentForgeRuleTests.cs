using AstralOath.Server.Entry;
using AstralOath.Server.Inventory;
using Xunit;

public sealed class EquipmentForgeRuleTests
{
    [Fact]
    public void SourceForgeRetainsOnlyEnhancementRollsAfterEachInitialSubstatRoll()
    {
        var input = new GmForgeInput(85, 6, "epic", "speed", "attack",
            [new("healthPct", 1200, 3), new("speed", 2, 1), new("critChanceBp", 300, 1), new("critDamageBp", 400, 1)],
            "equipment.enhancement.source-r4262.recorded-economy-v1");
        var forged = DevelopmentEquipmentForge.Create("weapon", input);
        var rows = System.Text.Json.JsonSerializer.SerializeToElement(forged.Substats, System.Text.Json.JsonSerializerOptions.Web);
        Assert.Equal(2, rows[0].GetProperty("enhancementCount").GetInt32());
        Assert.All(rows.EnumerateArray().Skip(1), stat => Assert.Equal(0, stat.GetProperty("enhancementCount").GetInt32()));
    }

    [Fact]
    public void EachAdvertisedSlotGradeAndMilestoneCanProduceAllItsMainStats()
    {
        var rules = DevelopmentEquipmentForge.Rules;
        foreach (var tier in rules.Tiers)
        foreach (var slot in rules.Slots)
        foreach (string mainKind in slot.MainKinds)
        foreach (var grade in rules.Grades)
        for (int level = tier.MinEnhance; level <= tier.MaxEnhance; level++)
        {
            int budget = grade.InitialSubstats + level / 3;
            int count = ExpectedUnlocks(grade.Grade).Count(at => at <= level);
            var substats = slot.SubKinds.Where(kind => kind != mainKind).Take(count).Select((kind, index) =>
            {
                int rolls = index == 0 ? 1 + budget - count : 1;
                var stat = tier.Stats.Single(rule => rule.Kind == kind);
                var range = stat.GradeRanges!.Single(r => r.Grade == grade.Grade);
                return new GmForgeSubstat(kind, rolls * range.RollMax, rolls);
            }).ToArray();
            var forge = new GmForgeInput(tier.ItemLevel, level, grade.Grade, "speed", mainKind, substats,
                rules.EnhancementRulesVersion);
            var item = DevelopmentEquipmentForge.Create(slot.Slot, forge);
            var main = tier.Stats.Single(rule => rule.Kind == mainKind);
            Assert.Equal(main.MainValues![level], item.MainStat.Value);
            Assert.NotNull(item.EnhancementProfileId);
            Assert.Equal(count, item.Substats.Length);
            Assert.Equal(tier.ItemLevel, item.ItemLevel);
        }
    }

    private static int[] ExpectedUnlocks(string grade) => grade switch
    {
        "normal" => [3, 6, 9, 12], "good" => [0, 3, 6, 9], "rare" => [0, 0, 9, 12],
        "heroic" => [0, 0, 0, 12], "epic" => [0, 0, 0, 0],
        _ => throw new ArgumentOutOfRangeException(nameof(grade))
    };

    [Theory]
    [InlineData("heroic", 15, 1, 1, 3, 3, false)]
    [InlineData("heroic", 15, 1, 2, 3, 2, true)]
    [InlineData("rare", 15, 1, 2, 2, 2, false)]
    [InlineData("rare", 15, 2, 2, 2, 1, true)]
    [InlineData("rare", 12, 1, 2, 2, 1, false)]
    [InlineData("rare", 12, 2, 2, 1, 1, true)]
    public void RollsOnLaterRowsMustFitAvailableUpgradeMilestones(string grade, int enhance,
        int first, int second, int third, int fourth, bool valid)
    {
        int[] rolls = [first, second, third, fourth];
        string[] kinds = ["health", "speed", "healthPct", "critChanceBp"];
        var tier = DevelopmentEquipmentForge.Rules.Tiers.Single(t => t.ItemLevel == 85);
        var stats = kinds.Select((kind, i) => new GmForgeSubstat(kind,
            rolls[i] * tier.Stats.Single(s => s.Kind == kind).RollMin, rolls[i])).ToArray();
        var input = new GmForgeInput(85, enhance, grade, "speed", "attack", stats);
        if (valid) Assert.Equal(4, DevelopmentEquipmentForge.Create("weapon", input).Substats.Length);
        else Assert.Throws<EntryProblem>(() => DevelopmentEquipmentForge.Create("weapon", input));
    }

    [Fact]
    public void InvalidTiersSlotsStatsAndRollDistributionsAreRejected()
    {
        GmForgeSubstat health = new("healthPct", 500, 1);
        GmForgeSubstat crit = new("critChanceBp", 300, 1);
        var basic = new GmForgeInput(85, 3, "good", "speed", "attack", [health, crit]);
        void Invalid(GmForgeInput? input, string slot = "weapon") =>
            Assert.Equal("invalid_gm_forge", Assert.Throws<EntryProblem>(() => DevelopmentEquipmentForge.Create(slot, input)).Message);
        Invalid(null);
        Invalid(basic, "artifact");
        Invalid(basic with { ItemLevel = 15 });
        Invalid(basic with { ItemLevel = 90, EnhanceLevel = 14 });
        Invalid(basic with { EnhanceLevel = -1 });
        Invalid(basic with { EnhanceLevel = 16 });
        Invalid(basic with { Grade = "unknown" });
        Invalid(basic with { SetId = "unknown" });
        Invalid(basic with { MainKind = "speed" });
        Invalid(basic with { Substats = null });
        Invalid(basic with { Substats = [health, null] });
        Invalid(basic with { Substats = [health, health] });
        Invalid(basic with { Substats = [health, new("attack", 24, 1)] });
        Invalid(basic with { Substats = [health, new("defense", 12, 1)] });
        Invalid(basic with { MainKind = "defense", Substats = [health, new("attackPct", 500, 1)] }, "armor");
        Invalid(basic with { Substats = [health, crit with { Kind = "unknown" }] });
        Invalid(basic with { Substats = [health] });
        Invalid(basic with { Substats = [health, crit, new("speed", 3, 1)] });
        Invalid(basic with { Substats = [health, crit with { Value = 299 }] });
        Invalid(basic with { Substats = [health, crit with { Value = 301 }] });
        Invalid(basic with { Substats = [health, crit with { Value = 600 }] });
        Invalid(basic with { Substats = [health, crit with { Rolls = 0 }] });
        Invalid(basic with { Substats = [health, crit with { Rolls = int.MaxValue }] });
        Invalid(basic with { Grade = "epic", EnhanceLevel = 3, Substats = [health, crit, new("speed", 3, 1), new("attackPct", 500, 1)] });
        Assert.Equal(2, DevelopmentEquipmentForge.Create("weapon", basic).Substats.Length);
    }
}
