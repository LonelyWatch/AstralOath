namespace AstralOath.Server.Inventory;

internal static class EquipmentEnhancementCatalog
{
    // Official STOVE probability notice 7902683: one mutually exclusive result per operation.
    internal static EquipmentEnhancementRules Rules { get; } = new("equipment.enhancement.source-r4262.recorded-economy-v1",
        EquipmentEnhancementSource.Materials(), EquipmentEnhancementSource.Profiles(),
    [new("normal", 8900, 10000), new("good", 1000, 15000), new("great", 100, 20000)]);
}
