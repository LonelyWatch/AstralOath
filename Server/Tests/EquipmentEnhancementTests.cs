using System.Net;
using System.Text.Json;
using AstralOath.Server.Inventory;
using AstralOath.Server.Entry;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class EquipmentEnhancementTests
{
    [Fact]
    public void ExistingSubstatCountsAdvanceForEveryMilestoneWhileUnselectedStatsKeepZero()
    {
        var item = EpicWeapon() with { Substats = [new("healthPct", 400, 0), new("speed", 2, 0),
            new("critChanceBp", 300, 0), new("critDamageBp", 400, 0)] };
        var after = EquipmentEnhancementEngine.Calculate(item, [new("ma_est6e", 7)], _ => 0).After;
        var rows = JsonSerializer.SerializeToElement(after.Substats, JsonSerializerOptions.Web);
        Assert.Equal(5, rows[0].GetProperty("enhancementCount").GetInt32());
        Assert.True(rows[0].GetProperty("enhancementCountKnown").GetBoolean());
        Assert.All(rows.EnumerateArray().Skip(1), stat => Assert.Equal(0, stat.GetProperty("enhancementCount").GetInt32()));
    }

    [Fact]
    public void UnknownExistingCountsStayUnknownButNewlyUnlockedSubstatsStartAtZero()
    {
        var item = EpicWeapon() with { Grade = "rare", Substats = [new("healthPct", 400), new("speed", 2)] };
        var after = EquipmentEnhancementEngine.Calculate(item, [new("ma_est6e", 7)], _ => 0).After;
        var rows = JsonSerializer.SerializeToElement(after.Substats, JsonSerializerOptions.Web);
        Assert.False(rows[0].TryGetProperty("enhancementCount", out _));
        Assert.False(rows[0].GetProperty("enhancementCountKnown").GetBoolean());
        Assert.False(rows[1].TryGetProperty("enhancementCount", out _));
        Assert.Equal(0, rows[2].GetProperty("enhancementCount").GetInt32());
        Assert.Equal(0, rows[3].GetProperty("enhancementCount").GetInt32());
    }

    [Fact]
    public void LegacyCountsRequireCompleteHistoryButWitnessedUnlocksCanRecoverIndependently()
    {
        var item = EpicWeapon() with { Grade = "rare", EnhanceLevel = 15 };
        EquipmentEnhancementStep[] history = [new(3, "healthPct", 400, 800, false), new(6, "healthPct", 800, 1200, false),
            new(9, "critChanceBp", 0, 300, true), new(12, "critDamageBp", 0, 400, true), new(15, "healthPct", 1200, 1600, false)];
        var complete = EquipmentEnhancementEngine.RecoverEnhancementCounts(item, history);
        Assert.Equal(new int?[] { 3, 0, 0, 0 }, complete.Substats.Select(stat => stat.EnhancementCount));
        var incomplete = EquipmentEnhancementEngine.RecoverEnhancementCounts(item, history.Skip(1).ToArray());
        Assert.Equal(new int?[] { null, null, 0, 0 }, incomplete.Substats.Select(stat => stat.EnhancementCount));
        var missingLater = EquipmentEnhancementEngine.RecoverEnhancementCounts(item, history.Take(4).ToArray());
        Assert.All(missingLater.Substats, stat => Assert.Null(stat.EnhancementCount));
        Assert.All(EquipmentEnhancementEngine.RecoverEnhancementCounts(EpicWeapon(), []).Substats,
            stat => Assert.Equal(0, stat.EnhancementCount));
        Assert.All(EquipmentEnhancementEngine.RecoverEnhancementCounts(EpicWeapon() with { EnhanceLevel = 6 }, []).Substats,
            stat => Assert.Null(stat.EnhancementCount));
    }

    [Fact]
    public void FutureOrDuplicateHistoryCannotSupplyAKnownCount()
    {
        var item = EpicWeapon() with { EnhanceLevel = 3 };
        EquipmentEnhancementStep[] future = [new(3, "healthPct", 0, 400, true), new(6, "healthPct", 400, 800, false)];
        Assert.Null(EquipmentEnhancementEngine.RecoverEnhancementCounts(item, future).Substats[0].EnhancementCount);
        EquipmentEnhancementStep[] duplicate = [new(3, "healthPct", 400, 800, false), new(3, "speed", 2, 4, false)];
        Assert.All(EquipmentEnhancementEngine.RecoverEnhancementCounts(item, duplicate).Substats,
            stat => Assert.Null(stat.EnhancementCount));
    }

    [Fact]
    public async Task EnhancementRulesRequireAnAuthenticatedAccount()
    {
        using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/inventory/equipment/enhancement-rules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void SourceRulesRecalculateEpic85StatsAndMatchRecordedPercentAndExperienceAnchors()
    {
        var profile = Assert.Single(EquipmentEnhancementCatalog.Rules.Profiles,
            p => p.ItemLevel == 85 && p.Grade == "epic" && p.Slot == "helmet" && p.MainKind == "health");
        Assert.True(profile.Supported);
        Assert.Equal(16, profile.XpThresholds.Length);
        Assert.Equal(0, profile.XpThresholds[0]);
        Assert.Equal(525, profile.XpThresholds[1]);
        Assert.Equal(1969, profile.XpThresholds[3]);
        Assert.Equal(1050, profile.XpThresholds[4] - profile.XpThresholds[3]);
        Assert.Equal(540, profile.MainValues[0]);
        Assert.Equal(2700, profile.MainValues[15]);
        var percent = Assert.Single(EquipmentEnhancementCatalog.Rules.Profiles,
            p => p.ItemLevel == 85 && p.Grade == "epic" && p.Slot == "necklace" && p.MainKind == "healthPct");
        Assert.Equal(1900, percent.MainValues[3]);
        Assert.Equal(2600, percent.MainValues[6]);
        Assert.Equal(6000, percent.MainValues[15]);
    }

    [Fact]
    public void PercentageMainAtPlusNineMatchesRecorded34WhileFixedDefenseKeepsFlooredBasis()
    {
        var percent = Assert.Single(EquipmentEnhancementCatalog.Rules.Profiles,
            p => p.ItemLevel == 85 && p.Grade == "epic" && p.Slot == "boots" && p.MainKind == "healthPct");
        Assert.Equal(3400, percent.MainValues[9]); // Original 69s preview: 12% + 22%.
        var defense = Assert.Single(EquipmentEnhancementCatalog.Rules.Profiles,
            p => p.ItemLevel == 90 && p.Grade == "heroic" && p.Slot == "armor" && p.MainKind == "defense");
        Assert.Equal(62, defense.MainValues[0]);
        Assert.Equal(310, defense.MainValues[15]);
    }

    [Fact]
    public void MaterialsKeepPartialExperienceAndCostsSeparateFromTheOneSuccessRoll()
    {
        var item = EpicWeapon();
        var normal = EquipmentEnhancementEngine.Calculate(item, [new("ma_est2e", 1), new("ma_est1e", 2)], _ => 0);
        Assert.Equal(1700, normal.Result.GrantedXp);
        Assert.Equal(17600, normal.Result.ConsumedGold);
        Assert.Equal(2, normal.Result.AfterLevel);
        Assert.Equal(1700, normal.After.Xp);
        Assert.Empty(normal.Steps);
        var great = EquipmentEnhancementEngine.Calculate(item, [new("ma_est2e", 1)], limit => limit - 1);
        Assert.Equal("great", great.Result.SuccessTier);
        Assert.Equal(3000, great.Result.GrantedXp);
        Assert.Equal(14400, great.Result.ConsumedGold);
        Assert.Equal(3, great.Result.AfterLevel);
        Assert.Single(great.Steps);
        Assert.Equal(4, great.After.Substats.Count);
        Assert.Equal(160, great.After.MainStat.Value);
    }

    [Fact]
    public void MultiLevelEnhancementAppliesEachUnlockMilestoneWithoutDuplicatingStats()
    {
        var item = EpicWeapon() with { Grade = "rare", Substats = [new("healthPct", 400), new("speed", 2)] };
        var result = EquipmentEnhancementEngine.Calculate(item, [new("ma_est6e", 5)], _ => 0);
        Assert.Equal(15, result.After.EnhanceLevel);
        Assert.Equal(4, result.After.Substats.Count);
        Assert.Equal(4, result.After.Substats.Select(s => s.Kind).Distinct().Count());
        Assert.Equal(5, result.Steps.Length);
        Assert.Equal(new[] { 9, 12 }, result.Steps.Where(s => s.Added).Select(s => s.Milestone));
        Assert.DoesNotContain(result.After.Substats, s => s.Kind is "attack" or "defense" or "defensePct");
    }

    [Fact]
    public void IneligibleInputsAreRejectedBeforeRandomOrBalancesChange()
    {
        int calls = 0;
        int Random(int bound) { calls++; return 0; }
        void Reject(OwnedEquipment item, EquipmentEnhancementMaterial?[]? materials, string code) =>
            Assert.Equal(code, Assert.Throws<EntryProblem>(() => EquipmentEnhancementEngine.Calculate(item, materials, Random)).Message);
        Reject(EpicWeapon() with { EnhancementSupported = false }, [new("ma_est2e", 1)], "equipment_profile_unsupported");
        Reject(EpicWeapon() with { EnhanceLevel = 15 }, [new("ma_est2e", 1)], "equipment_enhancement_cap");
        Reject(EpicWeapon(), [new("ma_est2j", 1)], "invalid_equipment_material");
        Reject(EpicWeapon(), [new("ma_est2e", 0)], "invalid_equipment_material");
        Reject(EpicWeapon(), [new("ma_est2e", 1), new("ma_est2e", 1)], "invalid_equipment_material");
        Reject(EpicWeapon() with { MainStat = new("attack", 80) }, [new("ma_est2e", 1)], "equipment_profile_unsupported");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void OverflowReturnsOnlyWholePowdersFromBaseMaterialExperience()
    {
        var basis = EpicWeapon();
        var profile = EquipmentEnhancementEngine.Profile(basis)!;
        var item = basis with { EnhanceLevel = 14, Xp = profile.XpThresholds[15] - 50,
            MainStat = new("attack", profile.MainValues[14]) };
        foreach (var random in new Func<int,int>[] { _ => 0, bound => bound - 1 })
        {
            var result = EquipmentEnhancementEngine.Calculate(item, [new("ma_est2e", 1)], random).Result;
            Assert.Equal(15, result.AfterLevel);
            Assert.Equal("ma_est1e", result.ReturnedPowderKind);
            Assert.Equal(14, result.ReturnedPowderQuantity);
        }
        var onlyBonus = item with { Xp = profile.XpThresholds[15] - 2000 };
        var bonus = EquipmentEnhancementEngine.Calculate(onlyBonus, [new("ma_est2e", 1)], bound => bound - 1).Result;
        Assert.Equal(15, bonus.AfterLevel);
        Assert.Equal(0, bonus.ReturnedPowderQuantity);
    }

    private static OwnedEquipment EpicWeapon() => new("11111111111111111111111111111111", "gm_forge_weapon", "weapon",
        "speed", 85, 0, "epic", new("attack", 100),
        [new("healthPct", 400), new("speed", 2), new("critChanceBp", 300), new("critDamageBp", 400)], null,
        EnhancementSupported: true);
}
