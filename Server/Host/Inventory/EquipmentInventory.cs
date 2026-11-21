using System.Text.Json;

namespace AstralOath.Server.Inventory;

internal sealed record EquipmentInventory(int Capacity, int Used, long Version, IReadOnlyList<OwnedEquipment> Items);
internal sealed record OwnedEquipment(string InstanceId, string TemplateId, string Slot, string SetId, int ItemLevel,
    int EnhanceLevel, string Grade, OwnedEquipmentStat MainStat, IReadOnlyList<OwnedEquipmentStat> Substats, string? EquippedHeroId);
internal sealed record OwnedEquipmentStat(string Kind, int Value);
internal sealed record EquipmentMutation(string? RequestId, string? Action, string? InstanceId,
    string? HeroInstanceId, string? Slot, long? ExpectedVersion);

internal sealed record TrainingEquipment(string TemplateId, string Slot, string SetId, OwnedEquipmentStat MainStat,
    OwnedEquipmentStat[] Substats)
{
    internal static IReadOnlyList<TrainingEquipment> Read()
    {
        using var resource = typeof(TrainingEquipment).Assembly.GetManifestResourceStream("AstralOath.TrainingEquipment")
            ?? throw new InvalidDataException("The training equipment resource is missing.");
        return JsonSerializer.Deserialize<TrainingEquipment[]>(resource, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("The training equipment resource is empty.");
    }
}
