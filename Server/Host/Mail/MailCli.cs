using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstralOath.Server.Entry;

namespace AstralOath.Server.Mail;

internal static class MailCli
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public static async Task<int> Run(string[] arguments, string? connectionString, CancellationToken cancellation)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var options = Parse(arguments);
            object result;
            if (options.Action == "preview")
            {
                // Read all input before opening a database connection. The store freezes these values.
                var draft = await ReadDraft(options.TemplateFile!, deadline.Token);
                var recipients = options.AllowFile is null ? options.PlayerIds! : await ReadIds(options.AllowFile, deadline.Token);
                var denied = options.DenyFile is null ? [] : await ReadIds(options.DenyFile, deadline.Token);
                var request = new MailOperatorPreviewRequest(options.RealmId!, OperatorId(), options.Scope!, recipients, denied, draft);
                result = await new MailStore(connectionString, MailCatalog.Load()).PreviewOperator(request, deadline.Token);
            }
            else
            {
                var store = new MailStore(connectionString, MailCatalog.Load());
                result = options.Action switch
                {
                    "confirm" => await store.ConfirmOperator(options.PreviewId!, OperatorId(), deadline.Token),
                    "status" => await store.BatchStatus(options.BatchId!, deadline.Token),
                    "retry" => await store.RetryBatch(options.BatchId!, OperatorId(), deadline.Token),
                    _ => throw InvalidArguments()
                };
            }
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
            return 0;
        }
        catch (EntryProblem problem)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = problem.Code }, Json));
            return problem.Status < 500 ? 2 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("{\"error\":\"mail_command_canceled\"}");
            return 130;
        }
        catch (Exception)
        {
            // Raw SQL/file/serializer failures may contain credentials or mail text.
            Console.Error.WriteLine("{\"error\":\"mail_operation_failed\"}");
            return 1;
        }
    }

    internal static MailCliOptions Parse(string[] arguments)
    {
        if (arguments.FirstOrDefault() == "mail") arguments = arguments[1..];
        if (arguments.Length is < 1 or > 32 || arguments.Any(x => x.Length > 8192)) throw InvalidArguments();
        var action = arguments[0];
        string[] allowed = action switch
        {
            "preview" => ["--realm", "--to", "--allow-file", "--deny-file", "--all-in-realm", "--template"],
            "confirm" => ["--preview-id"],
            "status" or "retry" => ["--batch-id"],
            _ => throw InvalidArguments()
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < arguments.Length; i++)
        {
            var key = arguments[i];
            if (!allowed.Contains(key) || values.ContainsKey(key)) throw InvalidArguments();
            if (key == "--all-in-realm") { values.Add(key, "true"); continue; }
            if (++i >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i]) || arguments[i].StartsWith("--", StringComparison.Ordinal))
                throw InvalidArguments();
            values.Add(key, arguments[i]);
        }
        string Required(string key) => values.TryGetValue(key, out var value) ? value : throw InvalidArguments();
        if (action == "confirm") return new(action, PreviewId: Id(Required("--preview-id")));
        if (action is "status" or "retry") return new(action, BatchId: Id(Required("--batch-id")));
        var realm = Required("--realm");
        if (realm.Length > 64 || realm.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_'))) throw InvalidArguments();
        var bases = new[] { "--to", "--allow-file", "--all-in-realm" }.Count(values.ContainsKey);
        if (bases != 1) throw InvalidArguments();
        var ids = values.TryGetValue("--to", out var to) ? to.Split(',').Select(x => Id(x.Trim())).ToArray() : [];
        var scope = values.ContainsKey("--to") ? "ids" : values.ContainsKey("--allow-file") ? "whitelist" : "allInRealm";
        return new(action, realm, scope, ids, values.GetValueOrDefault("--allow-file"),
            values.GetValueOrDefault("--deny-file"), Required("--template"));
    }

    internal static async Task<string[]> ReadIds(string path, CancellationToken cancellation)
    {
        var text = await ReadText(path, 4 * 1024 * 1024, "mail_list_unreadable", cancellation);
        var rows = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (rows.Length == 0) throw new EntryProblem(400, "mail_list_empty");
        if (rows.Length > 100000) throw new EntryProblem(400, "mail_list_too_large");
        try { return rows.Select(Id).ToArray(); }
        catch (EntryProblem) { throw new EntryProblem(400, "mail_list_invalid"); }
    }

    internal static async Task<MailDraft> ReadDraft(string path, CancellationToken cancellation)
    {
        var text = await ReadText(path, 256 * 1024, "mail_template_unreadable", cancellation);
        try { return JsonSerializer.Deserialize<MailDraft>(text, Json) ?? throw new EntryProblem(400, "invalid_mail_spec"); }
        catch (JsonException) { throw new EntryProblem(400, "invalid_mail_spec"); }
    }

    private static async Task<string> ReadText(string path, int limit, string code, CancellationToken cancellation)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            if (file.Length > limit) throw new EntryProblem(400, code);
            var bytes = new byte[limit + 1];
            var size = 0;
            while (size < bytes.Length)
            {
                var read = await file.ReadAsync(bytes.AsMemory(size), cancellation);
                if (read == 0) break;
                size += read;
            }
            if (size > limit) throw new EntryProblem(400, code);
            return new UTF8Encoding(false, true).GetString(bytes, 0, size).TrimStart('\uFEFF');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
        { throw new EntryProblem(400, code); }
    }

    internal static string OperatorId()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? throw new EntryProblem(403, "mail_operator_unavailable");
        }
        var identityHash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName));
        return "local-" + Convert.ToHexString(identityHash)[..40].ToLowerInvariant();
    }

    private static string Id(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty
        ? id.ToString("N") : throw InvalidArguments();
    private static EntryProblem InvalidArguments() => new(400, "invalid_mail_cli_arguments");
}

internal sealed record MailCliOptions(string Action, string? RealmId = null, string? Scope = null,
    string[]? PlayerIds = null, string? AllowFile = null, string? DenyFile = null,
    string? TemplateFile = null, string? PreviewId = null, string? BatchId = null);

internal sealed record MailOperatorPreviewRequest(string RealmId, string OperatorId, string Scope,
    string[] PlayerIds, string[] DenyIds, MailDraft Draft);
