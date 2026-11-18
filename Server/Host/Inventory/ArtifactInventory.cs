namespace AstralOath.Server.Inventory;

internal sealed record ArtifactInventory(int Capacity, int Used, IReadOnlyList<OwnedArtifact> Artifacts,
    ArtifactMaterials Materials);
internal sealed record OwnedArtifact(string InstanceId, string TemplateId, int Level, int Experience,
    int LimitBreaks, bool Locked, string? HeroInstanceId, string Source);
internal sealed record ArtifactMaterials(int Gold, int LesserCharm, int GreaterCharm, int EpicCharm,
    int Bottle, int Powder);
internal sealed record ArtifactMutation(string? RequestId, string? HeroInstanceId, bool? Locked, ArtifactMaterial?[]? Materials);
internal sealed record ArtifactMaterial(string? Kind, string? InstanceId, int Quantity);
