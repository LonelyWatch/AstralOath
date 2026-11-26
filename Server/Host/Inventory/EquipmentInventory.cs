using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstralOath.Server.Inventory;

internal sealed record EquipmentInventory(int Capacity, int Used, long Version, IReadOnlyList<OwnedEquipment> Items,
    EquipmentEnhancementBalances? Materials = null);
internal sealed record OwnedEquipment(string InstanceId, string TemplateId, string Slot, string SetId, int ItemLevel,
    int EnhanceLevel, string Grade, OwnedEquipmentStat MainStat, IReadOnlyList<OwnedEquipmentStat> Substats, string? EquippedHeroId,
    long Xp = 0, bool EnhancementSupported = false, string? EnhancementBlockReason = null);
internal sealed record OwnedEquipmentStat(string Kind, int Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EnhancementCount = null)
{
    public bool EnhancementCountKnown => EnhancementCount.HasValue;
}
internal sealed record EquipmentGrant(string TemplateId, string Slot, string SetId, int ItemLevel, int EnhanceLevel,
    string Grade, OwnedEquipmentStat MainStat, OwnedEquipmentStat[] Substats, string? EnhancementProfileId = null);
internal sealed record EquipmentMutation(string? RequestId, string? Action, string? InstanceId,
    string? HeroInstanceId, string? Slot, long? ExpectedVersion);

internal sealed record EquipmentEnhancementBalances(long Gold, EquipmentEnhancementBalance[] Items);
internal sealed record EquipmentEnhancementBalance(string Kind, long Quantity);
internal sealed record EquipmentEnhancementRequest(string? RequestId, long? ExpectedVersion,
    EquipmentEnhancementMaterial?[]? Materials);
internal sealed record EquipmentEnhancementMaterial(string? Kind, int Quantity);
internal sealed record EquipmentEnhancementReceipt(EquipmentInventory Inventory, EquipmentEnhancementResult Result);
internal sealed record EquipmentEnhancementResult(string InstanceId, int BeforeLevel, int AfterLevel, long BeforeXp,
    long AfterXp, long GrantedXp, long ConsumedGold, string SuccessTier, EquipmentEnhancementChange[] Changes,
    string? ReturnedPowderKind = null, int ReturnedPowderQuantity = 0);
internal sealed record EquipmentEnhancementChange(string Kind, int Before, int After, bool Added);
internal sealed record EquipmentEnhancementRules(string RulesVersion, EquipmentEnhancementMaterialRule[] Materials,
    EquipmentEnhancementProfile[] Profiles, EquipmentEnhancementSuccess[] Successes,
    string EconomyScope = "recorded-economy-v1");
internal sealed record EquipmentEnhancementMaterialRule(string Id, string[] Slots, int Experience, int GoldPerUnit);
internal sealed record EquipmentEnhancementProfile(string Id, int ItemLevel, string Grade, string Slot, string MainKind,
    int[] MainValues, long[] XpThresholds, bool Supported, string? BlockReason);
internal sealed record EquipmentEnhancementSuccess(string Tier, int ProbabilityBp, int ExperienceMultiplierBp);
internal sealed record EquipmentEnhancementRoll(int ItemLevel, string Grade, string Family, int Value, int Weight);

internal sealed record TrainingEquipment(string TemplateId, string Slot, string SetId, OwnedEquipmentStat MainStat,
    OwnedEquipmentStat[] Substats)
{
    internal EquipmentGrant ToGrant() =>
        new(TemplateId, Slot, SetId, 15, 0, Substats.Length == 0 ? "normal" : "good", MainStat, Substats);

    internal static IReadOnlyList<TrainingEquipment> Read()
    {
        using var resource = typeof(TrainingEquipment).Assembly.GetManifestResourceStream("AstralOath.TrainingEquipment")
            ?? throw new InvalidDataException("The training equipment resource is missing.");
        return JsonSerializer.Deserialize<TrainingEquipment[]>(resource, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The training equipment resource is empty.");
    }
}
