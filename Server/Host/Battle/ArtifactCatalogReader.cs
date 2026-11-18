using System.Text.Json;
using AstralOath.Shared.Battle;

namespace AstralOath.Server.Battle;

internal static class ArtifactCatalogReader
{
    internal static ArtifactCatalog Load()
    {
        using var stream = typeof(ArtifactCatalogReader).Assembly.GetManifestResourceStream("AstralOath.ArtifactCatalog")
            ?? throw new InvalidDataException("The artifact catalog resource is missing.");
        var data = JsonSerializer.Deserialize<ArtifactCatalogData>(stream, new JsonSerializerOptions { IncludeFields = true })
            ?? throw new InvalidDataException("The artifact catalog resource is empty.");
        return new ArtifactCatalog(data);
    }
}
