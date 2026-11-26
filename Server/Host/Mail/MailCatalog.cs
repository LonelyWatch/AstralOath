using System.Text.Json;
using System.Text.RegularExpressions;
using AstralOath.Server.Entry;
using AstralOath.Server.Inventory;
using AstralOath.Server.Summon;
using AstralOath.Server.Battle;

namespace AstralOath.Server.Mail;
internal sealed class MailCatalog
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly MailConfiguration config;
    private readonly SummonCatalog rewards;
    internal static MailCatalog Load() => new(ReadEmbedded(), SummonCatalog.Load(ArtifactCatalogReader.Load()));
    private static string ReadEmbedded()
    {
        using var input = typeof(MailCatalog).Assembly.GetManifestResourceStream("AstralOath.MailCatalog") ?? throw new InvalidDataException("Missing mail catalog.");
        return new StreamReader(input).ReadToEnd();
    }

    internal MailCatalog(string source, SummonCatalog rewards)
    {
        this.rewards = rewards;
        config = JsonSerializer.Deserialize<MailConfiguration>(source, Json) ?? throw new InvalidDataException("Invalid mail catalog.");
        if (config.Energy == null || config.Senders is not { Length: > 0 and <= 100 } || config.Senders.Any(x => x == null) || config.Resources == null || config.Resources.Any(x => x == null) || config.Bundles == null || config.Bundles.Any(x => x == null) || config.Templates == null || config.Templates.Any(x => x == null) || config.LetterTypes == null || config.LetterTypes.Any(x => x == null))
            throw new InvalidDataException("Invalid mail catalog fields.");
        if (config.SchemaVersion != 1 || config.RulesVersion < 1 || config.Energy.InitialBalance != 0 || config.Energy.NaturalCap < 1 || config.Energy.NaturalCap > config.Energy.AbsoluteCap || config.Energy.AbsoluteCap != 1_000_000_000 || config.LetterTypes.Length != 2 || !config.LetterTypes.Any(x => x.Id == "standard" && x.BulkEligible) || !config.LetterTypes.Any(x => x.Id == "gm" && !x.BulkEligible))
            throw new InvalidDataException("Invalid mail rules.");
        if (config.Senders.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 64 || x.Name.Any(char.IsControl) || !PresentationId(x.Icon)) || config.Resources.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 120 || !PresentationId(x.Icon)) || config.Bundles.Any(x => x.Attachments == null || x.Attachments.Any(x => x == null) || !PresentationId(x.Icon) || (!string.IsNullOrEmpty(x.Cover) && !PresentationId(x.Cover))))
            throw new InvalidDataException("Invalid mail presentation.");
        foreach (var ids in new[]
        {
            config.Senders.Select(x => x.Id),
            config.Resources.Select(x => x.Kind + "." + x.Key),
            config.Bundles.Select(x => x.Id),
            config.Templates.Select(x => x.Id)
        }

        )
            if (ids.Any(x => !Regex.IsMatch(x, "^[A-Za-z0-9_.-]{1,64}$")) || ids.Distinct().Count() != ids.Count())
                throw new InvalidDataException("Invalid mail IDs.");
        try
        {
            foreach (var bundle in config.Bundles)
                FreezeAttachment(new("bundle", bundle.Id, 1), new HashSet<string>(), 0);
            foreach (var resource in config.Resources)
                FreezeAttachment(new(resource.Kind, resource.Key, 1), new HashSet<string>(), 0);
            foreach (var template in config.Templates)
                Freeze(new(TemplateId: template.Id));
        }
        catch (EntryProblem)
        {
            throw new InvalidDataException("Invalid mail references or rewards.");
        }
    }

    internal MailEnergyRule Energy => config.Energy;

    internal object SafeCatalog() => new
    {
        config.RulesVersion,
        config.LetterTypes,
        config.Senders,
        resources = config.Resources.Select(x => new { x.Kind, x.Key, x.Name, x.Icon, maxQuantity = IsInstance(x.Kind) ? 10 : 1_000_000 }),
        config.Bundles,
        config.Templates,
        limits = new
        {
            maxAttachments = 20,
            maxQuantity = 1_000_000,
            maxTitleLength = 120,
            maxBodyLength = 8000,
            maxExpiryDays = 90
        }
    };
    internal MailFrozen Freeze(MailDraft input)
    {
        try
        {
            return FreezeCore(input);
        }
        catch (OverflowException)
        {
            throw Invalid();
        }
    }

    private MailFrozen FreezeCore(MailDraft input)
    {
        var template = input.TemplateId == null ? null : config.Templates.FirstOrDefault(x => x.Id == input.TemplateId) ?? throw Invalid();
        string type = input.LetterType ?? template?.LetterType ?? "standard";
        var letter = config.LetterTypes.FirstOrDefault(x => x.Id == type) ?? throw Invalid();
        string senderId = input.SenderId ?? template?.SenderId ?? config.Senders[0].Id;
        var sender = config.Senders.FirstOrDefault(x => x.Id == senderId) ?? throw Invalid();
        string title = input.Title ?? template?.Title ?? "", body = input.Body ?? template?.Body ?? "";
        int days = input.ExpiresInDays ?? template?.ExpiresInDays ?? 7;
        if (title.Length is < 1 or > 120 || body.Length > 8000 || days is < 1 or > 90 || title.Any(char.IsControl) || body.Any(x => char.IsControl(x) && x != '\r' && x != '\n' && x != '\t'))
            throw Invalid();
        var attachments = input.Attachments ?? template?.Attachments ?? [];
        if (attachments.Length > 20)
            throw Invalid();
        var outer = attachments.Select(x => FreezeAttachment(x, new HashSet<string>(), 0)).ToArray();
        var flattened = Merge(outer.SelectMany(Flatten));
        if (flattened.Sum(x => (long)x.Quantity) > 10_000_000 || flattened.Where(x => IsInstance(x.Kind)).Sum(x => x.Quantity) > 100)
            throw Invalid();
        string presentation = outer.Any(x => x.Presentation == "gmGift") ? "gmGift" : outer.Any(x => x.Kind == "bundle") ? "bundle" : "item";
        return new(config.RulesVersion, template?.Id ?? "custom", type, sender.Id, sender.Name, sender.Icon, title, body, days, letter.BulkEligible, outer, flattened, presentation, outer.FirstOrDefault(x => x.Presentation == presentation)?.Cover ?? "");
    }

    private MailReward FreezeAttachment(MailAttachmentInput input, HashSet<string> stack, int depth)
    {
        if (input == null || input.Quantity is < 1 or > 1_000_000 || input.Kind == null || input.Key == null || depth > 8)
            throw Invalid();
        if (input.Kind == "bundle")
        {
            var b = config.Bundles.FirstOrDefault(x => x.Id == input.Key) ?? throw Invalid();
            if (!stack.Add(b.Id) || b.Attachments.Length > 20 || b.Presentation is not ("bundle" or "gmGift"))
                throw Invalid();
            var children = b.Attachments.Select(x => FreezeAttachment(x with { Quantity = checked(x.Quantity * input.Quantity) }, stack, depth + 1)).ToArray();
            stack.Remove(b.Id);
            return new("bundle", b.Id, input.Quantity, b.Name, b.Icon, children, b.Presentation, b.Cover);
        }

        var resource = config.Resources.FirstOrDefault(x => x.Kind == input.Kind && x.Key == input.Key) ?? throw Invalid();
        int stars = 0;
        EquipmentGrant? equipment = null;
        bool valid = input.Kind switch
        {
            "currency" => SummonStore.MailCurrencyKeys.Contains(input.Key),
            "material" => SummonStore.MailMaterialKeys.Contains(input.Key),
            "energy" => input.Key == "energy",
            "hero" => rewards.HeroGrades.TryGetValue(input.Key, out stars),
            "artifact" => rewards.ArtifactGrades.TryGetValue(input.Key, out stars),
            "equipment" => InventoryStore.DevelopmentEquipmentIds.Contains(input.Key),
            _ => false
        };
        if (!valid || (IsInstance(input.Kind) && input.Quantity > 10))
            throw Invalid();
        if (input.Kind == "equipment")
            equipment = InventoryStore.GetTrainingGrant(input.Key);
        return new(input.Kind, input.Key, input.Quantity, resource.Name, resource.Icon, [], Stars: stars, Equipment: equipment);
    }

    internal static MailReward[] Merge(IEnumerable<MailReward> source) => source.GroupBy(x => (x.Kind, x.Key)).Select(g => g.First()with { Quantity = checked(g.Sum(x => x.Quantity)) }).OrderBy(x => x.Kind).ThenBy(x => x.Key).ToArray();
    private static IEnumerable<MailReward> Flatten(MailReward value) => value.Kind == "bundle" ? value.Children.SelectMany(Flatten) : [value];
    private static bool IsInstance(string kind) => kind is "hero" or "artifact" or "equipment";
    private static EntryProblem Invalid() => new(400, "invalid_mail_spec");
    private static bool PresentationId(string? id) => id != null && Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,64}$");
}

internal sealed record MailConfiguration(int SchemaVersion, int RulesVersion, MailEnergyRule Energy, MailLetterType[] LetterTypes, MailSender[] Senders, MailResource[] Resources, MailBundle[] Bundles, MailTemplate[] Templates);
internal sealed record MailEnergyRule(int InitialBalance, int NaturalCap, int AbsoluteCap);
internal sealed record MailLetterType(string Id, bool BulkEligible);
internal sealed record MailSender(string Id, string Name, string Icon);
internal sealed record MailResource(string Kind, string Key, string Name, string Icon);
internal sealed record MailBundle(string Id, string Name, string Icon, string Presentation, string Cover, MailAttachmentInput[] Attachments);
internal sealed record MailTemplate(string Id, string Purpose, string LetterType, string SenderId, string Title, string Body, int ExpiresInDays, MailAttachmentInput[] Attachments);
