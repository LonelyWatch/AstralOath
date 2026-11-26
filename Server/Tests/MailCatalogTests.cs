using System.Text.Json.Nodes;
using AstralOath.Server.Battle;
using AstralOath.Server.Entry;
using AstralOath.Server.Mail;
using AstralOath.Server.Summon;
using Xunit;

public sealed class MailCatalogTests
{
    internal static JsonNode Source()
    {
        using var stream = typeof(MailCatalog).Assembly.GetManifestResourceStream("AstralOath.MailCatalog")!;
        return JsonNode.Parse(stream)!;
    }

    internal static MailCatalog Catalog(JsonNode source) => new(source.ToJsonString(), SummonCatalog.Load(ArtifactCatalogReader.Load()));
    [Theory]
    [InlineData("currency", "unknown", 1)]
    [InlineData("energy", "energy", 0)]
    [InlineData("material", "gold", -1)]
    [InlineData("hero", "unknown", 1)]
    [InlineData("equipment", "training_speed_weapon", 11)]
    public void InvalidRewardsCannotBeFrozen(string kind, string key, int quantity)
    {
        Assert.Throws<EntryProblem>(() => MailCatalog.Load().Freeze(new(TemplateId: "maintenance", Attachments: [new(kind, key, quantity)])));
    }

    [Fact]
    public void CyclicBundleCannotLoad()
    {
        var json = Source();
        json["bundles"]![0]!["attachments"] = JsonNode.Parse("[{\"kind\":\"bundle\",\"key\":\"gm_gift\",\"quantity\":1}]");
        Assert.Throws<InvalidDataException>(() => Catalog(json));
    }

    [Fact]
    public void NestedQuantityOverflowIsControlledInvalidSpec()
    {
        var json = Source();
        json["bundles"]![0]!["attachments"] = JsonNode.Parse("[{\"kind\":\"material\",\"key\":\"gold\",\"quantity\":1000000}]");
        var catalog = Catalog(json);
        Assert.Throws<EntryProblem>(() => catalog.Freeze(new(TemplateId: "maintenance", Attachments: [new("bundle", "gm_gift", 1000000)])));
    }

    [Fact]
    public void NullAttachmentIsControlledInvalidSpec()
    {
        Assert.Throws<EntryProblem>(() => MailCatalog.Load().Freeze(new(TemplateId: "maintenance", Attachments: [null!])));
    }

    [Fact]
    public void MissingSenderRejectsCatalogAtLoad()
    {
        var source = Source();
        source["senders"] = new JsonArray();
        source["templates"] = new JsonArray();
        Assert.Throws<InvalidDataException>(() => Catalog(source));
    }
}
